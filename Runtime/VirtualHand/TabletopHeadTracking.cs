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
        void OnDisable(){Application.onBeforeRender-=Track;}
        void Update(){Track();}
        void Track()
        {
            if(!trackingSpace||!float.IsFinite(physicalTableHeight))return;
            var origin=trackingSpace.localPosition;origin.y=-physicalTableHeight;trackingSpace.localPosition=origin;
            var d=InputDevices.GetDeviceAtXRNode(XRNode.Head);
            Tracked=d.TryGetFeatureValue(CommonUsages.isTracked,out bool ok)&&ok;
            if(!Tracked)return;
            SubsystemManager.GetSubsystems(inputs);
            foreach(var i in inputs)if(i.running&&i.GetTrackingOriginMode()!=TrackingOriginModeFlags.Floor)i.TrySetTrackingOriginMode(TrackingOriginModeFlags.Floor);
            if(d.TryGetFeatureValue(CommonUsages.devicePosition,out var p)&&d.TryGetFeatureValue(CommonUsages.deviceRotation,out var q))
                transform.SetPositionAndRotation(trackingSpace.TransformPoint(p),trackingSpace.rotation*q);
        }
    }
}
