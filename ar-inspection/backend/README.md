# AR 巡检后端（Go，仅标准库）

为 Unity AR 移动端和 React 管理端提供问题上报 / 查询 / 状态修改接口，数据持久化到本地 JSON 文件。
接口契约以 [`../docs/API.md`](../docs/API.md) 为唯一权威，本服务严格按其实现。

## 运行

```bash
cd ar-inspection/backend
go build -o ar-inspection-backend .   # 可选：先编译
go run .                              # 默认监听 8080，数据文件 ./data/issues.json
```

> 本仓库开发机的沙箱环境需要先设置 Go 工具链环境变量，见 `../docs/DEV_ENV.md`。

可用环境变量：

| 变量 | 默认值 | 说明 |
| --- | --- | --- |
| `PORT` | `8080` | 监听端口 |
| `DATA_FILE` | `./data/issues.json` | 数据文件（不存在视为空列表，启动时自动创建目录/文件） |
| `ALLOW_ORIGIN` | `*` | CORS 允许来源 |

Windows PowerShell 示例：

```powershell
$env:PORT='8080'; $env:DATA_FILE='./data/issues.json'; $env:ALLOW_ORIGIN='*'
go run .
```

## 接口

| 方法 | 路径 | 说明 | 成功 |
| --- | --- | --- | --- |
| GET | `/api/health` | 健康检查 | `200 {status,service,version,time}` |
| GET | `/api/issues` | 问题列表，按 `createdAt` 倒序 | `200 {items:[Issue]}` |
| POST | `/api/issues` | 上报问题（Unity） | `201 Issue` |
| PATCH | `/api/issues/{id}` | 修改处理状态（React） | `200 Issue` |
| OPTIONS | 任意路径 | CORS 预检 | `204` + `Access-Control-Allow-*` |

- 错误统一为 `{code,message,fields?}`：`400 VALIDATION_ERROR` / `400 INVALID_JSON` / `404 NOT_FOUND` / `405 METHOD_NOT_ALLOWED` / `500 INTERNAL_ERROR`；
- 校验：`title` 去首尾空白后 1–200 **字符**、`description` ≤2000 字符、`priority` ∈ low/medium/high（缺省 medium）、`position.x/y/z` 必须是有限数字；新建时 `status` 强制为 `open`；`PATCH` 只接受 open/in_progress/resolved；
- 时间统一为 RFC 3339 UTC 毫秒，如 `2026-10-29T05:01:02.123Z`。

## 测试

```bash
go vet ./...
go test ./...            # 覆盖校验逻辑、store 读写/重载/并发、handler 全流程（httptest）
```

自测（真实进程 + 真实 HTTP）可在 18080 端口进行：

```powershell
$env:PORT='18080'; $env:DATA_FILE='./data/test-18080.json'; go run .
# 另开终端：GET /api/health、POST 合法与非法、PATCH、OPTIONS，重启进程后 GET 列表验证持久化
```

## 实现要点

- **单文件持久化 + 原子写**：互斥锁（`sync.RWMutex`）保护内存列表；写入同目录临时文件 → `fsync` → `rename` 原子替换，避免写一半损坏数据；读失败（JSON 损坏）时拒绝启动而不是静默清空。
- **代码结构**：`main.go`（配置/启动/优雅关闭）、`handlers.go`（路由/中间件/错误响应）、`store.go`（持久化）、`issue.go`（模型/枚举/校验）。
- **CORS**：所有响应带 `Access-Control-Allow-Origin/Methods/Headers`，OPTIONS 预检直接 204。
- **可观测**：每个请求输出「方法 路径 状态码 耗时」日志；保存失败记录 500 日志。
- **优雅关闭**：SIGINT / SIGTERM 触发 `http.Server.Shutdown`，最多等待 5 秒在途请求。
- 数据文件格式为 `Issue` JSON 数组（已忽略内存排序字段），方便人工查看与排查。
