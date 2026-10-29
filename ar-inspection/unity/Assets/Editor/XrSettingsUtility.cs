#if UNITY_EDITOR
using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace ArInspection.EditorTools
{
    /// <summary>
    /// XR Plug-in Management / PlayerSettings 的可选 API 封装。
    ///
    /// 本机无法安装 Unity 编译验证，所以对「可能随包版本变化」的 API（XR Management 编辑器接口、
    /// 新版 PlayerSettings 属性、Input System 的 TrackedPoseDriver 等）统一用反射调用：
    /// - 存在就正常执行；
    /// - 不存在则返回 false + 可读提示，由调用方回退到 README 的手动步骤，绝不会导致编辑器脚本编译失败。
    /// </summary>
    public static class XrSettingsUtility
    {
        public const string ArCoreLoaderTypeName = "UnityEngine.XR.ARCore.ARCoreLoader";

        private static readonly string[] PoseDriverTypeNames =
        {
            "UnityEngine.InputSystem.XR.TrackedPoseDriver, Unity.InputSystem",
            "UnityEngine.XR.ARFoundation.ARPoseDriver, Unity.XR.ARFoundation",
            "UnityEngine.SpatialTracking.TrackedPoseDriver, Unity.XR.LegacyInputHelpers"
        };

        /// <summary>ARCore 在 AR Foundation 6 中对应的 XR Loader 类型全名（用于 XR Plug-in Management）。</summary>
        public static string LoaderTypeName
        {
            get { return ArCoreLoaderTypeName; }
        }

        // ---------- XR Plug-in Management ----------

        /// <summary>
        /// 通过 UnityEditor.XR.Management.XRGeneralSettingsPerBuildTarget +
        /// XRPackageMetadataStore.AssignLoader(...) 为指定平台启用 loader（本工程是 Android + ARCore）。
        /// 全部通过反射调用，避免不同 XR Management 版本 API 差异导致编译失败。
        /// </summary>
        public static bool EnableLoader(string loaderTypeName, BuildTargetGroup buildTargetGroup, out string message)
        {
            message = string.Empty;

            Type perBuildTargetType = FindType("UnityEditor.XR.Management.XRGeneralSettingsPerBuildTarget", "Unity.XR.Management.Editor");
            Type generalSettingsType = FindType("UnityEngine.XR.Management.XRGeneralSettings", "Unity.XR.Management");
            Type metadataStoreType = FindType("UnityEditor.XR.Management.Metadata.XRPackageMetadataStore", "Unity.XR.Management.Editor");

            if (perBuildTargetType == null || generalSettingsType == null || metadataStoreType == null)
            {
                message = "未找到 XR Plug-in Management 编辑器 API（请确认 com.unity.xr.management 已安装）。"
                          + "手动步骤：Project Settings > XR Plug-in Management > Android 勾选 ARCore。";
                return false;
            }

            try
            {
                string settingsKey = "com.unity.xr.management.loader_settings";
                FieldInfo keyField = generalSettingsType.GetField(
                    "k_SettingsKey",
                    BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
                if (keyField != null)
                {
                    // 用 GetValue 而不是 GetRawConstantValue：兼容 const 与 static readonly 两种实现。
                    string constant = keyField.GetValue(null) as string;
                    if (!string.IsNullOrEmpty(constant)) settingsKey = constant;
                }

                UnityEngine.Object perBuildTarget = GetOrCreatePerBuildTargetSettings(perBuildTargetType, settingsKey);
                if (perBuildTarget == null)
                {
                    message = "无法创建 XRGeneralSettingsPerBuildTarget 配置对象。"
                              + "手动步骤：Project Settings > XR Plug-in Management > Android 勾选 ARCore。";
                    return false;
                }

                object generalSettings = GetOrCreateGeneralSettings(
                    perBuildTarget, perBuildTargetType, generalSettingsType, buildTargetGroup);
                if (generalSettings == null)
                {
                    message = "无法为 Android 创建 XRGeneralSettings。"
                              + "手动步骤：Project Settings > XR Plug-in Management > Android 勾选 ARCore。";
                    return false;
                }

                object managerSettings = GetAssignedManagerSettings(generalSettings, generalSettingsType);
                if (managerSettings == null)
                {
                    // 部分版本需要先创建默认 XRManagerSettings。
                    MethodInfo createDefaultManager = perBuildTargetType.GetMethod(
                        "CreateDefaultManagerSettingsForBuildTarget",
                        new[] { typeof(BuildTargetGroup) });
                    if (createDefaultManager != null)
                    {
                        createDefaultManager.Invoke(perBuildTarget, new object[] { buildTargetGroup });
                        EditorUtility.SetDirty(perBuildTarget);
                        AssetDatabase.SaveAssets();

                        generalSettings = GetOrCreateGeneralSettings(
                            perBuildTarget, perBuildTargetType, generalSettingsType, buildTargetGroup);
                        managerSettings = generalSettings != null
                            ? GetAssignedManagerSettings(generalSettings, generalSettingsType)
                            : null;
                    }
                }

                if (managerSettings == null)
                {
                    message = "无法获取 XRManagerSettings（AssignedSettings 为空）。"
                              + "手动步骤：Project Settings > XR Plug-in Management > Android 勾选 ARCore。";
                    return false;
                }

                MethodInfo assignLoader = FindStaticMethod(metadataStoreType, "AssignLoader", 3);
                if (assignLoader == null)
                {
                    message = "未找到 XRPackageMetadataStore.AssignLoader 方法。"
                              + "手动步骤：Project Settings > XR Plug-in Management > Android 勾选 ARCore。";
                    return false;
                }

                object result = assignLoader.Invoke(
                    null, new object[] { managerSettings, loaderTypeName, buildTargetGroup });
                bool success = result is bool && (bool)result;
                if (!success)
                {
                    message = "AssignLoader 返回 false，可能是 ARCore 包（com.unity.xr.arcore）未安装或版本不匹配。"
                              + "手动步骤：Project Settings > XR Plug-in Management > Android 勾选 ARCore。";
                    return false;
                }

                EditorUtility.SetDirty(perBuildTarget);
                AssetDatabase.SaveAssets();
                message = "已在 XR Plug-in Management（" + buildTargetGroup + "）启用 loader: " + loaderTypeName;
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                message = "启用 XR Loader 时发生异常: " + exception.Message
                          + "。手动步骤：Project Settings > XR Plug-in Management > Android 勾选 ARCore。";
                return false;
            }
        }

        /// <summary>查询某平台是否已启用指定 loader（用于 Validate 菜单）。</summary>
        public static bool IsLoaderAssigned(string loaderTypeName, BuildTargetGroup buildTargetGroup)
        {
            Type metadataStoreType = FindType("UnityEditor.XR.Management.Metadata.XRPackageMetadataStore", "Unity.XR.Management.Editor");
            if (metadataStoreType == null) return false;

            try
            {
                MethodInfo isLoaderAssigned = FindStaticMethod(metadataStoreType, "IsLoaderAssigned", 2);
                if (isLoaderAssigned != null)
                {
                    object result = isLoaderAssigned.Invoke(null, new object[] { loaderTypeName, buildTargetGroup });
                    return result is bool && (bool)result;
                }

                // 退路：直接看 AssignedSettings.activeLoaders 里有没有目标类型。
                Type perBuildTargetType = FindType("UnityEditor.XR.Management.XRGeneralSettingsPerBuildTarget", "Unity.XR.Management.Editor");
                Type generalSettingsType = FindType("UnityEngine.XR.Management.XRGeneralSettings", "Unity.XR.Management");
                if (perBuildTargetType == null || generalSettingsType == null) return false;

                object generalSettings = GetGeneralSettingsForBuildTarget(perBuildTargetType, generalSettingsType, buildTargetGroup);
                object managerSettings = generalSettings != null
                    ? GetAssignedManagerSettings(generalSettings, generalSettingsType)
                    : null;
                if (managerSettings == null) return false;

                PropertyInfo activeLoadersProperty = managerSettings.GetType().GetProperty("activeLoaders");
                if (activeLoadersProperty == null) return false;

                System.Collections.IEnumerable loaders = activeLoadersProperty.GetValue(managerSettings, null) as System.Collections.IEnumerable;
                if (loaders == null) return false;

                foreach (object loader in loaders)
                {
                    if (loader != null && loader.GetType().FullName == loaderTypeName) return true;
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[AR Inspection] 查询 XR Loader 状态失败: " + exception.Message);
            }

            return false;
        }

        private static UnityEngine.Object GetOrCreatePerBuildTargetSettings(Type perBuildTargetType, string settingsKey)
        {
            // EditorBuildSettings.TryGetConfigObject<T>(name, out T obj) 是泛型方法，需要反射调用。
            MethodInfo tryGetConfigObject = null;
            MethodInfo[] editorBuildSettingsMethods = typeof(EditorBuildSettings).GetMethods(
                BindingFlags.Public | BindingFlags.Static);
            for (int index = 0; index < editorBuildSettingsMethods.Length; index++)
            {
                MethodInfo method = editorBuildSettingsMethods[index];
                if (method.Name != "TryGetConfigObject" || !method.IsGenericMethodDefinition) continue;
                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length == 2 && parameters[0].ParameterType == typeof(string))
                {
                    tryGetConfigObject = method;
                    break;
                }
            }

            UnityEngine.Object existing = null;
            if (tryGetConfigObject != null)
            {
                try
                {
                    object[] arguments = { settingsKey, null };
                    bool found = (bool)tryGetConfigObject.MakeGenericMethod(perBuildTargetType).Invoke(null, arguments);
                    if (found)
                    {
                        existing = arguments[1] as UnityEngine.Object;
                    }
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("[AR Inspection] 读取 XR 配置失败（将新建）: " + exception.Message);
                }
            }

            if (existing != null) return existing;

            ScriptableObject settings = ScriptableObject.CreateInstance(perBuildTargetType);
            if (settings == null) return null;

            settings.name = "XRGeneralSettingsPerBuildTarget";
            EnsureFolder("Assets/XR");
            string assetPath = "Assets/XR/XRGeneralSettingsPerBuildTarget.asset";
            AssetDatabase.CreateAsset(settings, assetPath);
            EditorBuildSettings.AddConfigObject(settingsKey, settings, true);
            AssetDatabase.SaveAssets();
            return settings;
        }

        private static object GetOrCreateGeneralSettings(
            UnityEngine.Object perBuildTarget,
            Type perBuildTargetType,
            Type generalSettingsType,
            BuildTargetGroup buildTargetGroup)
        {
            object settings = GetGeneralSettingsForBuildTarget(perBuildTargetType, generalSettingsType, buildTargetGroup);
            if (settings != null) return settings;

            MethodInfo createDefault = perBuildTargetType.GetMethod(
                "CreateDefaultSettingsForBuildTarget",
                new[] { typeof(BuildTargetGroup) });
            if (createDefault != null)
            {
                createDefault.Invoke(perBuildTarget, new object[] { buildTargetGroup });
                EditorUtility.SetDirty(perBuildTarget);
                AssetDatabase.SaveAssets();

                settings = GetGeneralSettingsForBuildTarget(perBuildTargetType, generalSettingsType, buildTargetGroup);
                if (settings != null) return settings;
            }

            MethodInfo setSettings = perBuildTargetType.GetMethod(
                "SetSettingsForBuildTarget",
                new[] { typeof(BuildTargetGroup), generalSettingsType });
            if (setSettings == null) return null;

            ScriptableObject created = ScriptableObject.CreateInstance(generalSettingsType);
            if (created == null) return null;

            created.name = buildTargetGroup + " Settings";
            AssetDatabase.AddObjectToAsset(created, perBuildTarget);
            setSettings.Invoke(perBuildTarget, new object[] { buildTargetGroup, created });
            EditorUtility.SetDirty(perBuildTarget);
            AssetDatabase.SaveAssets();

            return GetGeneralSettingsForBuildTarget(perBuildTargetType, generalSettingsType, buildTargetGroup);
        }

        private static object GetGeneralSettingsForBuildTarget(
            Type perBuildTargetType,
            Type generalSettingsType,
            BuildTargetGroup buildTargetGroup)
        {
            object perBuildTarget = GetPerBuildTargetSettingsFromEditorBuildSettings(perBuildTargetType);
            if (perBuildTarget == null) return null;

            MethodInfo settingsForBuildTarget = perBuildTargetType.GetMethod(
                "SettingsForBuildTarget", new[] { typeof(BuildTargetGroup) });
            if (settingsForBuildTarget == null) return null;

            return settingsForBuildTarget.Invoke(perBuildTarget, new object[] { buildTargetGroup });
        }

        private static object GetPerBuildTargetSettingsFromEditorBuildSettings(Type perBuildTargetType)
        {
            MethodInfo tryGetConfigObject = null;
            MethodInfo[] methods = typeof(EditorBuildSettings).GetMethods(BindingFlags.Public | BindingFlags.Static);
            for (int index = 0; index < methods.Length; index++)
            {
                MethodInfo method = methods[index];
                if (method.Name != "TryGetConfigObject" || !method.IsGenericMethodDefinition) continue;
                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length == 2 && parameters[0].ParameterType == typeof(string))
                {
                    tryGetConfigObject = method;
                    break;
                }
            }

            if (tryGetConfigObject == null) return null;

            string settingsKey = "com.unity.xr.management.loader_settings";
            Type generalSettingsType = FindType("UnityEngine.XR.Management.XRGeneralSettings", "Unity.XR.Management");
            if (generalSettingsType != null)
            {
                FieldInfo keyField = generalSettingsType.GetField(
                    "k_SettingsKey",
                    BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
                if (keyField != null)
                {
                    string constant = keyField.GetValue(null) as string;
                    if (!string.IsNullOrEmpty(constant)) settingsKey = constant;
                }
            }

            try
            {
                object[] arguments = { settingsKey, null };
                bool found = (bool)tryGetConfigObject.MakeGenericMethod(perBuildTargetType).Invoke(null, arguments);
                return found ? arguments[1] : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static object GetAssignedManagerSettings(object generalSettings, Type generalSettingsType)
        {
            PropertyInfo assignedSettings = generalSettingsType.GetProperty("AssignedSettings");
            return assignedSettings != null ? assignedSettings.GetValue(generalSettings, null) : null;
        }

        // ---------- 姿态驱动 ----------

        /// <summary>
        /// 反射尝试给 AR 相机挂官方姿态驱动（TrackedPoseDriver / ARPoseDriver）。
        /// 都没找到时返回 null，由调用方挂 ArInspection.XrPoseFallback 兜底。
        /// </summary>
        public static Component TryAddPoseDriver(GameObject target, out string driverName)
        {
            driverName = null;
            if (target == null) return null;

            for (int index = 0; index < PoseDriverTypeNames.Length; index++)
            {
                string assemblyQualifiedName = PoseDriverTypeNames[index];
                Type driverType = Type.GetType(assemblyQualifiedName, false);
                if (driverType == null)
                {
                    string fullName = assemblyQualifiedName;
                    int commaIndex = fullName.IndexOf(',');
                    if (commaIndex > 0) fullName = fullName.Substring(0, commaIndex).Trim();
                    driverType = FindType(fullName);
                }

                if (driverType == null) continue;
                if (!typeof(Component).IsAssignableFrom(driverType)) continue;

                try
                {
                    if (target.GetComponent(driverType) != null) continue;
                    Component driver = target.AddComponent(driverType);
                    driverName = driverType.FullName;
                    return driver;
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("[AR Inspection] 挂载姿态驱动 " + assemblyQualifiedName + " 失败: " + exception.Message);
                }
            }

            return null;
        }

        // ---------- PlayerSettings ----------

        /// <summary>读取 Active Input Handling：0 = Input Manager (Old)，1 = Input System (New)，2 = Both，-1 = 未知。</summary>
        public static int GetActiveInputHandler()
        {
            try
            {
                SerializedObject playerSettingsAsset = GetPlayerSettingsSerializedObject();
                if (playerSettingsAsset == null) return -1;

                SerializedProperty property = playerSettingsAsset.FindProperty("activeInputHandler");
                return property != null ? property.intValue : -1;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[AR Inspection] 读取 Active Input Handling 失败: " + exception.Message);
                return -1;
            }
        }

        /// <summary>设置 Active Input Handling：0 = Old，1 = New，2 = Both（需要重启编辑器生效）。</summary>
        public static bool SetActiveInputHandler(int value)
        {
            try
            {
                SerializedObject playerSettingsAsset = GetPlayerSettingsSerializedObject();
                if (playerSettingsAsset == null) return false;

                SerializedProperty property = playerSettingsAsset.FindProperty("activeInputHandler");
                if (property == null) return false;

                property.intValue = value;
                playerSettingsAsset.ApplyModifiedProperties();
                AssetDatabase.SaveAssets();
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[AR Inspection] 设置 Active Input Handling 失败: " + exception.Message);
                return false;
            }
        }

        /// <summary>允许下载 / 请求明文 HTTP（局域网后端是 http://，Android 9+ 默认禁止明文流量）。</summary>
        public static bool TryAllowInsecureHttp()
        {
            // 优先使用 PlayerSettings.insecureHttpOption（Unity 2021.2+）
            try
            {
                PropertyInfo property = typeof(PlayerSettings).GetProperty(
                    "insecureHttpOption", BindingFlags.Public | BindingFlags.Static);
                if (property != null && property.CanWrite && property.PropertyType.IsEnum)
                {
                    object alwaysAllowed = Enum.Parse(property.PropertyType, "AlwaysAllowed");
                    property.SetValue(null, alwaysAllowed, null);
                    return true;
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[AR Inspection] 设置 insecureHttpOption 失败（尝试序列化字段兜底）: " + exception.Message);
            }

            // 退路：直接改 ProjectSettings.asset 的序列化字段
            try
            {
                SerializedObject playerSettingsAsset = GetPlayerSettingsSerializedObject();
                if (playerSettingsAsset == null) return false;

                SerializedProperty property = playerSettingsAsset.FindProperty("insecureHttpOption");
                if (property == null) return false;

                property.intValue = 2; // AlwaysAllowed
                playerSettingsAsset.ApplyModifiedProperties();
                AssetDatabase.SaveAssets();
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[AR Inspection] 设置明文 HTTP 失败: " + exception.Message);
                return false;
            }
        }

        /// <summary>强制勾选 Android 的 INTERNET 权限（PlayerSettings.Android.forceInternetPermission）。</summary>
        public static bool TryForceInternetPermission()
        {
            try
            {
                PropertyInfo property = typeof(PlayerSettings.Android).GetProperty(
                    "forceInternetPermission", BindingFlags.Public | BindingFlags.Static);
                if (property != null && property.CanWrite && property.PropertyType == typeof(bool))
                {
                    property.SetValue(null, true, null);
                    return true;
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[AR Inspection] 设置 forceInternetPermission 失败: " + exception.Message);
            }

            return false;
        }

        private static SerializedObject GetPlayerSettingsSerializedObject()
        {
            UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
            if (assets == null || assets.Length == 0) return null;
            return new SerializedObject(assets[0]);
        }

        // ---------- 工具方法 ----------

        /// <summary>按全名查找类型：先直接查，再按程序集名查，最后遍历所有已加载程序集。</summary>
        public static Type FindType(string fullName, params string[] assemblyNames)
        {
            if (string.IsNullOrEmpty(fullName)) return null;

            Type type = Type.GetType(fullName, false);
            if (type != null) return type;

            if (assemblyNames != null)
            {
                for (int index = 0; index < assemblyNames.Length; index++)
                {
                    string assemblyName = assemblyNames[index];
                    if (string.IsNullOrEmpty(assemblyName)) continue;
                    type = Type.GetType(fullName + ", " + assemblyName, false);
                    if (type != null) return type;
                }
            }

            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int index = 0; index < assemblies.Length; index++)
            {
                type = assemblies[index].GetType(fullName, false);
                if (type != null) return type;
            }

            return null;
        }

        private static MethodInfo FindStaticMethod(Type type, string methodName, int parameterCount)
        {
            MethodInfo[] methods = type.GetMethods(BindingFlags.Public | BindingFlags.Static);
            for (int index = 0; index < methods.Length; index++)
            {
                MethodInfo method = methods[index];
                if (method.Name == methodName && method.GetParameters().Length == parameterCount)
                {
                    return method;
                }
            }

            return null;
        }

        /// <summary>确保 Assets 下的目录存在（逐级创建）。</summary>
        public static void EnsureFolder(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath)) return;
            if (AssetDatabase.IsValidFolder(folderPath)) return;

            string normalized = folderPath.Replace('\\', '/').TrimEnd('/');
            int lastSlashIndex = normalized.LastIndexOf('/');
            if (lastSlashIndex <= 0) return;

            string parent = normalized.Substring(0, lastSlashIndex);
            string folderName = normalized.Substring(lastSlashIndex + 1);

            EnsureFolder(parent);
            if (!AssetDatabase.IsValidFolder(normalized))
            {
                AssetDatabase.CreateFolder(parent, folderName);
            }
        }
    }
}
#endif
