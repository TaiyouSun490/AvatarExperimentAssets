using UnityEngine;
using UnityEngine.XR.Hands;

namespace AvatarExperiments
{
    /// <summary>Tracking-space palm ray and size-normalized pinch. No avatar calibration offsets.</summary>
    public sealed class HandUiPointer
    {
        private bool ready, pinched;
        private Vector3 position, direction;
        public void Reset() { ready=false;pinched=false; }
        public static bool TryReadHand(XRHand hand,out Vector3 knuckle,out Vector3 direction,out float pinchRatio)
        {
            knuckle=direction=default;pinchRatio=0;
            if(!hand.isTracked)return false;
            bool valid=hand.GetJoint(XRHandJointID.Wrist).TryGetPose(out var wrist);
            valid&=hand.GetJoint(XRHandJointID.MiddleProximal).TryGetPose(out var middle);
            valid&=hand.GetJoint(XRHandJointID.IndexProximal).TryGetPose(out var indexBase);
            valid&=hand.GetJoint(XRHandJointID.IndexTip).TryGetPose(out var index);
            valid&=hand.GetJoint(XRHandJointID.ThumbTip).TryGetPose(out var thumb);
            if(!valid)return false;
            knuckle=indexBase.position;direction=middle.position-wrist.position;
            pinchRatio=Vector3.Distance(index.position,thumb.position)/Mathf.Clamp(direction.magnitude,.055f,.13f);
            return Finite(knuckle)&&Finite(direction)&&direction.sqrMagnitude>1e-6f&&float.IsFinite(pinchRatio);
        }
        public bool Step(bool valid,Vector3 knuckle,Vector3 palmDirection,float pinchRatio,float dt,out Ray ray,out bool pressed)
        {
            ray=default;pressed=false;
            if(!valid||!Finite(knuckle)||!Finite(palmDirection)||palmDirection.sqrMagnitude<1e-6f||
                !float.IsFinite(pinchRatio)||pinchRatio<0||!float.IsFinite(dt)||dt<=0||dt>.1f)
            {Reset();return false;}
            bool next=pinched?pinchRatio<.45f:pinchRatio<.28f;
            Vector3 aim=palmDirection.normalized;
            if(ready&&Vector3.Distance(position,knuckle)>.25f){Reset();return false;}
            if(!ready)
            {position=knuckle;direction=aim;ready=true;}
            else if(!(next&&!pinched)) // Keep pre-pinch aim on the click frame.
            {
                float blend=1-Mathf.Exp(-22*dt);
                position=Vector3.Lerp(position,knuckle,blend);
                direction=Vector3.Slerp(direction,aim,blend).normalized;
            }
            pinched=next;pressed=next;ray=new Ray(position,direction);return true;
        }
        private static bool Finite(Vector3 v)=>float.IsFinite(v.x)&&float.IsFinite(v.y)&&float.IsFinite(v.z);
    }

    /// <summary>Deliberate hands-only menu open gesture. Must release after loss/open before arming again.</summary>
    public sealed class HandUiMenuHold
    {
        private float elapsed;
        private bool armed;
        public bool Pending=>elapsed>0;
        public void Reset(){elapsed=0;armed=false;}
        public bool Step(bool valid,bool gesture,float dt)
        {
            if(!valid||!float.IsFinite(dt)||dt<=0||dt>.1f){Reset();return false;}
            if(!gesture){elapsed=0;armed=true;return false;}
            if(!armed)return false;
            elapsed+=dt;
            if(elapsed<.8f)return false;
            Reset();return true;
        }
    }
}
