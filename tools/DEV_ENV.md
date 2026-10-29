# 开发环境与被沙箱限制绕行说明（内部文件）

> 本机沙箱只允许写工作区 `D:\项目\面试测试`，因此 Go / npm 的缓存都放在工作区内。

## Go（已安装在工作区）

版本：go1.27.3 windows/amd64，位置 `D:\项目\面试测试\.tools\go\bin\go.exe`。

每条 pwsh 调用都是新进程，使用前先设环境变量：

```powershell
$env:GOROOT='D:\项目\面试测试\.tools\go'; $env:GOCACHE='D:\项目\面试测试\.tools\gocache'; $env:GOPATH='D:\项目\面试测试\.tools\gopath'; $env:GOTOOLCHAIN='local'; $env:GOPROXY='off'
& "$env:GOROOT\bin\go.exe" version
```

> GOPROXY=off：本项目后端只用标准库，不允许引入第三方依赖。go.mod 里写 `go 1.22` 即可。

## npm

Node v24.16.0 / npm 11.13.0 已安装（全局），但 npm 默认缓存目录在用户目录不可写，必须改到工作区：

```powershell
$env:npm_config_cache='D:\项目\面试测试\.npm-cache'; $env:npm_config_fund='false'; $env:npm_config_audit='false'
npm install
npm run build
```

## 浏览器 / 截图

无头 Chrome 只能由 Lead 用 CDP 方式拉起（沙箱下 Chrome 会 crashpad 报错，需提权一次）。**Teammate 不要尝试启动 Chrome**，UI 视觉验收由 Lead 完成。

## 端口约定

| 用途 | 端口 | 归属 |
| --- | --- | --- |
| Go 后端（最终联调） | 8080 | Lead |
| Vite dev（最终联调） | 5173 | Lead |
| 后端自测（backend 同学） | 18080 | backend |
| 后端自测（web 同学联调） | 18081 | web |
| Vite dev（web 自测） | 5174 | web |

用完自己启动的进程必须关闭（后台 job 用 job_kill），不要留在 8080/5173 上。

## 目录归属（写权限边界）

| 目录 | Owner |
| --- | --- |
| `ar-inspection/backend/**` | backend teammate |
| `ar-inspection/web/**` | web teammate |
| `ar-inspection/unity/**` | unity teammate |
| `ar-inspection/README.md` / `AI_LOG.md` / `docs/**` | Lead |
| `tools/`、`out/`、`.tools/` | Lead（抓取/工具链，不属于交付物） |
