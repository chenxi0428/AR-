using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace ArInspection
{
    /// <summary>
    /// 运行时生成的 uGUI 表单 + HUD + 后端地址设置面板。
    ///
    /// 功能对应任务书 3.1：
    /// - 标题必填（为空则红字提示且不提交）；
    /// - 描述可选；
    /// - 优先级下拉框 low / medium / high；
    /// - 显示当前标记的 x/y/z 坐标；
    /// - 提交调用 ApiClient.CreateIssue，成功提示“上报成功：标题（id=...）”并自动关闭表单，
    ///   失败展示后端返回的 message（保持待上报状态，可修改后重试）；
    /// - HUD 显示后端地址与操作提示，并提供“后端设置”面板（PlayerPrefs 持久化）。
    ///
    /// 所有 UI 都在 Awake 里用代码创建，不依赖预制体；点击 UI 不会误放标记由
    /// ArPlacementController 通过 EventSystem.current.IsPointerOverGameObject 判断。
    /// </summary>
    [DisallowMultipleComponent]
    public class IssueFormUI : MonoBehaviour
    {
        public const string DefaultHint = "移动手机寻找平面（地面/桌面），点击已识别的平面放置标记";

        [Tooltip("API 客户端。留空时启动自动查找。")]
        public ApiClient apiClient;

        [Tooltip("后端地址配置。留空时启动自动查找。")]
        public AppConfig appConfig;

        private RectTransform safeAreaRoot;
        private Text hudServerText;
        private Text hudHintText;

        private GameObject formPanel;
        private Text formCoordText;
        private InputField titleInput;
        private InputField descriptionInput;
        private Dropdown priorityDropdown;
        private Text formStatusText;
        private Button submitButton;
        private Button cancelButton;

        private GameObject settingsPanel;
        private InputField serverUrlInput;
        private Text settingsStatusText;

        private readonly List<string> priorityOptions = new List<string>
        {
            IssuePriority.Low,
            IssuePriority.Medium,
            IssuePriority.High
        };

        private MarkerVisual currentMarker;
        private Vector3 currentPosition;
        private bool isSubmitting;
        private Coroutine autoCloseCoroutine;
        private Coroutine healthCoroutine;

        /// <summary>表单或设置面板是否打开（打开期间 ArPlacementController 不处理点击，避免误放标记）。</summary>
        public bool IsOpen
        {
            get
            {
                bool formOpen = formPanel != null && formPanel.activeSelf;
                bool settingsOpen = settingsPanel != null && settingsPanel.activeSelf;
                return formOpen || settingsOpen;
            }
        }

        private void Awake()
        {
            if (apiClient == null) apiClient = FindFirstObjectByType<ApiClient>();
            if (appConfig == null) appConfig = FindFirstObjectByType<AppConfig>();

            try
            {
                BuildUi();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                Debug.LogError("[IssueFormUI] UI 构建失败，表单将不可用。请查看 Console 中的异常信息。");
            }

            HideFormImmediate();
            UpdateHudServerText();
            SetHint(DefaultHint);
        }

        private void Start()
        {
            healthCoroutine = StartCoroutine(CheckHealthDelayed(0.5f));
        }

        private void OnDestroy()
        {
            if (autoCloseCoroutine != null)
            {
                StopCoroutine(autoCloseCoroutine);
                autoCloseCoroutine = null;
            }

            if (healthCoroutine != null)
            {
                StopCoroutine(healthCoroutine);
                healthCoroutine = null;
            }
        }

        private void Update()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            if (!Input.GetKeyDown(KeyCode.Escape)) return;

            if (settingsPanel != null && settingsPanel.activeSelf)
            {
                HideSettings();
                return;
            }

            if (formPanel != null && formPanel.activeSelf)
            {
                OnCancelClicked();
            }
#endif
        }

        // ---------- 对外接口 ----------

        /// <summary>打开表单（由 ArPlacementController 在放置标记后调用）。</summary>
        public void ShowForm(Vector3 worldPosition, MarkerVisual marker)
        {
            currentMarker = marker;
            currentPosition = worldPosition;

            if (formPanel == null)
            {
                Debug.LogError("[IssueFormUI] 表单未创建，无法打开。");
                return;
            }

            if (settingsPanel != null && settingsPanel.activeSelf) settingsPanel.SetActive(false);
            if (autoCloseCoroutine != null)
            {
                StopCoroutine(autoCloseCoroutine);
                autoCloseCoroutine = null;
            }

            formPanel.SetActive(true);
            if (formCoordText != null)
            {
                formCoordText.text = "标记坐标（Unity 世界坐标）: " + FormatPosition(worldPosition);
            }

            if (titleInput != null)
            {
                titleInput.text = string.Empty;
                titleInput.interactable = true;
            }

            if (descriptionInput != null)
            {
                descriptionInput.text = string.Empty;
                descriptionInput.interactable = true;
            }

            if (priorityDropdown != null && priorityDropdown.options.Count > 0)
            {
                priorityDropdown.interactable = true;
                priorityDropdown.value = Mathf.Clamp(1, 0, priorityDropdown.options.Count - 1); // 默认 medium
                priorityDropdown.RefreshShownValue();
            }

            isSubmitting = false;
            if (submitButton != null) submitButton.interactable = true;
            if (cancelButton != null) cancelButton.interactable = true;

            SetFormStatus(string.Empty, UiFactory.MutedTextColor);
            SetHint("填写标题（必填）/ 描述 / 优先级后提交；点击取消可放弃本次标记");
            Debug.Log("[IssueFormUI] 打开表单，坐标 = " + FormatPosition(worldPosition));
        }

        /// <summary>更新 HUD 提示文字（例如“未命中平面”）。</summary>
        public void SetHint(string hint)
        {
            if (hudHintText != null && hint != null)
            {
                hudHintText.text = hint;
            }
        }

        // ---------- UI 构建 ----------

        private void BuildUi()
        {
            Canvas canvas = UiFactory.CreateMainCanvas("ARInspectionCanvas");
            safeAreaRoot = UiFactory.CreateSafeAreaRoot(canvas);

            BuildHud();
            BuildFormPanel();
            BuildSettingsPanel();
        }

        private void BuildHud()
        {
            Image hud = UiFactory.CreatePanel(safeAreaRoot, "HudBar", UiFactory.PanelColor);
            UiFactory.TopStretch(hud.rectTransform, 168f, 0f);

            hudServerText = UiFactory.CreateText(hud.rectTransform, "ServerText", string.Empty, 28, UiFactory.MutedTextColor, TextAnchor.MiddleLeft);
            UiFactory.TopStretchRow(hudServerText.rectTransform, 10f, 38f, 20f, 270f);

            hudHintText = UiFactory.CreateText(hud.rectTransform, "HintText", DefaultHint, 34, UiFactory.TextColor, TextAnchor.UpperLeft);
            UiFactory.TopStretchRow(hudHintText.rectTransform, 56f, 96f, 20f, 20f);
            hudHintText.horizontalOverflow = HorizontalWrapMode.Wrap;
            hudHintText.verticalOverflow = VerticalWrapMode.Overflow;

            Button settingsButton = UiFactory.CreateButton(hud.rectTransform, "SettingsButton", "后端设置", UiFactory.ButtonSecondaryColor);
            UiFactory.TopRight(settingsButton.GetComponent<RectTransform>(), new Vector2(20f, 10f), new Vector2(230f, 64f));
            settingsButton.onClick.AddListener(ShowSettings);
        }

        private void BuildFormPanel()
        {
            Image panel = UiFactory.CreatePanel(safeAreaRoot, "IssueFormPanel", UiFactory.CardColor);
            formPanel = panel.gameObject;
            UiFactory.Center(panel.rectTransform, new Vector2(0f, -30f), new Vector2(1000f, 820f));

            RectTransform panelRect = panel.rectTransform;

            Text title = UiFactory.CreateText(panelRect, "Title", "上报问题", 46, UiFactory.TextColor, TextAnchor.MiddleLeft);
            UiFactory.TopStretchRow(title.rectTransform, 20f, 60f, 40f, 40f);

            formCoordText = UiFactory.CreateText(panelRect, "CoordText", "标记坐标: (0.00, 0.00, 0.00)", 26, UiFactory.MutedTextColor, TextAnchor.MiddleLeft);
            UiFactory.TopStretchRow(formCoordText.rectTransform, 84f, 34f, 40f, 40f);

            Text titleLabel = UiFactory.CreateText(panelRect, "TitleLabel", "标题 *（必填，1~200 字）", 28, UiFactory.MutedTextColor, TextAnchor.MiddleLeft);
            UiFactory.TopStretchRow(titleLabel.rectTransform, 128f, 32f, 40f, 40f);

            titleInput = UiFactory.CreateInputField(panelRect, "TitleInput", "例如：入口墙面破损", false);
            UiFactory.TopStretchRow(titleInput.GetComponent<RectTransform>(), 164f, 74f, 40f, 40f);
            titleInput.characterLimit = 200;

            Text descriptionLabel = UiFactory.CreateText(panelRect, "DescriptionLabel", "描述（可选，≤2000 字）", 28, UiFactory.MutedTextColor, TextAnchor.MiddleLeft);
            UiFactory.TopStretchRow(descriptionLabel.rectTransform, 250f, 32f, 40f, 40f);

            descriptionInput = UiFactory.CreateInputField(panelRect, "DescriptionInput", "例如：左侧墙体存在裂缝", true);
            UiFactory.TopStretchRow(descriptionInput.GetComponent<RectTransform>(), 288f, 162f, 40f, 40f);

            Text priorityLabel = UiFactory.CreateText(panelRect, "PriorityLabel", "优先级", 28, UiFactory.MutedTextColor, TextAnchor.MiddleLeft);
            UiFactory.TopStretchRow(priorityLabel.rectTransform, 462f, 32f, 40f, 40f);

            priorityDropdown = UiFactory.CreateDropdown(panelRect, "PriorityDropdown", priorityOptions, 1);
            UiFactory.TopLeft(priorityDropdown.GetComponent<RectTransform>(), new Vector2(40f, 500f), new Vector2(400f, 70f));

            formStatusText = UiFactory.CreateText(panelRect, "StatusText", string.Empty, 28, UiFactory.MutedTextColor, TextAnchor.UpperLeft);
            UiFactory.TopStretchRow(formStatusText.rectTransform, 584f, 92f, 40f, 40f);
            formStatusText.horizontalOverflow = HorizontalWrapMode.Wrap;
            formStatusText.verticalOverflow = VerticalWrapMode.Overflow;

            submitButton = UiFactory.CreateButton(panelRect, "SubmitButton", "提交上报", UiFactory.ButtonColor);
            UiFactory.TwoColumns(submitButton.GetComponent<RectTransform>(), 692f, 80f, true);
            submitButton.onClick.AddListener(OnSubmitClicked);

            cancelButton = UiFactory.CreateButton(panelRect, "CancelButton", "取消", UiFactory.ButtonSecondaryColor);
            UiFactory.TwoColumns(cancelButton.GetComponent<RectTransform>(), 692f, 80f, false);
            cancelButton.onClick.AddListener(OnCancelClicked);
        }

        private void BuildSettingsPanel()
        {
            Image panel = UiFactory.CreatePanel(safeAreaRoot, "ServerSettingsPanel", UiFactory.CardColor);
            settingsPanel = panel.gameObject;
            UiFactory.Center(panel.rectTransform, new Vector2(0f, -20f), new Vector2(1000f, 460f));

            RectTransform panelRect = panel.rectTransform;

            Text title = UiFactory.CreateText(panelRect, "SettingsTitle", "后端地址设置", 40, UiFactory.TextColor, TextAnchor.MiddleLeft);
            UiFactory.TopStretchRow(title.rectTransform, 20f, 56f, 40f, 40f);

            Text hint = UiFactory.CreateText(panelRect, "SettingsHint",
                "真机请填 PC 的局域网 IP，例如 http://192.168.1.20:8080。手机与 PC 需在同一局域网，PC 防火墙放行 TCP 8080。",
                26, UiFactory.MutedTextColor, TextAnchor.UpperLeft);
            UiFactory.TopStretchRow(hint.rectTransform, 82f, 62f, 40f, 40f);
            hint.horizontalOverflow = HorizontalWrapMode.Wrap;
            hint.verticalOverflow = VerticalWrapMode.Overflow;

            Text label = UiFactory.CreateText(panelRect, "SettingsLabel", "后端 Base URL", 28, UiFactory.MutedTextColor, TextAnchor.MiddleLeft);
            UiFactory.TopStretchRow(label.rectTransform, 152f, 32f, 40f, 40f);

            serverUrlInput = UiFactory.CreateInputField(panelRect, "ServerUrlInput", "http://192.168.1.20:8080", false);
            UiFactory.TopStretchRow(serverUrlInput.GetComponent<RectTransform>(), 190f, 74f, 40f, 40f);

            settingsStatusText = UiFactory.CreateText(panelRect, "SettingsStatus", string.Empty, 26, UiFactory.MutedTextColor, TextAnchor.UpperLeft);
            UiFactory.TopStretchRow(settingsStatusText.rectTransform, 278f, 72f, 40f, 40f);
            settingsStatusText.horizontalOverflow = HorizontalWrapMode.Wrap;
            settingsStatusText.verticalOverflow = VerticalWrapMode.Overflow;

            Button saveButton = UiFactory.CreateButton(panelRect, "SaveSettingsButton", "保存", UiFactory.ButtonColor);
            UiFactory.TwoColumns(saveButton.GetComponent<RectTransform>(), 366f, 76f, true);
            saveButton.onClick.AddListener(OnSaveServerUrlClicked);

            Button closeButton = UiFactory.CreateButton(panelRect, "CloseSettingsButton", "关闭", UiFactory.ButtonSecondaryColor);
            UiFactory.TwoColumns(closeButton.GetComponent<RectTransform>(), 366f, 76f, false);
            closeButton.onClick.AddListener(HideSettings);
        }

        // ---------- 表单行为 ----------

        private void OnSubmitClicked()
        {
            if (isSubmitting) return;

            string title = titleInput != null ? titleInput.text.Trim() : string.Empty;
            if (title.Length == 0)
            {
                SetFormStatus("标题不能为空（必填）", UiFactory.ErrorColor);
                return;
            }

            if (title.Length > 200)
            {
                SetFormStatus("标题不能超过 200 个字符", UiFactory.ErrorColor);
                return;
            }

            string description = descriptionInput != null ? descriptionInput.text.Trim() : string.Empty;
            if (description.Length > 2000)
            {
                SetFormStatus("描述不能超过 2000 个字符", UiFactory.ErrorColor);
                return;
            }

            string priority = IssuePriority.Medium;
            if (priorityDropdown != null && priorityOptions.Count > 0)
            {
                int index = Mathf.Clamp(priorityDropdown.value, 0, priorityOptions.Count - 1);
                priority = priorityOptions[index];
            }

            IssueCreateRequest request = new IssueCreateRequest
            {
                title = title,
                description = description,
                priority = priority,
                position = Position.FromVector3(currentPosition)
            };

            ApiClient client = apiClient != null ? apiClient : ApiClient.Instance;
            if (client == null)
            {
                SetFormStatus("内部错误：ApiClient 未初始化", UiFactory.ErrorColor);
                return;
            }

            isSubmitting = true;
            if (submitButton != null) submitButton.interactable = false;
            if (cancelButton != null) cancelButton.interactable = false;
            SetFormStatus("提交中……", UiFactory.MutedTextColor);

            Debug.Log("[IssueFormUI] 提交问题: title=" + title + ", priority=" + priority
                      + ", position=" + request.position);
            client.CreateIssue(request, OnCreateIssueSuccess, OnCreateIssueFailed);
        }

        private void OnCreateIssueSuccess(Issue issue)
        {
            isSubmitting = false;
            if (submitButton != null) submitButton.interactable = true;
            if (cancelButton != null) cancelButton.interactable = true;

            string title = issue != null && !string.IsNullOrEmpty(issue.title)
                ? issue.title
                : (titleInput != null ? titleInput.text.Trim() : string.Empty);
            string id = issue != null ? issue.id : string.Empty;

            string successMessage = "上报成功：" + title + "（id=" + id + "）";
            SetFormStatus(successMessage, UiFactory.SuccessColor);
            SetHint(successMessage);
            Debug.Log("[IssueFormUI] " + successMessage);

            if (currentMarker != null)
            {
                currentMarker.ApplySubmitted(issue);
                currentMarker = null;
            }

            if (autoCloseCoroutine != null) StopCoroutine(autoCloseCoroutine);
            autoCloseCoroutine = StartCoroutine(CloseFormAfterDelay(1.6f));
        }

        private void OnCreateIssueFailed(string message)
        {
            isSubmitting = false;
            if (submitButton != null) submitButton.interactable = true;
            if (cancelButton != null) cancelButton.interactable = true;

            string errorMessage = "上报失败：" + message;
            SetFormStatus(errorMessage, UiFactory.ErrorColor);
            SetHint("上报失败：请检查后端地址与网络后重试（修改后可直接再次提交）");
            Debug.LogWarning("[IssueFormUI] " + errorMessage);

            if (currentMarker != null)
            {
                currentMarker.ApplyFailed(message);
            }
        }

        private void OnCancelClicked()
        {
            if (isSubmitting) return;

            if (currentMarker != null)
            {
                currentMarker.CancelAndDestroy();
                currentMarker = null;
            }

            CloseForm();
            SetHint("已取消本次标记。移动手机寻找平面，点击平面可重新放置");
        }

        private IEnumerator CloseFormAfterDelay(float seconds)
        {
            yield return new WaitForSeconds(seconds);
            autoCloseCoroutine = null;
            CloseForm();
        }

        private void CloseForm()
        {
            if (formPanel != null) formPanel.SetActive(false);
            if (titleInput != null) titleInput.text = string.Empty;
            if (descriptionInput != null) descriptionInput.text = string.Empty;
            if (formStatusText != null) formStatusText.text = string.Empty;
            currentMarker = null;
            isSubmitting = false;
            if (submitButton != null) submitButton.interactable = true;
            if (cancelButton != null) cancelButton.interactable = true;
        }

        private void HideFormImmediate()
        {
            if (formPanel != null) formPanel.SetActive(false);
            if (settingsPanel != null) settingsPanel.SetActive(false);
        }

        private void SetFormStatus(string message, Color color)
        {
            if (formStatusText == null) return;
            formStatusText.text = message;
            formStatusText.color = color;
        }

        // ---------- 后端地址设置 ----------

        private void ShowSettings()
        {
            if (settingsPanel == null) return;

            if (serverUrlInput != null)
            {
                serverUrlInput.text = appConfig != null ? appConfig.ServerUrl : AppConfig.BaseUrl;
            }

            SetSettingsStatus(string.Empty, UiFactory.MutedTextColor);
            settingsPanel.SetActive(true);
            SetHint("修改后端地址后点击保存；真机请填写 PC 的局域网 IP");
        }

        private void HideSettings()
        {
            if (settingsPanel != null) settingsPanel.SetActive(false);
            SetHint(DefaultHint);
        }

        private void OnSaveServerUrlClicked()
        {
            string raw = serverUrlInput != null ? serverUrlInput.text.Trim() : string.Empty;
            if (raw.Length == 0)
            {
                SetSettingsStatus("地址不能为空", UiFactory.ErrorColor);
                return;
            }

            if (!raw.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                && !raw.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                raw = "http://" + raw;
            }

            AppConfig.SaveServerUrlOverride(raw);
            string normalized = AppConfig.BaseUrl;

            if (appConfig == null) appConfig = FindFirstObjectByType<AppConfig>();
            if (appConfig != null) appConfig.SetServerUrl(normalized);

            if (serverUrlInput != null) serverUrlInput.text = normalized;
            SetSettingsStatus("已保存：" + normalized, UiFactory.SuccessColor);
            UpdateHudServerText();
            CheckHealth();
        }

        private void SetSettingsStatus(string message, Color color)
        {
            if (settingsStatusText == null) return;
            settingsStatusText.text = message;
            settingsStatusText.color = color;
        }

        // ---------- 健康检查 / HUD ----------

        private IEnumerator CheckHealthDelayed(float delay)
        {
            yield return new WaitForSeconds(delay);
            healthCoroutine = null;
            CheckHealth();
        }

        private void CheckHealth()
        {
            ApiClient client = apiClient != null ? apiClient : ApiClient.Instance;
            if (client == null) return;

            if (hudServerText != null)
            {
                hudServerText.text = "后端: " + AppConfig.BaseUrl + "（检测中……）";
            }

            client.Health(OnHealthResult);
        }

        private void OnHealthResult(bool online, string info)
        {
            // 健康检查是 ApiClient 上的协程，回调时本组件可能已随场景销毁。
            if (!this) return;
            if (hudServerText == null) return;

            if (online)
            {
                hudServerText.text = "后端: " + AppConfig.BaseUrl + "（在线 " + info + "）";
            }
            else
            {
                hudServerText.text = "后端: " + AppConfig.BaseUrl + "（离线）";
                SetHint("后端不可达：" + info);
            }
        }

        private void UpdateHudServerText()
        {
            if (hudServerText != null)
            {
                hudServerText.text = "后端: " + AppConfig.BaseUrl;
            }
        }

        // ---------- 工具方法 ----------

        private static string FormatPosition(Vector3 position)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "({0:0.00}, {1:0.00}, {2:0.00})",
                position.x, position.y, position.z);
        }
    }
}
