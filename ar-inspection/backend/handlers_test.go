package main

import (
	"encoding/json"
	"fmt"
	"io"
	"log"
	"net/http"
	"net/http/httptest"
	"os"
	"path/filepath"
	"regexp"
	"strings"
	"testing"
	"time"
)

var issueIDPattern = regexp.MustCompile(`^iss_\d{8}_[0-9a-f]{6}$`)

// testServer 用临时数据文件启动一个真实 httptest 服务，测试结束自动关闭。
func testServer(t *testing.T, allowOrigin string) (*httptest.Server, *Store) {
	t.Helper()
	store := NewStore(filepath.Join(t.TempDir(), "data", "issues.json"))
	if err := store.Load(); err != nil {
		t.Fatalf("Load 失败: %v", err)
	}
	ts := httptest.NewServer(NewServer(store, log.New(io.Discard, "", 0), allowOrigin).Handler())
	t.Cleanup(ts.Close)
	return ts, store
}

// httpDo 发送 HTTP 请求，返回状态码、响应体、响应头。
func httpDo(t *testing.T, ts *httptest.Server, method, path, body string, headers map[string]string) (int, []byte, http.Header) {
	t.Helper()
	var reader io.Reader
	if body != "" {
		reader = strings.NewReader(body)
	}
	req, err := http.NewRequest(method, ts.URL+path, reader)
	if err != nil {
		t.Fatalf("构造请求失败: %v", err)
	}
	if reader != nil {
		req.Header.Set("Content-Type", "application/json")
	}
	for k, v := range headers {
		req.Header.Set(k, v)
	}
	resp, err := ts.Client().Do(req)
	if err != nil {
		t.Fatalf("发送请求失败: %v", err)
	}
	defer resp.Body.Close()
	data, err := io.ReadAll(resp.Body)
	if err != nil {
		t.Fatalf("读取响应失败: %v", err)
	}
	return resp.StatusCode, data, resp.Header
}

func decodeError(t *testing.T, data []byte) errorResponse {
	t.Helper()
	var e errorResponse
	if err := json.Unmarshal(data, &e); err != nil {
		t.Fatalf("错误响应不是合法 JSON: %v（原文: %s）", err, data)
	}
	return e
}

func validIssueBody(title string) string {
	return fmt.Sprintf(
		`{"title":%q,"description":"左侧墙体存在裂缝","priority":"high","position":{"x":0.42,"y":0.03,"z":1.26}}`,
		title,
	)
}

// createIssue 用请求体新建问题并返回解析后的 Issue（断言 201）。
func createIssue(t *testing.T, ts *httptest.Server, body string) Issue {
	t.Helper()
	status, data, _ := httpDo(t, ts, http.MethodPost, "/api/issues", body, nil)
	if status != http.StatusCreated {
		t.Fatalf("期望 201，实际 %d，响应: %s", status, data)
	}
	var issue Issue
	if err := json.Unmarshal(data, &issue); err != nil {
		t.Fatalf("创建响应不是合法 Issue JSON: %v（原文: %s）", err, data)
	}
	return issue
}

func TestHealth(t *testing.T) {
	ts, _ := testServer(t, "*")
	status, data, headers := httpDo(t, ts, http.MethodGet, "/api/health", "", nil)
	if status != http.StatusOK {
		t.Fatalf("GET /api/health 期望 200，实际 %d: %s", status, data)
	}
	if ct := headers.Get("Content-Type"); ct != "application/json; charset=utf-8" {
		t.Fatalf("Content-Type 不符合契约: %q", ct)
	}
	if origin := headers.Get("Access-Control-Allow-Origin"); origin != "*" {
		t.Fatalf("默认 CORS 头应为 *，实际 %q", origin)
	}
	var health healthResponse
	if err := json.Unmarshal(data, &health); err != nil {
		t.Fatalf("健康检查响应不是合法 JSON: %v", err)
	}
	if health.Status != "ok" || health.Service != serviceName || health.Version != serviceVersion {
		t.Fatalf("健康检查字段不符合契约: %+v", health)
	}
	if _, err := time.Parse(time.RFC3339, health.Time); err != nil {
		t.Fatalf("健康检查时间不是 RFC3339: %q", health.Time)
	}
}

func TestCreateValid(t *testing.T) {
	ts, _ := testServer(t, "*")
	issue := createIssue(t, ts, validIssueBody("  入口墙面破损 "))

	if !issueIDPattern.MatchString(issue.ID) {
		t.Fatalf("id 格式不符合契约: %q", issue.ID)
	}
	if issue.Title != "入口墙面破损" {
		t.Fatalf("title 应去掉首尾空白，实际 %q", issue.Title)
	}
	if issue.Description != "左侧墙体存在裂缝" {
		t.Fatalf("description 不正确: %q", issue.Description)
	}
	if issue.Priority != PriorityHigh {
		t.Fatalf("priority 不正确: %q", issue.Priority)
	}
	if issue.Status != StatusOpen {
		t.Fatalf("新建问题 status 必须强制 open，实际 %q", issue.Status)
	}
	if issue.Position != (Position{X: 0.42, Y: 0.03, Z: 1.26}) {
		t.Fatalf("position 不正确: %+v", issue.Position)
	}
	if issue.CreatedAt != issue.UpdatedAt {
		t.Fatalf("新建问题 createdAt 应等于 updatedAt: %q vs %q", issue.CreatedAt, issue.UpdatedAt)
	}
	if _, err := time.Parse(time.RFC3339, issue.CreatedAt); err != nil {
		t.Fatalf("createdAt 不是 RFC3339: %q", issue.CreatedAt)
	}
}

func TestCreateDefaults(t *testing.T) {
	ts, _ := testServer(t, "*")
	// 只传 title + position：description 应为 ""，priority 应为 medium，status 为 open。
	issue := createIssue(t, ts, `{"title":"只有标题和位置","position":{"x":0,"y":0,"z":0}}`)
	if issue.Description != "" {
		t.Fatalf("description 缺省应为空串，实际 %q", issue.Description)
	}
	if issue.Priority != PriorityMedium {
		t.Fatalf("priority 缺省应为 medium，实际 %q", issue.Priority)
	}
	if issue.Status != StatusOpen {
		t.Fatalf("status 应为 open，实际 %q", issue.Status)
	}
	if issue.Position != (Position{X: 0, Y: 0, Z: 0}) {
		t.Fatalf("position 全 0 应合法: %+v", issue.Position)
	}
}

func TestCreateIgnoresClientStatusAndUnknownFields(t *testing.T) {
	ts, _ := testServer(t, "*")
	bodies := []string{
		`{"title":"客户端传合法状态","status":"resolved","position":{"x":1,"y":2,"z":3}}`,
		`{"title":"客户端传非法状态","status":"done","position":{"x":1,"y":2,"z":3}}`,
		`{"title":"带未知字段","foo":"bar","position":{"x":1,"y":2,"z":3}}`,
	}
	for _, body := range bodies {
		issue := createIssue(t, ts, body)
		if issue.Status != StatusOpen {
			t.Fatalf("新建时客户端 status 应被忽略，实际 %q（body: %s）", issue.Status, body)
		}
	}
}

func TestCreateValidationErrors(t *testing.T) {
	ts, _ := testServer(t, "*")
	cases := []struct {
		name      string
		body      string
		wantCode  string
		wantField string // 非空时要求 fields 中包含该字段
	}{
		{
			name:      "title 空串",
			body:      `{"title":"","position":{"x":0,"y":0,"z":0}}`,
			wantCode:  codeValidationError,
			wantField: "title",
		},
		{
			name:      "title 纯空白",
			body:      `{"title":"   ","position":{"x":0,"y":0,"z":0}}`,
			wantCode:  codeValidationError,
			wantField: "title",
		},
		{
			name:      "title 超过 200 字符",
			body:      fmt.Sprintf(`{"title":%q,"position":{"x":0,"y":0,"z":0}}`, strings.Repeat("测", MaxTitleRunes+1)),
			wantCode:  codeValidationError,
			wantField: "title",
		},
		{
			name: "description 超过 2000 字符",
			body: fmt.Sprintf(`{"title":"x","description":%q,"position":{"x":0,"y":0,"z":0}}`,
				strings.Repeat("描", MaxDescriptionRunes+1)),
			wantCode:  codeValidationError,
			wantField: "description",
		},
		{
			name:      "priority 非法枚举",
			body:      `{"title":"x","priority":"urgent","position":{"x":0,"y":0,"z":0}}`,
			wantCode:  codeValidationError,
			wantField: "priority",
		},
		{
			name:      "缺少 position",
			body:      `{"title":"x"}`,
			wantCode:  codeValidationError,
			wantField: "position",
		},
		{
			name:      "position 缺 z 分量",
			body:      `{"title":"x","position":{"x":0,"y":0}}`,
			wantCode:  codeValidationError,
			wantField: "position.z",
		},
		{
			name:     "position 分量类型不符",
			body:     `{"title":"x","position":{"x":"0","y":0,"z":0}}`,
			wantCode: codeInvalidJSON,
		},
		{
			name:     "空请求体",
			body:     "",
			wantCode: codeInvalidJSON,
		},
		{
			name:     "非法 JSON",
			body:     `{"title":`,
			wantCode: codeInvalidJSON,
		},
		{
			name:     "JSON 数组而不是对象",
			body:     `[1,2,3]`,
			wantCode: codeInvalidJSON,
		},
		{
			name:     "对象后面跟多余内容",
			body:     `{"title":"x","position":{"x":0,"y":0,"z":0}}garbage`,
			wantCode: codeInvalidJSON,
		},
	}

	for _, tc := range cases {
		t.Run(tc.name, func(t *testing.T) {
			status, data, headers := httpDo(t, ts, http.MethodPost, "/api/issues", tc.body, nil)
			if status != http.StatusBadRequest {
				t.Fatalf("期望 400，实际 %d: %s", status, data)
			}
			if ct := headers.Get("Content-Type"); ct != "application/json; charset=utf-8" {
				t.Fatalf("错误响应 Content-Type 不符合契约: %q", ct)
			}
			e := decodeError(t, data)
			if e.Code != tc.wantCode {
				t.Fatalf("错误码不正确: got=%q want=%q（message=%q）", e.Code, tc.wantCode, e.Message)
			}
			if e.Message == "" {
				t.Fatal("错误响应 message 不能为空")
			}
			if tc.wantField != "" {
				if e.Fields[tc.wantField] == "" {
					t.Fatalf("fields 应包含 %q，实际: %v", tc.wantField, e.Fields)
				}
			}
		})
	}
}

func TestListOrder(t *testing.T) {
	ts, _ := testServer(t, "*")
	first := createIssue(t, ts, validIssueBody("第一条"))
	second := createIssue(t, ts, validIssueBody("第二条"))
	third := createIssue(t, ts, validIssueBody("第三条"))

	status, data, _ := httpDo(t, ts, http.MethodGet, "/api/issues", "", nil)
	if status != http.StatusOK {
		t.Fatalf("GET /api/issues 期望 200，实际 %d: %s", status, data)
	}
	var list issueListResponse
	if err := json.Unmarshal(data, &list); err != nil {
		t.Fatalf("列表响应不是合法 JSON: %v", err)
	}
	if len(list.Items) != 3 {
		t.Fatalf("列表长度不正确: got=%d want=3", len(list.Items))
	}
	if list.Items[0].ID != third.ID || list.Items[1].ID != second.ID || list.Items[2].ID != first.ID {
		t.Fatalf("列表未按 createdAt 倒序（最新在前）: %v", []string{list.Items[0].ID, list.Items[1].ID, list.Items[2].ID})
	}
	for i := 1; i < len(list.Items); i++ {
		if list.Items[i-1].CreatedAt < list.Items[i].CreatedAt {
			t.Fatalf("createdAt 未倒序: %q 在 %q 之前", list.Items[i-1].CreatedAt, list.Items[i].CreatedAt)
		}
	}
}

func TestEmptyListShape(t *testing.T) {
	ts, _ := testServer(t, "*")
	status, data, _ := httpDo(t, ts, http.MethodGet, "/api/issues", "", nil)
	if status != http.StatusOK {
		t.Fatalf("期望 200，实际 %d: %s", status, data)
	}
	var raw map[string]json.RawMessage
	if err := json.Unmarshal(data, &raw); err != nil {
		t.Fatalf("响应不是合法 JSON: %v", err)
	}
	items, ok := raw["items"]
	if !ok {
		t.Fatalf("响应缺少 items 字段: %s", data)
	}
	if string(items) != "[]" {
		t.Fatalf("空列表必须输出 [], 实际 %s", items)
	}
}

func TestPatchStatusFlow(t *testing.T) {
	ts, _ := testServer(t, "*")
	issue := createIssue(t, ts, validIssueBody("需要改状态"))

	// 保证 updatedAt 至少跨过 1ms（契约时间精度为毫秒）。
	time.Sleep(5 * time.Millisecond)

	status, data, _ := httpDo(t, ts, http.MethodPatch, "/api/issues/"+issue.ID, `{"status":"in_progress"}`, nil)
	if status != http.StatusOK {
		t.Fatalf("PATCH 期望 200，实际 %d: %s", status, data)
	}
	var updated Issue
	if err := json.Unmarshal(data, &updated); err != nil {
		t.Fatalf("PATCH 响应不是合法 Issue JSON: %v", err)
	}
	if updated.ID != issue.ID {
		t.Fatalf("返回的 id 不一致: got=%q want=%q", updated.ID, issue.ID)
	}
	if updated.Status != StatusInProgress {
		t.Fatalf("状态未更新: %q", updated.Status)
	}
	if updated.UpdatedAt == issue.UpdatedAt {
		t.Fatalf("updatedAt 未刷新: %q", updated.UpdatedAt)
	}
	if updated.CreatedAt != issue.CreatedAt {
		t.Fatalf("createdAt 不应变化: got=%q want=%q", updated.CreatedAt, issue.CreatedAt)
	}
	if _, err := time.Parse(time.RFC3339, updated.UpdatedAt); err != nil {
		t.Fatalf("updatedAt 不是 RFC3339: %q", updated.UpdatedAt)
	}

	// PATCH 只改 status：请求里夹带其它字段必须被忽略。
	status, data, _ = httpDo(t, ts, http.MethodPatch, "/api/issues/"+issue.ID,
		`{"status":"resolved","title":"试图篡改标题"}`, nil)
	if status != http.StatusOK {
		t.Fatalf("第二次 PATCH 期望 200，实际 %d: %s", status, data)
	}
	var updated2 Issue
	if err := json.Unmarshal(data, &updated2); err != nil {
		t.Fatalf("PATCH 响应不是合法 Issue JSON: %v", err)
	}
	if updated2.Status != StatusResolved {
		t.Fatalf("状态未更新为 resolved: %q", updated2.Status)
	}
	if updated2.Title != issue.Title {
		t.Fatalf("PATCH 不应修改 title: got=%q want=%q", updated2.Title, issue.Title)
	}

	// 列表里也应是更新后的状态。
	_, listData, _ := httpDo(t, ts, http.MethodGet, "/api/issues", "", nil)
	var list issueListResponse
	if err := json.Unmarshal(listData, &list); err != nil {
		t.Fatalf("列表响应非法: %v", err)
	}
	if len(list.Items) != 1 || list.Items[0].Status != StatusResolved {
		t.Fatalf("列表未反映最新状态: %+v", list.Items)
	}
}

func TestPatchValidationErrors(t *testing.T) {
	ts, _ := testServer(t, "*")
	issue := createIssue(t, ts, validIssueBody("待改状态"))
	path := "/api/issues/" + issue.ID

	cases := []struct {
		name      string
		body      string
		wantCode  string
		wantField string
	}{
		{"缺少 status", `{}`, codeValidationError, "status"},
		{"status 空串", `{"status":""}`, codeValidationError, "status"},
		{"status 纯空白", `{"status":"  "}`, codeValidationError, "status"},
		{"status 非法枚举", `{"status":"done"}`, codeValidationError, "status"},
		{"status 大小写不匹配", `{"status":"OPEN"}`, codeValidationError, "status"},
		{"status 类型不符", `{"status":123}`, codeInvalidJSON, ""},
		{"非法 JSON", `{"status":`, codeInvalidJSON, ""},
	}
	for _, tc := range cases {
		t.Run(tc.name, func(t *testing.T) {
			status, data, _ := httpDo(t, ts, http.MethodPatch, path, tc.body, nil)
			if status != http.StatusBadRequest {
				t.Fatalf("期望 400，实际 %d: %s", status, data)
			}
			e := decodeError(t, data)
			if e.Code != tc.wantCode {
				t.Fatalf("错误码不正确: got=%q want=%q", e.Code, tc.wantCode)
			}
			if tc.wantField != "" && e.Fields[tc.wantField] == "" {
				t.Fatalf("fields 应包含 %q，实际: %v", tc.wantField, e.Fields)
			}
		})
	}

	// 校验失败不应改动状态。
	_, data, _ := httpDo(t, ts, http.MethodGet, "/api/issues", "", nil)
	var list issueListResponse
	if err := json.Unmarshal(data, &list); err != nil {
		t.Fatalf("列表响应非法: %v", err)
	}
	if len(list.Items) != 1 || list.Items[0].Status != StatusOpen {
		t.Fatalf("校验失败后状态仍应为 open: %+v", list.Items)
	}
}

func TestPatchUnknownID(t *testing.T) {
	ts, _ := testServer(t, "*")
	paths := []string{
		"/api/issues/iss_29990101_abcdef",
		"/api/issues/not-exist",
	}
	for _, path := range paths {
		status, data, _ := httpDo(t, ts, http.MethodPatch, path, `{"status":"open"}`, nil)
		if status != http.StatusNotFound {
			t.Fatalf("PATCH %s 期望 404，实际 %d: %s", path, status, data)
		}
		if e := decodeError(t, data); e.Code != codeNotFound {
			t.Fatalf("错误码应为 NOT_FOUND，实际 %q", e.Code)
		}
	}

	// 空 id / 多余斜杠同样视为不存在。
	for _, path := range []string{"/api/issues/", "/api/issues/a/b"} {
		status, data, _ := httpDo(t, ts, http.MethodPatch, path, `{"status":"open"}`, nil)
		if status != http.StatusNotFound {
			t.Fatalf("PATCH %s 期望 404，实际 %d: %s", path, status, data)
		}
	}
}

func TestMethodNotAllowed(t *testing.T) {
	ts, _ := testServer(t, "*")
	issue := createIssue(t, ts, validIssueBody("方法测试"))

	cases := []struct {
		name      string
		method    string
		path      string
		wantAllow string
	}{
		{"集合不支持 DELETE", http.MethodDelete, "/api/issues", "GET, POST, OPTIONS"},
		{"集合不支持 PUT", http.MethodPut, "/api/issues", "GET, POST, OPTIONS"},
		{"健康检查不支持 POST", http.MethodPost, "/api/health", "GET, OPTIONS"},
		{"单个问题不支持 GET", http.MethodGet, "/api/issues/" + issue.ID, "PATCH, OPTIONS"},
	}
	for _, tc := range cases {
		t.Run(tc.name, func(t *testing.T) {
			status, data, headers := httpDo(t, ts, tc.method, tc.path, "", nil)
			if status != http.StatusMethodNotAllowed {
				t.Fatalf("期望 405，实际 %d: %s", status, data)
			}
			if e := decodeError(t, data); e.Code != codeMethodNotAllowed {
				t.Fatalf("错误码应为 METHOD_NOT_ALLOWED，实际 %q", e.Code)
			}
			if allow := headers.Get("Allow"); allow != tc.wantAllow {
				t.Fatalf("Allow 头不正确: got=%q want=%q", allow, tc.wantAllow)
			}
		})
	}
}

func TestNotFound(t *testing.T) {
	ts, _ := testServer(t, "*")
	for _, path := range []string{"/api/unknown", "/", "/api/issues/a/b/c", "/api/health/"} {
		status, data, _ := httpDo(t, ts, http.MethodGet, path, "", nil)
		if status != http.StatusNotFound {
			t.Fatalf("GET %s 期望 404，实际 %d: %s", path, status, data)
		}
		if e := decodeError(t, data); e.Code != codeNotFound {
			t.Fatalf("GET %s 错误码应为 NOT_FOUND，实际 %q", path, e.Code)
		}
	}
}

func TestCORSAndPreflight(t *testing.T) {
	ts, _ := testServer(t, "*")

	// 预检请求：204 + 允许方法/头。
	status, data, headers := httpDo(t, ts, http.MethodOptions, "/api/issues", "", map[string]string{
		"Origin":                         "http://localhost:5173",
		"Access-Control-Request-Method":  "POST",
		"Access-Control-Request-Headers": "Content-Type",
	})
	if status != http.StatusNoContent {
		t.Fatalf("OPTIONS 预检期望 204，实际 %d: %s", status, data)
	}
	if len(data) != 0 {
		t.Fatalf("OPTIONS 预检响应不应有 body: %s", data)
	}
	if got := headers.Get("Access-Control-Allow-Origin"); got != "*" {
		t.Fatalf("Access-Control-Allow-Origin 不正确: %q", got)
	}
	methods := headers.Get("Access-Control-Allow-Methods")
	for _, m := range []string{"GET", "POST", "PATCH", "OPTIONS"} {
		if !strings.Contains(methods, m) {
			t.Fatalf("Access-Control-Allow-Methods 缺少 %s: %q", m, methods)
		}
	}
	if got := headers.Get("Access-Control-Allow-Headers"); !strings.Contains(got, "Content-Type") {
		t.Fatalf("Access-Control-Allow-Headers 缺少 Content-Type: %q", got)
	}

	// 普通请求也要带 CORS 头。
	_, _, headers = httpDo(t, ts, http.MethodGet, "/api/health", "", nil)
	if got := headers.Get("Access-Control-Allow-Origin"); got != "*" {
		t.Fatalf("普通响应缺少 CORS 头: %q", got)
	}

	// 配置了具体来源时，回显该来源。
	ts2, _ := testServer(t, "http://localhost:5173")
	_, _, headers = httpDo(t, ts2, http.MethodGet, "/api/health", "", nil)
	if got := headers.Get("Access-Control-Allow-Origin"); got != "http://localhost:5173" {
		t.Fatalf("ALLOW_ORIGIN 配置未生效: %q", got)
	}
}

func TestPersistenceAcrossServerRestart(t *testing.T) {
	dataFile := filepath.Join(t.TempDir(), "data", "issues.json")
	openServer := func() *httptest.Server {
		store := NewStore(dataFile)
		if err := store.Load(); err != nil {
			t.Fatalf("Load 失败: %v", err)
		}
		return httptest.NewServer(NewServer(store, log.New(io.Discard, "", 0), "*").Handler())
	}

	// 第一个"进程"创建数据后关闭。
	ts1 := openServer()
	issue := createIssue(t, ts1, validIssueBody("重启后应该还在"))
	ts1.Close()

	// 第二个"进程"用同一个 DATA_FILE 启动，应能读到数据。
	ts2 := openServer()
	t.Cleanup(ts2.Close)
	status, data, _ := httpDo(t, ts2, http.MethodGet, "/api/issues", "", nil)
	if status != http.StatusOK {
		t.Fatalf("重启后 GET 列表期望 200，实际 %d: %s", status, data)
	}
	var list issueListResponse
	if err := json.Unmarshal(data, &list); err != nil {
		t.Fatalf("列表响应非法: %v", err)
	}
	if len(list.Items) != 1 || list.Items[0].ID != issue.ID {
		t.Fatalf("重启后未读到重启前创建的问题: %+v", list.Items)
	}
}

func TestCreatePersistFailureReturns500(t *testing.T) {
	dir := t.TempDir()
	blocker := filepath.Join(dir, "blocker")
	if err := os.WriteFile(blocker, []byte("x"), 0o644); err != nil {
		t.Fatalf("准备测试文件失败: %v", err)
	}
	// 让数据文件路径经过一个普通文件，持久化必然失败。
	store := NewStore(filepath.Join(blocker, "issues.json"))
	ts := httptest.NewServer(NewServer(store, log.New(io.Discard, "", 0), "*").Handler())
	t.Cleanup(ts.Close)

	status, data, _ := httpDo(t, ts, http.MethodPost, "/api/issues", validIssueBody("写入会失败"), nil)
	if status != http.StatusInternalServerError {
		t.Fatalf("持久化失败时期望 500，实际 %d: %s", status, data)
	}
	if e := decodeError(t, data); e.Code != codeInternalError {
		t.Fatalf("错误码应为 INTERNAL_ERROR，实际 %q", e.Code)
	}
}
