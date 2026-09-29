using System;
using UnityEngine;
using UnityEngine.XR.Hands;

namespace AvatarExperiments.FpsArms
{
    /// <summary>References a user-supplied model; contains no third-party mesh or motion data.</summary>
    [CreateAssetMenu(menuName="Avatar Experiments/FPS Arm Model Profile")]
    public sealed class FpsArmModelProfile : ScriptableObject
    {
        public GameObject modelPrefab;
        public AnimationClip openPose,closedPose;
        [Tooltip("Paths relative to the model prefab root.")]
        public string upperArm,elbow,wrist,indexBase,middleBase,littleBase;
        [Tooltip("Defaults to the first skinned renderer when empty.")]
        public string rendererPath;
        public bool mirrorLeft=true;
        [Serializable] public struct Finger
        {
            public string bone,tip;
            public XRHandJointID joint,nextJoint;
            [Range(0,2)] public int controllerGroup;
        }
        public Finger[] fingers=Array.Empty<Finger>();
    }
}
