using UnityEngine;

namespace AvatarExperiments.Perspectives
{
    /// <summary>Participant-confirmed steps. Dwell excludes transitions, pauses and active trials.</summary>
    public sealed class PerspectiveProtocol : MonoBehaviour
    {
        public PerspectiveController perspective;
        public PerspectiveEvent onEvent = new PerspectiveEvent();
        public string ProtocolId { get; private set; }
        public int StepIndex { get; private set; } = -1;
        public float DwellSeconds { get; private set; }
        public bool Running { get; private set; }
        public bool Complete { get; private set; }
        public string Instruction => Running ? definition.steps[StepIndex].instruction : "";
        public bool CanContinue => Running && perspective.CanChange &&
                                   DwellSeconds >= definition.steps[StepIndex].minimum_dwell_seconds;
        private ProtocolDefinition definition;

        public bool Begin(string id, out string error)
        {
            error = null;
            if (Running || !perspective || !perspective.CanChange) { error = "Protocol cannot start in this state."; return false; }
            ProtocolDefinition candidate;
            try { candidate = perspective.Catalog.Protocol(id); }
            catch (System.ArgumentException e) { error = e.Message; return false; }
            if (!perspective.TryChange(candidate.steps[0].profile_id, out error)) return false;
            definition = candidate;
            ProtocolId = id;
            StepIndex = 0;
            DwellSeconds = 0;
            Complete = false;
            Running = true;
            Emit("protocol_started");
            return true;
        }

        public bool Continue(out string error)
        {
            error = null;
            if (!CanContinue) { error = "Wait for dwell and confirm while tracking is valid."; return false; }
            if (StepIndex + 1 == definition.steps.Length)
            { Running = false; Complete = true; Emit("protocol_completed"); return true; }
            if (!perspective.TryChange(definition.steps[StepIndex + 1].profile_id, out error)) return false;
            StepIndex++;
            DwellSeconds = 0;
            Emit("protocol_step_started");
            return true;
        }

        public void Tick(float deltaSeconds)
        {
            if (!PerspectiveCatalog.Finite(deltaSeconds) || deltaSeconds < 0) throw new System.ArgumentOutOfRangeException(nameof(deltaSeconds));
            if (!Running) return;
            if (perspective.Paused) { DwellSeconds = 0; return; }
            if (perspective.CanChange && perspective.CurrentProfileId == definition.steps[StepIndex].profile_id)
                DwellSeconds += deltaSeconds;
        }

        public void Cancel()
        {
            if (!Running) return;
            perspective.Pause("protocol_cancelled");
            Running = false;
            Complete = false;
            Emit("protocol_cancelled");
        }

        public bool RestartStep(out string error)
        {
            error = null;
            if (!Running) { error = "No protocol to resume."; return false; }
            if (!perspective.TryChange(definition.steps[StepIndex].profile_id, out error)) return false;
            DwellSeconds = 0;
            Emit("protocol_step_restarted");
            return true;
        }

        private void Update() => Tick(Time.unscaledDeltaTime);
        [System.Serializable] private sealed class ProtocolEventData
        {
            public string type, protocol_id;
            public int step_index;
            public float dwell_seconds;
            public double monotonic_seconds;
        }
        private void Emit(string type) => onEvent.Invoke(JsonUtility.ToJson(new ProtocolEventData {
            type = type, protocol_id = ProtocolId, step_index = StepIndex, dwell_seconds = DwellSeconds,
            monotonic_seconds = Time.realtimeSinceStartupAsDouble
        }));
    }
}
