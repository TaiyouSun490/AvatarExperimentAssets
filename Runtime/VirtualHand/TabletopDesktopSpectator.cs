using UnityEngine;

namespace AvatarExperiments.VirtualHand
{
    /// <summary>PC observer only. The XR camera, physical tracking frame and experimental coordinates are never moved.</summary>
    [DefaultExecutionOrder(9000), DisallowMultipleComponent]
    public sealed class TabletopDesktopSpectator : MonoBehaviour
    {
        public TabletopExperiment experiment;
        public DesktopSpectator spectator;
        public bool showDesktopView = true;
        public bool MeasurementHidden => experiment && experiment.study && experiment.study.presenter && experiment.study.presenter.Active;

        void OnEnable()
        {
            if (spectator) spectator.DetailPointProvider = DetailPoint;
        }
        Vector3? DetailPoint()
        {
            if (!experiment) return null;
            if (experiment.ball && experiment.ball.gameObject.activeInHierarchy) return experiment.ball.position;
            return experiment.hand && experiment.hand.skin ? experiment.hand.skin.bounds.center : (Vector3?)null;
        }
        void LateUpdate() => RefreshVisibility();
        public void RefreshVisibility()
        {
            if (!spectator) return;
            // Do not expose the hidden hand/reference through another PC camera during measurement.
            bool show = isActiveAndEnabled && showDesktopView && experiment && experiment.isActiveAndEnabled && !MeasurementHidden;
            if (spectator.enabled != show) spectator.enabled = show;
        }
        void OnDisable()
        {
            if (!spectator) return;
            spectator.enabled = false;
            spectator.DetailPointProvider = null;
        }
    }
}
