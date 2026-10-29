using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ArInspection
{
    /// <summary>
    /// 运行时 uGUI 工厂：所有界面都由代码创建，不依赖任何手工预制体 / 字体资产，
    /// 空场景（只要挂了业务脚本）也能把整套 UI 建出来。
    ///
    /// 实现方式：用 uGUI 自带的 DefaultControls 创建控件（层级与 Editor 中
    /// GameObject &gt; UI 菜单生成的完全一致），再统一改字体、颜色、尺寸。
    /// 这样避免手写 InputField / Dropdown 的复杂子层级（模板、Viewport、Item 等）出错。
    ///
    /// 输入模块说明：优先使用 legacy 的 StandaloneInputModule（与工程的 Active Input Handling
    /// = Both 对应）；只有完全没有 legacy input 时才尝试反射挂 Input System 的 UI 模块。
    /// </summary>
    public static class UiFactory
    {
        // 统一配色（深色底 + 蓝色主按钮），短剧界面风格简单干净即可。
        public static readonly Color PanelColor = new Color(0.05f, 0.07f, 0.10f, 0.82f);
        public static readonly Color CardColor = new Color(0.11f, 0.14f, 0.19f, 0.97f);
        public static readonly Color FieldColor = new Color(1f, 1f, 1f, 0.10f);
        public static readonly Color ButtonColor = new Color(0.16f, 0.45f, 0.86f, 1f);
        public static readonly Color ButtonSecondaryColor = new Color(0.28f, 0.31f, 0.38f, 1f);
        public static readonly Color TextColor = new Color(0.95f, 0.96f, 0.98f, 1f);
        public static readonly Color MutedTextColor = new Color(0.66f, 0.71f, 0.79f, 1f);
        public static readonly Color ErrorColor = new Color(1f, 0.38f, 0.34f, 1f);
        public static readonly Color SuccessColor = new Color(0.36f, 0.88f, 0.46f, 1f);

        // DefaultControls 需要一组 Sprite 资源；运行时没有 Editor 的内置贴图，
        // 全传 null 即可：Image 没有 sprite 时会画纯色矩形，正好符合本工程的扁平风格。
        private static readonly DefaultControls.Resources DefaultUiResources = new DefaultControls.Resources();

        private static Font cachedFont;
        private static bool fontResolved;

        /// <summary>
        /// 取 UI 字体。优先系统中文字体（保证中文不显示成方块），再退回 Unity 内置字体。
        /// </summary>
        public static Font GetFont()
        {
            if (fontResolved) return cachedFont;
            fontResolved = true;

            // 1) 系统中文字体优先
            try
            {
                string[] installedFonts = Font.GetOSInstalledFontNames();
                string preferredFont = FindPreferredCjkFont(installedFonts);
                if (!string.IsNullOrEmpty(preferredFont))
                {
                    cachedFont = Font.CreateDynamicFontFromOSFont(preferredFont, 32);
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[UiFactory] 读取系统字体失败，将使用内置字体: " + exception.Message);
            }

            // 2) Unity 内置字体兜底（Unity 2022.2+ 内置字体名为 LegacyRuntime.ttf）
            if (cachedFont == null)
            {
                try
                {
                    cachedFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                }
                catch (Exception)
                {
                    cachedFont = null;
                }
            }

            if (cachedFont == null)
            {
                try
                {
                    cachedFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
                }
                catch (Exception)
                {
                    cachedFont = null;
                }
            }

            if (cachedFont == null)
            {
                Debug.LogWarning("[UiFactory] 未找到可用字体，UI 文本可能不显示。请检查系统字体或改用 TextMeshPro。");
            }
            else
            {
                Debug.Log("[UiFactory] UI 字体: " + cachedFont.name);
            }

            return cachedFont;
        }

        private static string FindPreferredCjkFont(string[] installedFonts)
        {
            if (installedFonts == null || installedFonts.Length == 0) return null;

            // 常见中文字体关键字，按优先级排列；Android 一般能命中 Noto/Droid 系列。
            string[] keywords =
            {
                "Noto Sans CJK", "NotoSansCJK", "Source Han Sans", "SourceHanSans",
                "Microsoft YaHei", "微软雅黑", "PingFang SC", "PingFangSC",
                "Droid Sans Fallback", "DroidSansFallback", "Noto Sans SC", "NotoSansSC",
                "Heiti", "SimHei", "SimSun", "WenQuanYi", "Arial Unicode", "CJK"
            };

            for (int keywordIndex = 0; keywordIndex < keywords.Length; keywordIndex++)
            {
                string keyword = keywords[keywordIndex];
                for (int fontIndex = 0; fontIndex < installedFonts.Length; fontIndex++)
                {
                    string fontName = installedFonts[fontIndex];
                    if (!string.IsNullOrEmpty(fontName)
                        && fontName.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return fontName;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// 创建全屏 uGUI Canvas（ScreenSpaceOverlay + CanvasScaler 1920x1080），
        /// 并保证场景里有 EventSystem，否则所有按钮 / 输入框都点不动。
        /// 返回的 Canvas 下还需要自己再挂一个 SafeArea 容器（见 CreateSafeAreaRoot）。
        /// </summary>
        public static Canvas CreateMainCanvas(string name)
        {
            EnsureEventSystem();

            GameObject canvasObject = new GameObject(name, typeof(RectTransform));
            Canvas canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            CanvasScaler canvasScaler = canvasObject.AddComponent<CanvasScaler>();
            canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvasScaler.referenceResolution = new Vector2(1920f, 1080f);
            canvasScaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            canvasScaler.matchWidthOrHeight = 0.5f;

            canvasObject.AddComponent<GraphicRaycaster>();

            return canvas;
        }

        /// <summary>在 Canvas 下创建一个铺满的全屏 SafeArea 容器（适配刘海屏 / 手势条）。</summary>
        public static RectTransform CreateSafeAreaRoot(Canvas canvas)
        {
            RectTransform safeAreaRoot = CreateRect(canvas.transform, "SafeArea");
            Stretch(safeAreaRoot);
            safeAreaRoot.gameObject.AddComponent<SafeAreaFitter>();
            return safeAreaRoot;
        }

        /// <summary>保证场景中有 EventSystem（UI 点击 / 输入必须）。</summary>
        public static void EnsureEventSystem()
        {
            if (EventSystem.current != null) return;

            GameObject eventSystemObject = new GameObject("EventSystem");
            eventSystemObject.AddComponent<EventSystem>();

#if ENABLE_LEGACY_INPUT_MANAGER
            // 与 Active Input Handling = Both / Input Manager (Old) 对应，最稳定。
            eventSystemObject.AddComponent<StandaloneInputModule>();
#else
            if (!TryAddInputSystemUiModule(eventSystemObject))
            {
                Debug.LogError("[UiFactory] 没有可用的 UI 输入模块：请在 Project Settings > Player > "
                               + "Active Input Handling 选择 Both（推荐）或 Input Manager (Old)，然后重启编辑器。");
            }
#endif
        }

#if !ENABLE_LEGACY_INPUT_MANAGER
        private static bool TryAddInputSystemUiModule(GameObject target)
        {
            try
            {
                Type moduleType = Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
                if (moduleType != null && typeof(BaseInputModule).IsAssignableFrom(moduleType))
                {
                    target.AddComponent(moduleType);
                    return true;
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[UiFactory] 添加 InputSystemUIInputModule 失败: " + exception.Message);
            }

            return false;
        }
#endif

        /// <summary>创建一个空 RectTransform 节点。</summary>
        public static RectTransform CreateRect(Transform parent, string name)
        {
            GameObject gameObject = new GameObject(name, typeof(RectTransform));
            gameObject.transform.SetParent(parent, false);
            return gameObject.GetComponent<RectTransform>();
        }

        /// <summary>创建带颜色的面板（Image）。</summary>
        public static Image CreatePanel(Transform parent, string name, Color color)
        {
            GameObject gameObject = DefaultControls.CreatePanel(DefaultUiResources);
            gameObject.name = name;
            gameObject.transform.SetParent(parent, false);

            Image image = gameObject.GetComponent<Image>();
            if (image == null) image = gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = true;
            return image;
        }

        /// <summary>创建文本。</summary>
        public static Text CreateText(Transform parent, string name, string content, int fontSize, Color color, TextAnchor alignment)
        {
            GameObject gameObject = DefaultControls.CreateText(DefaultUiResources);
            gameObject.name = name;
            gameObject.transform.SetParent(parent, false);

            Text text = gameObject.GetComponent<Text>();
            if (text == null) text = gameObject.AddComponent<Text>();
            text.text = content;
            text.fontSize = fontSize;
            text.color = color;
            text.alignment = alignment;
            text.supportRichText = false;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            ApplyFont(text);
            return text;
        }

        /// <summary>创建按钮（返回 Button，文字在子节点 "Text" 上）。</summary>
        public static Button CreateButton(Transform parent, string name, string label, Color backgroundColor)
        {
            GameObject gameObject = DefaultControls.CreateButton(DefaultUiResources);
            gameObject.name = name;
            gameObject.transform.SetParent(parent, false);

            Image image = gameObject.GetComponent<Image>();
            if (image != null) image.color = backgroundColor;

            Button button = gameObject.GetComponent<Button>();
            if (button == null) button = gameObject.AddComponent<Button>();

            Text text = gameObject.GetComponentInChildren<Text>(true);
            if (text != null)
            {
                text.text = label;
                text.fontSize = 32;
                text.color = TextColor;
                text.alignment = TextAnchor.MiddleCenter;
                text.raycastTarget = false;
                ApplyFont(text);
            }

            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(0.88f, 0.88f, 0.88f, 1f);
            colors.pressedColor = new Color(0.72f, 0.72f, 0.72f, 1f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(0.55f, 0.55f, 0.55f, 0.6f);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.08f;
            button.colors = colors;

            return button;
        }

        /// <summary>创建输入框。multiline = true 时用于描述（多行）。</summary>
        public static InputField CreateInputField(Transform parent, string name, string placeholderText, bool multiline)
        {
            GameObject gameObject = DefaultControls.CreateInputField(DefaultUiResources);
            gameObject.name = name;
            gameObject.transform.SetParent(parent, false);

            InputField inputField = gameObject.GetComponent<InputField>();
            if (inputField == null) inputField = gameObject.AddComponent<InputField>();

            Image background = gameObject.GetComponent<Image>();
            if (background != null) background.color = FieldColor;

            Font font = GetFont();

            if (inputField.textComponent != null)
            {
                if (font != null) inputField.textComponent.font = font;
                inputField.textComponent.fontSize = 30;
                inputField.textComponent.color = TextColor;
                inputField.textComponent.supportRichText = false;
                inputField.textComponent.raycastTarget = false;
                inputField.textComponent.alignment = multiline ? TextAnchor.UpperLeft : TextAnchor.MiddleLeft;
                inputField.textComponent.horizontalOverflow = HorizontalWrapMode.Wrap;
            }

            Text placeholder = inputField.placeholder as Text;
            if (placeholder != null)
            {
                if (font != null) placeholder.font = font;
                placeholder.text = placeholderText;
                placeholder.fontSize = 28;
                placeholder.fontStyle = FontStyle.Normal;
                placeholder.color = new Color(MutedTextColor.r, MutedTextColor.g, MutedTextColor.b, 0.65f);
                placeholder.alignment = multiline ? TextAnchor.UpperLeft : TextAnchor.MiddleLeft;
                placeholder.raycastTarget = false;
            }

            if (multiline)
            {
                inputField.lineType = InputField.LineType.MultiLineNewline;
                inputField.characterLimit = 2000;
            }

            return inputField;
        }

        /// <summary>
        /// 创建优先级下拉框。使用 DefaultControls 生成标准 Dropdown 层级，
        /// 再补上独立 Canvas（保证展开列表显示在表单之上）、统一字体与配色。
        /// </summary>
        public static Dropdown CreateDropdown(Transform parent, string name, IList<string> options, int defaultIndex)
        {
            GameObject gameObject = DefaultControls.CreateDropdown(DefaultUiResources);
            gameObject.name = name;
            gameObject.transform.SetParent(parent, false);

            Dropdown dropdown = gameObject.GetComponent<Dropdown>();
            if (dropdown == null) dropdown = gameObject.AddComponent<Dropdown>();

            Image background = gameObject.GetComponent<Image>();
            if (background != null) background.color = FieldColor;

            StyleDropdown(dropdown);

            dropdown.ClearOptions();
            if (options != null && options.Count > 0)
            {
                dropdown.AddOptions(new List<string>(options));
                dropdown.value = Mathf.Clamp(defaultIndex, 0, options.Count - 1);
                dropdown.RefreshShownValue();
            }

            return dropdown;
        }

        private static void StyleDropdown(Dropdown dropdown)
        {
            if (dropdown == null) return;

            Font font = GetFont();

            if (dropdown.captionText != null)
            {
                if (font != null) dropdown.captionText.font = font;
                dropdown.captionText.fontSize = 30;
                dropdown.captionText.color = TextColor;
                dropdown.captionText.alignment = TextAnchor.MiddleLeft;
            }

            // DefaultControls 的箭头是 Image（运行时没有 sprite，会显示成白块），直接隐藏。
            Transform arrow = dropdown.transform.Find("Arrow");
            if (arrow != null) arrow.gameObject.SetActive(false);

            RectTransform template = dropdown.template;
            if (template == null) return;

            // 展开列表加上独立 Canvas，避免被表单面板遮挡。
            Canvas templateCanvas = template.GetComponent<Canvas>();
            if (templateCanvas == null) templateCanvas = template.gameObject.AddComponent<Canvas>();
            templateCanvas.overrideSorting = true;
            templateCanvas.sortingOrder = 500;

            if (template.GetComponent<GraphicRaycaster>() == null)
            {
                template.gameObject.AddComponent<GraphicRaycaster>();
            }

            Image templateBackground = template.GetComponent<Image>();
            if (templateBackground != null)
            {
                templateBackground.color = new Color(0.15f, 0.18f, 0.24f, 0.98f);
            }

            Text[] templateTexts = template.GetComponentsInChildren<Text>(true);
            for (int index = 0; index < templateTexts.Length; index++)
            {
                Text templateText = templateTexts[index];
                if (font != null) templateText.font = font;
                templateText.fontSize = 28;
                templateText.color = TextColor;
            }

            Image[] templateImages = template.GetComponentsInChildren<Image>(true);
            for (int index = 0; index < templateImages.Length; index++)
            {
                Image templateImage = templateImages[index];
                if (templateImage.gameObject == template.gameObject) continue;

                if (templateImage.gameObject.name.Contains("Checkmark"))
                {
                    templateImage.color = ButtonColor;
                }
                else if (templateImage.gameObject.name.Contains("Background"))
                {
                    templateImage.color = new Color(1f, 1f, 1f, 0.08f);
                }
                else
                {
                    templateImage.color = new Color(1f, 1f, 1f, 0.04f);
                }
            }

            // 兼容旧版 uGUI：把 itemText 指向模板里的 Item Label（新版本已标记为 Obsolete，用反射避免警告）
            TryAssignItemText(dropdown, templateTexts);
        }

        private static void TryAssignItemText(Dropdown dropdown, Text[] templateTexts)
        {
            if (templateTexts == null || templateTexts.Length == 0) return;

            try
            {
                PropertyInfo property = typeof(Dropdown).GetProperty("itemText", BindingFlags.Public | BindingFlags.Instance);
                if (property != null && property.CanWrite && property.PropertyType == typeof(Text))
                {
                    property.SetValue(dropdown, templateTexts[0]);
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[UiFactory] 设置 Dropdown.itemText 失败（不影响使用）: " + exception.Message);
            }
        }

        /// <summary>
        /// 创建世界空间文字标签（用于标记上显示问题标题）。
        /// 返回的 Text 需要调用方自己设置内容 / 开关，并根据需要让 Canvas 朝向相机（见 MarkerVisual）。
        /// </summary>
        public static Text CreateWorldLabel(Transform parent, string content, Vector3 localPosition, Vector2 size, float worldScale)
        {
            GameObject labelRoot = new GameObject("WorldLabel", typeof(RectTransform));
            labelRoot.transform.SetParent(parent, false);

            Canvas canvas = labelRoot.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 50;

            RectTransform canvasRect = labelRoot.GetComponent<RectTransform>();
            canvasRect.sizeDelta = size;
            canvasRect.localPosition = localPosition;
            canvasRect.localRotation = Quaternion.identity;
            canvasRect.localScale = Vector3.one * worldScale;

            Text label = CreateText(labelRoot.transform, "LabelText", content, 40, Color.white, TextAnchor.MiddleCenter);
            Stretch(label.rectTransform);
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;

            Outline outline = label.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.9f);
            outline.effectDistance = new Vector2(2f, -2f);

            return label;
        }

        /// <summary>把 Text 的字体换成统一字体（null 时保持 DefaultControls 内置字体）。</summary>
        public static void ApplyFont(Text text)
        {
            if (text == null) return;
            Font font = GetFont();
            if (font != null) text.font = font;
        }

        // ---------- RectTransform 布局辅助 ----------

        /// <summary>铺满父节点（可带四边间距）。</summary>
        public static void Stretch(RectTransform rectTransform, float left = 0f, float bottom = 0f, float right = 0f, float top = 0f)
        {
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.offsetMin = new Vector2(left, bottom);
            rectTransform.offsetMax = new Vector2(-right, -top);
        }

        /// <summary>居中固定尺寸。</summary>
        public static void Center(RectTransform rectTransform, Vector2 anchoredPosition, Vector2 size)
        {
            rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            rectTransform.anchoredPosition = anchoredPosition;
            rectTransform.sizeDelta = size;
        }

        /// <summary>顶部横向拉伸，固定高度。</summary>
        public static void TopStretch(RectTransform rectTransform, float height, float margin = 0f)
        {
            rectTransform.anchorMin = new Vector2(0f, 1f);
            rectTransform.anchorMax = new Vector2(1f, 1f);
            rectTransform.pivot = new Vector2(0.5f, 1f);
            rectTransform.offsetMin = new Vector2(margin, -height - margin);
            rectTransform.offsetMax = new Vector2(-margin, -margin);
        }

        /// <summary>相对父节点左上角定位（offset 用正数表示向右 / 向下）。</summary>
        public static void TopLeft(RectTransform rectTransform, Vector2 topLeftOffset, Vector2 size)
        {
            rectTransform.anchorMin = new Vector2(0f, 1f);
            rectTransform.anchorMax = new Vector2(0f, 1f);
            rectTransform.pivot = new Vector2(0f, 1f);
            rectTransform.anchoredPosition = new Vector2(Mathf.Abs(topLeftOffset.x), -Mathf.Abs(topLeftOffset.y));
            rectTransform.sizeDelta = size;
        }

        /// <summary>相对父节点右上角定位（offset 用正数表示向左 / 向下）。</summary>
        public static void TopRight(RectTransform rectTransform, Vector2 topRightOffset, Vector2 size)
        {
            rectTransform.anchorMin = new Vector2(1f, 1f);
            rectTransform.anchorMax = new Vector2(1f, 1f);
            rectTransform.pivot = new Vector2(1f, 1f);
            rectTransform.anchoredPosition = new Vector2(-Mathf.Abs(topRightOffset.x), -Mathf.Abs(topRightOffset.y));
            rectTransform.sizeDelta = size;
        }

        /// <summary>在父节点内做“顶部起算的一行”（横向拉伸，可设左右边距）。</summary>
        public static void TopStretchRow(RectTransform rectTransform, float topOffset, float height, float left = 0f, float right = 0f)
        {
            rectTransform.anchorMin = new Vector2(0f, 1f);
            rectTransform.anchorMax = new Vector2(1f, 1f);
            rectTransform.pivot = new Vector2(0.5f, 1f);
            rectTransform.offsetMin = new Vector2(left, -topOffset - height);
            rectTransform.offsetMax = new Vector2(-right, -topOffset);
        }

        /// <summary>父节点内两列布局：leftColumn 为 true 取左半，false 取右半。</summary>
        public static void TwoColumns(RectTransform rectTransform, float topOffset, float height, bool leftColumn)
        {
            rectTransform.anchorMin = new Vector2(leftColumn ? 0f : 0.5f, 1f);
            rectTransform.anchorMax = new Vector2(leftColumn ? 0.5f : 1f, 1f);
            rectTransform.pivot = new Vector2(0.5f, 1f);
            rectTransform.offsetMin = new Vector2(leftColumn ? 40f : 20f, -topOffset - height);
            rectTransform.offsetMax = new Vector2(leftColumn ? -20f : -40f, -topOffset);
        }
    }
}
