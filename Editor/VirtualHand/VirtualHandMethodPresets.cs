using UnityEditor;
using UnityEngine;

namespace AvatarExperiments.VirtualHand.Editor
{
    public static class VirtualHandMethodPresets
    {
        const string Menu="Tools/Avatar Experiments/Virtual Hand/Measurement presets/";
        [MenuItem(Menu+"Pointing (Slater 2008 inspired)")]
        public static void Pointing()=>Apply(DriftMethod.InvisibleControllerPointing,"pointing-vr-adaptation");
        [MenuItem(Menu+"Marker adjustment (Rohde 2011 Exp 2 inspired)")]
        public static void Adjustment()=>Apply(DriftMethod.MarkerAdjustment,"marker-adjustment-vr-adaptation");
        [MenuItem(Menu+"Alternating staircase (Rohde 2011 Exp 1 inspired)")]
        public static void Staircase()=>Apply(DriftMethod.AlternatingStaircase,"alternating-staircase-vr-adaptation");
        [MenuItem(Menu+"Randomized ruler (Temporal dynamics 2023 inspired)")]
        public static void Ruler()=>Apply(DriftMethod.RandomizedRuler,"randomized-ruler-vr-adaptation");
        [MenuItem("Tools/Avatar Experiments/Virtual Hand/Condition presets/Melting and vibration comparison")]
        public static void MeltingConditions()
        {
            var study=Object.FindFirstObjectByType<VirtualHandStudy>();
            if(!study||study.Active)throw new System.InvalidOperationException("Open an idle study first.");
            Undo.RecordObject(study,"Set melting comparison");
            var conditions=new System.Collections.Generic.List<StudyCondition>();
            conditions.Add(new StudyCondition{code="no_melt_no_vibration",stimulus=new TrialSettings{enableMelting=false,handOffset=Vector3.right*.17f}});
            foreach(VibrationCondition vibration in System.Enum.GetValues(typeof(VibrationCondition)))
                conditions.Add(new StudyCondition{code="melt_"+vibration,stimulus=new TrialSettings{enableMelting=true,vibration=vibration,handOffset=Vector3.right*.17f}});
            study.protocol.conditions=conditions;study.protocol.protocolId="melting-vibration-comparison-v1";EditorUtility.SetDirty(study);OpenOperator();
        }
        [MenuItem("Tools/Avatar Experiments/Virtual Hand/Condition presets/Basic sync and async control")]
        public static void BasicConditions()
        {
            var study=Object.FindFirstObjectByType<VirtualHandStudy>();
            if(!study||study.Active)throw new System.InvalidOperationException("Open an idle study first.");
            Undo.RecordObject(study,"Set basic conditions");study.protocol.conditions=new StudyProtocol().conditions;
            study.protocol.protocolId="basic-vhi-control-v1";EditorUtility.SetDirty(study);OpenOperator();
        }
        static void OpenOperator()=>TabletopOperatorWindow.Open();
        static void Apply(DriftMethod method,string id)
        {
            var study=Object.FindFirstObjectByType<VirtualHandStudy>();
            if(!study||study.Active)throw new System.InvalidOperationException("Open an idle Virtual Hand study scene first.");
            Undo.RecordObject(study,"Set drift method");study.protocol.drift.method=method;study.protocol.protocolId=id;
            study.protocol.drift.repetitions=3;study.protocol.drift.coarseStep=.04f;study.protocol.drift.fineStep=.01f;
            study.protocol.drift.staircaseResponses=80;study.protocol.drift.requiredReversals=4;
            EditorUtility.SetDirty(study);TabletopOperatorWindow.Open();
            Debug.Log("Measurement method selected. Existing stimulus conditions, hand offsets and durations are retained. See Documentation~/VirtualHand.md for source-method differences.");
        }
    }
}
