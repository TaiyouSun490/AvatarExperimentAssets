using UnityEngine;

namespace AvatarExperiments.FpsArms
{
    /// <summary>Continuous bend frame, independent of wrist roll and elbow extension.</summary>
    public sealed class StableElbowPole
    {
        bool hasPrevious;
        Vector3 previousAxis,previousBend;
        public void Reset(){hasPrevious=false;}

        public static Vector3 CorrectReferenceBend(Vector3 referenceAxis,Vector3 referenceBend,bool left)
        {
            referenceAxis=UnitOr(referenceAxis,Vector3.forward);
            // Elbow probes can select the opposite IK solution. Choose the body's
            // outward/downward half-plane at the measured reach, before transporting
            // it. Do not choose the sign again at each live pose (that would flip).
            Vector3 neutral=new Vector3(left?-.45f:.45f,-.8f,0).normalized;
            Vector3 expected=Transport(Vector3.forward,referenceAxis,neutral);
            Vector3 bend=Vector3.ProjectOnPlane(referenceBend,referenceAxis);
            if(float.IsNaN(bend.sqrMagnitude)||float.IsInfinity(bend.sqrMagnitude)||bend.sqrMagnitude<1e-6f)
                return expected;
            bend.Normalize();
            return Vector3.Dot(bend,expected)<0?-bend:bend;
        }

        public Vector3 Resolve(Vector3 axis,Vector3 referenceAxis,Vector3 referenceBend,bool left,float deltaTime)
        {
            axis=UnitOr(axis,Vector3.forward);
            referenceAxis=UnitOr(referenceAxis,Vector3.forward);
            referenceBend=CorrectReferenceBend(referenceAxis,referenceBend,left);
            Vector3 preferred=Transport(referenceAxis,axis,referenceBend);
            Vector3 bend=preferred;
            if(hasPrevious)
            {
                bend=Transport(previousAxis,axis,previousBend);
                // A single reference frame has an antipodal singularity. Through that region
                // keep the previous transported plane, then return gradually on leaving it.
                float confidence=Mathf.InverseLerp(-.98f,-.8f,Vector3.Dot(referenceAxis,axis));
                float angle=Vector3.SignedAngle(bend,preferred,axis);
                float maxStep=180*Mathf.Max(0,deltaTime)*confidence;
                bend=Quaternion.AngleAxis(Mathf.Clamp(angle,-maxStep,maxStep),axis)*bend;
            }
            bend=Vector3.ProjectOnPlane(bend,axis).normalized;
            previousAxis=axis;previousBend=bend;hasPrevious=true;
            return bend;
        }
        static Vector3 Transport(Vector3 from,Vector3 to,Vector3 bend)
        {
            // At exactly opposite reach directions use the known bend as the rotation axis.
            var q=Vector3.Dot(from,to)<-.9999f?Quaternion.AngleAxis(180,bend):Quaternion.FromToRotation(from,to);
            return Vector3.ProjectOnPlane(q*bend,to).normalized;
        }
        static Vector3 UnitOr(Vector3 value,Vector3 fallback)
        {
            float length=value.sqrMagnitude;
            return float.IsNaN(length)||float.IsInfinity(length)||length<1e-8f?fallback:value.normalized;
        }
    }
}
