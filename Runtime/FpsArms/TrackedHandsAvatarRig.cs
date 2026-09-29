using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Hands;

namespace AvatarExperiments.FpsArms
{
    /// <summary>Arbitrates bare-hand joints and tracked physical controllers per hand.</summary>
    [DefaultExecutionOrder(200)]
    public sealed class TrackedHandsAvatarRig : MonoBehaviour
    {
        [Serializable] public struct JointBinding
        {
            public XRHandJointID joint, nextJoint;
            public Transform bone, tip;
        }
        public Transform trackingSpace;
        public Camera head;
        [Tooltip("Disable when another XR component already owns the camera pose.")]
        public bool driveHeadPose=true;
        [Tooltip("Opt-in tracking diagnostic logs, every five seconds.")]
        public bool logTracking;
        public TrackedArmAvatar leftArm, rightArm;
        public JointBinding[] leftJoints, rightJoints;
        public enum InputMode { Automatic, HandsOnly, ControllersOnly }
        public InputMode inputMode;
        public Vector3 leftWristOffset=new Vector3(0,-.015f,-.06f), rightWristOffset=new Vector3(0,-.015f,-.06f);
        public Vector3 leftWristEuler, rightWristEuler;
        public bool LeftUsesController { get; private set; }
        public bool RightUsesController { get; private set; }
        public bool HeadTracked { get; private set; }
        public bool SubsystemRunning => subsystem != null && subsystem.running;
        public int LeftValidJoints { get; private set; }
        public int RightValidJoints { get; private set; }
        public bool LeftPinching { get; private set; }
        public bool RightPinching { get; private set; }
        readonly List<XRHandSubsystem> subsystems = new List<XRHandSubsystem>();
        readonly List<XRInputSubsystem> inputs = new List<XRInputSubsystem>();
        readonly Vector3[] positions = new Vector3[XRHandJointID.EndMarker.ToIndex()];
        readonly bool[] valid = new bool[XRHandJointID.EndMarker.ToIndex()];
        readonly TrackedJointSample leftSample=new TrackedJointSample(),rightSample=new TrackedJointSample();
        XRHandSubsystem subsystem;
        bool floorOrigin;
        float lastUpdate, nextReport;

        void OnEnable() { MarkHandsUntracked(); Application.onBeforeRender+=UpdateControllers; }
        void OnDisable() { Application.onBeforeRender-=UpdateControllers; Unsubscribe(); MarkHandsUntracked(); }
        void Unsubscribe()
        {
            if (subsystem != null) subsystem.updatedHands -= OnHandsUpdated;
            subsystem = null;
        }
        void MarkHandsUntracked()
        {
            if(leftArm) leftArm.SetTracking(false);
            if(rightArm) rightArm.SetTracking(false);
            LeftValidJoints = RightValidJoints = 0;
            LeftPinching=RightPinching=false;
            LeftUsesController=RightUsesController=false;
            leftSample.Clear();rightSample.Clear();
        }
        void Update()
        {
            if(!head||!trackingSpace||!leftArm||!rightArm)return;
            if (!SubsystemRunning)
            {
                Unsubscribe(); SubsystemManager.GetSubsystems(subsystems);
                foreach (var candidate in subsystems)
                    if (candidate.running) { subsystem = candidate; subsystem.updatedHands += OnHandsUpdated; break; }
            }
            if (!floorOrigin)
            {
                SubsystemManager.GetSubsystems(inputs);
                foreach (var input in inputs)
                    if (input.running) floorOrigin |= input.TrySetTrackingOriginMode(TrackingOriginModeFlags.Floor);
            }
            UpdateHead();
            if (!SubsystemRunning || !HeadTracked || Time.unscaledTime-lastUpdate > .25f) MarkHandsUntracked();
            UpdateControllers();
            if (logTracking && Time.unscaledTime >= nextReport)
            {
                nextReport = Time.unscaledTime+5;
                Debug.Log($"HAND_FINGER_TRACKING subsystem={SubsystemRunning} head={HeadTracked} left={leftArm.tracked}/{LeftValidJoints} right={rightArm.tracked}/{RightValidJoints} controllers={LeftUsesController}/{RightUsesController}");
            }
        }
        void UpdateHead()
        {
            var device = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            HeadTracked = device.isValid && device.TryGetFeatureValue(CommonUsages.isTracked,out bool tracked) && tracked;
            if (!HeadTracked) return;
            bool p = device.TryGetFeatureValue(CommonUsages.centerEyePosition,out Vector3 position);
            bool r = device.TryGetFeatureValue(CommonUsages.centerEyeRotation,out Quaternion rotation);
            if (!p) p = device.TryGetFeatureValue(CommonUsages.devicePosition,out position);
            if (!r) r = device.TryGetFeatureValue(CommonUsages.deviceRotation,out rotation);
            HeadTracked = p && r;
            if (HeadTracked && driveHeadPose) head.transform.SetPositionAndRotation(trackingSpace.TransformPoint(position),trackingSpace.rotation*rotation);
        }
        void OnHandsUpdated(XRHandSubsystem source, XRHandSubsystem.UpdateSuccessFlags flags, XRHandSubsystem.UpdateType updateType)
        {
            if(!head||!trackingSpace||!leftArm||!rightArm)return;
            UpdateHead(); lastUpdate = Time.unscaledTime;
            LeftValidJoints = ReadHand(source.leftHand,leftArm,leftJoints,HeadTracked && (flags & XRHandSubsystem.UpdateSuccessFlags.LeftHandJoints) != 0);
            RightValidJoints = ReadHand(source.rightHand,rightArm,rightJoints,HeadTracked && (flags & XRHandSubsystem.UpdateSuccessFlags.RightHandJoints) != 0);
        }
        int ReadHand(XRHand hand, TrackedArmAvatar arm, JointBinding[] bindings, bool updated)
        {
            var sample=arm.leftHand?leftSample:rightSample;sample.Clear();
            if(TryApplyController(arm))return 0;
            if(inputMode==InputMode.ControllersOnly){arm.SetTracking(false);return 0;}
            Array.Clear(valid,0,valid.Length); int count=0;
            if (updated && hand.isTracked)
                for (int i=0;i<positions.Length;i++)
                {
                    var joint = hand.GetJoint(XRHandJointIDUtility.FromIndex(i));
                    if (joint.TryGetPose(out var pose))
                    { valid[i]=true;positions[i]=trackingSpace.TransformPoint(pose.position);count++; }
                }
            sample.Capture(positions,valid,Time.unscaledTime);
            bool applied=ApplyJoints(arm,bindings,positions,valid);
            bool pinch=applied && valid[XRHandJointID.ThumbTip.ToIndex()] && valid[XRHandJointID.IndexTip.ToIndex()] &&
                Vector3.Distance(positions[XRHandJointID.ThumbTip.ToIndex()],positions[XRHandJointID.IndexTip.ToIndex()])<.025f;
            if(arm.leftHand)LeftPinching=pinch;else RightPinching=pinch;
            return count;
        }
        public bool TryGetTrackedJoint(bool left,XRHandJointID joint,out Vector3 position)
        {
            position=default;
            if(!HeadTracked||inputMode==InputMode.ControllersOnly||(left?LeftUsesController:RightUsesController))return false;
            return (left?leftSample:rightSample).TryGet(joint.ToIndex(),Time.unscaledTime,out position);
        }
        public bool TryGetController(bool left,out Pose worldPose,out float trigger,out float grip)
        {
            worldPose=default;trigger=grip=0;
            var device=InputDevices.GetDeviceAtXRNode(left?XRNode.LeftHand:XRNode.RightHand);
            if(!HeadTracked || !device.isValid || (device.characteristics&InputDeviceCharacteristics.Controller)==0 ||
                (device.characteristics&InputDeviceCharacteristics.HandTracking)!=0 ||
                !device.TryGetFeatureValue(CommonUsages.isTracked,out bool tracked) || !tracked ||
                !device.TryGetFeatureValue(CommonUsages.devicePosition,out Vector3 p) ||
                !device.TryGetFeatureValue(CommonUsages.deviceRotation,out Quaternion q))return false;
            worldPose=new Pose(trackingSpace.TransformPoint(p),trackingSpace.rotation*q);
            device.TryGetFeatureValue(CommonUsages.trigger,out trigger);device.TryGetFeatureValue(CommonUsages.grip,out grip);
            return true;
        }
        void UpdateControllers()
        {
            if(!head||!trackingSpace||!leftArm||!rightArm)return;
            UpdateHead();
            UpdateController(leftArm);UpdateController(rightArm);
        }
        void UpdateController(TrackedArmAvatar arm)
        {
            bool previously=arm.leftHand?LeftUsesController:RightUsesController;
            if(!TryApplyController(arm) && (previously || inputMode==InputMode.ControllersOnly))arm.SetTracking(false);
        }
        bool TryApplyController(TrackedArmAvatar arm)
        {
            bool available=inputMode!=InputMode.HandsOnly && TryGetController(arm.leftHand,out _,out _,out _);
            if(arm.leftHand)LeftUsesController=available;else RightUsesController=available;
            if(!available)return false;
            TryGetController(arm.leftHand,out var pose,out float trigger,out float grip);
            var offset=arm.leftHand?leftWristOffset:rightWristOffset;
            var euler=arm.leftHand?leftWristEuler:rightWristEuler;
            ApplyControllerPose(arm,pose,offset,euler,trigger,grip);
            if(arm.leftHand)LeftPinching=false;else RightPinching=false;
            return true;
        }
        public static void ApplyControllerPose(TrackedArmAvatar arm,Pose pose,Vector3 offset,Vector3 euler,float trigger,float grip)
        {
            arm.wristTarget.SetPositionAndRotation(pose.position+pose.rotation*offset,pose.rotation*Quaternion.Euler(euler));
            arm.trigger=Mathf.Clamp01(trigger);arm.grip=arm.thumb=Mathf.Clamp01(grip);arm.SetTracking(true);arm.Solve();
        }

        public static bool ApplyJoints(TrackedArmAvatar arm, JointBinding[] bindings, Vector3[] worldPositions, bool[] validJoints)
        {
            bool complete = validJoints[XRHandJointID.Wrist.ToIndex()] && validJoints[XRHandJointID.MiddleProximal.ToIndex()]
                && validJoints[XRHandJointID.IndexProximal.ToIndex()] && validJoints[XRHandJointID.LittleProximal.ToIndex()];
            foreach (var binding in bindings)
                if(!IsPalmHelper(binding.joint))complete &= validJoints[binding.joint.ToIndex()] && validJoints[binding.nextJoint.ToIndex()];
            if (!complete) { arm.SetTracking(false);return false; }
            Vector3 wrist = worldPositions[XRHandJointID.Wrist.ToIndex()];
            Vector3 forward = worldPositions[XRHandJointID.MiddleProximal.ToIndex()]-wrist;
            Vector3 across = worldPositions[XRHandJointID.IndexProximal.ToIndex()]-worldPositions[XRHandJointID.LittleProximal.ToIndex()];
            Vector3 back = Vector3.Cross(across,forward)*(arm.leftHand?-1:1);
            if(forward.sqrMagnitude<1e-8f || back.sqrMagnitude<1e-10f)
            { arm.SetTracking(false);return false; }
            arm.tracked=true;
            // Neutralize the old pose before solving the arm, then apply each measured finger segment.
            arm.grip=arm.trigger=arm.thumb=0;
            arm.wristTarget.SetPositionAndRotation(wrist,Quaternion.LookRotation(forward,back));
            arm.Solve();
            foreach (var binding in bindings)
            {
                // The FBX's four *_bone transforms share one pivot near the wrist.
                // They are palm deformation helpers, not anatomical metacarpal joints.
                if(IsPalmHelper(binding.joint))continue;
                Vector3 direction = worldPositions[binding.nextJoint.ToIndex()]-worldPositions[binding.joint.ToIndex()];
                if(direction.sqrMagnitude<1e-10f)continue;
                TrackedArmAvatar.RotateWorld(binding.bone,Quaternion.FromToRotation(binding.tip.position-binding.bone.position,direction));
            }
            return true;
        }
        public static bool IsPalmHelper(XRHandJointID joint) => joint==XRHandJointID.IndexMetacarpal ||
            joint==XRHandJointID.MiddleMetacarpal || joint==XRHandJointID.RingMetacarpal || joint==XRHandJointID.LittleMetacarpal;
    }
}
