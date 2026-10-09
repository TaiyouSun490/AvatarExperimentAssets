using UnityEngine;

namespace AvatarExperiments.Perspectives
{
    /// <summary>Parameterless entry points for participant UI UnityEvents. No SDK-specific button bindings.</summary>
    public sealed class PerspectiveActions : MonoBehaviour
    {
        public PerspectiveController perspective;
        public PerspectiveProtocol protocol;
        public string protocolId = "guided_alternation";
        public PerspectiveEvent onStatus = new PerspectiveEvent();

        public void Initialize()
        {
            if (!perspective) { onStatus.Invoke("Assign perspective controller."); return; }
            if (!perspective.Initialize(out string error)) onStatus.Invoke(error);
            else onStatus.Invoke("Ready for tracking validation and participant confirmation.");
        }
        public void StartProtocol()
        {
            if (!protocol) { onStatus.Invoke("Assign protocol."); return; }
            if (!protocol.Begin(protocolId, out string error)) onStatus.Invoke(error);
        }
        public void Continue()
        {
            if (!protocol) return;
            if (!protocol.Continue(out string error)) onStatus.Invoke(error);
        }
        public void Pause() { if (perspective) perspective.Pause(); }
        public void Resume()
        {
            if (!perspective || !perspective.Resume()) { onStatus.Invoke("Tracking/calibration is not ready."); return; }
            if (protocol && protocol.Running && !protocol.RestartStep(out string error)) onStatus.Invoke(error);
        }
        public void Cancel() { if (protocol) protocol.Cancel(); }
    }
}
