using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace AvatarExperiments.VirtualHand
{
    public sealed class TabletopHeadTracking:MonoBehaviour
    {
        public Transform trackingSpace;
        [Min(0)] public float physicalTableHeight=.75f;
        readonly List<XRInputSubsystem> inputs=new List<XRInputSubsystem>();
        public bool Tracked {get;private set;}
        void OnEnable(){Application.onBeforeRender+=Track;}
        void OnDisable(){Application.onBeforeRender-=Track;Tracked=false;}
        void Update(){Track();}
        void Track()
        {
            Tracked=false;
            if(!trackingSpace||!float.IsFinite(physicalTableHeight)||physicalTableHeight<0)return;
            var origin=trackingSpace.localPosition;origin.y=-physicalTableHeight;trackingSpace.localPosition=origin;
            var d=InputDevices.GetDeviceAtXRNode(XRNode.Head);
            if(!d.isValid||!d.TryGetFeatureValue(CommonUsages.isTracked,out bool ok)||!ok||
                !d.TryGetFeatureValue(CommonUsages.trackingState,out var state)||
                (state&(InputTrackingState.Position|InputTrackingState.Rotation))!=(InputTrackingState.Position|InputTrackingState.Rotation)||
                !d.TryGetFeatureValue(CommonUsages.devicePosition,out var p)||!d.TryGetFeatureValue(CommonUsages.deviceRotation,out var q)||
                !float.IsFinite(p.sqrMagnitude)||!float.IsFinite(Quaternion.Dot(q,q))||Mathf.Abs(Quaternion.Dot(q,q)-1)>.01f)return;
            SubsystemManager.GetSubsystems(inputs);
            foreach(var i in inputs)if(i.running&&i.GetTrackingOriginMode()!=TrackingOriginModeFlags.Floor)
            {i.TrySetTrackingOriginMode(TrackingOriginModeFlags.Floor);return;}
            transform.SetPositionAndRotation(trackingSpace.TransformPoint(p),trackingSpace.rotation*q);Tracked=true;
        }
    }
}
