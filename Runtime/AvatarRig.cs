using UnityEngine;

namespace AvatarExperiments
{
    public sealed class AvatarRig : MonoBehaviour
    {
        public Animator AvatarAnimator { get; private set; }
        public Transform TrackingOrigin { get; private set; }
        public Transform Head => AvatarAnimator.GetBoneTransform(HumanBodyBones.Head);
        public Transform HeadTarget { get; private set; }
        public Transform LeftHandTarget { get; private set; }
        public Transform RightHandTarget { get; private set; }
        public void Configure(Animator avatar, Transform origin)
        {
            AvatarAnimator=avatar; TrackingOrigin=origin;
            HeadTarget=Target("Head target",Head);
            LeftHandTarget=Target("Left hand target",avatar.GetBoneTransform(HumanBodyBones.LeftHand));
            RightHandTarget=Target("Right hand target",avatar.GetBoneTransform(HumanBodyBones.RightHand));
        }
        private Transform Target(string label, Transform bone)
        {
            var t=new GameObject(label).transform;
            t.SetParent(transform,false); t.SetPositionAndRotation(bone.position,bone.rotation);
            return t;
        }
    }
}
