using UnityEngine;

namespace AvatarExperiments
{
    /// <summary>Add once to an empty object; assign a user-owned humanoid and XR origin/camera.</summary>
    [DisallowMultipleComponent]
    public sealed class AvatarExperimentSetup : MonoBehaviour
    {
        public Animator Avatar;
        public Transform TrackingOrigin;
        public Camera XrCamera;
        [Tooltip("Optional rendering-only origin for third person. Keep TrackingOrigin fixed for body targets. Camera should already be under this branch.")]
        public Transform ObserverOrigin;
        [Tooltip("Optional perspective module; observer, head visibility and UI use its rendering frame.")]
        public Perspectives.PerspectiveController Perspective;
        public bool EnableFirstPersonHeadHiding;
        public bool DesktopMenuPreview;
        private void Awake()
        {
            if(Avatar==null || Avatar.avatar==null || !Avatar.isHuman || !Avatar.avatar.isValid ||
                TrackingOrigin==null || XrCamera==null)
            { Debug.LogError("Assign a valid Humanoid Animator, tracking origin and XR camera.",this); enabled=false;return; }
            var rig=gameObject.AddComponent<AvatarRig>();rig.Configure(Avatar,TrackingOrigin);
            var input=gameObject.AddComponent<AvatarTrackingInput>();input.Configure(rig);
            var calibration=gameObject.AddComponent<AvatarCalibration>();
            calibration.Configure(rig,Avatar.gameObject,TrackingOrigin.position.y);
            var fingers=gameObject.AddComponent<XRHandHumanoidFingerDriver>();
            fingers.Configure(Avatar,TrackingOrigin);
            gameObject.AddComponent<FinalIkBridge>().Configure(Avatar.gameObject,rig);
            var renderingOrigin=Perspective!=null?Perspective.observerRoot:ObserverOrigin;
            var session=gameObject.AddComponent<XrSession>();session.Configure(rig,XrCamera,renderingOrigin);
            if(EnableFirstPersonHeadHiding)
            {
                var visibility=gameObject.AddComponent<FirstPersonHeadVisibility>();
                visibility.Configure(Avatar,XrCamera,session);visibility.Perspective=Perspective;
            }
            if(Perspective!=null)
                calibration.CanResizeOverride=()=>!Perspective.Initialized ||
                    (!Perspective.TrialActive&&!Perspective.IsTransitioning&&Perspective.CurrentOffset.sqrMagnitude<.000001f);
            var menu=gameObject.AddComponent<VrCalibrationMenu>();menu.DesktopPreview=DesktopMenuPreview;menu.Configure(rig,XrCamera,renderingOrigin);
        }
    }
}
