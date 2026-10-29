package main

import (
	"encoding/json"
	"errors"
	"io"
	"log"
	"net/http"
	"strings"
	"time"
)

const (
	serviceName    = "ar-inspection-backend"
	serviceVersion = "0.1.0"

	// 错误码，见 docs/API.md 第 3 节。
	codeValidationError  = "VALIDATION_ERROR"
	codeInvalidJSON      = "INVALID_JSON"
	codeNotFound         = "NOT_FOUND"
	codeMethodNotAllowed = "METHOD_NOT_ALLOWED"
	codeInternalError    = "INTERNAL_ERROR"

	// maxBodyBytes 限制请求体大小，避免异常请求占用过多内存。
	maxBodyBytes = 1 << 20 // 1 MiB
)

// errorResponse 是所有 4xx/5xx 响应的统一结构；fields 可省略。
type errorResponse struct {
	Code    string            `json:"code"`
	Message string            `json:"message"`
	Fields  map[string]string `json:"fields,omitempty"`
}

// healthResponse 是 GET /api/health 的响应体。
type healthResponse struct {
	Status  string `json:"status"`
	Service string `json:"service"`
	Version string `json:"version"`
	Time    string `json:"time"`
}

// issueListResponse 是 GET /api/issues 的响应体；空列表也必须输出 []，不能是 null。
type issueListResponse struct {
	Items []Issue `json:"items"`
}

// Server 聚合 HTTP handler 依赖（store / 日志 / CORS 配置），便于 main 与 httptest 复用。
type Server struct {
	store       *Store
	logger      *log.Logger
	allowOrigin string
}

// NewServer 构造 Server。logger 为空时丢弃日志，allowOrigin 为空时回退为 *。
func NewServer(store *Store, logger *log.Logger, allowOrigin string) *Server {
	if logger == nil {
		logger = log.New(io.Discard, "", 0)
	}
	if strings.TrimSpace(allowOrigin) == "" {
		allowOrigin = "*"
	}
	return &Server{store: store, logger: logger, allowOrigin: allowOrigin}
}

// Handler 组装路由与中间件。日志中间件放在最外层，CORS 预检请求同样会被记录。
func (s *Server) Handler() http.Handler {
	mux := http.NewServeMux()
	mux.HandleFunc("/api/health", s.handleHealth)
	mux.HandleFunc("/api/issues", s.handleIssues)     // 集合：GET 列表 / POST 新建
	mux.HandleFunc("/api/issues/", s.handleIssueByID) // 单个：PATCH 改状态
	mux.HandleFunc("/", s.handleNotFound)             // 兜底：未知路径统一 404 JSON
	return withLogging(s.logger, withCORS(s.allowOrigin, mux))
}

// ===== 中间件 =====

// withCORS 为所有响应加 CORS 头，并直接响应 OPTIONS 预检请求（204）。
func withCORS(allowOrigin string, next http.Handler) http.Handler {
	return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		h := w.Header()
		h.Set("Access-Control-Allow-Origin", allowOrigin)
		h.Set("Access-Control-Allow-Methods", "GET, POST, PATCH, OPTIONS")
		h.Set("Access-Control-Allow-Headers", "Content-Type")
		h.Set("Access-Control-Max-Age", "600")
		h.Add("Vary", "Origin")
		if r.Method == http.MethodOptions {
			w.WriteHeader(http.StatusNoContent)
			return
		}
		next.ServeHTTP(w, r)
	})
}

// statusRecorder 记录实际写出的状态码，供请求日志使用。
type statusRecorder struct {
	http.ResponseWriter
	status int
}

func (r *statusRecorder) WriteHeader(code int) {
	if r.status == 0 {
		r.status = code
	}
	r.ResponseWriter.WriteHeader(code)
}

func (r *statusRecorder) Write(b []byte) (int, error) {
	if r.status == 0 {
		r.status = http.StatusOK
	}
	return r.ResponseWriter.Write(b)
}

// withLogging 输出简单请求日志：方法 / 路径 / 状态码 / 耗时。
func withLogging(logger *log.Logger, next http.Handler) http.Handler {
	return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		start := time.Now()
		rec := &statusRecorder{ResponseWriter: w}
		next.ServeHTTP(rec, r)
		status := rec.status
		if status == 0 {
			status = http.StatusOK
		}
		logger.Printf("%s %s %d %s", r.Method, r.URL.RequestURI(), status, time.Since(start).Round(time.Microsecond))
	})
}

// ===== Handler =====

// handleHealth 处理 GET /api/health。
func (s *Server) handleHealth(w http.ResponseWriter, r *http.Request) {
	if r.Method != http.MethodGet {
		writeMethodNotAllowed(w, "GET, OPTIONS")
		return
	}
	writeJSON(w, http.StatusOK, healthResponse{
		Status:  "ok",
		Service: serviceName,
		Version: serviceVersion,
		Time:    formatTime(time.Now()),
	})
}

// handleIssues 处理 /api/issues 集合：GET 列表、POST 新建。
func (s *Server) handleIssues(w http.ResponseWriter, r *http.Request) {
	switch r.Method {
	case http.MethodGet:
		s.handleListIssues(w, r)
	case http.MethodPost:
		s.handleCreateIssue(w, r)
	default:
		writeMethodNotAllowed(w, "GET, POST, OPTIONS")
	}
}

// handleIssueByID 处理 /api/issues/{id}：PATCH 修改处理状态。
func (s *Server) handleIssueByID(w http.ResponseWriter, r *http.Request) {
	id := strings.TrimPrefix(r.URL.Path, "/api/issues/")
	// 空 id 或多余的 / 都视为不存在，避免把 /api/issues/a/b 误当成 id 为 a/b。
	if id == "" || strings.Contains(id, "/") {
		writeError(w, http.StatusNotFound, codeNotFound, "问题不存在", nil)
		return
	}
	switch r.Method {
	case http.MethodPatch:
		s.handlePatchIssue(w, r, id)
	default:
		writeMethodNotAllowed(w, "PATCH, OPTIONS")
	}
}

// handleListIssues 处理 GET /api/issues：按 createdAt 倒序返回全部问题。
func (s *Server) handleListIssues(w http.ResponseWriter, r *http.Request) {
	items := s.store.List()
	if items == nil {
		items = []Issue{} // 保证序列化为 [] 而不是 null
	}
	writeJSON(w, http.StatusOK, issueListResponse{Items: items})
}

// handleCreateIssue 处理 POST /api/issues：校验 -> 生成 Issue -> 持久化 -> 201。
func (s *Server) handleCreateIssue(w http.ResponseWriter, r *http.Request) {
	var req createIssueRequest
	if !decodeJSONBody(w, r, &req) {
		return
	}

	in, fields := validateCreate(req)
	if len(fields) > 0 {
		writeError(w, http.StatusBadRequest, codeValidationError, validationMessage(fields), fields)
		return
	}

	issue, err := s.store.Create(in)
	if err != nil {
		s.logger.Printf("保存问题失败: %v", err)
		writeError(w, http.StatusInternalServerError, codeInternalError, "保存问题失败，请稍后重试", nil)
		return
	}
	writeJSON(w, http.StatusCreated, issue)
}

// handlePatchIssue 处理 PATCH /api/issues/{id}：只接受合法 status 枚举。
func (s *Server) handlePatchIssue(w http.ResponseWriter, r *http.Request, id string) {
	var req updateStatusRequest
	if !decodeJSONBody(w, r, &req) {
		return
	}

	status, fields := validateStatus(req.Status)
	if len(fields) > 0 {
		writeError(w, http.StatusBadRequest, codeValidationError, validationMessage(fields), fields)
		return
	}

	issue, found, err := s.store.UpdateStatus(id, status)
	if err != nil {
		s.logger.Printf("更新问题 %s 状态失败: %v", id, err)
		writeError(w, http.StatusInternalServerError, codeInternalError, "更新问题状态失败，请稍后重试", nil)
		return
	}
	if !found {
		writeError(w, http.StatusNotFound, codeNotFound, "问题不存在", nil)
		return
	}
	writeJSON(w, http.StatusOK, issue)
}

// handleNotFound 兜底未知路径，统一返回 404 JSON。
func (s *Server) handleNotFound(w http.ResponseWriter, r *http.Request) {
	writeError(w, http.StatusNotFound, codeNotFound, "接口不存在", nil)
}

// ===== 请求/响应辅助 =====

// decodeJSONBody 解析 JSON 请求体；失败时已写出 400 INVALID_JSON，返回 false。
// 限制请求体大小，并拒绝"一个 JSON 对象后面还跟多余内容"的请求。
func decodeJSONBody(w http.ResponseWriter, r *http.Request, dst any) bool {
	r.Body = http.MaxBytesReader(w, r.Body, maxBodyBytes)
	dec := json.NewDecoder(r.Body)
	if err := dec.Decode(dst); err != nil {
		writeError(w, http.StatusBadRequest, codeInvalidJSON, invalidJSONMessage(err), nil)
		return false
	}
	if err := dec.Decode(&struct{}{}); !errors.Is(err, io.EOF) {
		writeError(w, http.StatusBadRequest, codeInvalidJSON, "请求体必须是单个合法 JSON 对象", nil)
		return false
	}
	return true
}

// invalidJSONMessage 把底层解码错误转成可直接展示的中文提示。
func invalidJSONMessage(err error) string {
	var syntaxErr *json.SyntaxError
	if errors.As(err, &syntaxErr) {
		return "请求体不是合法 JSON"
	}
	var typeErr *json.UnmarshalTypeError
	if errors.As(err, &typeErr) {
		if typeErr.Field != "" {
			return "字段 " + typeErr.Field + " 类型不正确"
		}
		return "请求体字段类型不正确"
	}
	var maxBytesErr *http.MaxBytesError
	if errors.As(err, &maxBytesErr) {
		return "请求体过大"
	}
	if errors.Is(err, io.EOF) {
		return "请求体不能为空"
	}
	return "请求体不是合法 JSON"
}

// validationOrder 用于生成稳定的 message（优先展示更重要的字段错误）。
var validationOrder = []string{
	"title", "description", "priority",
	"position", "position.x", "position.y", "position.z",
	"status",
}

// validationMessage 从字段错误中挑一条拼成 message（fields 里仍会返回全部错误）。
func validationMessage(fields map[string]string) string {
	for _, key := range validationOrder {
		if msg, ok := fields[key]; ok {
			return key + " " + msg
		}
	}
	for key, msg := range fields {
		return key + " " + msg
	}
	return "请求参数不合法"
}

// writeJSON 输出 JSON 响应；Content-Type 按契约固定带 charset。
func writeJSON(w http.ResponseWriter, status int, body any) {
	w.Header().Set("Content-Type", "application/json; charset=utf-8")
	w.WriteHeader(status)
	if body == nil {
		return
	}
	_ = json.NewEncoder(w).Encode(body)
}

// writeError 输出统一错误结构。
func writeError(w http.ResponseWriter, status int, code, message string, fields map[string]string) {
	writeJSON(w, status, errorResponse{Code: code, Message: message, Fields: fields})
}

// writeMethodNotAllowed 输出 405，并带上 Allow 头说明该资源支持的方法。
func writeMethodNotAllowed(w http.ResponseWriter, allow string) {
	w.Header().Set("Allow", allow)
	writeError(w, http.StatusMethodNotAllowed, codeMethodNotAllowed, "请求方法不支持", nil)
}
