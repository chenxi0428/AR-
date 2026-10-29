# AR 巡检问题标注 · 初版（Unity AR + React + Go）

园区巡检人员用手机在 AR 现场放置标记并上报问题，管理人员在 PC 网页查看问题并更新处理状态。三端打通一条最小闭环：

```
Unity AR（Android + ARCore）放置标记并上报
   → Go 后端校验并持久化
   → React 管理端查看列表、修改处理状态
```

> 初版范围：只做任务书「必做功能」，不做复杂架构与精美界面；加分项留作后续。

---

## 1. 目录结构

```
ar-inspection/
├── backend/          Go 后端（仅标准库，JSON 文件持久化）
│   ├── main.go       配置 / 启动 / 优雅关闭
│   ├── handlers.go   路由 / CORS / 统一错误 / 请求日志
│   ├── issue.go      数据模型 / 枚举 / 校验 / ID 与时间生成
│   ├── store.go      JSON 文件存储（互斥锁 + 原子写）
│   ├── *_test.go     单测（校验 / 持久化 / httptest 全流程）
│   └── README.md     后端运行与接口说明
├── web/              React 管理端（Vite + TypeScript）
│   └── src/          api.ts（契约封装）/ App.tsx / components/ / styles.css
├── unity/            Unity 移动端（Android + ARCore，AR Foundation 6）
│   ├── Assets/Scripts/    AR 放置标记、运行时 uGUI 表单、API 客户端
│   ├── Assets/Editor/     一键 Setup 菜单（切 Android / 启用 ARCore / 生成场景）
│   ├── Packages/manifest.json
│   └── README.md     Unity 打开 / 构建 / 真机步骤 + 手动兜底
├── docs/
│   ├── API.md        三端接口契约（唯一权威）
│   └── screenshots/  管理端实测截图
├── AI_LOG.md         AI 使用记录（任务书第 5 节要求）
└── README.md         本文件
```

## 2. 技术栈与版本

| 部分 | 版本 / 选型 |
| --- | --- |
| 移动端 | Unity **6000.0 LTS**（`6000.0.32f1`）+ C# + **AR Foundation 6.0.5** + **ARCore 6.0.5** + XR Plug-in Management 4.5.0；平台 **Android + ARCore**（ARM64 / IL2CPP / minSdk 24） |
| 管理端 | React **19.3.1** + TypeScript **5.9.3** + Vite **8.3.5**（Rolldown 内核；无 UI 框架，纯 CSS）；需要 **Node.js ≥ 20.19 或 ≥ 22.12** |
| 后端 | **Go 1.22+**（仅标准库，零第三方依赖）；数据存本地 JSON 文件（任务书允许 SQLite 或本地 JSON） |
| 接口 | REST + JSON，契约见 [`docs/API.md`](docs/API.md) |

## 3. 快速开始

### 3.1 启动 Go 后端（默认 8080）

```bash
cd backend
go run .

# 可选环境变量
# PORT=8080           监听端口
# DATA_FILE=./data/issues.json   数据文件（默认，目录会被自动创建）
# ALLOW_ORIGIN=*       CORS 允许来源
```

验证：

```bash
curl http://127.0.0.1:8080/api/health
# {"status":"ok","service":"ar-inspection-backend","version":"0.1.0","time":"..."}

# 上报一条（等价于 Unity 端的行为）
curl -X POST http://127.0.0.1:8080/api/issues \
  -H "Content-Type: application/json" \
  -d '{"title":"入口墙面破损","description":"左侧墙体存在裂缝","priority":"high","position":{"x":0.42,"y":0.03,"z":1.26}}'
```

跑测试：`cd backend && go vet ./... && go test ./...`

### 3.2 启动 React 管理端（默认 5173）

```bash
cd web
# 需要 Node.js >= 20.19 或 >= 22.12（Vite 8 要求）
npm install          # 若装依赖时报脚本权限问题，可加 --ignore-scripts
npm run dev -- --host 0.0.0.0 --port 5173
```

- 浏览器打开 `http://127.0.0.1:5173`
- 后端不在本机或不是 8080 时，用环境变量覆盖：
  - Windows PowerShell：`$env:VITE_API_BASE='http://192.168.1.20:8080'; npm run dev`
  - bash：`VITE_API_BASE=http://192.168.1.20:8080 npm run dev`
- 生产构建：`npm run build`（产物在 `web/dist/`）

### 3.3 打开 / 构建 Unity 移动端

1. Unity Hub 安装 **Unity 6000.0 LTS**，勾选 **Android Build Support**（含 SDK / NDK / OpenJDK）。
2. 用 Unity Hub 打开 `unity/` 目录。
3. 菜单 **Tools → AR Inspection → Setup Android + ARCore Project**：
   切换 Android 平台、启用 ARCore、创建 `Assets/Scenes/Main.unity` 并加入 Build Settings。
4. 选中场景中的 `AppConfig` 物体，把 **Server Url** 改成 PC 的局域网地址，例如 `http://192.168.1.20:8080`
   （真机不能用 `127.0.0.1`；PC 防火墙需放行 TCP 8080，手机与 PC 在同一局域网）。
5. 手机开启「开发者选项 + USB 调试」，Unity 中 **Build And Run**（ARM64 / IL2CPP）。

详细步骤、手动兜底（一键 Setup 失败时如何手工搭场景）见 [`unity/README.md`](unity/README.md)。

## 4. 功能对照任务书

| 任务书要求 | 状态 | 实现位置 |
| --- | --- | --- |
| 固定技术栈：Unity LTS/C#/AR Foundation、React/TS、Go | ✅ | unity/、web/、backend/ |
| 3.1 真机运行、检测水平平面、点击放置标记 | ✅（待真机验证） | `ArPlacementController.cs`、`PlaneVisualizer.cs` |
| 3.1 表单提交问题（标题必填 / 描述可选 / 优先级 low·medium·high / 位置 xyz） | ✅（待真机验证） | `IssueFormUI.cs`、`Models.cs` |
| 3.1 成功/失败明确提示；点击 UI 不误放标记 | ✅（待真机验证） | `IssueFormUI.cs`、`ArPlacementController.cs`（EventSystem 判定） |
| 3.2 拉取列表、显示标题/优先级/状态 | ✅ | `web/src/App.tsx`、`IssueCard.tsx` |
| 3.2 修改状态 open / in_progress / resolved | ✅ | `StatusSelect.tsx` + `PATCH /api/issues/{id}` |
| 3.2 后端不可用显示错误、不白屏 | ✅ | `ErrorBanner.tsx`、`ErrorBoundary.tsx` |
| 3.3 health / 上报 / 列表 / 改状态 | ✅ | `handlers.go` |
| 3.3 唯一 ID、默认 status=open | ✅ | `issue.go` |
| 3.3 标题为空或枚举错误返回明确参数错误 | ✅ | `issue.go` + 统一错误 JSON |
| 3.3 重启后数据仍在；三端同一后端与数据 | ✅ | `store.go`（JSON 文件） |
| 3.3 React 浏览器可访问后端 | ✅ | CORS（`ALLOW_ORIGIN`，默认 `*`） |
| 4 基础数据结构三端字段一致 | ✅ | `docs/API.md` + `Models.cs` + `web/src/types.ts` |
| 5 AI 使用记录 AI_LOG.md | ✅ 初版底稿 | [`AI_LOG.md`](AI_LOG.md) |
| 6 交付：三端源码 + README | ✅ | 本仓库 |
| 6 交付：AI_LOG.md | ✅ | [`AI_LOG.md`](AI_LOG.md) |
| 6 交付：两段 3～5 分钟演示视频 | ⏳ 未录制 | 需真机 + 录屏 |

## 5. 初版验收记录

- **后端**：`go vet ./...` 通过；`go test ./...` 通过（校验 / 持久化·重载 / handler 全流程）。
  8080 端口实测：health 200、POST 201、空标题/非法优先级/缺坐标 → 400 且 `fields` 精确、GET 倒序、PATCH 200 且 `updatedAt` 刷新、非法枚举 400、未知 id 404、OPTIONS 204 + CORS 头；**重启进程后数据仍在**。
- **管理端**：`npm run build` 通过（tsc + vite）；`web/dist` 用真实后端实测：
  - 列表 3 条（不同优先级/状态）正常渲染，见 [`docs/screenshots/web-list.png`](docs/screenshots/web-list.png)；
  - 页面上把「地面油污」改为 `resolved`，后端 GET 确认已落库，见 [`docs/screenshots/web-after-status.png`](docs/screenshots/web-after-status.png)；
  - 停掉后端再刷新：显示红色错误横幅（含原因与重试按钮），页面不白屏，见 [`docs/screenshots/web-error.png`](docs/screenshots/web-error.png)；
  - 重启后端点「重试」恢复列表，数据（含修改过的状态）完整保留。
- **Unity**：本机没有 Unity / Android 环境，**未编译、未真机验证**，仅做静态审查（字段与契约一致、可选 API 用反射兜底）；操作步骤与风险见 `unity/README.md`。

## 6. 已知问题 / 未完成（如实说明）

1. **Unity 端未在本机验证**：需要 Unity 6000.0 LTS + Android 真机；一键 Setup 失败时按 `unity/README.md` 手动搭场景。
2. Unity 依赖包版本固定为 AR Foundation / ARCore `6.0.5`；若你的 Unity 版本 Package Manager 中无该版本，改用可用的 `6.0.x`（脚本不依赖小版本差异）。
3. 演示视频（手机 + 电脑各 3～5 分钟）未录制。
4. 初版无鉴权、无分页、无删除 / 编辑问题（任务书未要求，属加分项）。
5. 不要求重启后恢复历史标记的完全相同的物理位置（任务书明确不要求）；跨设备/跨会话定位未实现。
6. AI_LOG.md 目前是初版底稿，后续每次让 AI 干活都要把 Prompt 原文追加进去。

## 7. 后续可做的加分项

- 登录 / 权限、React 筛选与详情、Unity 历史问题列表；
- 按优先级或状态改变 AR 标记样式（初版已按优先级着色，可继续扩展）；
- WebSocket 实时更新、跨设备定位方案、Docker 化、自动化测试；
- 结合巡检场景的 AI 功能（如照片缺陷识别、问题自动分类）。

## 8. 文档索引

- 接口契约（三端唯一权威）：[`docs/API.md`](docs/API.md)
- 后端说明：[`backend/README.md`](backend/README.md)
- Unity 打开 / 构建 / 兜底：[`unity/README.md`](unity/README.md)
- AI 使用记录：[`AI_LOG.md`](AI_LOG.md)
