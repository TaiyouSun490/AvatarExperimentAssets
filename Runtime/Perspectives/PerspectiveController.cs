using System;
using UnityEngine;
using UnityEngine.Events;

namespace AvatarExperiments.Perspectives
{
    [Serializable] public sealed class PerspectiveEvent : UnityEvent<string> { }

    /// <summary>Moves only the parent of the XR rig. Never writes HMD or avatar poses.</summary>
    public sealed class PerspectiveController : MonoBehaviour
    {
        public TextAsset catalogAsset;
        public Transform bodyStage;
        public Transform observerRoot;
        public Transform trackedHead;
        public CanvasGroup fadeSurface;
        public PerspectiveEvent onEvent = new PerspectiveEvent();
        public PerspectiveCatalog Catalog { get; private set; }
        public string CurrentProfileId { get; private set; } = "first_person";
        public Vector3 CurrentOffset { get; private set; }
        public bool Initialized { get; private set; }
        public bool IsTransitioning { get; private set; }
        public bool Paused { get; private set; } = true;
        public bool TrialActive { get; private set; }
        public bool TrackingValid { get; private set; }
        public bool CanChange => Initialized && !Paused && TrackingValid && !TrialActive && !IsTransitioning;

        private Vector3 stagePosition, destination;
        private Quaternion stageRotation;
        private string destinationId;
        private float elapsed;
        private bool applied;
        private float? overlapOffset;

        public bool Initialize(out string error)
        {
            error = null;
            if (Initialized) { error = "Already initialized. Use Recalibrate while paused."; return false; }
            if (!ValidateBindings(out error)) return false;
            try { Catalog = PerspectiveCatalog.Parse(catalogAsset.text); }
            catch (Exception e) { error = e.Message; return false; }
            CaptureStage();
            CurrentProfileId = "first_person";
            CurrentOffset = Vector3.zero;
            ApplyOffset(CurrentOffset);
            fadeSurface.alpha = 0;
            Initialized = true;
            Emit("initialized");
            return true;
        }

        public bool ValidateBindings(out string error)
        {
            error = null;
            if (!catalogAsset || !bodyStage || !observerRoot || !trackedHead || !fadeSurface)
                error = "Assign catalog, body stage, observer root, tracked head and fade surface.";
            else if (!PerspectiveCatalog.Finite(bodyStage.position))
                error = "Stage position must be finite.";
            else if (bodyStage == observerRoot || bodyStage.IsChildOf(observerRoot) || observerRoot.IsChildOf(bodyStage))
                error = "Body stage and observer root must be independent branches.";
            else if (trackedHead == observerRoot || !trackedHead.IsChildOf(observerRoot))
                error = "Tracked head must be a descendant of observer root.";
            else if ((bodyStage.lossyScale - Vector3.one).sqrMagnitude > 0.000001f ||
                     (observerRoot.lossyScale - Vector3.one).sqrMagnitude > 0.000001f)
                error = "Stage and observer must use unit world scale.";
            else if (Vector3.Dot(bodyStage.up, Vector3.up) < 0.99999f)
                error = "Stage calibration supports yaw only; keep gravity vertical.";
            else if (!fadeSurface.isActiveAndEnabled)
                error = "Fade surface must be active.";
            return error == null;
        }

        public void SetTrackingValid(bool valid)
        {
            if (TrackingValid == valid) return;
            TrackingValid = valid;
            if (!valid) Pause("tracking_lost");
            if (Initialized) Emit(valid ? "tracking_recovered" : "tracking_lost");
        }

        public bool Resume()
        {
            if (!Initialized || !TrackingValid || !StageUnchanged() || !ValidateBindings(out _)) return false;
            Paused = false;
            Emit("resumed");
            return true;
        }

        public bool TryBeginTrial()
        {
            if (!CanChange) return false;
            TrialActive = true;
            return true;
        }

        public void EndTrial() => TrialActive = false;

        public void Pause(string reason = "participant")
        {
            if (Paused && !TrialActive && !IsTransitioning) return;
            if (TrialActive) Emit("trial_invalidation_required", reason);
            TrialActive = false;
            Paused = true;
            // Keep the last fully applied position. No visible snap back on interruption.
            if (IsTransitioning) Emit("transition_cancelled", reason);
            IsTransitioning = false;
            if (fadeSurface) fadeSurface.alpha = 0;
            if (Initialized) Emit("paused", reason);
        }

        public bool Recalibrate(out string error)
        {
            error = null;
            if (!Initialized || !Paused) { error = "Pause before recalibrating."; return false; }
            if (!ValidateBindings(out error)) return false;
            // Caller must hide the scene during recentering and update Movement data in the same operation.
            CaptureStage();
            overlapOffset = null;
            CurrentProfileId = "first_person";
            CurrentOffset = Vector3.zero;
            ApplyOffset(CurrentOffset);
            Emit("recalibrated");
            return true;
        }

        public bool SetCalibratedOverlap(float signedLateralMetres)
        {
            if (!Initialized || !Paused || !PerspectiveCatalog.Finite(signedLateralMetres) || signedLateralMetres == 0)
                return false;
            overlapOffset = signedLateralMetres;
            Emit("overlap_calibrated", signedLateralMetres.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            return true;
        }

        public bool TryChange(string profileId, out string error)
        {
            error = null;
            if (!CanChange) { error = "Finish the trial/transition, confirm tracking, then resume."; return false; }
            if (!StageUnchanged() || !ValidateBindings(out error))
            { error = error ?? "Stage changed; pause and recalibrate."; Pause("configuration_changed"); return false; }
            SpatialProfile profile;
            try { Catalog.Validate(); profile = Catalog.Profile(profileId); }
            catch (ArgumentException e) { error = e.Message; return false; }
            if (profile.requires_overlap_calibration && !overlapOffset.HasValue)
            { error = "Confirm individual partial-overlap geometry before using this profile."; return false; }
            destination = profile.offset_m;
            if (profile.requires_overlap_calibration) destination.x = overlapOffset.Value;
            destinationId = profile.id;
            elapsed = 0;
            applied = false;
            IsTransitioning = true;
            Emit("transition_started", destinationId);
            return true;
        }

        public void Tick(float deltaSeconds)
        {
            if (!PerspectiveCatalog.Finite(deltaSeconds) || deltaSeconds < 0) throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
            if (!Initialized || Paused) return;
            if (!StageUnchanged() || !ValidateBindings(out _)) { Pause("configuration_changed"); return; }
            if (!IsTransitioning) return;
            elapsed += deltaSeconds;
            float outTime = Catalog.fade_out_seconds;
            if (elapsed < outTime) { fadeSurface.alpha = elapsed / outTime; return; }
            if (!applied)
            {
                // Hold full black for a submitted frame before moving; even a long frame cannot skip it.
                fadeSurface.alpha = 1;
                ApplyOffset(destination);
                CurrentOffset = destination;
                CurrentProfileId = destinationId;
                applied = true;
                elapsed = outTime;
                Emit("perspective_changed");
                return;
            }
            fadeSurface.alpha = 1 - Mathf.Clamp01((elapsed - outTime) / Catalog.fade_in_seconds);
            if (elapsed >= outTime + Catalog.fade_in_seconds)
            {
                IsTransitioning = false;
                Emit("transition_completed");
            }
        }

        private void Update() => Tick(Time.unscaledDeltaTime);
        private void OnApplicationFocus(bool focus) { if (!focus) Pause("focus_lost"); }
        private void OnApplicationPause(bool pause) { if (pause) Pause("app_suspended"); }
        private void OnDisable() { if (Initialized) Pause("component_disabled"); }
        private void CaptureStage() { stagePosition = bodyStage.position; stageRotation = bodyStage.rotation; }
        private bool StageUnchanged() => bodyStage && (bodyStage.position - stagePosition).sqrMagnitude < 0.000001f &&
                                         Quaternion.Angle(bodyStage.rotation, stageRotation) < 0.001f;
        private void ApplyOffset(Vector3 offset) => observerRoot.SetPositionAndRotation(stagePosition + stageRotation * offset, stageRotation);

        [Serializable] private sealed class EventData
        {
            public string type, profile_id, detail;
            public Vector3 offset_m;
            public double monotonic_seconds;
            public int frame;
        }
        private void Emit(string type, string detail = null) => onEvent.Invoke(JsonUtility.ToJson(new EventData {
            type = type, profile_id = CurrentProfileId, detail = detail, offset_m = CurrentOffset,
            monotonic_seconds = Time.realtimeSinceStartupAsDouble, frame = Time.frameCount
        }));
    }
}
