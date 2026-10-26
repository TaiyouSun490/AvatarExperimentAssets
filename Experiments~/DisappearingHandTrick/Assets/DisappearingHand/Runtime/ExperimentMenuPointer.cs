using AvatarExperiments.FpsArms;
using UnityEngine;
using UnityEngine.UI;

namespace DisappearingHand
{
    [DefaultExecutionOrder(11000)]
    public sealed class ExperimentMenuPointer : MonoBehaviour
    {
        public DisappearingHandExperiment experiment;
        public TendonHand.TendonHandExperiment tendonExperiment;
        public RectTransform panel;
        public Button[] buttons;
        public RectTransform cursor;
        bool wasDown;
        void Update()
        {
            var rig=tendonExperiment ? tendonExperiment.rig : experiment.rig;
            bool controller=rig.TryGetController(false,out Pose pose,out float trigger,out _);
            bool down=controller?trigger>.65f:rig.RightPinching;
            bool clicked=down&&!wasDown; wasDown=down;
            bool ready=tendonExperiment ? tendonExperiment.TrackingReady : experiment.TrackingReady;
            if (!panel.gameObject.activeInHierarchy || !ready) { cursor.gameObject.SetActive(false); return; }
            Ray ray=controller?new Ray(pose.position,pose.rotation*Vector3.forward):new Ray(rig.head.transform.position,rig.head.transform.forward);
            var plane=new Plane(panel.forward,panel.position);
            if (!plane.Raycast(ray,out float distance)||distance>3) { cursor.gameObject.SetActive(false); return; }
            Vector3 hit=ray.GetPoint(distance);
            var local=panel.InverseTransformPoint(hit);
            if (!panel.rect.Contains(new Vector2(local.x,local.y))) { cursor.gameObject.SetActive(false); return; }
            cursor.gameObject.SetActive(true);cursor.position=hit-panel.forward*.002f;
            if (!clicked) return;
            foreach (var button in buttons)
            {
                if (!button || !button.interactable) continue;
                var rect=(RectTransform)button.transform;
                Vector3 b=rect.InverseTransformPoint(hit);
                if (rect.rect.Contains(new Vector2(b.x,b.y))) { button.onClick.Invoke(); break; }
            }
        }
    }
}
