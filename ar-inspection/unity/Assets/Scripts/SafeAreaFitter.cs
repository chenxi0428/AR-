using UnityEngine;

namespace ArInspection
{
    /// <summary>
    /// 让一个 RectTransform 适配屏幕 SafeArea（刘海屏 / 挖孔屏 / 手势条）。
    /// 挂在 Canvas 下的一个全屏容器上即可，屏幕尺寸或安全区变化时自动更新。
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    [DisallowMultipleComponent]
    public class SafeAreaFitter : MonoBehaviour
    {
        private RectTransform rectTransform;
        private Rect lastSafeArea;
        private Vector2Int lastScreenSize;

        private void Awake()
        {
            rectTransform = GetComponent<RectTransform>();
            ApplySafeArea();
        }

        private void Update()
        {
            if (Screen.safeArea != lastSafeArea
                || lastScreenSize.x != Screen.width
                || lastScreenSize.y != Screen.height)
            {
                ApplySafeArea();
            }
        }

        private void ApplySafeArea()
        {
            if (rectTransform == null)
            {
                rectTransform = GetComponent<RectTransform>();
                if (rectTransform == null) return;
            }

            lastSafeArea = Screen.safeArea;
            lastScreenSize = new Vector2Int(Screen.width, Screen.height);

            if (Screen.width <= 0 || Screen.height <= 0) return;

            Vector2 safeMin = lastSafeArea.position;
            Vector2 safeMax = safeMin + lastSafeArea.size;

            safeMin.x /= Screen.width;
            safeMin.y /= Screen.height;
            safeMax.x /= Screen.width;
            safeMax.y /= Screen.height;

            rectTransform.anchorMin = safeMin;
            rectTransform.anchorMax = safeMax;
            rectTransform.offsetMin = Vector2.zero;
            rectTransform.offsetMax = Vector2.zero;
        }
    }
}
