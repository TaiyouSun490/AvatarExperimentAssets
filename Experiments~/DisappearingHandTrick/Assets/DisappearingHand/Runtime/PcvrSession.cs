using System.Collections;
using UnityEngine;
using UnityEngine.XR.Management;

namespace DisappearingHand
{
    /// <summary>Explicit startup keeps the desktop preview independent of an installed XR runtime.</summary>
    public sealed class PcvrSession : MonoBehaviour
    {
        public DisappearingHandExperiment experiment;
        public string Status { get; private set; } = "Starting";
        XRManagerSettings manager;
        bool ownsSession;
        IEnumerator Start()
        {
            // All Awake methods, including command-line preview selection, have run.
            if (experiment.desktopPreview) { Status = "Desktop preview"; yield break; }
            yield return null;
            manager = XRGeneralSettings.Instance ? XRGeneralSettings.Instance.Manager : null;
            if (!manager) { Status = "OpenXR configuration missing"; Debug.LogError(Status); yield break; }
            if (!manager.activeLoader)
            {
                Status = "Connecting to OpenXR";
                yield return manager.InitializeLoader();
                if (!manager.activeLoader)
                { Status = "No headset. Connect HMD / OpenXR runtime and restart."; Debug.LogWarning(Status); yield break; }
                ownsSession = true;
            }
            manager.StartSubsystems(); Status = "OpenXR running";
        }
        void OnDestroy()
        {
            if (!ownsSession || !manager || !manager.activeLoader) return;
            manager.StopSubsystems(); manager.DeinitializeLoader();
        }
    }
}
