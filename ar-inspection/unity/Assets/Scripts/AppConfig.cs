using UnityEngine;

namespace ArInspection
{
    /// <summary>
    /// 后端地址配置。挂在场景中的任意 GameObject 上（一键 Setup 脚本会挂在 ARInspectionApp 上）。
    ///
    /// 规则：
    /// 1. Inspector 里的 serverUrl 是默认值（默认 http://127.0.0.1:8080）；
    /// 2. 运行时通过“后端设置”面板修改后写入 PlayerPrefs，下次启动优先使用保存值；
    /// 3. 其它脚本统一通过静态属性 <see cref="BaseUrl"/> 读取，保证末尾不带 '/'。
    /// </summary>
    [DisallowMultipleComponent]
    public class AppConfig : MonoBehaviour
    {
        /// <summary>默认后端地址（本机调试）。真机请改为 PC 的局域网 IP，例如 http://192.168.1.20:8080。</summary>
        public const string DefaultServerUrl = "http://127.0.0.1:8080";

        /// <summary>PlayerPrefs 中保存运行时覆盖地址的 key。</summary>
        public const string PlayerPrefsKey = "ar_inspection.server_url";

        [Tooltip("后端 Base URL，默认 http://127.0.0.1:8080。真机请改成 PC 的局域网 IP，例如 http://192.168.1.20:8080")]
        [SerializeField]
        private string serverUrl = DefaultServerUrl;

        [Tooltip("是否优先使用 PlayerPrefs 中保存的地址（运行时在设置面板改过地址后，下次启动会覆盖 Inspector 值）")]
        [SerializeField]
        private bool preferRuntimeOverride = true;

        private static AppConfig instance;

        /// <summary>当前场景中的 AppConfig 实例（可能为 null，调用方需判空）。</summary>
        public static AppConfig Instance
        {
            get { return instance; }
        }

        /// <summary>Inspector 配置 / 当前生效的服务器地址（已规范化，无尾部 '/'）。</summary>
        public string ServerUrl
        {
            get { return Normalize(serverUrl); }
        }

        /// <summary>
        /// 全局生效的后端 Base URL（无尾部 '/'）。
        /// 有实例时用实例配置；没有实例时退回 PlayerPrefs；再退回默认值。
        /// </summary>
        public static string BaseUrl
        {
            get
            {
                if (instance != null)
                {
                    return instance.ServerUrl;
                }

                string saved = PlayerPrefs.GetString(PlayerPrefsKey, string.Empty);
                if (!string.IsNullOrEmpty(saved))
                {
                    return Normalize(saved);
                }

                return DefaultServerUrl;
            }
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                // 原型只有一个场景，这里不销毁，只记录警告，避免误删手动摆放的组件。
                Debug.LogWarning("[AppConfig] 场景中存在多个 AppConfig，将使用第一个实例。");
                return;
            }

            instance = this;

            if (preferRuntimeOverride)
            {
                string saved = PlayerPrefs.GetString(PlayerPrefsKey, string.Empty);
                if (!string.IsNullOrEmpty(saved))
                {
                    string normalized = Normalize(saved);
                    if (normalized != Normalize(serverUrl))
                    {
                        serverUrl = normalized;
                        Debug.Log("[AppConfig] 使用 PlayerPrefs 中保存的后端地址: " + serverUrl);
                    }
                }
            }

            Debug.Log("[AppConfig] 后端地址 = " + ServerUrl);
        }

        private void OnDestroy()
        {
            if (instance == this)
            {
                instance = null;
            }
        }

        private void OnValidate()
        {
            serverUrl = Normalize(serverUrl);
        }

        /// <summary>运行时修改后端地址并持久化（下次启动仍生效）。</summary>
        public void SetServerUrl(string url)
        {
            SaveServerUrlOverride(url);
        }

        /// <summary>
        /// 静态版本：即使场景里暂时找不到 AppConfig 实例，也能把地址写入 PlayerPrefs。
        /// 真机改 IP 时不需要重新出包。
        /// </summary>
        public static void SaveServerUrlOverride(string url)
        {
            string normalized = Normalize(url);
            if (instance != null)
            {
                instance.serverUrl = normalized;
            }

            PlayerPrefs.SetString(PlayerPrefsKey, normalized);
            PlayerPrefs.Save();
            Debug.Log("[AppConfig] 已保存后端地址: " + normalized);
        }

        /// <summary>清除运行时覆盖，回到 Inspector / 默认配置。</summary>
        public static void ClearSavedServerUrl()
        {
            PlayerPrefs.DeleteKey(PlayerPrefsKey);
            PlayerPrefs.Save();
            Debug.Log("[AppConfig] 已清除运行时保存的后端地址。");
        }

        /// <summary>
        /// 规范化 URL：去首尾空白、去尾部 '/'。空字符串返回默认地址。
        /// 注意：不自动补 http://，补协议在 UI 层做（避免模型层做太多猜测）。
        /// </summary>
        public static string Normalize(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                return DefaultServerUrl;
            }

            string trimmed = url.Trim();
            if (trimmed.Length == 0)
            {
                return DefaultServerUrl;
            }

            return trimmed.TrimEnd('/');
        }
    }
}
