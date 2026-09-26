using System;
using System.Reflection;
using UnityEngine;

namespace AvatarExperiments
{
    /// <summary>
    /// Connects the tracked targets to FinalIK VRIK without making the repository depend on
    /// the paid FinalIK sources. The component activates automatically when FinalIK is present.
    /// </summary>
    public sealed class FinalIkBridge : MonoBehaviour
    {
        private const BindingFlags PublicInstance = BindingFlags.Instance | BindingFlags.Public;

        [SerializeField] private bool plantFeet = true;
        [SerializeField, Range(0f, 1f)] private float headPositionWeight = 1f;
        [SerializeField, Range(0f, 1f)] private float headRotationWeight = 1f;
        [SerializeField, Range(0f, 1f)] private float handPositionWeight = 1f;
        [SerializeField, Range(0f, 1f)] private float handRotationWeight = 1f;

        public bool IsActive { get; private set; }

        private GameObject avatarRoot;
        private AvatarRig rig;
        private Component vrik;
        private float originalSolverScale = 1;

        public bool SetLimbLengths(float leftArm, float rightArm, float leftLeg, float rightLeg)
        {
            var solver = GetField(vrik, "solver");
            string[] limbs = { "leftArm", "rightArm", "leftLeg", "rightLeg" };
            float[] values = { leftArm, rightArm, leftLeg, rightLeg };
            for (int i = 0; i < 4; i++)
                if (!float.IsFinite(values[i]) || values[i] < .7f || values[i] > 1.3f ||
                    GetField(solver, limbs[i])?.GetType().GetField(i < 2 ? "armLengthMlp" : "legLengthMlp", PublicInstance) == null)
                    return false;
            for (int i = 0; i < 4; i++)
                SetField(GetField(solver, limbs[i]), i < 2 ? "armLengthMlp" : "legLengthMlp", values[i]);
            return true;
        }

        public void RefreshAvatarScale(float relativeScale)
        {
            var solver = GetField(vrik, "solver");
            if (solver == null) return;
            SetField(solver, "scale", originalSolverScale * relativeScale);
            solver.GetType().GetMethod("Reset", PublicInstance)?.Invoke(solver, null);
        }

        public void Configure(GameObject humanoidRoot, AvatarRig avatarRig)
        {
            avatarRoot = humanoidRoot;
            rig = avatarRig;
            TryInitialize();
        }

        private void Start()
        {
            if (!IsActive)
                TryInitialize();
        }

        private void TryInitialize()
        {
            if (avatarRoot == null || rig == null)
                return;

            var vrikType = FindType("RootMotion.FinalIK.VRIK");
            if (vrikType == null)
            {
                Debug.LogWarning("FinalIK VRIK was not found. avatar remains visible with desktop targets, but full-body IK is disabled.", this);
                return;
            }

            vrik = avatarRoot.GetComponent(vrikType) ?? avatarRoot.AddComponent(vrikType);
            vrikType.GetMethod("AutoDetectReferences", PublicInstance)?.Invoke(vrik, null);

            var references = GetField(vrik, "references");
            var isFilled = references != null &&
                (bool)(references.GetType().GetProperty("isFilled", PublicInstance)?.GetValue(references) ?? false);
            if (!isFilled)
            {
                Debug.LogError("FinalIK could not auto-detect avatar's Humanoid bone references.", avatarRoot);
                return;
            }

            var solver = GetField(vrik, "solver");
            originalSolverScale = (float)(GetField(solver, "scale") ?? 1f);
            var spine = GetField(solver, "spine");
            var leftArm = GetField(solver, "leftArm");
            var rightArm = GetField(solver, "rightArm");
            SetField(solver, "plantFeet", plantFeet);
            SetField(spine, "headTarget", rig.HeadTarget);
            SetField(spine, "positionWeight", headPositionWeight);
            SetField(spine, "rotationWeight", headRotationWeight);
            ConfigureArm(leftArm, rig.LeftHandTarget);
            ConfigureArm(rightArm, rig.RightHandTarget);
            vrikType.GetMethod("GuessHandOrientations", PublicInstance)?.Invoke(vrik, null);

            if (vrik is Behaviour behaviour)
                behaviour.enabled = true;
            IsActive = true;
            Debug.Log("Avatar Experiments: FinalIK VRIK connected to head and both hand targets.", this);
        }

        private void ConfigureArm(object arm, Transform target)
        {
            SetField(arm, "target", target);
            SetField(arm, "positionWeight", handPositionWeight);
            SetField(arm, "rotationWeight", handRotationWeight);
        }

        private static Type FindType(string fullName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(fullName, false);
                if (type != null)
                    return type;
            }
            return null;
        }

        private static object GetField(object instance, string name) =>
            instance?.GetType().GetField(name, PublicInstance)?.GetValue(instance);

        private static void SetField(object instance, string name, object value)
        {
            instance?.GetType().GetField(name, PublicInstance)?.SetValue(instance, value);
        }
    }
}
