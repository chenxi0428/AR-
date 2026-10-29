package main

import (
	"crypto/rand"
	"encoding/hex"
	"fmt"
	"math"
	"strings"
	"time"
	"unicode/utf8"
)

// ===== 枚举（必须与 docs/API.md 第 1 节保持一致）=====

const (
	StatusOpen       = "open"
	StatusInProgress = "in_progress"
	StatusResolved   = "resolved"

	PriorityLow    = "low"
	PriorityMedium = "medium"
	PriorityHigh   = "high"
)

const (
	// MaxTitleRunes 标题去首尾空白后的最大字符数（按 Unicode 字符计，不是字节数）。
	MaxTitleRunes = 200
	// MaxDescriptionRunes 描述最大字符数（同样按 Unicode 字符计）。
	MaxDescriptionRunes = 2000
)

// TimeLayout 是契约要求的时间格式：RFC 3339、UTC、固定三位毫秒。
// 例：2026-10-29T05:01:02.123Z。固定用 ".000" 而不是 RFC3339Nano，
// 是为了保证毫秒位永远存在（RFC3339Nano 会裁掉末尾的 0）。
const TimeLayout = "2006-01-02T15:04:05.000Z"

// formatTime 把任意时间统一格式化为契约要求的 UTC 毫秒时间串。
func formatTime(t time.Time) string {
	return t.UTC().Format(TimeLayout)
}

// Position 是 Unity 世界坐标（单位：米）。
type Position struct {
	X float64 `json:"x"`
	Y float64 `json:"y"`
	Z float64 `json:"z"`
}

// Issue 是问题实体，字段名与 docs/API.md 第 1 节完全一致。
type Issue struct {
	ID          string   `json:"id"`
	Title       string   `json:"title"`
	Description string   `json:"description"`
	Priority    string   `json:"priority"`
	Status      string   `json:"status"`
	Position    Position `json:"position"`
	CreatedAt   string   `json:"createdAt"`
	UpdatedAt   string   `json:"updatedAt"`

	// seq 只在内存中用于排序兜底：同一毫秒内创建的多条问题，
	// 用创建顺序保证"后创建的排前面"，不参与 JSON 序列化。
	seq int64
}

// createIssueRequest 是 POST /api/issues 的请求体。
// position 用指针是为了区分"没传字段"（校验失败）和"传了 0"（合法坐标）。
// 注意这里刻意不接收 status：新建时后端强制为 open，客户端传了也忽略。
type createIssueRequest struct {
	Title       string       `json:"title"`
	Description string       `json:"description"`
	Priority    string       `json:"priority"`
	Position    *positionReq `json:"position"`
}

type positionReq struct {
	X *float64 `json:"x"`
	Y *float64 `json:"y"`
	Z *float64 `json:"z"`
}

// updateStatusRequest 是 PATCH /api/issues/{id} 的请求体。
type updateStatusRequest struct {
	Status string `json:"status"`
}

// validatedIssue 是校验并归一化（trim、补默认值）后的新建数据。
type validatedIssue struct {
	Title       string
	Description string
	Priority    string
	Position    Position
}

func isValidPriority(p string) bool {
	switch p {
	case PriorityLow, PriorityMedium, PriorityHigh:
		return true
	}
	return false
}

func isValidStatus(s string) bool {
	switch s {
	case StatusOpen, StatusInProgress, StatusResolved:
		return true
	}
	return false
}

// validateCreate 校验新建问题的请求体。
// 返回字段级错误，key 为字段名（例如 title、priority、position.x），值可直接展示给用户；
// 校验通过时返回归一化后的数据（title 去掉首尾空白，priority 缺省为 medium）。
func validateCreate(req createIssueRequest) (validatedIssue, map[string]string) {
	fields := map[string]string{}
	out := validatedIssue{
		Title:       strings.TrimSpace(req.Title),
		Description: req.Description,
		Priority:    strings.TrimSpace(req.Priority),
	}

	// title：去空白后 1–200 个字符。
	switch n := utf8.RuneCountInString(out.Title); {
	case n == 0:
		fields["title"] = "不能为空"
	case n > MaxTitleRunes:
		fields["title"] = fmt.Sprintf("不能超过 %d 个字符", MaxTitleRunes)
	}

	// description：可选，≤2000 个字符，缺省为空串。
	if utf8.RuneCountInString(out.Description) > MaxDescriptionRunes {
		fields["description"] = fmt.Sprintf("不能超过 %d 个字符", MaxDescriptionRunes)
	}

	// priority：缺省 medium，否则必须命中枚举。
	if out.Priority == "" {
		out.Priority = PriorityMedium
	} else if !isValidPriority(out.Priority) {
		fields["priority"] = "只能是 low、medium 或 high"
	}

	// position：必填，x/y/z 都必须是有限数字（JSON 里 0 是合法值）。
	if req.Position == nil {
		fields["position"] = "不能为空"
	} else {
		position, positionFields := validatePosition(req.Position)
		out.Position = position
		for k, v := range positionFields {
			fields[k] = v
		}
	}

	if len(fields) == 0 {
		fields = nil
	}
	return out, fields
}

// validatePosition 校验 x/y/z 三个分量：必须存在且为有限数字。
func validatePosition(req *positionReq) (Position, map[string]string) {
	fields := map[string]string{}
	var pos Position
	components := []struct {
		name string
		in   *float64
		dst  *float64
	}{
		{"x", req.X, &pos.X},
		{"y", req.Y, &pos.Y},
		{"z", req.Z, &pos.Z},
	}
	for _, c := range components {
		switch {
		case c.in == nil:
			fields["position."+c.name] = "不能为空"
		case math.IsNaN(*c.in) || math.IsInf(*c.in, 0):
			// JSON 本身无法表达 NaN/Inf，这里是内部调用的兜底校验。
			fields["position."+c.name] = "必须是有限数字"
		default:
			*c.dst = *c.in
		}
	}
	if len(fields) == 0 {
		fields = nil
	}
	return pos, fields
}

// validateStatus 校验 PATCH 请求里的 status：必填，且必须是合法枚举。
// 返回错误时 map 非 nil；通过时 map 为 nil。
func validateStatus(raw string) (string, map[string]string) {
	status := strings.TrimSpace(raw)
	if status == "" {
		return status, map[string]string{"status": "不能为空"}
	}
	if !isValidStatus(status) {
		return status, map[string]string{"status": "只能是 open、in_progress 或 resolved"}
	}
	return status, nil
}

// newIssueID 生成形如 iss_20261029_7f3a2c 的唯一 ID：日期 + 3 字节随机十六进制。
// 随机空间 2^24，Store 侧还会做一次防重复兜底。
func newIssueID(t time.Time) (string, error) {
	buf := make([]byte, 3)
	if _, err := rand.Read(buf); err != nil {
		return "", fmt.Errorf("生成随机 ID 失败: %w", err)
	}
	return fmt.Sprintf("iss_%s_%s", t.UTC().Format("20060102"), hex.EncodeToString(buf)), nil
}
