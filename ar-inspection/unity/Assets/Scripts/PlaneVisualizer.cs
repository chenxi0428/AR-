using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace ArInspection
{
    /// <summary>
    /// 已识别平面的可视化。
    ///
    /// AR Foundation 6 的 ARPlaneManager 可以不配置 planePrefab（本工程就不依赖预制体），
    /// 因此这里在每个 ARPlane 下生成一个极薄的 Cube 作为“平面贴片”，让用户能看见识别到的地面/桌面，
    /// 方便演示与现场使用。纯运行时生成，不依赖预制体 / 美术资源。
    ///
    /// 平面贴片不参与物理碰撞（销毁了 BoxCollider），AR 拾取走 ARRaycastManager，与物理无关。
    /// </summary>
    [DisallowMultipleComponent]
    public class PlaneVisualizer : MonoBehaviour
    {
        [Tooltip("ARPlaneManager。留空时启动自动查找。")]
        public ARPlaneManager planeManager;

        [Tooltip("识别到的平面贴片颜色")]
        public Color planeColor = new Color(0.35f, 0.75f, 0.95f, 1f);

        [Tooltip("平面贴片厚度（米）")]
        public float thickness = 0.01f;

        private readonly Dictionary<TrackableId, GameObject> visuals = new Dictionary<TrackableId, GameObject>();

        private void Awake()
        {
            if (planeManager == null)
            {
                planeManager = FindFirstObjectByType<ARPlaneManager>();
            }
        }

        private void Update()
        {
            if (planeManager == null) return;

            foreach (ARPlane plane in planeManager.trackables)
            {
                if (plane == null) continue;

                GameObject visual;
                if (!visuals.TryGetValue(plane.trackableId, out visual) || visual == null)
                {
                    visual = CreateVisual(plane);
                    visuals[plane.trackableId] = visual;
                }

                UpdateVisual(plane, visual);
            }
        }

        private GameObject CreateVisual(ARPlane plane)
        {
            GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.name = "PlaneVisual";
            visual.transform.SetParent(plane.transform, false);

            // 平面贴片只做视觉提示，不需要参与物理。
            Collider planeCollider = visual.GetComponent<Collider>();
            if (planeCollider != null)
            {
                Destroy(planeCollider);
            }

            MeshRenderer meshRenderer = visual.GetComponent<MeshRenderer>();
            if (meshRenderer != null)
            {
                Material material = meshRenderer.material;
                if (material != null)
                {
                    material.color = planeColor;
                }

                meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
                meshRenderer.receiveShadows = false;
            }

            return visual;
        }

        private void UpdateVisual(ARPlane plane, GameObject visual)
        {
            if (visual == null) return;

            Vector2 planeSize = plane.size;
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
            visual.transform.localScale = new Vector3(
                Mathf.Max(planeSize.x, 0.01f),
                Mathf.Max(thickness, 0.001f),
                Mathf.Max(planeSize.y, 0.01f));
        }

        private void OnDestroy()
        {
            foreach (KeyValuePair<TrackableId, GameObject> pair in visuals)
            {
                if (pair.Value != null)
                {
                    Destroy(pair.Value);
                }
            }

            visuals.Clear();
        }
    }
}
