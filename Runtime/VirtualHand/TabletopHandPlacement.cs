using System;
using UnityEngine;

namespace AvatarExperiments.VirtualHand
{
    /// <summary>Neutral real-hand alignment and experimental displacement are separate transforms.</summary>
    [DisallowMultipleComponent]
    public sealed class TabletopHandPlacement : MonoBehaviour
    {
        public TabletopExperiment experiment;
        public bool Calibrated { get; private set; }
        public Vector3 NeutralPosition { get; private set; }
        public Quaternion NeutralRotation { get; private set; } = Quaternion.identity;
        public Vector3 Offset { get; private set; }
        public float Yaw { get; private set; }
        Vector3 expectedPosition, expectedScale;
        Quaternion expectedRotation;
        public void SetNeutral(Vector3 position, Quaternion rotation)
        {
            NeutralPosition=position; NeutralRotation=rotation; Calibrated=true; Apply(Vector3.zero,0);
        }
        public void Apply(Vector3 offset, float yaw)
        {
            if(experiment.Running)throw new InvalidOperationException("Placement is fixed while stimuli are running.");
            if(!Calibrated)throw new InvalidOperationException("Calibrate the real hand before applying an offset.");
            if(!float.IsFinite(offset.sqrMagnitude)||!float.IsFinite(yaw))throw new ArgumentException("Invalid offset.");
            Offset=offset; Yaw=yaw;
            var hand=experiment.hand.transform;
            hand.SetPositionAndRotation(NeutralPosition+NeutralRotation*offset,NeutralRotation*Quaternion.Euler(0,yaw,0));
            expectedPosition=hand.position;expectedRotation=hand.rotation;expectedScale=hand.lossyScale;
        }
        public bool Unchanged => !Calibrated ||
            (Vector3.Distance(expectedPosition,experiment.hand.transform.position)<.001f &&
             Quaternion.Angle(expectedRotation,experiment.hand.transform.rotation)<.1f &&
             Vector3.Distance(expectedScale,experiment.hand.transform.lossyScale)<.001f);
        public void Invalidate() { Calibrated=false; }
    }
}
