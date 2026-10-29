using UnityEngine;
using UnityEngine.XR;

namespace ArInspection
{
    /// <summary>
    /// AR 相机位姿兜底驱动。
    ///
    /// 背景：AR Foundation 6 官方推荐用 Input System 的 TrackedPoseDriver 驱动相机（由一键 Setup
    /// 脚本通过反射尝试挂载），但若项目没有安装 Input System，或驱动没有配置 Action，相机不会跟随
    /// 手机移动（表现为画面不动 / 内容漂移）。
    ///
    /// 本组件通过 UnityEngine.XR.InputDevices 直接读取 XR 输入子系统的 CenterEye 位姿，在检测到
    /// 相机位姿与设备位姿不一致时补写 localPosition/localRotation：
    /// - 官方驱动正常工作时，值已一致，本组件不产生任何修改（无副作用）；
    /// - 官方驱动缺失或未生效时，由本组件接管，保证 AR 相机仍然跟随设备。
    ///
    /// 这是对「TrackedPoseDriver / ARPoseDriver 都找不到」的运行时兜底，不依赖 com.unity.inputsystem。
    /// </summary>
    [DefaultExecutionOrder(10000)] // 在其它位姿驱动之后执行
    [DisallowMultipleComponent]
    public class XrPoseFallback : MonoBehaviour
    {
        [Tooltip("相机当前 localPosition 与设备位姿差异超过该阈值（米）时接管")]
        public float positionThreshold = 0.01f;

        [Tooltip("相机当前 localRotation 与设备位姿差异超过该角度（度）时接管")]
        public float rotationThreshold = 2f;

        private InputDevice device;
        private float nextProbeTime;
        private bool hasLoggedTakeover;

        private void Update()
        {
            if (Time.unscaledTime >= nextProbeTime)
            {
                nextProbeTime = Time.unscaledTime + 1f;
                // ARCore 一般把 AR 设备注册为 CenterEye；个别运行时只有 Head 设备，做两级兜底。
                device = InputDevices.GetDeviceAtXRNode(XRNode.CenterEye);
                if (!device.isValid)
                {
                    device = InputDevices.GetDeviceAtXRNode(XRNode.Head);
                }
            }

            if (!device.isValid) return;

            Vector3 devicePosition;
            Quaternion deviceRotation;
            if (!device.TryGetFeatureValue(CommonUsages.devicePosition, out devicePosition)) return;
            if (!device.TryGetFeatureValue(CommonUsages.deviceRotation, out deviceRotation)) return;

            bool positionDiffers = Vector3.Distance(transform.localPosition, devicePosition) > positionThreshold;
            bool rotationDiffers = Quaternion.Angle(transform.localRotation, deviceRotation) > rotationThreshold;
            if (!positionDiffers && !rotationDiffers) return;

            transform.localPosition = devicePosition;
            transform.localRotation = deviceRotation;

            if (!hasLoggedTakeover)
            {
                hasLoggedTakeover = true;
                Debug.Log("[XrPoseFallback] 主位姿驱动未生效，已由 XrPoseFallback 接管 AR 相机位姿。"
                          + "如需使用官方驱动，请安装 Input System 并给 Main Camera 添加 TrackedPoseDriver。");
            }
        }
    }
}
