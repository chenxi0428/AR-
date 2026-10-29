# AI 使用记录（AI_LOG）

> 本项目按任务书第 5 节要求记录开发过程中由我主动发给 AI 的指令与结果。
> 记录原则：保留 Prompt 原文（或可复现的等价记录），写清目标、结果与验证方式。
> 说明：P-001/P-002 为用户直接下达给 AI 的原始指令；P-003 起为 AI 在协作开发中主动下达给子任务的指令（内部编排），按摘要记录。

---

## P-001 提取线上试题

- 目标：进入飞书 wiki 页面（有访问密码），把《Unity-AR研发实操题-候选人任务书-简略版》完整提取下来，作为开发依据。

### Prompt 原文

> https://xx2pxezy6js.feishu.cn/wiki/CY4YwwKrLiyuTmkcErIcsNs8nnb
> 访问密码：UNITY2026    进入这个网页提取试题

### 结果

- 采用（AI 完成）。
- 过程：无头 Chrome + CDP 打开页面 → 填密码解锁 → 按块滚动采集 129 个 block → 重建 Markdown → 用页面全文交叉校验 135 项内容全部命中。
- 产物：`Unity-AR研发实操题-候选人任务书-简略版.md`（工作目录根）。
- 验证：逐条文本校验 + 表格/代码块截图人工核对。

---

## P-002 实现初版（Android + ARCore）

- 目标：按任务书实现三端可运行的最小闭环初版。

### Prompt 原文

> Unity 我要用Android + ARCore，其余你按照任务书去做，先给我一个初版

### 结果

- 采用；拆成三个并行子任务（Go 后端 / React 管理端 / Unity 移动端），先冻结三端 API 契约再并行开发。
- 关键决策：
  - 后端用 Go 标准库 + 本地 JSON 文件持久化（任务书允许 SQLite 或本地 JSON）；
  - 前端用 Vite + React + TypeScript，不用 UI 框架；
  - Unity 用 Unity 6000.0 LTS + AR Foundation 6 + ARCore 6，Android 平台；场景与 uGUI 由 Editor 脚本/运行时脚本生成，减少手工配置。
- 验证方式：
  - 后端：`go vet` / `go test` + 18080 端口全流程 curl（含重启后数据仍在）；
  - 前端：`npm run build` + 18081 后端联调（含 CORS 预检、PATCH 成功/失败分支），Lead 再用无头浏览器做视觉验收；
  - Unity：本机无 Unity 环境，只能静态审查 + 真机操作步骤写入 README（未编译/未真机验证，作为已知风险如实说明）。

---

## P-003 内部实现指令（AI → 子任务，摘要）

- 目标：拆分三个互不重叠的写目录，并行实现，最后统一联调验收。
- 指令要点（摘要）：
  1. Go 后端：严格按 frozen 的 `docs/API.md` 实现 health/issues/status 接口、校验与统一错误格式、JSON 原子持久化、CORS，附单测并用 18080 端口自测（含重启持久化）。
  2. React 管理端：按同一契约实现列表、状态修改、加载/空态/错误兜底，`npm run build` 必须通过，用 18081 端口联调。
  3. Unity 移动端：Android + ARCore，平面识别、点击放置标记、运行时 uGUI 表单、POST 上报、成功/失败提示、点击 UI 不误放标记、后端地址可配置；提供 Editor 一键 Setup 与 README（含手动兜底步骤）。
- 结果：三个子任务并行完成，Lead 按契约做端到端联调（详见 README「验收记录」）。

---

## P-004 修正与自查（摘要）

- 目标：对初版做端到端联调与缺陷修正。
- 做法：
  - 以 `docs/API.md` 为基准，逐接口验证后端；前端用真实后端数据走一遍「加载 → 改状态 → 刷新」；
  - 关停后端验证前端错误兜底（不白屏）；
  - 检查 Unity 脚本字段名与契约一致、可选 API 的反射兜底、README 步骤可复现。
- 结果：初版三端全部完成，端到端联调通过，具体证据如下。
- 实测结果：
  - **后端**：`go vet` / `go test` 通过；8080 真进程验证 health 200、POST 201、GET 倒序、PATCH 200（updatedAt 刷新）、空标题/非法枚举 → 400、未知 id → 404、OPTIONS → 204 + CORS 头；进程重启后数据仍在。
  - **管理端**：`npm run build` 通过（tsc + vite，27 modules）；用真实后端数据测出列表正常渲染、把一条问题改为 `resolved` 后后端确认落库（`docs/screenshots/web-after-status.png`）、停掉后端后显示错误横幅且不白屏（`docs/screenshots/web-error.png`）、重启后端后重试恢复。
  - **Unity**：本机无 Unity/Android 环境，只做静态审查（字段与契约一致、风险 API 反射兜底、README 步骤可复现），未编译、未真机验证（如实写在 README「已知问题」）。
- 结论：见 README「功能对照任务书 / 初版验收记录 / 已知问题」。

---

## 备注

- 本文件是初版的 AI 使用记录底稿；后续继续开发时，请把新的 Prompt 原文追加为 P-005、P-006…，不要只写总结。
- 提交前请自查：不含 API Key、Token、账号、个人隐私等敏感信息。
