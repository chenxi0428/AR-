# AR 巡检原型 — 三端 API 契约（v1，初版冻结）

> 本文件是 Go 后端、React 管理端、Unity 移动端共同遵守的唯一接口约定。
> 任何一端需要改字段/路径，先改本文件，再同步三端。

## 0. 基本信息

- 后端 Base URL：`http://<PC-IP>:8080`（本机调试 `http://127.0.0.1:8080`）
- 传输：HTTP/1.1，请求与响应均 `Content-Type: application/json; charset=utf-8`
- 编码：UTF-8；时间：RFC 3339（UTC，带毫秒，例 `2026-10-29T05:01:02.123Z`）
- 鉴权：初版无
- CORS：允许任意来源（配置 `ALLOW_ORIGIN`，默认 `*`），允许方法 `GET, POST, PATCH, OPTIONS`，允许头 `Content-Type`
- 端口/数据文件可用环境变量覆盖：`PORT=8080`、`DATA_FILE=./data/issues.json`

## 1. 数据模型 Issue

```json
{
  "id": "iss_20261029_7f3a2c",
  "title": "入口墙面破损",
  "description": "左侧墙体存在裂缝",
  "priority": "high",
  "status": "open",
  "position": { "x": 0.42, "y": 0.03, "z": 1.26 },
  "createdAt": "2026-10-29T05:01:02.123Z",
  "updatedAt": "2026-10-29T05:01:02.123Z"
}
```

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| id | string | 后端生成，唯一，只读 |
| title | string | 必填，去首尾空白后 1–200 字符 |
| description | string | 可选，≤2000 字符，默认 `""` |
| priority | enum | `low` / `medium` / `high`，缺省 `medium` |
| status | enum | `open` / `in_progress` / `resolved`，新建时后端固定为 `open`，客户端传了也忽略 |
| position | object | `x`/`y`/`z` 三个有限数字（Unity 世界坐标） |
| createdAt | string | 后端生成 |
| updatedAt | string | 后端生成，任何修改后刷新 |

## 2. 接口列表

### 2.1 健康检查

`GET /api/health` → `200`

```json
{ "status": "ok", "service": "ar-inspection-backend", "version": "0.1.0", "time": "2026-10-29T05:01:02.123Z" }
```

### 2.2 问题列表

`GET /api/issues` → `200`

```json
{ "items": [ Issue, Issue, ... ] }
```

- 按 `createdAt` 倒序（最新在前），空列表返回 `{"items":[]}`。

### 2.3 上报问题（Unity 调用）

`POST /api/issues` → `201`

请求体：

```json
{
  "title": "入口墙面破损",
  "description": "左侧墙体存在裂缝",
  "priority": "high",
  "position": { "x": 0.42, "y": 0.03, "z": 1.26 }
}
```

- 校验失败 → `400`，`code = VALIDATION_ERROR`，`fields` 指明具体字段；
- 成功 → `201`，返回完整 Issue（含 `id`、`status=open`、时间戳）。

### 2.4 修改处理状态（React 调用）

`PATCH /api/issues/{id}` → `200`

请求体：

```json
{ "status": "in_progress" }
```

- `status` 必填，枚举 `open` / `in_progress` / `resolved`；
- 非法枚举 → `400 VALIDATION_ERROR`；
- 未知 id → `404 NOT_FOUND`；
- 成功 → `200`，返回更新后的完整 Issue（`updatedAt` 已刷新）。

## 3. 错误响应格式

所有 4xx/5xx 统一返回：

```json
{
  "code": "VALIDATION_ERROR",
  "message": "title 不能为空",
  "fields": { "title": "不能为空" }
}
```

`fields` 可省略；`message` 用于 UI 直接展示（中文）。

| HTTP | code | 场景 |
| --- | --- | --- |
| 400 | VALIDATION_ERROR | 字段缺失/非法（title 空、priority 枚举错误、status 枚举错误、position 非法） |
| 400 | INVALID_JSON | 请求体不是合法 JSON 或类型不符 |
| 404 | NOT_FOUND | 问题 id 不存在 / 路径不存在 |
| 405 | METHOD_NOT_ALLOWED | 方法不支持 |
| 500 | INTERNAL_ERROR | 服务内部错误（如持久化失败） |

## 4. 三端约定

### Go 后端
- 仅标准库；`net/http` + `encoding/json`；单文件 JSON 持久化（原子写：临时文件 + rename），带互斥锁。
- 启动时读取 `DATA_FILE`，不存在则视为空列表；写入失败返回 500 并记录日志。
- 目录：`backend/`，入口 `backend/main.go`，`go run .` 启动。

### React 管理端
- 开发期通过 `VITE_API_BASE`（默认 `http://127.0.0.1:8080`）直连后端，依赖后端 CORS。
- 页面需展示：标题、优先级、状态；可把状态改为 `open/in_progress/resolved`；后端不可用时显示错误提示而不是白屏。
- 目录：`web/`，`npm install && npm run dev`（默认 http://127.0.0.1:5173）。

### Unity 移动端（Android + ARCore）
- 后端地址在场景 `AppConfig` 组件上配置（默认 `http://127.0.0.1:8080`，真机改为 PC 局域网 IP，例如 `http://192.168.1.20:8080`）。
- 流程：识别水平平面 → 点击平面放置标记 → 弹出表单（标题必填/描述/优先级）→ 提交 POST → 成功/失败提示；点击 UI 不得误放标记。
- 目录：`unity/`（Unity 6000.0 LTS，AR Foundation 6.0.x + ARCore 6.0.x）。
