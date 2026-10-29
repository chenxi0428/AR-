#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using Unity.XR.CoreUtils;

namespace ArInspection.EditorTools
{
    /// <summary>
    /// 一键工程初始化 / 配置检查。
    ///
    /// 菜单：
    ///   Tools/AR Inspection/Setup Android + ARCore Project   切 Android、启用 ARCore loader、
    ///                                                      配置 PlayerSettings、生成 Assets/Scenes/Main.unity
    ///   Tools/AR Inspection/Validate Setup                   逐项检查当前工程配置并在 Console 输出清单
    ///   Tools/AR Inspection/Open Main Scene                  打开生成的场景
    ///
    /// 说明：本机没有安装 Unity / Android SDK，无法编译验证，所以凡是「可能随包版本变化」的 API
    /// 都通过 XrSettingsUtility 反射调用；任一可选步骤失败只会打印警告 + README 手动步骤，
    /// 不会中断整个流程。
    /// </summary>
    public static class ProjectSetupWizard
    {
        public const string MainScenePath = "Assets/Scenes/Main.unity";
        public const string ApplicationIdentifier = "com.huafeng.arinspection";

        [MenuItem("Tools/AR Inspection/Setup Android + ARCore Project", false, 1)]
        public static void SetupProject()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            List<string> done = new List<string>();
            List<string> warnings = new List<string>();

            try
            {
                XrSettingsUtility.EnsureFolder("Assets/Scenes");
                AssetDatabase.Refresh();

                // ---------- 1. 切换构建目标 ----------
                bool switched = EditorUserBuildSettings.SwitchActiveBuildTarget(
                    BuildTargetGroup.Android, BuildTarget.Android);
                if (switched)
                {
                    done.Add("构建目标已切换为 Android");
                }
                else
                {
                    warnings.Add("切换 Android 构建目标失败：请用 Unity Hub 给 Unity 6000.0.32f1 安装 "
                                 + "Android Build Support（含 Android SDK & NDK、OpenJDK），然后重跑本菜单。");
                }

                // ---------- 2. PlayerSettings ----------
                ConfigurePlayerSettings(done, warnings);

                // ---------- 3. XR Plug-in Management：启用 ARCore ----------
                string loaderMessage;
                bool loaderEnabled = XrSettingsUtility.EnableLoader(
                    XrSettingsUtility.LoaderTypeName, BuildTargetGroup.Android, out loaderMessage);
                if (loaderEnabled)
                {
                    done.Add(loaderMessage);
                }
                else
                {
                    warnings.Add(loaderMessage);
                }

                // ---------- 4. 输入处理 / HTTP / 权限 ----------
                if (XrSettingsUtility.SetActiveInputHandler(2))
                {
                    done.Add("Active Input Handling 已设为 Both（若弹出重启提示请重启编辑器）");
                }
                else
                {
                    warnings.Add("未能自动设置 Active Input Handling，请手动设为 Both（Project Settings > Player）。");
                }

                if (XrSettingsUtility.TryAllowInsecureHttp())
                {
                    done.Add("已允许明文 HTTP（Android 局域网后端需要）");
                }
                else
                {
                    warnings.Add("未能自动允许明文 HTTP，若真机请求失败请检查 "
                                 + "Player Settings > Other Settings > Allow downloads over HTTP。");
                }

                if (XrSettingsUtility.TryForceInternetPermission())
                {
                    done.Add("Android INTERNET 权限已强制勾选");
                }
                else
                {
                    warnings.Add("未能自动勾选 Internet Access，若真机无法联网请检查 "
                                 + "Player Settings > Other Settings > Internet Access = Require。");
                }

                // ---------- 5. 图形 API ----------
                if (ConfigureGraphicsApis())
                {
                    done.Add("Android Graphics APIs 已设为 OpenGLES3（ARCore 最稳）");
                }
                else
                {
                    warnings.Add("未能自动设置 Graphics APIs，请在 Player Settings 中把 Android 的 "
                                 + "Graphics APIs 设为 OpenGLES3（或 Automatic）");
                }

                // ---------- 6. 生成 Main 场景 ----------
                CreateMainScene(done, warnings);

                // ---------- 7. 场景加入 Build Settings ----------
                AddSceneToBuildSettings(MainScenePath);
                done.Add("Main.unity 已加入 Build Settings 并置顶启用");

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog(
                    "AR Inspection 工程初始化失败",
                    "发生异常：\n" + exception.Message
                    + "\n\n请查看 Console，并按 README 的「手动兜底步骤」完成配置。",
                    "好");
                return;
            }

            string report = BuildReport(done, warnings);
            Debug.Log("[AR Inspection][Setup]\n" + report);
            EditorUtility.DisplayDialog("AR Inspection 工程初始化完成", report, "好");
        }

        [MenuItem("Tools/AR Inspection/Validate Setup", false, 20)]
        public static void ValidateSetup()
        {
            List<string> lines = new List<string>();
            bool allOk = true;

            // 构建目标
            bool androidTarget = EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android;
            AppendCheck(lines, ref allOk, androidTarget,
                "构建目标: " + EditorUserBuildSettings.activeBuildTarget
                + (androidTarget ? "" : "（应为 Android）"));

            // ARCore loader
            bool loaderAssigned = XrSettingsUtility.IsLoaderAssigned(
                XrSettingsUtility.LoaderTypeName, BuildTargetGroup.Android);
            AppendCheck(lines, ref allOk, loaderAssigned,
                loaderAssigned
                    ? "XR Plug-in Management: Android 已启用 ARCoreLoader"
                    : "XR Plug-in Management: Android 未启用 ARCoreLoader（Project Settings > XR Plug-in Management > Android 勾选 ARCore）");

            // 场景文件
            bool sceneExists = System.IO.File.Exists(MainScenePath);
            AppendCheck(lines, ref allOk, sceneExists,
                "场景文件: " + MainScenePath + (sceneExists ? " 存在" : " 不存在（执行 Setup 菜单生成）"));

            // Build Settings
            bool sceneInBuildSettings = false;
            EditorBuildSettingsScene[] buildSettingsScenes = EditorBuildSettings.scenes;
            for (int index = 0; index < buildSettingsScenes.Length; index++)
            {
                EditorBuildSettingsScene buildScene = buildSettingsScenes[index];
                if (buildScene.path == MainScenePath && buildScene.enabled)
                {
                    sceneInBuildSettings = true;
                    break;
                }
            }

            AppendCheck(lines, ref allOk, sceneInBuildSettings,
                "Build Settings: " + (sceneInBuildSettings ? "Main.unity 已启用" : "Main.unity 未加入 / 未启用"));

            // applicationIdentifier
            string appId = PlayerSettings.applicationIdentifier;
            AppendCheck(lines, ref allOk, appId == ApplicationIdentifier,
                "applicationIdentifier: " + appId + "（期望 " + ApplicationIdentifier + "）");

            // minSdk
            int minSdk = (int)PlayerSettings.Android.minSdkVersion;
            AppendCheck(lines, ref allOk, minSdk >= 24,
                "Android minSdkVersion: " + minSdk + "（ARCore 要求 >= 24）");

            // IL2CPP
            ScriptingImplementation backend = PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android);
            AppendCheck(lines, ref allOk, backend == ScriptingImplementation.IL2CPP,
                "Scripting Backend: " + backend + "（应为 IL2CPP）");

            // ARM64
            AndroidArchitecture architectures = PlayerSettings.Android.targetArchitectures;
            bool arm64 = (architectures & AndroidArchitecture.ARM64) != 0;
            AppendCheck(lines, ref allOk, arm64,
                "Target Architectures: " + architectures + "（应包含 ARM64）");

            // 竖屏
            bool portrait = PlayerSettings.defaultInterfaceOrientation == UIOrientation.Portrait;
            AppendCheck(lines, ref allOk, portrait,
                "默认屏幕方向: " + PlayerSettings.defaultInterfaceOrientation + "（应为 Portrait）");

            // 输入处理
            int inputHandler = XrSettingsUtility.GetActiveInputHandler();
            string inputName = inputHandler == 0
                ? "Input Manager (Old)"
                : inputHandler == 1
                    ? "Input System Package (New)"
                    : inputHandler == 2
                        ? "Both"
                        : "未知";
            AppendCheck(lines, ref allOk, inputHandler == 0 || inputHandler == 2,
                "Active Input Handling: " + inputName
                + "（本工程使用 legacy Input + uGUI StandaloneInputModule，需 Both 或 Input Manager (Old)；改动后需重启编辑器）");

            // Graphics APIs
            GraphicsDeviceType[] graphicsApis = PlayerSettings.GetGraphicsAPIs(BuildTarget.Android);
            bool hasOpenGlEs3 = graphicsApis != null && graphicsApis.Length > 0
                                 && Array.IndexOf(graphicsApis, GraphicsDeviceType.OpenGLES3) >= 0;
            AppendCheck(lines, ref allOk, hasOpenGlEs3,
                "Android Graphics APIs: " + (graphicsApis != null ? string.Join(", ", Array.ConvertAll(graphicsApis, api => api.ToString())) : "未设置")
                + "（建议仅 OpenGLES3）");

            // 明文 HTTP
            bool insecureHttpAllowed = IsInsecureHttpAllowed();
            AppendCheck(lines, ref allOk, insecureHttpAllowed,
                "明文 HTTP: " + (insecureHttpAllowed ? "已允许" : "未允许（Android 9+ 会拒绝 http:// 请求）"));

            string report = (allOk ? "✅ 配置检查通过" : "⚠️ 存在未通过项") + "\n\n" + string.Join("\n", lines.ToArray());
            Debug.Log("[AR Inspection][Validate]\n" + report);
            EditorUtility.DisplayDialog("AR Inspection 配置检查", report + "\n\n详细内容见 Console。", "好");
        }

        [MenuItem("Tools/AR Inspection/Open Main Scene", false, 10)]
        public static void OpenMainScene()
        {
            if (!System.IO.File.Exists(MainScenePath))
            {
                EditorUtility.DisplayDialog(
                    "场景不存在",
                    "未找到 " + MainScenePath + "。\n请先执行 Tools > AR Inspection > Setup Android + ARCore Project。",
                    "好");
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            EditorSceneManager.OpenScene(MainScenePath);
        }

        // ---------- PlayerSettings ----------

        private static void ConfigurePlayerSettings(List<string> done, List<string> warnings)
        {
            try
            {
                PlayerSettings.companyName = "HuaFeng";
                PlayerSettings.productName = "AR Inspection";
                PlayerSettings.bundleVersion = "1.0.0";
                PlayerSettings.Android.bundleVersionCode = 1;
                PlayerSettings.applicationIdentifier = ApplicationIdentifier;
                PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel24;

                PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
                PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;

                PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
                PlayerSettings.allowedAutorotateToPortrait = true;
                PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
                PlayerSettings.allowedAutorotateToLandscapeLeft = false;
                PlayerSettings.allowedAutorotateToLandscapeRight = false;

                done.Add("PlayerSettings: com.huafeng.arinspection / minSdk 24 / IL2CPP / ARM64 / 竖屏");
                AssetDatabase.SaveAssets();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                warnings.Add("配置 PlayerSettings 时发生异常: " + exception.Message + "（请按 README 手动设置）");
            }
        }

        private static bool ConfigureGraphicsApis()
        {
            try
            {
                PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
                PlayerSettings.SetGraphicsAPIs(
                    BuildTarget.Android,
                    new[] { GraphicsDeviceType.OpenGLES3 });
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[AR Inspection] 设置 Graphics APIs 失败: " + exception.Message);
                return false;
            }
        }

        private static bool IsInsecureHttpAllowed()
        {
            try
            {
                System.Reflection.PropertyInfo property = typeof(PlayerSettings).GetProperty("insecureHttpOption");
                if (property != null)
                {
                    object value = property.GetValue(null, null);
                    return value != null && value.ToString() == "AlwaysAllowed";
                }
            }
            catch (Exception)
            {
                // 忽略，视为未配置
            }

            return false;
        }

        // ---------- 场景生成 ----------

        private static void CreateMainScene(List<string> done, List<string> warnings)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // --- AR Session（负责开启 / 关闭 XR 会话与 XR 输入子系统） ---
            GameObject sessionObject = new GameObject("AR Session");
            sessionObject.AddComponent<ARSession>();
            sessionObject.AddComponent<ARInputManager>();

            // --- XR Origin（设备跟踪 / 坐标转换） ---
            GameObject originObject = new GameObject("XR Origin");
            XROrigin xrOrigin = originObject.AddComponent<XROrigin>();

            GameObject cameraOffsetObject = new GameObject("Camera Offset");
            cameraOffsetObject.transform.SetParent(originObject.transform, false);
            cameraOffsetObject.transform.localPosition = Vector3.zero;

            GameObject cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetParent(cameraOffsetObject.transform, false);

            Camera arCamera = cameraObject.AddComponent<Camera>();
            arCamera.clearFlags = CameraClearFlags.SolidColor;
            arCamera.backgroundColor = Color.black;
            arCamera.nearClipPlane = 0.1f;
            arCamera.farClipPlane = 1000f;
            cameraObject.AddComponent<AudioListener>();
            cameraObject.AddComponent<ARCameraManager>();
            cameraObject.AddComponent<ARCameraBackground>();

            xrOrigin.Camera = arCamera;
            xrOrigin.CameraFloorOffsetObject = cameraOffsetObject;
            xrOrigin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Device;

            // 姿态驱动：优先官方 TrackedPoseDriver / ARPoseDriver（反射添加，包没装也不会编译失败）
            string poseDriverName;
            Component poseDriver = XrSettingsUtility.TryAddPoseDriver(cameraObject, out poseDriverName);
            if (poseDriver != null)
            {
                done.Add("AR 相机已挂载姿态驱动: " + poseDriverName);
            }
            else
            {
                warnings.Add("未找到 TrackedPoseDriver / ARPoseDriver（通常是未安装 com.unity.inputsystem）。"
                             + "已挂自研 XrPoseFallback 兜底驱动；若仍无跟踪，请安装 Input System 并手动给 Main Camera 添加 TrackedPoseDriver。");
            }

            // 兜底驱动：官方驱动生效时不会修改任何东西；官方驱动缺失 / 未生效时接管相机位姿。
            XrPoseFallback fallback = cameraObject.AddComponent<XrPoseFallback>();
            fallback.positionThreshold = 0.01f;
            fallback.rotationThreshold = 2f;

            // --- 平面检测 / 射线检测（RequireComponent(XROrigin)，必须与 XROrigin 同物体） ---
            ARPlaneManager planeManager = originObject.AddComponent<ARPlaneManager>();
            planeManager.requestedDetectionMode = PlaneDetectionMode.Horizontal;
            ARRaycastManager raycastManager = originObject.AddComponent<ARRaycastManager>();

            // --- 灯光（标记 / 平面贴片使用 Standard 材质，没有灯会变黑） ---
            GameObject lightObject = new GameObject("Directional Light");
            Light directionalLight = lightObject.AddComponent<Light>();
            directionalLight.type = LightType.Directional;
            directionalLight.color = Color.white;
            directionalLight.intensity = 1f;
            lightObject.transform.rotation = Quaternion.Euler(45f, -30f, 0f);

            // --- 业务对象 ---
            GameObject appObject = new GameObject("ARInspectionApp");
            AppConfig appConfig = appObject.AddComponent<AppConfig>();
            ApiClient apiClient = appObject.AddComponent<ApiClient>();
            apiClient.config = appConfig;

            IssueFormUI formUI = appObject.AddComponent<IssueFormUI>();
            formUI.apiClient = apiClient;
            formUI.appConfig = appConfig;

            PlaneVisualizer planeVisualizer = appObject.AddComponent<PlaneVisualizer>();
            planeVisualizer.planeManager = planeManager;

            ArPlacementController placementController = appObject.AddComponent<ArPlacementController>();
            placementController.raycastManager = raycastManager;
            placementController.planeManager = planeManager;
            placementController.formUI = formUI;
            placementController.planeVisualizer = planeVisualizer;

            // --- 保存场景 ---
            bool saved = EditorSceneManager.SaveScene(scene, MainScenePath);
            if (saved)
            {
                done.Add("已生成场景 " + MainScenePath);
            }
            else
            {
                warnings.Add("保存场景失败: " + MainScenePath + "（请检查 Assets/Scenes 目录写权限）");
            }

            Selection.activeGameObject = originObject;
        }

        private static void AddSceneToBuildSettings(string scenePath)
        {
            List<EditorBuildSettingsScene> scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            int existingIndex = scenes.FindIndex(buildScene => buildScene.path == scenePath);
            if (existingIndex >= 0)
            {
                scenes[existingIndex] = new EditorBuildSettingsScene(scenePath, true);
            }
            else
            {
                scenes.Insert(0, new EditorBuildSettingsScene(scenePath, true));
            }

            EditorBuildSettings.scenes = scenes.ToArray();
        }

        // ---------- 输出 ----------

        private static void AppendCheck(List<string> lines, ref bool allOk, bool passed, string message)
        {
            lines.Add((passed ? "[OK] " : "[!!] ") + message);
            if (!passed) allOk = false;
        }

        private static string BuildReport(List<string> done, List<string> warnings)
        {
            System.Text.StringBuilder builder = new System.Text.StringBuilder();
            builder.AppendLine("已完成：");
            for (int index = 0; index < done.Count; index++)
            {
                builder.AppendLine("  ✓ " + done[index]);
            }

            if (warnings.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine("需要手动处理 / 请确认：");
                for (int index = 0; index < warnings.Count; index++)
                {
                    builder.AppendLine("  ! " + warnings[index]);
                }

                builder.AppendLine();
                builder.AppendLine("手动兜底步骤见 unity/README.md「手动兜底步骤」一节。");
            }

            return builder.ToString();
        }
    }
}
#endif
