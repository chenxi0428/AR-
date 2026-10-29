# Unity AR 巡检问题标注（Android + ARCore）

园区巡检场景的移动端原型：手机 AR 识别水平平面 → 点击平面放置标记 → 弹出表单填写问题（标题/描述/优先级）
→ 上报到 Go 后端 → 标记变绿并在标记上方显示标题。PC React 管理端从同一个 Go 后端读取问题并修改状态。

> 本目录是**完整的 Unity 工程根目录**（含 `Assets/`、`Packages/`、`ProjectSettings/`），用 Unity Hub 直接打开即可。

---

## 1. 版本与依赖

| 项 | 版本 / 说明 |
| --- | --- |
| Unity | **6000.0.32f1（Unity 6 LTS）**，见 `ProjectSettings/ProjectVersion.txt` |
| 渲染管线 | Built-in Render Pipeline（不依赖 URP；运行时代码用 `GameObject.CreatePrimitive` 的默认 Standard 材质） |
| 平台 | Android + ARCore（AR Foundation） |
| UI | uGUI legacy（`UnityEngine.UI.Text / InputField / Dropdown / Button`），不依赖 TextMeshPro / 字体资产 |

`Packages/manifest.json` 依赖：

| 包 | 版本 | 用途 |
| --- | --- | --- |
| `com.unity.xr.arfoundation` | 6.0.5 | AR Session / XR Origin / 平面 / 射线 |
| `com.unity.xr.arcore` | 6.0.5 | Android ARCore 提供方 |
| `com.unity.xr.management` | 4.5.0 | XR Plug-in Management |
| `com.unity.ugui` | 2.0.0 | 运行时 uGUI 表单 |
| `com.unity.modules.*` | 1.0.0 | 内置模块（xr / unitywebrequest / jsonserialize / physics 等） |

- AR Foundation 6 会自动带入 `com.unity.xr.core-utils` 等传递依赖（`XROrigin` 就在该包里）。
- 如果 Package Manager 中不存在 `6.0.5`（版本被撤回或私有镜像不同步），请在 **Window > Package Manager > Unity Registry**
  安装当前可用的 **6.0.x**（AR Foundation 与 ARCore XR Plugin **版本号保持一致**）以及 `XR Plug-in Management 4.5.x`，
  `manifest.json` 改成对应版本即可。
- 官方 AR Foundation 6 的相机姿态驱动依赖 Input System 包（`com.unity.inputsystem`，由 AR Foundation 传递依赖带入）。
  本工程对它是**可选项**：没有它也能通过自研的 `XrPoseFallback` 驱动相机（见 §7.4）。

### ARCore 真机要求

- Android 7.0（API 24）及以上，**64 位 ARM64**；
- 支持 ARCore 的机型，且已安装 **Google Play Services for AR（ARCore）**；
- 首次运行若提示安装/更新 Google Play Services for AR，请按提示安装。

---

## 2. 用 Unity Hub 打开工程

1. Unity Hub → **Add project from disk** → 选择本目录 `ar-inspection/unity`；
2. 用 **Unity 6000.0.32f1** 打开（若本机没有该小版本，可用任意 Unity 6000.0.x LTS 打开，允许升级工程即可）；
3. 首次打开 Unity 会解析 `Packages/manifest.json` 并自动生成 `Library/`、`*.meta`、`ProjectSettings/` 下缺失的默认配置。
   **首次编译完成后**再执行下一步。
4. 通过 Unity Hub 给该版本安装 **Android Build Support**（含 **Android SDK & NDK Tools**、**OpenJDK**）。
   （Hub → Installs → 6000.0.32f1 → 齿轮 → Add modules）

> 工程只提交了源码与工程配置；`Assets/Scenes/Main.unity`、`.meta`、`Library/` 由 Unity 首次打开 / 一键 Setup 生成，
> 生成后建议把 `.meta` 与 `Main.unity` 一并提交（`.gitignore` 已排除缓存与构建产物）。

---

## 3. 一键工程初始化（推荐）

打开工程后执行菜单：

```
Tools > AR Inspection > Setup Android + ARCore Project
```

该菜单会依次完成：

1. 切换构建目标为 Android；
2. 启用 **XR Plug-in Management → Android → ARCore**（通过 `XRGeneralSettingsPerBuildTarget` +
   `XRPackageMetadataStore.AssignLoader("UnityEngine.XR.ARCore.ARCoreLoader", BuildTargetGroup.Android)`）；
3. 配置 PlayerSettings：
   - Company / Product：`HuaFeng` / `AR Inspection`
   - applicationIdentifier：`com.huafeng.arinspection`
   - minSdkVersion：24，Scripting Backend：IL2CPP，Target Architectures：ARM64
   - 竖屏（Portrait），禁止自动横屏
   - 允许明文 HTTP（局域网后端是 `http://`，Android 9+ 默认禁止）
   - Android Graphics APIs：OpenGLES3（ARCore 最稳）
   - 强制 INTERNET 权限
   - Active Input Handling = **Both**（本工程 UI 用 legacy Input；设置后**需要重启编辑器**才生效）
4. 生成 `Assets/Scenes/Main.unity`：
   - `AR Session`（ARSession + ARInputManager）
   - `XR Origin`（XROrigin + Camera Offset + Main Camera；Main Camera 上有
     Camera + ARCameraManager + ARCameraBackground + 姿态驱动 + AudioListener；
     XR Origin 上有 ARPlaneManager + ARRaycastManager）
   - `Directional Light`（标记/平面贴片用 Standard 材质，需要灯光）
   - `ARInspectionApp`（AppConfig + ApiClient + IssueFormUI + PlaneVisualizer + ArPlacementController，引用已自动连好）
5. 把 `Main.unity` 加入 Build Settings 并置顶；
6. 弹出完成对话框，并打印需要手动处理的警告。

执行完成后建议再跑一次：

```
Tools > AR Inspection > Validate Setup
```

逐项检查构建目标 / ARCore loader / 场景 / appId / minSdk / IL2CPP / ARM64 / 竖屏 / Active Input Handling / Graphics APIs / 明文 HTTP，
结果同时输出到 Console。任何 `[!!]` 项按 §7 手动兜底。

勾选 **Console 里的 “I understand...” 重启提示**（如果出现），重启编辑器。

---

## 4. 配置后端地址（真机必做）

后端默认地址：`http://127.0.0.1:8080`（仅本机调试用，真机上 127.0.0.1 指向手机自己，**必须改成 PC 的局域网 IP**）。

两种方式任选：

1. **运行时改（推荐，不用重新出包）**：App 顶部 HUD 右上角「后端设置」→ 输入
   `http://192.168.1.20:8080`（替换成 PC 实际 IP）→ 保存。地址写入 PlayerPrefs，下次启动仍生效。
2. **Inspector 改**：选中场景里的 `ARInspectionApp` → `AppConfig.serverUrl` 改成 PC 局域网 IP → 重新 Build。
   （若之前运行时保存过地址，PlayerPrefs 中的值优先；可删除 App 数据或把地址改回来。）

PC 侧检查清单：

- 后端已启动（`go run .`，监听 `:8080`，默认监听所有网卡）；
- 手机与 PC 在同一 WiFi / 局域网，且路由器没开 AP 隔离；
- PC 防火墙放行 TCP 8080（Windows 管理员 PowerShell）：
  ```powershell
  netsh advfirewall firewall add rule name="AR Inspection Backend 8080" dir=in action=allow protocol=TCP localport=8080
  ```
- 用手机浏览器访问 `http://<PC-IP>:8080/api/health` 能看到 `{"status":"ok",...}` 再回 App 里试；
- 从 App 内 HUD 可以直接看到后端地址与健康状态（在线 / 离线）。

> Android 模拟器调试时，宿主机地址是 `http://10.0.2.2:8080`。

---

## 5. 真机 Build And Run

1. 手机开启**开发者模式** + **USB 调试**，用数据线连接 PC，允许调试授权；
2. Unity → **File > Build Settings** → Platform = Android → **Build And Run**；
   - 首次会要求选择 Android SDK/NDK/JDK 路径：用 Unity Hub 安装的路径即可（或选 “Use embedded” 无则选已安装）。
3. 首次启动 App 若提示安装/更新 **Google Play Services for AR**，按提示安装后重开；
4. 演示流程：
   1. 举起手机缓慢扫视地面/桌面，识别到平面后会出现浅蓝色平面贴片；
   2. 点击平面 → 位姿处出现橙色 Cube 标记，同时弹出表单；
   3. 填写标题（必填）、描述（可选）、优先级 low/medium/high → 提交；
   4. 提交成功：表单顶部绿字 `上报成功：<标题>（id=iss_...）`，约 1.6s 后自动关闭；标记变绿，上方显示标题；
   5. PC 浏览器打开 React 管理端刷新即可看到该问题；
   6. 提交失败（后端未启动 / IP 错 / 防火墙拦截）：表单红字展示后端 `message` 或网络错误说明，标记保持橙色，可直接改完再提交；
   7. 点击「取消」会销毁待上报标记，不会产生脏数据；
   8. 点击按钮 / 输入框 / 下拉框不会误在 AR 场景中放置标记（`EventSystem.current.IsPointerOverGameObject` 拦截）。

> **Editor Play Mode 不能验证真实 AR**：编辑器里没有 ARCore 子系统，只有 UI 与（可能的）黑色背景，
> 射线检测不到平面、放置不会成功，这是预期行为；AR 功能必须用真机验证。

---

## 6. 脚本清单

| 文件 | 职责 |
| --- | --- |
| `Assets/Scripts/Models.cs` | 与 `docs/API.md` 字段完全一致的 `[Serializable]` 模型：`Issue / Position / IssueCreateRequest / IssueListResponse / IssueStatusPatch / ApiError / HealthResponse` 等 |
| `Assets/Scripts/AppConfig.cs` | 后端地址配置：Inspector 默认值 + PlayerPrefs 持久化 + 静态 `BaseUrl`（去尾部 `/`） |
| `Assets/Scripts/ApiClient.cs` | `UnityWebRequest` 协程客户端：`CreateIssue(...)`（POST /api/issues）、`Health(...)`；超时 15s；解析后端 `{code,message,fields}` 为中文提示 |
| `Assets/Scripts/ArPlacementController.cs` | AR 射线放置：Android 触摸 / 编辑器鼠标；命中 `TrackableType.PlaneWithinPolygon` 后实例化标记；点 UI 不误放；打开表单 |
| `Assets/Scripts/MarkerVisual.cs` | 标记外观：待上报橙色 → 成功后绿色 + 世界空间标题；失败保持橙色；取消销毁 |
| `Assets/Scripts/IssueFormUI.cs` | 运行时 uGUI：HUD（后端地址 / 提示 / 健康状态）+ 表单（标题必填、描述、优先级、坐标、提交/取消、状态文字）+ 后端设置面板 |
| `Assets/Scripts/UiFactory.cs` | 程序化搭 Canvas（ScreenSpaceOverlay + CanvasScaler 1920×1080）、面板、按钮、输入框、下拉框、世界空间文字，统一字体与配色 |
| `Assets/Scripts/SafeAreaFitter.cs` | SafeArea 适配（刘海屏 / 手势条） |
| `Assets/Scripts/PlaneVisualizer.cs` | 已识别平面可视化（运行时生成薄 Cube 贴片，不依赖 planePrefab） |
| `Assets/Scripts/XrPoseFallback.cs` | 姿态驱动兜底：官方 TrackedPoseDriver/ARPoseDriver 缺失或未生效时，用 `UnityEngine.XR.InputDevices` 的 CenterEye 位姿驱动相机 |
| `Assets/Editor/ProjectSetupWizard.cs` | 一键 Setup / Validate / Open Main Scene 菜单（`Tools/AR Inspection/*`） |
| `Assets/Editor/XrSettingsUtility.cs` | XR Plug-in Management / PlayerSettings 可选 API 的反射封装（版本差异时不至于编译失败） |

数据流：

```
点击平面 → ARRaycastManager.Raycast(PlaneWithinPolygon)
        → 命中 Pose → 实例化 Cube（MarkerVisual，橙色）
        → IssueFormUI.ShowForm(坐标, marker)
        → 提交 → ApiClient.CreateIssue(IssueCreateRequest)
        → POST /api/issues（字段与 API.md 完全一致）
        → 201 返回 Issue → 标记变绿 + 标题 + 成功提示
        → 失败展示 {code,message,fields} 的中文 message，标记保持橙色
```

---

## 7. 手动兜底步骤（一键 Setup 失败时）

一键菜单所有可选步骤失败都只打印警告、不会中断。按下面顺序手动完成即可。

### 7.1 切 Android + 装模块

- Unity Hub 给 6000.0.32f1 安装 **Android Build Support**（SDK & NDK Tools、OpenJDK）；
- `File > Build Settings > Android > Switch Platform`。

### 7.2 启用 ARCore（XR Plug-in Management）

1. `Edit > Project Settings > XR Plug-in Management`；
2. 若提示安装 XR Plug-in Management，点 Install；
3. 切到 **Android** 标签页，勾选 **ARCore**；
4. 确认 `Assets/XR/XRGeneralSettingsPerBuildTarget.asset`（以及 Android 子配置）已生成。

### 7.3 场景组件清单（手动建 `Assets/Scenes/Main.unity`）

新建空场景，按下表创建 GameObject：

| GameObject | 组件 |
| --- | --- |
| `AR Session` | `AR Session`、`AR Input Manager` |
| `XR Origin` | `XR Origin`（Camera = Main Camera，Camera Floor Offset Object = Camera Offset，Tracking Origin Mode = Device）；`AR Plane Manager`（Detection Mode = Horizontal）；`AR Raycast Manager` |
| └ `Camera Offset` | 空物体，localPosition = (0,0,0) |
| &nbsp;&nbsp;└ `Main Camera` | `Camera`（Clear Flags = Solid Color，Background 黑，Near 0.1，Far 1000）、`AR Camera Manager`、`AR Camera Background`、`Audio Listener`、姿态驱动（见 7.4）；Tag 设为 MainCamera |
| `Directional Light` | `Light`（Directional，Intensity 1，旋转约 (45, -30, 0)） |
| `ARInspectionApp` | `AppConfig`、`ApiClient`、`IssueFormUI`、`PlaneVisualizer`、`ArPlacementController` |

引用赋值（Inspector 拖拽）：

- `ApiClient.config` ← `AppConfig`
- `IssueFormUI.apiClient` ← `ApiClient`，`IssueFormUI.appConfig` ← `AppConfig`
- `PlaneVisualizer.planeManager` ← XR Origin 上的 `ARPlaneManager`
- `ArPlacementController.raycastManager / planeManager / formUI / planeVisualizer` ← 对应组件

保存为 `Assets/Scenes/Main.unity`，并加入 `File > Build Settings > Scenes In Build` 且勾选启用。

> 场景里**不需要**手工摆 UI：`IssueFormUI` 运行时会用 `UiFactory` 创建 Canvas / EventSystem / 表单，空场景也能跑。

### 7.4 相机姿态驱动

- 优先方案（官方）：`Window > Package Manager` 安装 **Input System**，给 `Main Camera` 添加
  `TrackedPoseDriver`（Position Input = `XR HMD/centerEyePosition`，Rotation Input = `XR HMD/centerEyeRotation`）；
- 若没有 Input System / 不想加：保持场景里的 **`XrPoseFallback`** 组件即可，它会在检测到相机位姿与
  XR 输入设备 CenterEye 位姿不一致时接管（官方驱动正常工作时不会产生副作用）。

### 7.5 Player Settings（`Edit > Project Settings > Player`）

- Android 页签：**Other Settings**
  - Package Name：`com.huafeng.arinspection`
  - Minimum API Level：**24**
  - Scripting Backend：**IL2CPP**；Target Architectures：**ARM64**
  - **Active Input Handling：Both**（或 Input Manager (Old)；改完重启编辑器）
  - **Allow downloads over HTTP：Always allowed**（或自定义 AndroidManifest 加 `android:usesCleartextTraffic="true"`）
  - Internet Access：Require（默认即可）
- Resolution and Presentation：**Default Orientation = Portrait**，取消勾选 Landscape 自动旋转
- 无 Android 模块时：先按 7.1 安装模块。

### 7.6 一键脚本没有生成 EventSystem 的情况

正常不需要处理：`UiFactory.EnsureEventSystem()` 会在运行时自动创建 `EventSystem + StandaloneInputModule`
（Active Input Handling 为 New-only 时改为反射添加 `InputSystemUIInputModule`）。
若你手工加了 EventSystem，请保证有且只有一个。

### 7.7 中文显示成方块

运行时 `UiFactory.GetFont()` 会按优先级查找系统中文字体（Noto Sans CJK / 微软雅黑 / 苹方 / Droid Sans Fallback 等），
找不到才退回 Unity 内置字体。若某些设备仍显示方块，可：

- 在 `Assets/Resources` 放一个中文 `.ttf/.otf` 字体，并在 `UiFactory.GetFont()` 里优先加载它；
- 或改用 TextMeshPro + 中文 Font Asset（本工程刻意未用 TMP，避免字体资产缺失）。

---

## 8. 已完成 / 未完成 / 已知风险

### 已完成

- [x] Unity 6000.0 LTS 工程骨架（`Packages/manifest.json`、`ProjectVersion.txt`、`.gitignore`）；
- [x] AR Foundation 6 + ARCore 平面检测、平面可视化、点击平面放置标记；
- [x] 触摸（Android）/ 鼠标（Editor）双输入；点击 UI 不误放标记；
- [x] 运行时 uGUI 表单：标题必填校验、描述、优先级 Dropdown、坐标展示、提交/取消、状态与错误提示、SafeArea；
- [x] POST `/api/issues` 上报，成功 → 绿色标记 + 世界空间标题 + 成功提示；失败展示后端 `message` 且保持待上报状态；
- [x] HUD 显示后端地址、操作提示、健康检查状态；后端地址可在 App 内修改并 PlayerPrefs 持久化；
- [x] Editor 一键 Setup / Validate / Open Scene 菜单，生成场景并自动连好引用；
- [x] 姿态驱动反射兜底 + 自研 `XrPoseFallback` 二级兜底；编辑器脚本对版本敏感 API 全部反射调用；
- [x] 本 README 与 API 字段对照、手动兜底步骤。

### 未完成 / 未在本机验证

- [ ] **本机没有安装 Unity、Android SDK，也没有 ARCore 真机，因此没有编译过、没有在真机跑过**。
      代码按 Unity 6 + AR Foundation 6 的公开 API 逐行静态校对，但仍可能有少量编译期问题需要首次打开时修正
      （例如具体小版本 API 差异）。
- [ ] 未在真机上验证平面识别与放置的手感 / 精度；未录制演示视频（由项目负责人完成）。
- [ ] 未实现 Unity 端历史问题列表、状态编辑、跨会话定位恢复（任务书未要求，属加分项）。
- [ ] 未做登录 / 权限；未做 WebSocket 实时刷新。

### 已知风险与解释

| 风险 | 说明 / 兜底 |
| --- | --- |
| 包版本不存在 | `6.0.5` 若不可用，用 Package Manager 当前可用的 **6.0.x**（AR Foundation 与 ARCore 同版本），并按 §1 调整 manifest |
| 未编译验证 | 首次打开若有编译错误，优先检查 Console；编辑器脚本中 XR Management / PlayerSettings 可选 API 均为反射调用，失败只警告 |
| 相机不跟随手机 | 先看 Console 是否有 `[XrPoseFallback] 已接管`；若没有，检查 XR Origin 的 Camera 是否指向 Main Camera、AR Session 是否在场景里；官方方案见 §7.4 |
| 无平面 / 点不中 | 需要较好的环境光与纹理；缓慢扫视地面；确认 `ARPlaneManager` 与 `ARRaycastManager` 在 XR Origin 上；Detection Mode = Horizontal |
| 真机上报失败 | 99% 是后端地址仍是 127.0.0.1、手机与 PC 不同网段、PC 防火墙未放行 8080、或明文 HTTP 未允许；HUD 会显示「后端离线」与具体错误 |
| 标记/平面是黑色 | 场景缺少 Directional Light（Standard 材质需要灯）；或把工程切到了 URP（本工程按 Built-in RP 编写） |
| 中文显示方块 | 见 §7.7；Android 设备通常有 Noto CJK 字体，一般不会出现 |
| 工程切到 URP 后材质变粉 | 本工程使用 Built-in RP 的默认材质；若必须用 URP，请给标记/平面换成 URP Unlit/Lit 材质 |
| Editor Play Mode 无 AR | 正常现象：编辑器没有 ARCore 子系统，AR 功能只能真机验证 |
| ARCore 设备不支持 | 需 Google Play Services for AR 且机型在 ARCore 支持列表；不支持时 AR Session 无法启动 |

---

## 9. 与 `docs/API.md` 的字段对照

请求体（POST `/api/issues`，`IssueCreateRequest`）：

```json
{
  "title": "入口墙面破损",
  "description": "左侧墙体存在裂缝",
  "priority": "high",
  "position": { "x": 0.42, "y": 0.03, "z": 1.26 }
}
```

- `title`：必填，表单校验去首尾空白后非空且 ≤200；
- `description`：可选，≤2000，默认空字符串；
- `priority`：`low / medium / high`，Dropdown 默认 `medium`；
- `position`：放置标记时的 **Unity 世界坐标**（`Pose.position + up * markerHover`，与标记显示位置一致）。

响应（201）：完整 `Issue`（`id / title / description / priority / status / position / createdAt / updatedAt`），
Unity 端用 `JsonUtility.FromJson<Issue>` 解析，成功后绿色标记上显示 `title`，成功提示里显示 `id`。

错误（4xx/5xx）：`{ "code": "...", "message": "...", "fields": { ... } }` → `ApiClient` 优先展示 `message`（中文），
`fields` 拼成 `"title: 不能为空"` 形式追加；网络层错误（状态码 0）会给出「检查后端是否启动 / 局域网 IP / 防火墙 8080」的提示。

---

## 10. 接口自测（不依赖 Unity）

后端启动后可直接用 curl / PowerShell 验证前端字段契约（与 Unity 端发送的 JSON 完全一致）：

```powershell
$body = '{"title":"入口墙面破损","description":"左侧墙体存在裂缝","priority":"high","position":{"x":0.42,"y":0.03,"z":1.26}}'
Invoke-RestMethod -Method Post -Uri http://127.0.0.1:8080/api/issues -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($body))
```
