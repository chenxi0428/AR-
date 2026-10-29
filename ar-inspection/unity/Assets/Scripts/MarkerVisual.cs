using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace ArInspection
{
    /// <summary>
    /// AR 标记（Cube）的显示状态：
    /// - 待上报：橙色，等待填写表单；
    /// - 上报成功：绿色，并在上方显示问题标题（世界空间文字）；
    /// - 上报失败：保持待上报颜色（由表单负责展示红色错误信息）；
    /// - 取消：销毁标记。
    ///
    /// 标记由 ArPlacementController 在射线命中的位姿处创建，本组件只负责外观与数据。
    /// </summary>
    [DisallowMultipleComponent]
    public class MarkerVisual : MonoBehaviour
    {
        public static readonly Color PendingColor = new Color(1f, 0.62f, 0.12f, 1f);
        public static readonly Color SubmittedColor = new Color(0.16f, 0.82f, 0.32f, 1f);

        [Tooltip("标记边长（米）")]
        public float size = 0.12f;

        private Renderer markerRenderer;
        private Material markerMaterial;
        private Transform labelRoot;
        private Text labelText;
        private Issue reportedIssue;

        /// <summary>标记的世界坐标（上报的 position 就是这个值）。</summary>
        public Vector3 WorldPosition { get; private set; }

        /// <summary>是否已经上报成功。上报成功的标记不会被下一次放置覆盖。</summary>
        public bool IsSubmitted { get; private set; }

        /// <summary>上报成功后后端返回的 Issue（待上报时为 null）。</summary>
        public Issue ReportedIssue
        {
            get { return reportedIssue; }
        }

        private void Awake()
        {
            markerRenderer = GetComponent<Renderer>();
            if (markerRenderer != null)
            {
                // 每个标记一份材质实例，改颜色互不影响。
                markerMaterial = markerRenderer.material;
                markerRenderer.shadowCastingMode = ShadowCastingMode.Off;
                markerRenderer.receiveShadows = false;
            }
        }

        /// <summary>放置后初始化：橙色待上报状态。</summary>
        public void Initialize(Vector3 worldPosition, float markerSize)
        {
            WorldPosition = worldPosition;
            IsSubmitted = false;
            size = markerSize;
            SetColor(PendingColor);
        }

        /// <summary>上报成功：变绿 + 显示标题。</summary>
        public void ApplySubmitted(Issue issue)
        {
            IsSubmitted = true;
            reportedIssue = issue;
            SetColor(SubmittedColor);

            string title = issue != null && !string.IsNullOrEmpty(issue.title) ? issue.title : "（无标题）";
            string priority = issue != null && !string.IsNullOrEmpty(issue.priority) ? issue.priority : IssuePriority.Medium;

            if (labelRoot == null)
            {
                // 世界空间文字标签。注意：不能挂在标记 Cube 下面，否则会继承 Cube 的 0.12 缩放而变得极小；
                // 这里挂到场景根节点，再用世界坐标把它放在标记上方。
                labelText = UiFactory.CreateWorldLabel(
                    transform.parent, string.Empty, Vector3.zero, new Vector2(520f, 150f), 0.002f);
                if (labelText != null)
                {
                    labelRoot = labelText.transform.parent;
                    labelRoot.position = transform.position + Vector3.up * 0.22f;
                }
            }

            if (labelText != null)
            {
                labelText.text = title + "\n[" + priority + "]";
                labelText.color = Color.white;
            }
        }

        /// <summary>上报失败：保持待上报色（这里只写日志，错误信息由表单展示）。</summary>
        public void ApplyFailed(string message)
        {
            Debug.LogWarning("[MarkerVisual] 标记上报失败，保持待上报状态: " + message);
        }

        /// <summary>取消本次上报：销毁标记。</summary>
        public void CancelAndDestroy()
        {
            Destroy(gameObject);
        }

        private void LateUpdate()
        {
            if (labelRoot == null) return;

            Camera mainCamera = Camera.main;
            if (mainCamera == null) return;

            // 让世界空间标签始终面向相机（广告牌效果），否则从侧后方看不见文字。
            labelRoot.rotation = mainCamera.transform.rotation;
        }

        private void SetColor(Color color)
        {
            if (markerMaterial != null)
            {
                markerMaterial.color = color;
            }
        }

        private void OnDestroy()
        {
            // 标签是世界坐标下的独立对象，标记销毁时要一起销毁，避免留下漂浮文字。
            if (labelRoot != null)
            {
                Destroy(labelRoot.gameObject);
                labelRoot = null;
            }

            if (markerMaterial != null)
            {
                Destroy(markerMaterial);
                markerMaterial = null;
            }
        }
    }
}
