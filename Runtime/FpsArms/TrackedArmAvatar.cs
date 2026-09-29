using System;
using UnityEngine;

namespace AvatarExperiments.FpsArms
{
    /// <summary>Model-only arm solver. Targets can be driven by controllers or another tracking provider.</summary>
    public sealed class TrackedArmAvatar : MonoBehaviour
    {
        [Serializable] public struct FingerPose
        {
            public Transform bone;
            public Quaternion open, closed;
            public int group; // 0: thumb, 1: index, 2: other fingers
        }

        public Transform head, wristTarget, model, upperArm, elbow, wrist;
        public Transform indexBase, middleBase, littleBase;
        public SkinnedMeshRenderer skin;
        public bool leftHand;
        [Tooltip("Shoulder estimate in the horizontal head frame, in metres.")]
        public Vector3 shoulderOffset = new Vector3(.19f, -.2f, -.05f);
        [Tooltip("Elbow bend at Pole Reference Axis, in the horizontal head frame.")]
        public Vector3 elbowHint = new Vector3(.45f, -.8f, -.25f);
        public Vector3 poleReferenceAxis = Vector3.forward;
        public Vector3 BendDirection { get; private set; }
        public FingerPose[] fingers=Array.Empty<FingerPose>();
        [Range(0,1)] public float grip, trigger, thumb;
        public bool tracked = true;
        [Tooltip("Hide the mesh when tracking is unavailable. Off keeps the initial or last valid pose visible.")]
        public bool hideWhenUntracked;
        [Header("Twist Relaxation")]
        public bool relaxTwist = true;
        [Tooltip("Fraction of elbow twist distributed onto arm_down_deform.")]
        [Range(0,1)] public float upperTwistWeight = .5f;
        [Tooltip("Fraction of wrist twist spread across both forearm bones; the rest stays at the wrist.")]
        [Range(0,1)] public float forearmTwistWeight = .65f;
        [SerializeField, HideInInspector] ArmTwistRelaxer twistRelaxer = new ArmTwistRelaxer();
        public ArmTwistRelaxer TwistRelaxer => twistRelaxer;
        [HideInInspector]
        public float armLengthScale = 1;
        [Range(.6f,1.6f)] public float upperArmScale = 1, forearmScale = 1;
        [SerializeField, HideInInspector] bool segmentScalingInitialized;
        [SerializeField, HideInInspector] Vector3 originalUpperScale, originalElbowScale, originalWristScale;
        [SerializeField, HideInInspector] Transform[] lengthBones;
        [SerializeField, HideInInspector] Vector3[] originalLengthPositions;
        public float ArmLength => upperLength + lowerLength;
        public float UpperLength => upperLength;
        public float ForearmLength => lowerLength;
        public float WristError { get; private set; }
        Quaternion modelRestRotation;
        float upperLength, lowerLength;
        bool initialized;
        readonly StableElbowPole pole=new StableElbowPole();
        int poleFrame=-1;

        public void Initialize()
        {
            if(!head||!wristTarget||!model||!upperArm||!elbow||!wrist||!indexBase||!middleBase||!littleBase)return;
            modelRestRotation = model.localRotation;
            twistRelaxer.Initialize(upperArm,elbow,wrist);
            // Undo the retired v1 joint-translation calibration before capturing lengths.
            if(lengthBones!=null && originalLengthPositions!=null)
                for(int i=0;i<Mathf.Min(lengthBones.Length,originalLengthPositions.Length);i++)
                    if(lengthBones[i])lengthBones[i].localPosition=originalLengthPositions[i];
            if(!segmentScalingInitialized)
            {
                originalUpperScale=upperArm.localScale;originalElbowScale=elbow.localScale;originalWristScale=wrist.localScale;
                upperArmScale=forearmScale=1;armLengthScale=1;segmentScalingInitialized=true;
            }
            ApplyArmLength();
            initialized = true;
            ResetPole();
        }
        public void ResetPole(){pole.Reset();poleFrame=-1;}
        public void SetPoleReference(Vector3 reachAxis,Vector3 bend)
        {
            poleReferenceAxis=reachAxis.normalized;
            elbowHint=StableElbowPole.CorrectReferenceBend(poleReferenceAxis,bend,leftHand);
            ResetPole();
        }
        void ApplyArmLength()
        {
            // Scale each complete skinned segment, including its surface, instead of pulling
            // joints apart. The hand and fingers inherit the forearm's scale too.
            upperArm.localScale=originalUpperScale*upperArmScale;
            elbow.localScale=originalElbowScale*(forearmScale/upperArmScale);
            wrist.localScale=originalWristScale;
            upperLength = Vector3.Distance(upperArm.position, elbow.position);
            lowerLength = Vector3.Distance(elbow.position, wrist.position);
        }
        public bool CanSetArmLength(float metres)
        {
            if(!initialized)Initialize();
            float factor=metres/ArmLength;
            return CanSetSegmentLengths(upperLength*factor,lowerLength*factor);
        }
        public bool SetArmLength(float metres)
        {
            if(!CanSetArmLength(metres))return false;
            float factor=metres/ArmLength;
            return SetSegmentLengths(upperLength*factor,lowerLength*factor);
        }
        public bool CanSetSegmentLengths(float upper,float forearm)
        {
            if(!initialized)Initialize();
            float us=upper/(upperLength/upperArmScale),fs=forearm/(lowerLength/forearmScale);
            return upper>=.15f && forearm>=.15f && upper+forearm<=.95f && us>=.6f && us<=1.6f && fs>=.6f && fs<=1.6f;
        }
        public bool SetSegmentLengths(float upper,float forearm)
        {
            if(!CanSetSegmentLengths(upper,forearm))return false;
            upperArmScale=upper/(upperLength/upperArmScale);forearmScale=forearm/(lowerLength/forearmScale);
            ApplyArmLength();return true;
        }
        public void ResetArmLength()
        {
            if(!initialized)Initialize();
            armLengthScale=upperArmScale=forearmScale=1;ApplyArmLength();
        }
        void Awake() { Initialize(); }

        public void SetTracking(bool available)
        {
            tracked = available;
            if (skin) skin.enabled = available || !hideWhenUntracked;
        }

        public void Solve()
        {
            if (!initialized) Initialize();
            if (!initialized) return;
            SetTracking(tracked);
            if (!tracked) return;
            // IK starts from the same reference on every update. Otherwise the minimal
            // swing solve carries last frame's axial rotation into the next frame.
            twistRelaxer.RestorePose();
            foreach (var finger in fingers)
            {
                float amount = finger.group == 0 ? thumb : finger.group == 1 ? trigger : grip;
                finger.bone.localRotation = Quaternion.Slerp(finger.open, finger.closed, amount);
            }
            Vector3 forward = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < .01f) forward = Vector3.forward;
            Quaternion yaw = Quaternion.LookRotation(forward, Vector3.up);
            model.rotation = yaw * modelRestRotation;
            Vector3 shoulder = head.position + yaw * shoulderOffset;
            model.position += shoulder - upperArm.position;

            Vector3 direction = wristTarget.position - shoulder;
            float distance = Mathf.Clamp(direction.magnitude, Mathf.Abs(upperLength-lowerLength)+.001f, upperLength+lowerLength-.001f);
            Vector3 axis = direction.sqrMagnitude > .00001f ? direction.normalized : yaw * Vector3.forward;
            // Move the calibrated bend frame with the arm instead of projecting one fixed
            // vector onto it: that projection flips when the wrist passes the hint direction.
            float poleDt=!Application.isPlaying?1f/60f:poleFrame==Time.frameCount?0:Mathf.Min(Time.unscaledDeltaTime,.05f);
            poleFrame=Time.frameCount;
            Vector3 bend=yaw*pole.Resolve(Quaternion.Inverse(yaw)*axis,poleReferenceAxis,elbowHint,leftHand,poleDt);
            BendDirection=bend;
            float along = (upperLength*upperLength - lowerLength*lowerLength + distance*distance) / (2*distance);
            float height = Mathf.Sqrt(Mathf.Max(0, upperLength*upperLength-along*along));
            Vector3 desiredElbow = shoulder + axis*along + bend*height;
            RotateWorld(upperArm, Quaternion.FromToRotation(elbow.position-upperArm.position, desiredElbow-upperArm.position));
            Vector3 reachableWrist = shoulder + axis*distance;
            RotateWorld(elbow, Quaternion.FromToRotation(wrist.position-elbow.position, reachableWrist-elbow.position));
            RotateWorld(wrist, wristTarget.rotation * Quaternion.Inverse(HandFrame()));
            if(relaxTwist)twistRelaxer.Apply(upperTwistWeight,forearmTwistWeight);
            WristError = Vector3.Distance(wrist.position, wristTarget.position);
        }

        public Quaternion HandFrame()
        {
            Vector3 forward = middleBase.position-wrist.position;
            Vector3 back = Vector3.Cross(indexBase.position-littleBase.position, forward) * (leftHand ? -1 : 1);
            return Quaternion.LookRotation(forward, back);
        }

        // Conjugate through the parent's matrix so mirrored left arms rotate correctly too.
        public static void RotateWorld(Transform bone, Quaternion delta)
        {
            Matrix4x4 p = bone.parent.localToWorldMatrix;
            Matrix4x4 localDelta = p.inverse * Matrix4x4.Rotate(delta) * p;
            bone.localRotation = localDelta.rotation * bone.localRotation;
        }
    }
}
