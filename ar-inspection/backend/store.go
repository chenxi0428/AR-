package main

import (
	"bytes"
	"encoding/json"
	"errors"
	"fmt"
	"os"
	"path/filepath"
	"sort"
	"sync"
	"time"
)

// Store 用一个互斥锁保护内存列表，并把数据持久化到单个 JSON 文件。
// 写入采用"同目录临时文件 + rename 原子替换"，避免进程中断时留下半截文件。
type Store struct {
	mu      sync.RWMutex
	path    string  // 数据文件路径（DATA_FILE）
	issues  []Issue // 内存中的问题列表（按创建顺序追加）
	nextSeq int64   // 下一条问题的内存排序序号
}

// NewStore 创建 Store，此时还不读文件，需要再调用 Load 完成启动加载。
func NewStore(path string) *Store {
	return &Store{
		path:   path,
		issues: []Issue{},
	}
}

// Path 返回数据文件路径（日志/测试用）。
func (s *Store) Path() string {
	return s.path
}

// Load 从数据文件读取已有问题。
//   - 文件不存在或内容为空：视为空列表（首次启动的正常情况）；
//   - 文件内容损坏：返回错误并且不覆盖原文件，由调用方决定是否终止启动，
//     避免"读失败就清空"造成的数据丢失。
func (s *Store) Load() error {
	s.mu.Lock()
	defer s.mu.Unlock()

	data, err := os.ReadFile(s.path)
	if errors.Is(err, os.ErrNotExist) {
		s.issues = []Issue{}
		s.nextSeq = 0
		return nil
	}
	if err != nil {
		return fmt.Errorf("读取数据文件 %s 失败: %w", s.path, err)
	}
	if len(bytes.TrimSpace(data)) == 0 {
		s.issues = []Issue{}
		s.nextSeq = 0
		return nil
	}

	var issues []Issue
	if err := json.Unmarshal(data, &issues); err != nil {
		return fmt.Errorf("解析数据文件 %s 失败（请修复或删除该文件后重试）: %w", s.path, err)
	}
	if issues == nil {
		issues = []Issue{}
	}
	// 文件里的顺序就是创建顺序，用它重建内存排序序号。
	for i := range issues {
		issues[i].seq = int64(i)
	}
	s.issues = issues
	s.nextSeq = int64(len(issues))
	return nil
}

// List 返回按 createdAt 倒序（最新在前）的问题列表副本。
// 同一毫秒创建的多条问题按创建顺序倒序，保证返回结果稳定。
func (s *Store) List() []Issue {
	s.mu.RLock()
	defer s.mu.RUnlock()

	out := make([]Issue, len(s.issues))
	copy(out, s.issues)
	sort.SliceStable(out, func(i, j int) bool {
		if out[i].CreatedAt != out[j].CreatedAt {
			return out[i].CreatedAt > out[j].CreatedAt // 时间串同为 UTC 毫秒定长，可直接字典序比较
		}
		return out[i].seq > out[j].seq
	})
	return out
}

// Create 新建问题：生成唯一 ID、强制 status=open、写入时间戳并持久化。
// 持久化成功后才更新内存，失败时内存保持原状（由调用方返回 500）。
func (s *Store) Create(in validatedIssue) (Issue, error) {
	s.mu.Lock()
	defer s.mu.Unlock()

	now := time.Now().UTC()
	issue := Issue{
		Title:       in.Title,
		Description: in.Description,
		Priority:    in.Priority,
		Status:      StatusOpen, // 契约：新建时后端固定 open，忽略客户端传值
		Position:    in.Position,
		CreatedAt:   formatTime(now),
		UpdatedAt:   formatTime(now),
	}

	// 生成不与现有 ID 重复的 ID（随机空间足够大，这里只做兜底重试）。
	for attempt := 0; attempt < 5 && issue.ID == ""; attempt++ {
		id, err := newIssueID(now)
		if err != nil {
			return Issue{}, err
		}
		if !s.hasIDLocked(id) {
			issue.ID = id
			break
		}
	}
	if issue.ID == "" {
		return Issue{}, fmt.Errorf("生成唯一问题 ID 失败")
	}
	issue.seq = s.nextSeq

	next := make([]Issue, len(s.issues)+1)
	copy(next, s.issues)
	next[len(s.issues)] = issue
	if err := s.persistLocked(next); err != nil {
		return Issue{}, err
	}
	s.issues = next
	s.nextSeq++
	return issue, nil
}

// UpdateStatus 按 ID 修改处理状态并刷新 updatedAt。
// found=false 表示 ID 不存在（由 handler 转成 404）；持久化失败时内存保持原状。
func (s *Store) UpdateStatus(id, status string) (issue Issue, found bool, err error) {
	s.mu.Lock()
	defer s.mu.Unlock()

	idx := -1
	for i := range s.issues {
		if s.issues[i].ID == id {
			idx = i
			break
		}
	}
	if idx < 0 {
		return Issue{}, false, nil
	}

	next := make([]Issue, len(s.issues))
	copy(next, s.issues)
	next[idx].Status = status
	next[idx].UpdatedAt = formatTime(time.Now())
	if err := s.persistLocked(next); err != nil {
		return Issue{}, true, err
	}
	s.issues = next
	return next[idx], true, nil
}

// hasIDLocked 判断 ID 是否已存在，调用方必须持有写锁。
func (s *Store) hasIDLocked(id string) bool {
	for i := range s.issues {
		if s.issues[i].ID == id {
			return true
		}
	}
	return false
}

// persistLocked 原子写入数据文件：建目录 -> 写临时文件 -> fsync -> rename 替换。
// 调用方必须已持有 s.mu 写锁。
func (s *Store) persistLocked(issues []Issue) error {
	dir := filepath.Dir(s.path)
	if err := os.MkdirAll(dir, 0o755); err != nil {
		return fmt.Errorf("创建数据目录 %s 失败: %w", dir, err)
	}

	data, err := json.MarshalIndent(issues, "", "  ")
	if err != nil {
		return fmt.Errorf("序列化问题数据失败: %w", err)
	}
	data = append(data, '\n')

	tmp, err := os.CreateTemp(dir, ".issues-*.tmp")
	if err != nil {
		return fmt.Errorf("创建临时数据文件失败: %w", err)
	}
	tmpPath := tmp.Name()
	// 无论成功失败都清理临时文件；rename 成功后把 tmpPath 置空，跳过删除。
	defer func() {
		_ = tmp.Close()
		if tmpPath != "" {
			_ = os.Remove(tmpPath)
		}
	}()

	if _, err := tmp.Write(data); err != nil {
		return fmt.Errorf("写入临时数据文件失败: %w", err)
	}
	if err := tmp.Sync(); err != nil {
		return fmt.Errorf("刷新临时数据文件失败: %w", err)
	}
	if err := tmp.Close(); err != nil {
		return fmt.Errorf("关闭临时数据文件失败: %w", err)
	}
	// Windows / POSIX 上 os.Rename 都会覆盖已存在的目标文件，从而实现原子替换。
	if err := os.Rename(tmpPath, s.path); err != nil {
		return fmt.Errorf("替换数据文件 %s 失败: %w", s.path, err)
	}
	tmpPath = ""
	return nil
}
