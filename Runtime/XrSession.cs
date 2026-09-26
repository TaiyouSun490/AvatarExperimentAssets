using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;

namespace AvatarExperiments
{
    /// <summary>PCVR lifecycle and camera pose, independent of Meta SDK and avatar IK.</summary>
    [DefaultExecutionOrder(9900)]
    public sealed class XrSession : MonoBehaviour
    {
        private readonly List<XRDisplaySubsystem> displays = new();
        private readonly List<XRInputSubsystem> inputs = new();
        private AvatarRig rig;
        private Camera ownedCamera;
        private Transform desktopParent;
        private Vector3 desktopPosition;
        private Quaternion desktopRotation;
        private bool knownState;
        public bool Running { get; private set; }
        public bool Retrying { get; private set; }
        public string Status { get; private set; } = "XR: checking";

        public void Configure(AvatarRig avatarRig, Camera camera)
        {
            rig = avatarRig;
            // Only the explicitly assigned camera is controlled.
            if (camera != null)
            {
                ownedCamera = camera;
                desktopParent = camera.transform.parent;
                desktopPosition = camera.transform.localPosition;
                desktopRotation = camera.transform.localRotation;
            }
            RefreshState();
        }

        private void OnEnable() => Application.onBeforeRender += ApplyHeadPose;
        private void OnDisable() => Application.onBeforeRender -= ApplyHeadPose;
        private void LateUpdate() { RefreshState(); ApplyHeadPose(); }

        private void RefreshState()
        {
            displays.Clear();
            SubsystemManager.GetSubsystems(displays);
            bool running = false;
            foreach (var display in displays) running |= display.running;
            if (knownState && running == Running) return;
            knownState = true;
            Running = running;
            Status = running ? "XR running: " + OpenXRRuntime.name :
                "XR not running: connect Link/SteamVR, then Retry XR";
            if (running)
            {
                inputs.Clear(); SubsystemManager.GetSubsystems(inputs);
                foreach (var input in inputs)
                    if (input.running && (input.GetSupportedTrackingOriginModes() & TrackingOriginModeFlags.Floor) != 0)
                        input.TrySetTrackingOriginMode(TrackingOriginModeFlags.Floor);
                if (ownedCamera != null && rig != null)
                    ownedCamera.transform.SetParent(rig.TrackingOrigin, false);
            }
            else if (ownedCamera != null)
            {
                ownedCamera.transform.SetParent(desktopParent, false);
                ownedCamera.transform.SetLocalPositionAndRotation(desktopPosition, desktopRotation);
            }
            Debug.Log("Avatar Experiments: " + Status, this);
        }

        private void ApplyHeadPose()
        {
            if (!Running || ownedCamera == null || rig == null) return;
            var device = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            if (!device.isValid || !device.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) || !tracked)
                return;
            // Exactly one tracking transform. Never parent the XR camera to a solved bone.
            if (device.TryGetFeatureValue(CommonUsages.devicePosition, out var p))
                ownedCamera.transform.localPosition = p;
            if (device.TryGetFeatureValue(CommonUsages.deviceRotation, out var q))
                ownedCamera.transform.localRotation = q;
        }

        public void RetryXr()
        {
            if (!Running && !Retrying) StartCoroutine(Retry());
        }

        private IEnumerator Retry()
        {
            var manager = XRGeneralSettings.Instance?.Manager;
            if (manager == null) { Status = "XR manager missing; configure Standalone OpenXR"; yield break; }
            Retrying = true;
            try
            {
                manager.DeinitializeLoader();
                yield return manager.InitializeLoader();
                if (manager.activeLoader != null) manager.StartSubsystems();
                knownState = false;
                RefreshState();
            }
            finally { Retrying = false; }
        }
    }
}
