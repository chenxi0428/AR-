package main

import (
	"encoding/json"
	"math"
	"regexp"
	"strings"
	"testing"
	"time"
)

func f64Ptr(v float64) *float64 { return &v }

// validCreateReq 返回一份最小合法的新建请求，各用例按需覆盖字段。
func validCreateReq() createIssueRequest {
	return createIssueRequest{
		Title: "入口墙面破损",
		Position: &positionReq{
			X: f64Ptr(0.42),
			Y: f64Ptr(1.26),
			Z: f64Ptr(-0.03),
		},
	}
}

func TestFormatTime(t *testing.T) {
	// 契约要求固定三位毫秒 + Z 结尾；纳秒部分应截断，时区必须转成 UTC。
	got := formatTime(time.Date(2026, 10, 29, 5, 1, 2, 123456789, time.UTC))
	if want := "2026-10-29T05:01:02.123Z"; got != want {
		t.Fatalf("formatTime 结果不正确: got=%q want=%q", got, want)
	}
	cst := time.FixedZone("CST", 8*3600)
	got = formatTime(time.Date(2026, 10, 29, 13, 1, 2, 123000000, cst))
	if want := "2026-10-29T05:01:02.123Z"; got != want {
		t.Fatalf("非 UTC 时区格式化不正确: got=%q want=%q", got, want)
	}
	// 三端都按 RFC 3339 解析，必须能被标准库解析回来。
	if _, err := time.Parse(time.RFC3339, got); err != nil {
		t.Fatalf("时间串不能被 RFC3339 解析: %v", err)
	}
}

func TestNewIssueID(t *testing.T) {
	// 契约示例：iss_20261029_7f3a2c（日期 + 6 位小写十六进制）。
	pattern := regexp.MustCompile(`^iss_\d{8}_[0-9a-f]{6}$`)
	at := time.Date(2026, 10, 29, 5, 1, 2, 0, time.UTC)
	seen := make(map[string]bool, 200)
	for i := 0; i < 200; i++ {
		id, err := newIssueID(at)
		if err != nil {
			t.Fatalf("生成 ID 失败: %v", err)
		}
		if !pattern.MatchString(id) {
			t.Fatalf("ID 格式不符合契约: %q", id)
		}
		if !strings.HasPrefix(id, "iss_20261029_") {
			t.Fatalf("ID 日期部分不正确: %q", id)
		}
		if seen[id] {
			t.Fatalf("生成了重复 ID: %q", id)
		}
		seen[id] = true
	}
}

func TestIssueJSONShape(t *testing.T) {
	// Issue 的 JSON 字段必须与 docs/API.md 第 1 节完全一致（不能多也不能少）。
	issue := Issue{
		ID:          "iss_20261029_7f3a2c",
		Title:       "入口墙面破损",
		Description: "左侧墙体存在裂缝",
		Priority:    PriorityHigh,
		Status:      StatusOpen,
		Position:    Position{X: 0.42, Y: 0.03, Z: 1.26},
		CreatedAt:   "2026-10-29T05:01:02.123Z",
		UpdatedAt:   "2026-10-29T05:01:02.123Z",
	}
	data, err := json.Marshal(issue)
	if err != nil {
		t.Fatalf("序列化 Issue 失败: %v", err)
	}
	var m map[string]any
	if err := json.Unmarshal(data, &m); err != nil {
		t.Fatalf("反序列化 Issue 失败: %v", err)
	}
	wantKeys := []string{"id", "title", "description", "priority", "status", "position", "createdAt", "updatedAt"}
	if len(m) != len(wantKeys) {
		t.Fatalf("Issue JSON 字段数量不正确: got=%d want=%d (%s)", len(m), len(wantKeys), data)
	}
	for _, k := range wantKeys {
		if _, ok := m[k]; !ok {
			t.Fatalf("Issue JSON 缺少字段 %q: %s", k, data)
		}
	}
	pos, ok := m["position"].(map[string]any)
	if !ok || len(pos) != 3 {
		t.Fatalf("position 必须是只含 x/y/z 的对象: %s", data)
	}
	for _, k := range []string{"x", "y", "z"} {
		if _, ok := pos[k]; !ok {
			t.Fatalf("position 缺少字段 %q: %s", k, data)
		}
	}
}

func TestValidateCreate(t *testing.T) {
	hundredChinese := strings.Repeat("墙", 100) // 100 个字符 / 300 字节
	longTitle := strings.Repeat("测", MaxTitleRunes)
	tooLongTitle := strings.Repeat("测", MaxTitleRunes+1)
	longDesc := strings.Repeat("描", MaxDescriptionRunes)
	tooLongDesc := strings.Repeat("描", MaxDescriptionRunes+1)

	tests := []struct {
		name       string
		mutate     func(*createIssueRequest)
		wantFields map[string]string // nil 表示应校验通过
		want       *validatedIssue   // 校验通过时对归一化结果的额外断言
	}{
		{
			name:   "最小合法请求补默认值",
			mutate: func(*createIssueRequest) {},
			want: &validatedIssue{
				Title:    "入口墙面破损",
				Priority: PriorityMedium,
				Position: Position{X: 0.42, Y: 1.26, Z: -0.03},
			},
		},
		{
			name:   "title 去首尾空白后保存",
			mutate: func(r *createIssueRequest) { r.Title = "  入口墙面破损 \n" },
			want: &validatedIssue{
				Title:    "入口墙面破损",
				Priority: PriorityMedium,
				Position: Position{X: 0.42, Y: 1.26, Z: -0.03},
			},
		},
		{
			name:       "title 空串",
			mutate:     func(r *createIssueRequest) { r.Title = "" },
			wantFields: map[string]string{"title": "不能为空"},
		},
		{
			name:       "title 纯空白",
			mutate:     func(r *createIssueRequest) { r.Title = " \t\n " },
			wantFields: map[string]string{"title": "不能为空"},
		},
		{
			name:       "title 正好 200 字符通过",
			mutate:     func(r *createIssueRequest) { r.Title = longTitle },
			wantFields: nil,
		},
		{
			name:       "title 201 字符失败",
			mutate:     func(r *createIssueRequest) { r.Title = tooLongTitle },
			wantFields: map[string]string{"title": "不能超过 200 个字符"},
		},
		{
			name:   "title 按字符数而非字节数计（100 中文=300 字节）",
			mutate: func(r *createIssueRequest) { r.Title = hundredChinese },
			want: &validatedIssue{
				Title:    hundredChinese,
				Priority: PriorityMedium,
				Position: Position{X: 0.42, Y: 1.26, Z: -0.03},
			},
		},
		{
			name:       "description 正好 2000 字符通过",
			mutate:     func(r *createIssueRequest) { r.Description = longDesc },
			wantFields: nil,
		},
		{
			name:       "description 2001 字符失败",
			mutate:     func(r *createIssueRequest) { r.Description = tooLongDesc },
			wantFields: map[string]string{"description": "不能超过 2000 个字符"},
		},
		{
			name:   "priority high 合法",
			mutate: func(r *createIssueRequest) { r.Priority = PriorityHigh },
			want: &validatedIssue{
				Title:    "入口墙面破损",
				Priority: PriorityHigh,
				Position: Position{X: 0.42, Y: 1.26, Z: -0.03},
			},
		},
		{
			name:   "priority 带空白 trim 后合法",
			mutate: func(r *createIssueRequest) { r.Priority = " low " },
			want: &validatedIssue{
				Title:    "入口墙面破损",
				Priority: PriorityLow,
				Position: Position{X: 0.42, Y: 1.26, Z: -0.03},
			},
		},
		{
			name:   "priority 空串按缺省 medium",
			mutate: func(r *createIssueRequest) { r.Priority = "" },
			want: &validatedIssue{
				Title:    "入口墙面破损",
				Priority: PriorityMedium,
				Position: Position{X: 0.42, Y: 1.26, Z: -0.03},
			},
		},
		{
			name:       "priority 非法枚举",
			mutate:     func(r *createIssueRequest) { r.Priority = "urgent" },
			wantFields: map[string]string{"priority": "只能是 low、medium 或 high"},
		},
		{
			name:       "priority 大小写不匹配（枚举必须小写）",
			mutate:     func(r *createIssueRequest) { r.Priority = "HIGH" },
			wantFields: map[string]string{"priority": "只能是 low、medium 或 high"},
		},
		{
			name:       "position 缺失",
			mutate:     func(r *createIssueRequest) { r.Position = nil },
			wantFields: map[string]string{"position": "不能为空"},
		},
		{
			name:       "position 缺 x",
			mutate:     func(r *createIssueRequest) { r.Position.X = nil },
			wantFields: map[string]string{"position.x": "不能为空"},
		},
		{
			name:       "position x 为 NaN",
			mutate:     func(r *createIssueRequest) { r.Position.X = f64Ptr(math.NaN()) },
			wantFields: map[string]string{"position.x": "必须是有限数字"},
		},
		{
			name:       "position y 为 Inf",
			mutate:     func(r *createIssueRequest) { r.Position.Y = f64Ptr(math.Inf(1)) },
			wantFields: map[string]string{"position.y": "必须是有限数字"},
		},
		{
			name: "position 全为 0 合法",
			mutate: func(r *createIssueRequest) {
				r.Position = &positionReq{X: f64Ptr(0), Y: f64Ptr(0), Z: f64Ptr(0)}
			},
			want: &validatedIssue{
				Title:    "入口墙面破损",
				Priority: PriorityMedium,
				Position: Position{X: 0, Y: 0, Z: 0},
			},
		},
		{
			name: "多个字段错误同时返回",
			mutate: func(r *createIssueRequest) {
				r.Title = "  "
				r.Priority = "urgent"
				r.Position = nil
			},
			wantFields: map[string]string{
				"title":    "不能为空",
				"priority": "只能是 low、medium 或 high",
				"position": "不能为空",
			},
		},
	}

	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			req := validCreateReq()
			tt.mutate(&req)
			got, fields := validateCreate(req)

			if tt.wantFields == nil {
				if len(fields) != 0 {
					t.Fatalf("期望校验通过，实际返回错误: %v", fields)
				}
				if tt.want != nil && got != *tt.want {
					t.Fatalf("归一化结果不正确:\n got=%+v\nwant=%+v", got, *tt.want)
				}
				return
			}
			if len(fields) == 0 {
				t.Fatalf("期望校验失败，实际通过: %+v", got)
			}
			for k, wantMsg := range tt.wantFields {
				if fields[k] != wantMsg {
					t.Fatalf("字段 %q 错误信息不正确: got=%q want=%q（全部 fields: %v）", k, fields[k], wantMsg, fields)
				}
			}
		})
	}
}

func TestValidateStatus(t *testing.T) {
	tests := []struct {
		name    string
		raw     string
		want    string
		wantErr bool
	}{
		{"open 合法", "open", "open", false},
		{"in_progress 合法", "in_progress", "in_progress", false},
		{"resolved 合法", "resolved", "resolved", false},
		{"带首尾空白合法", "  resolved ", "resolved", false},
		{"空串必填", "", "", true},
		{"纯空白必填", "   ", "", true},
		{"非法枚举", "done", "done", true},
		{"大小写不匹配", "OPEN", "OPEN", true},
	}
	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			got, fields := validateStatus(tt.raw)
			if tt.wantErr {
				if len(fields) == 0 {
					t.Fatalf("期望校验失败，实际通过")
				}
				if fields["status"] == "" {
					t.Fatalf("fields 必须指明 status 字段: %v", fields)
				}
				return
			}
			if len(fields) != 0 {
				t.Fatalf("期望校验通过，实际返回错误: %v", fields)
			}
			if got != tt.want {
				t.Fatalf("status 归一化不正确: got=%q want=%q", got, tt.want)
			}
		})
	}
}
