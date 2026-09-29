using System;
using UnityEngine;

namespace AvatarExperiments.FpsArms
{
    /// <summary>Optional twist distribution for arms with one intermediate bone per segment.</summary>
    [Serializable]
    public sealed class ArmTwistRelaxer
    {
        [SerializeField] Transform[] chain;
        [SerializeField] Quaternion[] rest;
        bool hasUpper,hasForearm;
        float upperAngle,forearmAngle;
        public float UpperTwist { get; private set; }
        public float ForearmTwist { get; private set; }
        public bool IsReady => chain!=null && chain.Length==5 && rest!=null && rest.Length==5;

        public void Initialize(Transform upper,Transform elbow,Transform wrist)
        {
            // Capture once and serialize in the avatar prefab. Never capture the previous
            // relaxed pose on tracking recovery, calibration, or a second Initialize call.
            if(!IsReady && elbow.parent && wrist.parent &&
                (elbow.parent==upper || elbow.parent.parent==upper) &&
                (wrist.parent==elbow || wrist.parent.parent==elbow))
            {
                chain=new[]{upper,elbow.parent==upper?null:elbow.parent,elbow,wrist.parent==elbow?null:wrist.parent,wrist};
                rest=new Quaternion[chain.Length];
                for(int i=0;i<chain.Length;i++)rest[i]=chain[i]?chain[i].localRotation:Quaternion.identity;
            }
            ResetHistory();
        }
        public void ResetHistory(){hasUpper=hasForearm=false;upperAngle=forearmAngle=0;UpperTwist=ForearmTwist=0;}
        public void RestorePose()
        {
            if(!IsReady)return;
            for(int i=0;i<chain.Length;i++)if(chain[i])chain[i].localRotation=rest[i];
        }
        public void Apply(float upperWeight,float forearmWeight)
        {
            if(!IsReady)return;
            UpperTwist=chain[1]?Relax(chain[1],chain[2],rest[2],upperWeight,ref hasUpper,ref upperAngle):0;
            ForearmTwist=chain[3]?RelaxForearm(forearmWeight):0;
        }
        float RelaxForearm(float weight)
        {
            var elbow=chain[2];var middle=chain[3];var wrist=chain[4];
            Vector3 axis=wrist.localPosition.normalized;
            float angle=ReadAngle(wrist.localRotation*Quaternion.Inverse(rest[4]),axis,ref hasForearm,ref forearmAngle);
            weight=Mathf.Clamp01(weight);
            // The middle joint is a serial deformation bone, not a free twist helper.
            // Sharing everything there just moves the pinch halfway up the forearm.
            // Spread it over elbow -> middle -> wrist (32.5%, 32.5%, 35% at default).
            Matrix4x4 wristPose=wrist.localToWorldMatrix;
            float proximalShare=angle*weight*.5f;
            Vector3 forearmAxis=elbow.InverseTransformVector(wrist.position-elbow.position).normalized;
            elbow.localRotation*=Quaternion.AngleAxis(proximalShare,forearmAxis);
            // Rotation around the full elbow-wrist axis keeps both endpoints fixed,
            // even though the model's intermediate bone is slightly off that axis.
            wrist.localRotation=(wrist.parent.worldToLocalMatrix*wristPose).rotation;
            float remaining=RawAngle(wrist.localRotation*Quaternion.Inverse(rest[4]),axis,angle-proximalShare);
            remaining=angle-proximalShare+Mathf.DeltaAngle(angle-proximalShare,remaining);
            float middleShare=remaining-angle*(1-weight);
            Quaternion share=Quaternion.AngleAxis(middleShare,axis);
            middle.localRotation*=share;
            wrist.localRotation=Quaternion.Inverse(share)*wrist.localRotation;
            return angle;
        }
        static float Relax(Transform helper,Transform child,Quaternion childRest,float weight,ref bool hasPrevious,ref float previous)
        {
            // Work in helper-local space: negative model scale on the left is handled
            // by the hierarchy itself, without guessing a world-space quaternion sign.
            Vector3 axis=child.localPosition.normalized;
            Quaternion delta=child.localRotation*Quaternion.Inverse(childRest);
            float angle=ReadAngle(delta,axis,ref hasPrevious,ref previous);
            Quaternion share=Quaternion.AngleAxis(angle*Mathf.Clamp01(weight),axis);
            helper.localRotation=helper.localRotation*share;
            child.localRotation=Quaternion.Inverse(share)*child.localRotation;
            // The axis passes through the child joint: its position is unchanged.
            // The inverse rotation also keeps its world orientation and descendants.
            return angle;
        }
        static float ReadAngle(Quaternion delta,Vector3 axis,ref bool hasPrevious,ref float previous)
        {
            float angle=RawAngle(delta,axis,hasPrevious?previous:0);
            if(hasPrevious)angle=previous+Mathf.DeltaAngle(previous,angle);
            previous=angle;hasPrevious=true;return angle;
        }
        static float RawAngle(Quaternion delta,Vector3 axis,float fallback)
        {
            float axial=Vector3.Dot(new Vector3(delta.x,delta.y,delta.z),axis);
            return axial*axial+delta.w*delta.w<1e-8f?fallback:Mathf.DeltaAngle(0,2*Mathf.Atan2(axial,delta.w)*Mathf.Rad2Deg);
        }
    }
}
