using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace AvatarExperiments
{
    /// <summary>Local avatar sizing only; never scales the tracking origin or camera.</summary>
    public sealed class AvatarCalibration : MonoBehaviour
    {
        [Serializable] private sealed class Settings
        {
            public float Scale = 1;
            public bool HeightCalibrated;
            public Vector3 LeftWrist, RightWrist;
            public float LeftArm = 1, RightArm = 1, LeftLeg = 1, RightLeg = 1;
        }
        public string Status { get; private set; } = "Stand upright before fitting height.";
        public float RelativeScale => settings.Scale;
        public bool HeightCalibrated => settings.HeightCalibrated;
        public float ReferenceEyeHeight { get; private set; }
        public float LastMeasuredEyeHeight { get; private set; }
        public bool Busy => operation != 0;
        public Vector3 LeftWrist => settings.LeftWrist;
        public Vector3 RightWrist => settings.RightWrist;
        private Settings settings = new();
        private AvatarRig rig;
        private AvatarTrackingInput input;
        private Transform avatar;
        private Vector3 baselineScale, eyeToHead;
        private float baselineRootFloorOffset, initialFloor;
        private string key;
        private int operation, samples;
        private float deadline, sum, minimum, maximum;
        private readonly List<XRInputSubsystem> xrInputs = new();
        private bool loadSaved;
        private readonly float[] baselineLengths = new float[4];
        public float LimbLength(int index) => index >= 0 && index < 4 ? baselineLengths[index] * RelativeScale * LimbRatio(index) : 0;
        private float LimbRatio(int index) => index switch { 0 => settings.LeftArm, 1 => settings.RightArm, 2 => settings.LeftLeg, _ => settings.RightLeg };

        public void Configure(AvatarRig avatarRig, GameObject avatarRoot, float floorY)
        {
            rig = avatarRig; input = GetComponent<AvatarTrackingInput>(); avatar = avatarRoot.transform;
            baselineScale = avatar.localScale; initialFloor = floorY;
            baselineRootFloorOffset = avatar.position.y - floorY;
            var animator = rig.AvatarAnimator;
            HumanBodyBones[] chain = { HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand,
                HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand,
                HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot,
                HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot };
            for (int i = 0; i < 4; i++)
            {
                var a = animator.GetBoneTransform(chain[i * 3]); var b = animator.GetBoneTransform(chain[i * 3 + 1]); var c = animator.GetBoneTransform(chain[i * 3 + 2]);
                baselineLengths[i] = a != null && b != null && c != null ? Vector3.Distance(a.position,b.position) + Vector3.Distance(b.position,c.position) : 0;
            }
            var left = animator.GetBoneTransform(HumanBodyBones.LeftEye);
            var right = animator.GetBoneTransform(HumanBodyBones.RightEye);
            Vector3 eye = left != null && right != null ? (left.position + right.position) * .5f : rig.Head.position + avatar.up * .08f;
            ReferenceEyeHeight = eye.y - floorY;
            eyeToHead = Quaternion.Inverse(avatar.rotation) * (rig.Head.position - eye);
            key = "AvatarExperiments.Calibration.v1." + animator.avatar.name;
            loadSaved = PlayerPrefs.HasKey(key);
        }
        private void Start()
        {
            if (!loadSaved) return;
            LoadSettings(key);
        }
        private bool LoadSettings(string sourceKey)
        {
            try
            {
                if (!PlayerPrefs.HasKey(sourceKey)) { Status = "No saved values available."; return false; }
                var saved = JsonUtility.FromJson<Settings>(PlayerPrefs.GetString(sourceKey));
                if (saved == null || !ValidScale(saved.Scale) || !Finite(saved.LeftWrist) || !Finite(saved.RightWrist))
                { Status = "Invalid saved values; current settings kept."; return false; }
                if (!CanResize()) return false;
                // Wrist calibration also works without the optional paid IK package.
                settings.LeftWrist = ClampAngles(saved.LeftWrist); settings.RightWrist = ClampAngles(saved.RightWrist);
                ApplyWrist();
                var bridge = GetComponent<FinalIkBridge>();
                if (bridge == null || !bridge.IsActive)
                { Status = "Wrists loaded; install FinalIK to restore limb dimensions."; return true; }
                if (!ApplyLimbLengths(ValidLimb(saved.LeftArm) ? saved.LeftArm : 1, ValidLimb(saved.RightArm) ? saved.RightArm : 1,
                    ValidLimb(saved.LeftLeg) ? saved.LeftLeg : 1, ValidLimb(saved.RightLeg) ? saved.RightLeg : 1)) return false;
                if (!ApplyScale(saved.Scale, saved.HeightCalibrated)) return false;
                settings.LeftWrist = ClampAngles(saved.LeftWrist); settings.RightWrist = ClampAngles(saved.RightWrist);
                ApplyWrist();
                Status = "Saved calibration loaded.";
                return true;
            }
            catch (Exception e) { Status = "Saved calibration not loaded: " + e.Message; return false; }
        }
        public void RestorePrevious()
        {
            if (Busy) { Status = "Cancel calibration before restoring."; return; }
            if (LoadSettings(key + ".Previous"))
                Status = "Previous save restored. Press Save to keep it on next launch.";
        }
        public void BeginHeightCalibration()
        {
            if (!CanResize() || !TryEyeHeight(out _)) return;
            operation = 1; deadline = Time.unscaledTime + 3;
            samples = 0; sum = 0; minimum = float.MaxValue; maximum = float.MinValue;
        }
        public void Cancel() { operation = 0; Status = "Calibration cancelled."; }
        private void Update()
        {
            if (!Busy) return;
            float remaining = deadline - Time.unscaledTime;
            if (remaining > 0)
            {
                Status = $"Stand upright, look ahead; {(operation == 2 ? "both hands visible; " : "")}{Mathf.CeilToInt(remaining)}...";
                return;
            }
            if (!TryEyeHeight(out float height)) { operation = 0; return; }
            sum += height; samples++; minimum = Mathf.Min(minimum, height); maximum = Mathf.Max(maximum, height);
            Status = "Measuring standing eye height... stay still.";
            if (Time.unscaledTime < deadline + 1) return;
            operation = 0;
            if (samples < 8 || maximum - minimum > .045f)
            { Status = "Height was unstable. Stand still and retry."; return; }
            LastMeasuredEyeHeight = sum / samples;
            if (ApplyScale(LastMeasuredEyeHeight / ReferenceEyeHeight, true))
                Status = $"Fitted eye height {LastMeasuredEyeHeight * 100:F1} cm; avatar x{RelativeScale:F3}.";
        }
        private bool TryEyeHeight(out float height)
        {
            height = 0;
            xrInputs.Clear(); SubsystemManager.GetSubsystems(xrInputs);
            bool floor = false;
            foreach (var subsystem in xrInputs)
                floor |= subsystem.running && subsystem.GetTrackingOriginMode() == TrackingOriginModeFlags.Floor;
            if (!floor) { Status = "Floor origin required. Set your VR floor / room setup first."; return false; }
            var device = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            if (!device.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) || !tracked ||
                !device.TryGetFeatureValue(CommonUsages.devicePosition, out var p) ||
                !device.TryGetFeatureValue(CommonUsages.deviceRotation, out var q))
            { Status = "HMD tracking missing. Height not changed."; return false; }
            if (Mathf.Abs((q * Vector3.forward).y) > .45f)
            { Status = "Look straight ahead, not down, then retry."; return false; }
            height = p.y;
            if (!float.IsFinite(height) || height < .75f || height > 2.3f)
            { Status = "Standing eye height is outside 75-230 cm. Check floor setup."; return false; }
            return true;
        }
        // Experiments may block resizing during their own effects/simulation.
        public Func<bool> CanResizeOverride;
        public event Action DimensionsChanged;
        private bool CanResize()
        {
            if (CanResizeOverride == null || CanResizeOverride()) return true;
            Status = "Resizing blocked by the active experiment."; return false;
        }
        public bool ApplyScale(float relative, bool calibrated)
        {
            if (avatar == null || !ValidScale(relative) || ReferenceEyeHeight < .5f)
            { Status = "Invalid avatar scale (allowed: 0.5-1.6x)."; return false; }
            if (!CanResize()) return false;
            // Absolute baseline, not multiplying the current scale on each press.
            avatar.localScale = baselineScale * relative;
            var p = avatar.position;
            p.y = (rig.TrackingOrigin != null ? rig.TrackingOrigin.position.y : initialFloor) + baselineRootFloorOffset * relative;
            avatar.position = p;
            settings.Scale = relative; settings.HeightCalibrated = calibrated;
            input.HeadPositionOffset = calibrated ? eyeToHead * relative : Vector3.zero;
            GetComponent<FinalIkBridge>()?.RefreshAvatarScale(relative);
            DimensionsChanged?.Invoke();
            Status = $"Avatar scale x{relative:F3}. ";
            return true;
        }
        public void AdjustScale(float delta)
        { if (Busy) Cancel(); ApplyScale(Mathf.Clamp(RelativeScale + delta, .5f, 1.6f), true); }
        public bool ApplyLimbLengths(float leftArm, float rightArm, float leftLeg, float rightLeg)
        {
            if (!ValidLimb(leftArm) || !ValidLimb(rightArm) || !ValidLimb(leftLeg) || !ValidLimb(rightLeg))
            { Status = "Limb length must be 70-130% of the scaled avatar."; return false; }
            if (!CanResize()) return false;
            var bridge = GetComponent<FinalIkBridge>();
            if (bridge == null || !bridge.IsActive || !bridge.SetLimbLengths(leftArm, rightArm, leftLeg, rightLeg))
            { Status = "FinalIK limb length support unavailable."; return false; }
            settings.LeftArm = leftArm; settings.RightArm = rightArm; settings.LeftLeg = leftLeg; settings.RightLeg = rightLeg;
            DimensionsChanged?.Invoke();
            Status = "Limb lengths updated. Save when fitted; ";
            return true;
        }
        public void AdjustLimb(int index, float metres)
        {
            if (index < 0 || index > 3 || !float.IsFinite(metres) || baselineLengths[index] < .05f) return;
            if (Busy) Cancel();
            float[] ratios = { settings.LeftArm, settings.RightArm, settings.LeftLeg, settings.RightLeg };
            ratios[index] = Mathf.Clamp(ratios[index] + metres / (baselineLengths[index] * RelativeScale), .7f, 1.3f);
            ApplyLimbLengths(ratios[0], ratios[1], ratios[2], ratios[3]);
        }
        private static bool ValidLimb(float value) => float.IsFinite(value) && value >= .7f && value <= 1.3f;
        public void AdjustWrist(bool left, int axis, float degrees)
        {
            if (axis < 0 || axis > 2 || !float.IsFinite(degrees)) return;
            var angles = left ? settings.LeftWrist : settings.RightWrist;
            angles[axis] = Mathf.Clamp(angles[axis] + degrees, -180, 180);
            if (left) settings.LeftWrist = angles; else settings.RightWrist = angles;
            ApplyWrist();
        }
        public void ResetWrists() { settings.LeftWrist = settings.RightWrist = Vector3.zero; ApplyWrist(); }
        private void ApplyWrist() { input.LeftWristEuler = settings.LeftWrist; input.RightWristEuler = settings.RightWrist; }
        public void Save()
        {
            if (Busy) { Status = "Wait for calibration to finish before saving."; return; }
            string json = JsonUtility.ToJson(settings);
            // Repeated presses with unchanged values must not erase the undo snapshot.
            if (PlayerPrefs.HasKey(key) && PlayerPrefs.GetString(key) != json)
                PlayerPrefs.SetString(key + ".Previous", PlayerPrefs.GetString(key));
            PlayerPrefs.SetString(key, json); PlayerPrefs.Save();
            Status = "Saved on this PC. Previous save retained.";
        }
        public void ResetSettings()
        {
            Cancel();
            if (!ApplyScale(1, false)) return;
            if (!ApplyLimbLengths(1, 1, 1, 1)) return;
            if (PlayerPrefs.HasKey(key)) PlayerPrefs.SetString(key + ".Previous", PlayerPrefs.GetString(key));
            ResetWrists(); PlayerPrefs.DeleteKey(key); PlayerPrefs.Save(); Status = "Defaults restored. Previous save retained.";
        }
        private static bool ValidScale(float value) => float.IsFinite(value) && value >= .5f && value <= 1.6f;
        private static bool Finite(Vector3 value) => float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
        private static Vector3 ClampAngles(Vector3 value) => new(Mathf.Clamp(value.x,-180,180),Mathf.Clamp(value.y,-180,180),Mathf.Clamp(value.z,-180,180));
    }
}
