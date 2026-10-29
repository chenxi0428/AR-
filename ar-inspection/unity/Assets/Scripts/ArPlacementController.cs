using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace ArInspection
{
    /// <summary>
    /// AR 平面点击放置标记。
    ///
    /// 流程：
    /// 1. 用 ARRaycastManager 对已识别的平面做射线检测（TrackableType.PlaneWithinPolygon）；
    /// 2. 命中后在命中位姿实例化一个 Cube 标记（橙色待上报）；
    /// 3. 打开 IssueFormUI 填写并上报；
    /// 4. 点击 UI（按钮 / 输入框 / 下拉框）时通过 EventSystem 判断并忽略，不会误放标记。
    ///
    /// 输入兼容：
    /// - Android 真机：Input.touchCount / Input.GetTouch（begin 阶段）；
    /// - Unity Editor / Windows：鼠标左键（Input.GetMouseButtonDown(0)）；
    /// - 二者都依赖 legacy Input Manager（Active Input Handling = Both 或 Input Manager (Old)）。
    /// </summary>
    [DisallowMultipleComponent]
    public class ArPlacementController : MonoBehaviour
    {
        [Tooltip("AR 射线管理器（必须挂在 XR Origin 上）。留空时启动自动查找。")]
        public ARRaycastManager raycastManager;

        [Tooltip("AR 平面管理器。留空时启动自动查找。")]
        public ARPlaneManager planeManager;

        [Tooltip("问题表单 UI。留空时启动自动查找。")]
        public IssueFormUI formUI;

        [Tooltip("平面可视化组件（可由脚本自动查找）。")]
        public PlaneVisualizer planeVisualizer;

        [Tooltip("标记边长（米）")]
        public float markerSize = 0.12f;

        [Tooltip("标记中心距离平面的高度（米），避免立方体一半埋进平面")]
        public float markerHover = 0.06f;

        private readonly List<ARRaycastHit> hitResults = new List<ARRaycastHit>();
        private MarkerVisual pendingMarker;
        private bool warnedMissingManager;

        private void Awake()
        {
            if (raycastManager == null) raycastManager = FindFirstObjectByType<ARRaycastManager>();
            if (planeManager == null) planeManager = FindFirstObjectByType<ARPlaneManager>();
            if (formUI == null) formUI = FindFirstObjectByType<IssueFormUI>();
            if (planeVisualizer == null) planeVisualizer = FindFirstObjectByType<PlaneVisualizer>();

            if (planeManager != null)
            {
                // 任务只要求检测水平平面；如需垂直平面可在 Inspector 改回 Horizontal | Vertical。
                planeManager.requestedDetectionMode = PlaneDetectionMode.Horizontal;
            }

            if (raycastManager == null)
            {
                Debug.LogError("[ArPlacementController] 未找到 ARRaycastManager。请执行 Tools > AR Inspection > "
                               + "Setup Android + ARCore Project 生成场景，或手动把 ARRaycastManager 挂到 XR Origin 上。");
            }
        }

        private void Update()
        {
            // 表单 / 设置面板打开期间不处理 AR 点击，避免点 UI 时误放标记。
            if (formUI != null && formUI.IsOpen) return;

            if (raycastManager == null)
            {
                if (!warnedMissingManager)
                {
                    warnedMissingManager = true;
                    Debug.LogError("[ArPlacementController] 缺少 ARRaycastManager，无法放置标记。");
                }

                return;
            }

#if ENABLE_LEGACY_INPUT_MANAGER
            HandleLegacyInput();
#else
            // Active Input Handling = Input System Package (New) 时 legacy Input 不可用。
            // 一键 Setup 脚本会设置为 Both；若手动改成了 New，请改回 Both 或改用 TrackedPoseDriver + 新输入 API。
            if (!warnedMissingManager)
            {
                warnedMissingManager = true;
                Debug.LogError("[ArPlacementController] 当前工程禁用了 legacy Input Manager，点击放置不可用。"
                               + "请在 Project Settings > Player > Active Input Handling 选择 Both。");
            }
#endif
        }

#if ENABLE_LEGACY_INPUT_MANAGER
        private void HandleLegacyInput()
        {
            // 1) 触屏优先（Android）。注意 Android 上 Input.GetMouseButtonDown 也会随触摸触发，
            //    所以有触摸时直接 return，避免一次点击放置两个标记。
            int touchCount = Input.touchCount;
            if (touchCount > 0)
            {
                for (int index = 0; index < touchCount; index++)
                {
                    Touch touch = Input.GetTouch(index);
                    if (touch.phase != TouchPhase.Began) continue;

                    // 点在 UI 上：忽略（这是“点按钮不误放标记”的关键）
                    if (IsPointerOverUi(touch.fingerId)) return;

                    TryPlaceAtScreenPoint(touch.position);
                    return; // 一帧只处理一次点击
                }

                return;
            }

            // 2) 鼠标（Editor / Windows 调试）
            if (Input.GetMouseButtonDown(0))
            {
                if (IsPointerOverUi(-1)) return;
                TryPlaceAtScreenPoint(Input.mousePosition);
            }
        }

        private static bool IsPointerOverUi(int pointerId)
        {
            EventSystem currentEventSystem = EventSystem.current;
            if (currentEventSystem == null) return false;

            if (pointerId >= 0)
            {
                return currentEventSystem.IsPointerOverGameObject(pointerId);
            }

            return currentEventSystem.IsPointerOverGameObject();
        }

        private void TryPlaceAtScreenPoint(Vector2 screenPoint)
        {
            hitResults.Clear();
            bool hasHit = raycastManager.Raycast(screenPoint, hitResults, TrackableType.PlaneWithinPolygon);

            if (!hasHit || hitResults.Count == 0)
            {
                if (formUI != null)
                {
                    formUI.SetHint("未命中平面：请缓慢移动手机扫描地面 / 桌面，等平面出现后再点击");
                }

                Debug.Log("[ArPlacementController] 点击未命中平面，screenPoint = " + screenPoint);
                return;
            }

            PlaceMarker(hitResults[0].pose);
        }

        private void PlaceMarker(Pose hitPose)
        {
            // 只保留最新一个“待上报”标记；已上报成功的绿色标记保持不动。
            if (pendingMarker != null && !pendingMarker.IsSubmitted)
            {
                pendingMarker.CancelAndDestroy();
                pendingMarker = null;
            }

            Vector3 markerPosition = hitPose.position + hitPose.up * markerHover;

            GameObject markerObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            markerObject.name = "IssueMarker";
            markerObject.transform.SetPositionAndRotation(markerPosition, hitPose.rotation);
            markerObject.transform.localScale = Vector3.one * markerSize;

            // 放置不依赖物理碰撞，销毁 BoxCollider 避免与后续物理逻辑混淆。
            Collider markerCollider = markerObject.GetComponent<Collider>();
            if (markerCollider != null)
            {
                Destroy(markerCollider);
            }

            MarkerVisual marker = markerObject.AddComponent<MarkerVisual>();
            marker.Initialize(markerPosition, markerSize);
            pendingMarker = marker;

            Debug.Log("[ArPlacementController] 放置标记 position = " + markerPosition
                      + ", rotation = " + hitPose.rotation.eulerAngles);

            if (formUI != null)
            {
                formUI.ShowForm(markerPosition, marker);
            }
            else
            {
                Debug.LogError("[ArPlacementController] 未找到 IssueFormUI，标记已放置但无法填写上报表单。");
            }
        }
#endif
    }
}
