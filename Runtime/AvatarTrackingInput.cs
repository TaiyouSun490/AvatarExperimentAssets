using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Hands;

namespace AvatarExperiments
{
    public sealed class AvatarTrackingInput : MonoBehaviour
    {
        public bool UiOwnsGestures { get; set; }
        public Vector3 HeadPositionOffset, LeftWristEuler, RightWristEuler;
        private AvatarRig rig;
        private readonly List<XRHandSubsystem> hands=new();
        public void Configure(AvatarRig value) => rig=value;
        private void Update()
        {
            if(rig==null)return;
            if(DevicePose(XRNode.Head,out var hp,out var hq))
            {
                rig.HeadTarget.position=rig.TrackingOrigin.TransformPoint(hp)+rig.TrackingOrigin.rotation*HeadPositionOffset;
                rig.HeadTarget.rotation=rig.TrackingOrigin.rotation*hq;
            }
            hands.Clear(); SubsystemManager.GetSubsystems(hands);
            UpdateHand(true); UpdateHand(false);
        }
        private void UpdateHand(bool left)
        {
            Vector3 p=default; Quaternion q=Quaternion.identity; bool valid=false;
            foreach(var provider in hands)
            {
                if(!provider.running)continue;
                var hand=left?provider.leftHand:provider.rightHand;
                if(hand.isTracked && hand.GetJoint(XRHandJointID.Wrist).TryGetPose(out var pose))
                { p=pose.position; q=pose.rotation; valid=true; break; }
            }
            if(!valid)valid=DevicePose(left?XRNode.LeftHand:XRNode.RightHand,out p,out q);
            if(!valid)return;
            var target=left?rig.LeftHandTarget:rig.RightHandTarget;
            target.SetPositionAndRotation(rig.TrackingOrigin.TransformPoint(p),
                rig.TrackingOrigin.rotation*q*Quaternion.Euler(left?LeftWristEuler:RightWristEuler));
        }
        private static bool DevicePose(XRNode node,out Vector3 p,out Quaternion q)
        {
            p=default;q=Quaternion.identity;
            var d=InputDevices.GetDeviceAtXRNode(node);
            return d.TryGetFeatureValue(CommonUsages.isTracked,out bool tracked)&&tracked &&
                d.TryGetFeatureValue(CommonUsages.devicePosition,out p) &&
                d.TryGetFeatureValue(CommonUsages.deviceRotation,out q);
        }
    }
}
