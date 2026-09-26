using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Hands;

namespace AvatarExperiments
{
    /// <summary>Explicit open-hand calibration; parent-relative retargeting after VRIK.</summary>
    [DefaultExecutionOrder(10000)]
    public sealed class XRHandHumanoidFingerDriver : MonoBehaviour
    {
        private sealed class Binding
        {
            public Transform Bone;
            public XRHandJointID Joint;
            public XRHandJointID ParentJoint;
            public Quaternion RestLocal, ParentRestInHand, Reference, Basis, LastLocal;
            public bool Calibrated;
        }

        [Serializable] private sealed class SavedJoint
        {
            public int Joint;
            public Quaternion Reference, Basis;
        }
        [Serializable] private sealed class SavedHands
        {
            public SavedJoint[] Left, Right;
        }
        public bool Calibrated => AllCalibrated(leftBindings) && AllCalibrated(rightBindings);
        public bool Busy { get; private set; }
        public string Status { get; private set; } = "Fingers not calibrated. Use open-hand calibration.";
        private Transform leftHandBone, rightHandBone;
        private string saveKey;
        private float readyAt, expiresAt, stableSince;
        private int stableSamples;
        private readonly Quaternion[] leftSample = new Quaternion[15], rightSample = new Quaternion[15];
        private readonly Quaternion[] leftBasis = new Quaternion[15], rightBasis = new Quaternion[15];
        private readonly Quaternion[] leftReference = new Quaternion[15], rightReference = new Quaternion[15];

        private static readonly List<XRHandSubsystem> Subsystems = new();
        private readonly List<Binding> leftBindings = new();
        private readonly List<Binding> rightBindings = new();
        private Animator animator;
        private Transform trackingOrigin;
        private XRHandSubsystem subsystem;

        public void Configure(Animator humanoidAnimator, Transform origin)
        {
            animator = humanoidAnimator;
            trackingOrigin = origin;
            leftHandBone = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            rightHandBone = animator.GetBoneTransform(HumanBodyBones.RightHand);
            BuildBindings(leftBindings, true);
            BuildBindings(rightBindings, false);
            saveKey = "AvatarExperiments.Fingers.v1." + animator.avatar.name;
            LoadCalibration();
        }

        private void LateUpdate()
        {
            if (animator == null)
                return;
            if (subsystem == null || !subsystem.running)
            {
                Subsystems.Clear();
                SubsystemManager.GetSubsystems(Subsystems);
                subsystem = Subsystems.Find(candidate => candidate.running);
            }
            if (Busy) StepCalibration();
            bool running = subsystem != null && subsystem.running;
            ApplyHand(running ? subsystem.leftHand : default, leftBindings);
            ApplyHand(running ? subsystem.rightHand : default, rightBindings);
        }

        private void ApplyHand(XRHand hand, List<Binding> bindings)
        {
            foreach (var binding in bindings)
            {
                if (binding.Bone == null) continue;
                if (binding.Calibrated && hand.isTracked && TryRelative(hand, binding, out var relative))
                    binding.LastLocal = RetargetLocal(relative, binding.Reference, binding.Basis, binding.RestLocal);
                // Tracking loss keeps the last LOCAL pose. No automatic rebasing,
                // and moving/adjusting the wrist still carries the whole hand.
                binding.Bone.localRotation = binding.Calibrated ? binding.LastLocal : binding.RestLocal;
            }
        }

        public static Quaternion RetargetLocal(Quaternion relative, Quaternion reference, Quaternion basis, Quaternion rest) =>
            (basis * (relative * Quaternion.Inverse(reference)) * Quaternion.Inverse(basis) * rest).normalized;

        private static bool TryRelative(XRHand hand, Binding binding, out Quaternion relative)
        {
            relative = Quaternion.identity;
            if (!hand.GetJoint(binding.Joint).TryGetPose(out var joint) ||
                !hand.GetJoint(binding.ParentJoint).TryGetPose(out var parent) || !Valid(joint.rotation) || !Valid(parent.rotation)) return false;
            relative = (Quaternion.Inverse(parent.rotation) * joint.rotation).normalized;
            return true;
        }

        public void BeginOpenHandCalibration()
        {
            if (leftBindings.Count != 15 || rightBindings.Count != 15)
            { Status = "Avatar needs all 15 finger bones per hand."; return; }
            Busy = true; readyAt = Time.unscaledTime + 3; expiresAt = readyAt + 25;
            stableSamples = 0;
            Status = "Put controllers down; show BOTH open hands to the headset.";
        }
        public void CancelCalibration()
        { if (Busy) { Busy = false; Status = "Finger calibration cancelled; previous reference kept."; } }

        private void StepCalibration()
        {
            float now = Time.unscaledTime;
            if (now < readyAt)
            { Status = $"Open-hand calibration in {Mathf.CeilToInt(readyAt-now)}... Put controllers down."; return; }
            if (now > expiresAt)
            { Busy = false; Status = "Timed out. Enable hand tracking and show both open hands. Previous reference kept."; return; }
            if (subsystem == null || !subsystem.running || !ReadOpenHand(subsystem.leftHand, leftBindings, leftHandBone, leftSample, leftBasis) ||
                !ReadOpenHand(subsystem.rightHand, rightBindings, rightHandBone, rightSample, rightBasis))
            { stableSamples = 0; Status = "Waiting: show BOTH open hands, fingers straight. Keep thumbs open."; return; }
            if (stableSamples == 0 || !Stable(leftSample, leftReference) || !Stable(rightSample, rightReference))
            {
                Array.Copy(leftSample,leftReference,15); Array.Copy(rightSample,rightReference,15);
                stableSamples = 1; stableSince = now;
            }
            else stableSamples++;
            Status = "Both open hands detected. Hold still for 1 second...";
            if (now-stableSince < 1 || stableSamples < 10) return;
            // Both hands commit atomically, only after a complete stable sample.
            Commit(leftBindings,leftSample,leftBasis); Commit(rightBindings,rightSample,rightBasis);
            Busy = false;
            SaveCalibration();
            Status = "Open hands calibrated and saved. Tracking loss will NOT reset this reference.";
        }
        private bool ReadOpenHand(XRHand hand, List<Binding> bindings, Transform handBone, Quaternion[] sample, Quaternion[] basis)
        {
            if (!hand.isTracked || handBone == null || !hand.GetJoint(XRHandJointID.Wrist).TryGetPose(out var wrist) || !Valid(wrist.rotation)) return false;
            for (int finger = 0; finger < 5; finger++)
            {
                int start = finger * 3;
                if (!hand.GetJoint(bindings[start].Joint).TryGetPose(out var a) ||
                    !hand.GetJoint(bindings[start+1].Joint).TryGetPose(out var b) ||
                    !hand.GetJoint(bindings[start+2].Joint).TryGetPose(out var c) ||
                    !hand.GetJoint((XRHandJointID)((int)bindings[start+2].Joint+1)).TryGetPose(out var tip)) return false;
                // Thumb metacarpal is naturally angled; test only its distal chain.
                if (!Straight(b.position,c.position,tip.position,.75f) ||
                    (finger != 0 && !Straight(a.position,b.position,c.position,.8f))) return false;
            }
            var originRotation = trackingOrigin != null ? trackingOrigin.rotation : Quaternion.identity;
            var wristToAvatarHand = Quaternion.Inverse(handBone.rotation) * originRotation * wrist.rotation;
            for (int i=0;i<bindings.Count;i++)
            {
                var binding=bindings[i];
                if (!TryRelative(hand,binding,out sample[i]) || !hand.GetJoint(binding.ParentJoint).TryGetPose(out var parent)) return false;
                var parentInWrist = Quaternion.Inverse(wrist.rotation) * parent.rotation;
                basis[i] = (Quaternion.Inverse(binding.ParentRestInHand) * wristToAvatarHand * parentInWrist).normalized;
                if(!Valid(basis[i]))return false;
            }
            return true;
        }
        public static bool Straight(Vector3 a, Vector3 b, Vector3 c, float cosine)
        {
            Vector3 first=b-a,second=c-b;
            return first.sqrMagnitude>1e-8f && second.sqrMagnitude>1e-8f &&
                Vector3.Dot(first.normalized,second.normalized)>=cosine;
        }
        private static bool Stable(Quaternion[] sample, Quaternion[] reference)
        {
            for(int i=0;i<sample.Length;i++) if(Quaternion.Angle(sample[i],reference[i])>10) return false;
            return true;
        }
        private static void Commit(List<Binding> bindings, Quaternion[] sample, Quaternion[] basis)
        {
            for(int i=0;i<bindings.Count;i++)
            {
                var b=bindings[i];b.Reference=sample[i];b.Basis=basis[i];b.LastLocal=b.RestLocal;b.Calibrated=true;
            }
        }
        private static bool AllCalibrated(List<Binding> bindings)
        {
            if(bindings.Count!=15)return false;
            foreach(var b in bindings)if(!b.Calibrated)return false;
            return true;
        }
        private static bool Valid(Quaternion q) => float.IsFinite(q.x)&&float.IsFinite(q.y)&&float.IsFinite(q.z)&&float.IsFinite(q.w)&&
            Mathf.Abs(Quaternion.Dot(q,q)-1)<.1f;

        private static SavedJoint[] Pack(List<Binding> bindings)
        {
            var saved=new SavedJoint[bindings.Count];
            for(int i=0;i<saved.Length;i++)saved[i]=new SavedJoint{Joint=(int)bindings[i].Joint,Reference=bindings[i].Reference,Basis=bindings[i].Basis};
            return saved;
        }
        private void SaveCalibration()
        {
            if(!Calibrated)return;
            PlayerPrefs.SetString(saveKey,JsonUtility.ToJson(new SavedHands{Left=Pack(leftBindings),Right=Pack(rightBindings)}));
            PlayerPrefs.Save();
        }
        private static bool Matches(SavedJoint[] saved,List<Binding> bindings)
        {
            if(saved==null||saved.Length!=15||bindings.Count!=15)return false;
            for(int i=0;i<15;i++)if(saved[i]==null||saved[i].Joint!=(int)bindings[i].Joint||!Valid(saved[i].Reference)||!Valid(saved[i].Basis))return false;
            return true;
        }
        private void LoadCalibration()
        {
            if(!PlayerPrefs.HasKey(saveKey))return;
            try
            {
                var saved=JsonUtility.FromJson<SavedHands>(PlayerPrefs.GetString(saveKey));
                if(saved==null||!Matches(saved.Left,leftBindings)||!Matches(saved.Right,rightBindings))return;
                Restore(leftBindings,saved.Left);Restore(rightBindings,saved.Right);
                Status="Saved open-hand calibration loaded.";
            }
            catch(ArgumentException){Status="Invalid saved finger calibration. Please recalibrate open hands.";}
        }
        private static void Restore(List<Binding> bindings,SavedJoint[] saved)
        {
            for(int i=0;i<15;i++)
            {var b=bindings[i];b.Reference=saved[i].Reference;b.Basis=saved[i].Basis;b.Calibrated=true;b.LastLocal=b.RestLocal;}
        }
        public void ResetCalibration()
        {
            Busy=false;
            foreach(var b in leftBindings)b.Calibrated=false;
            foreach(var b in rightBindings)b.Calibrated=false;
            if(!string.IsNullOrEmpty(saveKey)){PlayerPrefs.DeleteKey(saveKey);PlayerPrefs.Save();}
            Status="Finger reference cleared. Wrist settings are unchanged.";
        }

        private void BuildBindings(List<Binding> bindings, bool left)
        {
            bindings.Clear();
            Add(bindings, left ? HumanBodyBones.LeftThumbProximal : HumanBodyBones.RightThumbProximal, XRHandJointID.ThumbMetacarpal);
            Add(bindings, left ? HumanBodyBones.LeftThumbIntermediate : HumanBodyBones.RightThumbIntermediate, XRHandJointID.ThumbProximal);
            Add(bindings, left ? HumanBodyBones.LeftThumbDistal : HumanBodyBones.RightThumbDistal, XRHandJointID.ThumbDistal);
            AddFinger(bindings, left, HumanBodyBones.LeftIndexProximal, HumanBodyBones.RightIndexProximal, HumanBodyBones.LeftIndexIntermediate, HumanBodyBones.RightIndexIntermediate, HumanBodyBones.LeftIndexDistal, HumanBodyBones.RightIndexDistal, XRHandJointID.IndexProximal, XRHandJointID.IndexIntermediate, XRHandJointID.IndexDistal);
            AddFinger(bindings, left, HumanBodyBones.LeftMiddleProximal, HumanBodyBones.RightMiddleProximal, HumanBodyBones.LeftMiddleIntermediate, HumanBodyBones.RightMiddleIntermediate, HumanBodyBones.LeftMiddleDistal, HumanBodyBones.RightMiddleDistal, XRHandJointID.MiddleProximal, XRHandJointID.MiddleIntermediate, XRHandJointID.MiddleDistal);
            AddFinger(bindings, left, HumanBodyBones.LeftRingProximal, HumanBodyBones.RightRingProximal, HumanBodyBones.LeftRingIntermediate, HumanBodyBones.RightRingIntermediate, HumanBodyBones.LeftRingDistal, HumanBodyBones.RightRingDistal, XRHandJointID.RingProximal, XRHandJointID.RingIntermediate, XRHandJointID.RingDistal);
            AddFinger(bindings, left, HumanBodyBones.LeftLittleProximal, HumanBodyBones.RightLittleProximal, HumanBodyBones.LeftLittleIntermediate, HumanBodyBones.RightLittleIntermediate, HumanBodyBones.LeftLittleDistal, HumanBodyBones.RightLittleDistal, XRHandJointID.LittleProximal, XRHandJointID.LittleIntermediate, XRHandJointID.LittleDistal);
        }

        private void AddFinger(List<Binding> bindings, bool left, HumanBodyBones left0, HumanBodyBones right0, HumanBodyBones left1, HumanBodyBones right1, HumanBodyBones left2, HumanBodyBones right2, XRHandJointID joint0, XRHandJointID joint1, XRHandJointID joint2)
        {
            Add(bindings, left ? left0 : right0, joint0);
            Add(bindings, left ? left1 : right1, joint1);
            Add(bindings, left ? left2 : right2, joint2);
        }

        private void Add(List<Binding> bindings, HumanBodyBones boneId, XRHandJointID jointId)
        {
            var bone = animator.GetBoneTransform(boneId);
            if (bone != null)
            {
                var parent=bindings.Count>0?bindings[bindings.Count-1]:null;
                var hand=bone.IsChildOf(leftHandBone)?leftHandBone:rightHandBone;
                bindings.Add(new Binding { Bone=bone, Joint=jointId,
                    ParentJoint=parent!=null&&bone.parent==parent.Bone?parent.Joint:XRHandJointID.Wrist,
                    RestLocal=bone.localRotation, LastLocal=bone.localRotation,
                    ParentRestInHand=Quaternion.Inverse(hand.rotation)*bone.parent.rotation });
            }
        }
    }
}
