using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace ArInspection
{
    /// <summary>
    /// 后端 HTTP 客户端（协程 + UnityWebRequest 手写）。
    ///
    /// 只依赖 docs/API.md 冻结的接口：
    ///   POST /api/issues            上报问题 -> 201 + Issue
    ///   GET  /api/health            健康检查 -> 200
    ///
    /// 为什么不使用 UnityWebRequest.Post(url, json)：
    /// 该重载在不同 Unity 版本里存在行为差异（Content-Type / upload handler 处理不同），
    /// 这里显式构造 UploadHandlerRaw + DownloadHandlerBuffer，写法在 Unity 6 上最稳定。
    /// </summary>
    [DisallowMultipleComponent]
    public class ApiClient : MonoBehaviour
    {
        /// <summary>请求超时（秒）。</summary>
        public const int TimeoutSeconds = 15;

        [Tooltip("后端地址配置组件。留空时启动自动从场景中查找。")]
        public AppConfig config;

        private static ApiClient instance;

        /// <summary>当前 ApiClient 实例（可能为 null，调用方需判空）。</summary>
        public static ApiClient Instance
        {
            get { return instance; }
        }

        /// <summary>当前使用的后端 Base URL（无尾部 '/'）。</summary>
        public static string BaseUrl
        {
            get { return AppConfig.BaseUrl; }
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Debug.LogWarning("[ApiClient] 场景中存在多个 ApiClient，将使用第一个实例。");
                return;
            }

            instance = this;

            if (config == null)
            {
                config = FindFirstObjectByType<AppConfig>();
            }

            Debug.Log("[ApiClient] 初始化完成，后端地址 = " + BaseUrl);
        }

        private void OnDestroy()
        {
            if (instance == this)
            {
                instance = null;
            }
        }

        /// <summary>
        /// 上报问题（POST /api/issues）。
        /// 成功回调收到后端返回的完整 Issue（含 id / status / 时间戳）；
        /// 失败回调收到可直接展示给用户的中文错误信息。
        /// </summary>
        public void CreateIssue(IssueCreateRequest request, Action<Issue> onSuccess, Action<string> onError)
        {
            if (request == null)
            {
                if (onError != null) onError("内部错误：请求数据为空");
                return;
            }

            StartCoroutine(CreateIssueRoutine(request, onSuccess, onError));
        }

        /// <summary>
        /// 后端健康检查（GET /api/health）。onResult(是否在线, 在线时返回版本号 / 离线时返回错误描述)。
        /// </summary>
        public void Health(Action<bool, string> onResult)
        {
            StartCoroutine(HealthRoutine(onResult));
        }

        private IEnumerator CreateIssueRoutine(IssueCreateRequest request, Action<Issue> onSuccess, Action<string> onError)
        {
            string url = BaseUrl + "/api/issues";
            string json = JsonUtility.ToJson(request);
            Debug.Log("[ApiClient] POST " + url + "\n请求体: " + json);

            using (UnityWebRequest webRequest = new UnityWebRequest(url, "POST"))
            {
                byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
                webRequest.uploadHandler = new UploadHandlerRaw(bodyRaw);
                webRequest.downloadHandler = new DownloadHandlerBuffer();
                webRequest.SetRequestHeader("Content-Type", "application/json; charset=utf-8");
                webRequest.SetRequestHeader("Accept", "application/json");
                webRequest.timeout = TimeoutSeconds;
                // 本项目后端是局域网 HTTP（非 HTTPS），Android 端需要 PlayerSettings 允许明文 HTTP，
                // 一键 Setup 脚本已经设置 insecureHttpOption = AlwaysAllowed。

                yield return webRequest.SendWebRequest();

                long statusCode = webRequest.responseCode;
                string responseText = webRequest.downloadHandler != null ? webRequest.downloadHandler.text : string.Empty;

                Debug.Log("[ApiClient] POST " + url + " -> HTTP " + statusCode
                          + (string.IsNullOrEmpty(responseText) ? "（空响应）" : "\n响应体: " + responseText));

                bool httpOk = statusCode >= 200 && statusCode < 300;
                if (!httpOk)
                {
                    string message = DescribeHttpError(statusCode, responseText, webRequest.error);
                    Debug.LogWarning("[ApiClient] 上报失败: " + message);
                    if (onError != null) onError(message);
                    yield break;
                }

                Issue issue = null;
                try
                {
                    issue = JsonUtility.FromJson<Issue>(responseText);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("[ApiClient] 解析 Issue 失败: " + exception.Message);
                }

                if (issue == null || string.IsNullOrEmpty(issue.id))
                {
                    if (onError != null) onError("上报成功，但无法解析后端返回的数据：" + PreviewText(responseText));
                    yield break;
                }

                if (onSuccess != null) onSuccess(issue);
            }
        }

        private IEnumerator HealthRoutine(Action<bool, string> onResult)
        {
            string url = BaseUrl + "/api/health";

            using (UnityWebRequest webRequest = UnityWebRequest.Get(url))
            {
                webRequest.timeout = TimeoutSeconds;
                webRequest.SetRequestHeader("Accept", "application/json");

                yield return webRequest.SendWebRequest();

                long statusCode = webRequest.responseCode;
                string responseText = webRequest.downloadHandler != null ? webRequest.downloadHandler.text : string.Empty;
                bool httpOk = statusCode >= 200 && statusCode < 300;

                if (!httpOk)
                {
                    string message = DescribeHttpError(statusCode, responseText, webRequest.error);
                    Debug.LogWarning("[ApiClient] 健康检查失败: " + message);
                    if (onResult != null) onResult(false, message);
                    yield break;
                }

                HealthResponse health = null;
                try
                {
                    health = JsonUtility.FromJson<HealthResponse>(responseText);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("[ApiClient] 解析健康检查响应失败: " + exception.Message);
                }

                string info = health != null && !string.IsNullOrEmpty(health.version) ? health.version : "ok";
                Debug.Log("[ApiClient] 健康检查通过: " + url + " (" + info + ")");
                if (onResult != null) onResult(true, info);
            }
        }

        /// <summary>
        /// 把 HTTP 状态码 / 网络错误翻译成可读中文。
        /// 后端 4xx/5xx 会返回 { code, message, fields }，优先展示 message（API.md 第 3 节）。
        /// </summary>
        private static string DescribeHttpError(long statusCode, string responseText, string transportError)
        {
            ApiError apiError = TryParseApiError(responseText);
            if (apiError != null && !string.IsNullOrEmpty(apiError.message))
            {
                return apiError.ToDisplayMessage();
            }

            if (statusCode == 0)
            {
                return "无法连接后端（" + BaseUrl + "）："
                       + (string.IsNullOrEmpty(transportError) ? "网络错误" : transportError)
                       + "。请检查：1) 后端已启动；2) 手机与 PC 在同一局域网；"
                       + "3) 后端地址是 PC 的局域网 IP（不是 127.0.0.1）；4) PC 防火墙放行 TCP 8080。";
            }

            switch (statusCode)
            {
                case 400:
                    return "后端参数校验失败（HTTP 400）" + Suffix(responseText);
                case 404:
                    return "接口不存在（HTTP 404），请确认后端版本与接口路径一致" + Suffix(responseText);
                case 405:
                    return "请求方法不支持（HTTP 405）" + Suffix(responseText);
                case 500:
                    return "后端内部错误（HTTP 500），请查看后端日志" + Suffix(responseText);
                default:
                    return "请求失败（HTTP " + statusCode + "）"
                           + (string.IsNullOrEmpty(transportError) ? string.Empty : "：" + transportError)
                           + Suffix(responseText);
            }
        }

        private static string Suffix(string responseText)
        {
            return string.IsNullOrEmpty(responseText) ? string.Empty : "：" + PreviewText(responseText);
        }

        private static ApiError TryParseApiError(string responseText)
        {
            if (string.IsNullOrEmpty(responseText))
            {
                return null;
            }

            try
            {
                ApiError apiError = JsonUtility.FromJson<ApiError>(responseText);
                if (apiError != null && (!string.IsNullOrEmpty(apiError.code) || !string.IsNullOrEmpty(apiError.message)))
                {
                    return apiError;
                }
            }
            catch (Exception)
            {
                // 不是标准错误 JSON，忽略，走状态码兜底文案。
            }

            return null;
        }

        private static string PreviewText(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "（空）";
            }

            const int maxLength = 160;
            string singleLine = text.Replace("\r", " ").Replace("\n", " ").Trim();
            return singleLine.Length <= maxLength ? singleLine : singleLine.Substring(0, maxLength) + "…";
        }
    }
}
