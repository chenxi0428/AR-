package main

import (
	"encoding/json"
	"fmt"
	"os"
	"path/filepath"
	"strings"
	"sync"
	"testing"
)

// newTestStore 在临时目录创建一个已 Load 的空 Store。
func newTestStore(t *testing.T) *Store {
	t.Helper()
	store := NewStore(filepath.Join(t.TempDir(), "data", "issues.json"))
	if err := store.Load(); err != nil {
		t.Fatalf("Load 空数据文件不应失败: %v", err)
	}
	return store
}

func testValidatedIssue(title string) validatedIssue {
	return validatedIssue{
		Title:    title,
		Priority: PriorityMedium,
		Position: Position{X: 0.1, Y: 0.2, Z: 0.3},
	}
}

func TestStoreMissingFileIsEmpty(t *testing.T) {
	store := newTestStore(t)
	list := store.List()
	if len(list) != 0 {
		t.Fatalf("数据文件不存在时应为空列表，实际 %d 条", len(list))
	}
	if list == nil {
		t.Fatal("List 不能返回 nil，否则 JSON 会输出 null 而不是 []")
	}
}

func TestStoreCreateListSortedDesc(t *testing.T) {
	store := newTestStore(t)
	titles := []string{"第一条", "第二条", "第三条"}
	created := make([]Issue, 0, len(titles))
	for _, title := range titles {
		issue, err := store.Create(testValidatedIssue(title))
		if err != nil {
			t.Fatalf("Create(%s) 失败: %v", title, err)
		}
		if issue.Status != StatusOpen {
			t.Fatalf("新建问题状态必须是 open，实际 %q", issue.Status)
		}
		if issue.ID == "" || issue.CreatedAt == "" || issue.UpdatedAt == "" {
			t.Fatalf("新建问题缺少后端生成字段: %+v", issue)
		}
		created = append(created, issue)
	}

	list := store.List()
	if len(list) != len(titles) {
		t.Fatalf("列表长度不正确: got=%d want=%d", len(list), len(titles))
	}
	// 最新创建的问题必须排最前（同一毫秒内创建时依赖 seq 兜底，结果同样稳定）。
	for i := range list {
		wantID := created[len(created)-1-i].ID
		if list[i].ID != wantID {
			t.Fatalf("第 %d 条不是最新优先的顺序: got=%s want=%s", i, list[i].ID, wantID)
		}
	}
	// createdAt 必须非递增。
	for i := 1; i < len(list); i++ {
		if list[i-1].CreatedAt < list[i].CreatedAt {
			t.Fatalf("列表未按 createdAt 倒序: %q 在 %q 之前", list[i-1].CreatedAt, list[i].CreatedAt)
		}
	}
}

func TestStorePersistAndReload(t *testing.T) {
	store := newTestStore(t)
	first, err := store.Create(testValidatedIssue("入口墙面破损"))
	if err != nil {
		t.Fatalf("Create 失败: %v", err)
	}
	second, err := store.Create(testValidatedIssue("消防通道堵塞"))
	if err != nil {
		t.Fatalf("Create 失败: %v", err)
	}

	updated, found, err := store.UpdateStatus(first.ID, StatusInProgress)
	if err != nil {
		t.Fatalf("UpdateStatus 失败: %v", err)
	}
	if !found {
		t.Fatalf("UpdateStatus 应能找到 id=%s", first.ID)
	}
	if updated.Status != StatusInProgress {
		t.Fatalf("状态更新不正确: %q", updated.Status)
	}

	// 用同一个数据文件新建 Store 并 Load，模拟服务重启后重新加载。
	reloaded := NewStore(store.Path())
	if err := reloaded.Load(); err != nil {
		t.Fatalf("重启加载失败: %v", err)
	}
	list := reloaded.List()
	if len(list) != 2 {
		t.Fatalf("重启后应加载 2 条，实际 %d 条", len(list))
	}
	if list[0].ID != second.ID || list[1].ID != first.ID {
		t.Fatalf("重启后顺序不正确: got=[%s,%s]", list[0].ID, list[1].ID)
	}
	if list[1].Status != StatusInProgress || list[1].UpdatedAt != updated.UpdatedAt {
		t.Fatalf("状态/时间戳未持久化: %+v", list[1])
	}
	if list[1].Title != "入口墙面破损" || list[1].Priority != PriorityMedium {
		t.Fatalf("字段未正确持久化: %+v", list[1])
	}
}

func TestStoreUpdateStatusUnknownID(t *testing.T) {
	store := newTestStore(t)
	_, found, err := store.UpdateStatus("iss_20260101_abcdef", StatusResolved)
	if err != nil {
		t.Fatalf("未知 ID 不应返回错误: %v", err)
	}
	if found {
		t.Fatal("未知 ID 不应 found=true")
	}
	if len(store.List()) != 0 {
		t.Fatal("未知 ID 更新不应改动数据")
	}
}

func TestStoreWritesOnlyDataFile(t *testing.T) {
	store := newTestStore(t)
	if _, err := store.Create(testValidatedIssue("问题一")); err != nil {
		t.Fatalf("Create 失败: %v", err)
	}
	dir := filepath.Dir(store.Path())
	entries, err := os.ReadDir(dir)
	if err != nil {
		t.Fatalf("读取数据目录失败: %v", err)
	}
	if len(entries) != 1 || entries[0].Name() != "issues.json" {
		names := make([]string, 0, len(entries))
		for _, e := range entries {
			names = append(names, e.Name())
		}
		t.Fatalf("原子写结束后不应残留临时文件，目录内容: %v", names)
	}

	// 数据文件必须是合法的 Issue 数组，且不能混入内存字段。
	data, err := os.ReadFile(store.Path())
	if err != nil {
		t.Fatalf("读取数据文件失败: %v", err)
	}
	var issues []Issue
	if err := json.Unmarshal(data, &issues); err != nil {
		t.Fatalf("数据文件不是合法 JSON: %v", err)
	}
	if len(issues) != 1 {
		t.Fatalf("数据文件条数不正确: %d", len(issues))
	}
	if strings.Contains(string(data), "\"seq\"") {
		t.Fatalf("数据文件不应包含内存排序字段 seq: %s", data)
	}
}

func TestStorePersistFailureKeepsMemory(t *testing.T) {
	dir := t.TempDir()
	// 用一个普通文件占住目录路径，使 MkdirAll/临时文件创建必然失败。
	blocker := filepath.Join(dir, "blocker")
	if err := os.WriteFile(blocker, []byte("x"), 0o644); err != nil {
		t.Fatalf("准备测试文件失败: %v", err)
	}
	store := NewStore(filepath.Join(blocker, "issues.json"))

	if _, err := store.Create(testValidatedIssue("写入会失败的问题")); err == nil {
		t.Fatal("期望持久化失败返回错误")
	}
	if list := store.List(); len(list) != 0 {
		t.Fatalf("持久化失败后内存不应被更新，实际 %d 条", len(list))
	}
}

func TestStoreLoadCorruptFile(t *testing.T) {
	path := filepath.Join(t.TempDir(), "issues.json")
	if err := os.WriteFile(path, []byte("{这不是合法 JSON"), 0o644); err != nil {
		t.Fatalf("准备测试文件失败: %v", err)
	}
	store := NewStore(path)
	if err := store.Load(); err == nil {
		t.Fatal("数据文件损坏时应返回错误（避免静默覆盖数据）")
	}
}

func TestStoreLoadEmptyOrNullFile(t *testing.T) {
	for _, content := range []string{"", "  \n\t", "null", "[]"} {
		t.Run(fmt.Sprintf("内容=%q", content), func(t *testing.T) {
			path := filepath.Join(t.TempDir(), "issues.json")
			if err := os.WriteFile(path, []byte(content), 0o644); err != nil {
				t.Fatalf("准备测试文件失败: %v", err)
			}
			store := NewStore(path)
			if err := store.Load(); err != nil {
				t.Fatalf("空内容应视为空列表，实际报错: %v", err)
			}
			if list := store.List(); len(list) != 0 {
				t.Fatalf("应为空列表，实际 %d 条", len(list))
			}
		})
	}
}

func TestStoreConcurrentCreate(t *testing.T) {
	store := newTestStore(t)
	const n = 20

	var wg sync.WaitGroup
	errs := make(chan error, n)
	for i := 0; i < n; i++ {
		wg.Add(1)
		go func(i int) {
			defer wg.Done()
			if _, err := store.Create(testValidatedIssue(fmt.Sprintf("并发问题 %d", i))); err != nil {
				errs <- err
			}
		}(i)
	}
	wg.Wait()
	close(errs)
	for err := range errs {
		t.Fatalf("并发 Create 失败: %v", err)
	}

	list := store.List()
	if len(list) != n {
		t.Fatalf("并发写入后条数不正确: got=%d want=%d", len(list), n)
	}
	seen := make(map[string]bool, n)
	for _, issue := range list {
		if seen[issue.ID] {
			t.Fatalf("出现重复 ID: %s", issue.ID)
		}
		seen[issue.ID] = true
	}

	// 数据文件必须始终完整可解析（并发 + 原子写）。
	reloaded := NewStore(store.Path())
	if err := reloaded.Load(); err != nil {
		t.Fatalf("并发写入后重新加载失败: %v", err)
	}
	if got := len(reloaded.List()); got != n {
		t.Fatalf("重新加载条数不正确: got=%d want=%d", got, n)
	}
}
