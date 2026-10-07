using UnityEditor;
using UnityEngine;

namespace AvatarExperiments.VirtualHand.Editor
{
    public sealed class TabletopOperatorWindow : EditorWindow
    {
        Vector2 scroll;
        bool showProtocol,showStimulus;
        float rulerAnswer=50;
        Vector3 pointingOffset;
        [MenuItem("Tools/Avatar Experiments/Virtual Hand/Experiment operator")]
        public static void Open()
        {
            var window=GetWindow<TabletopOperatorWindow>("Virtual Hand Operator");
            var rect=window.position;rect.width=Mathf.Max(610,rect.width);rect.height=Mathf.Max(650,rect.height);window.position=rect;window.Show();
        }
        void OnEnable(){minSize=new Vector2(610,650);}
        void OnInspectorUpdate()=>Repaint();
        void OnGUI()
        {
            var experiment=Object.FindFirstObjectByType<TabletopExperiment>();
            if(!experiment){EditorGUILayout.HelpBox("Open or create a Virtual Hand experiment scene.",MessageType.Info);return;}
            var study=experiment.study;var brush=experiment.trackedBrush;
            if(!study||!brush){EditorGUILayout.HelpBox("Study / tracked brush references are missing.",MessageType.Error);return;}
            scroll=EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.LabelField("Virtual hand experiment",EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(experiment.desktopPreview?"PC simulation — exported results are marked as simulated.":"VR study — operator panel is separate from participant prompts.",MessageType.Info);
            using(new EditorGUI.DisabledScope(study.Active||experiment.Running))
            {
                var data=new SerializedObject(experiment);data.Update();
                foreach(var name in new[]{"participantCode","sessionCode","writeLog"})EditorGUILayout.PropertyField(data.FindProperty(name));data.ApplyModifiedProperties();
                var fields=new SerializedObject(study);fields.Update();
                showProtocol=EditorGUILayout.Foldout(showProtocol,"Protocol, conditions, offsets and measurement method",true);
                if(showProtocol)EditorGUILayout.PropertyField(fields.FindProperty("protocol"),true);
                EditorGUILayout.PropertyField(fields.FindProperty("virtualTarget"),new GUIContent("Virtual anatomical target (index tip)"));
                EditorGUILayout.PropertyField(fields.FindProperty("setupChecked"),new GUIContent("Checked hand/table pose, brush, occlusion and stimulation instructions"));
                fields.ApplyModifiedProperties();
                showStimulus=EditorGUILayout.Foldout(showStimulus,"Standalone stimulus settings (outside study)",true);
                if(showStimulus){data.Update();EditorGUILayout.PropertyField(data.FindProperty("settings"),true);data.ApplyModifiedProperties();}
            }
            EditorGUILayout.Space();EditorGUILayout.LabelField("Calibration",EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(brush.Status,brush.Ready?MessageType.Info:MessageType.Warning);
            using(new EditorGUI.DisabledScope(!Application.isPlaying||study.Active||experiment.Running))
            {
                brush.calibrationPreview=EditorGUILayout.Toggle("Show calibration markers only",brush.calibrationPreview);
                if(GUILayout.Button("Pivot-calibrate physical brush tip"))brush.BeginPivot();
                brush.tipOffset=EditorGUILayout.Vector3Field("Measured brush offset (m)",brush.tipOffset);
                if(GUILayout.Button("Confirm measured brush offset"))brush.ConfirmMeasuredTip();
                if(GUILayout.Button("Capture next matching hand landmark ("+brush.CalibrationStep+"/3)"))brush.CaptureLandmark();
                if(GUILayout.Button("Clear hand calibration"))brush.ClearHandCalibration();
                EditorGUILayout.HelpBox("For drift, place the calibrated brush tip on the REAL right index fingertip, then capture. Keep that hand fixed throughout the block.",MessageType.None);
                if(GUILayout.Button("Capture REAL drift target"))study.CapturePhysicalTarget();
                EditorGUILayout.LabelField("Physical target",study.PhysicalTargetConfirmed?"Confirmed":"Not captured");
                pointingOffset=EditorGUILayout.Vector3Field("LEFT pointing tip offset (m)",pointingOffset);
                if(GUILayout.Button("Confirm LEFT tip after checking a known physical point"))study.ConfirmPointingTip(pointingOffset);
                if(experiment.desktopPreview&&GUILayout.Button("Prepare labelled PC measurement simulation"))
                {
                    var target=study.virtualTarget.position;
                    brush.Sample(Time.realtimeSinceStartupAsDouble,new Pose(brush.trackingSpace.InverseTransformPoint(brush.MapToReal(target)),Quaternion.identity));
                    study.CapturePhysicalTarget();study.ConfirmPointingTip(Vector3.zero);study.setupChecked=true;
                }
            }
            EditorGUILayout.Space();EditorGUILayout.LabelField("ESP32 (optional)",EditorStyles.boldLabel);
            using(new EditorGUI.DisabledScope(study.Active||experiment.Running))
            {
                var connection=new SerializedObject(experiment.vibration);connection.Update();
                EditorGUILayout.PropertyField(connection.FindProperty("portName"));EditorGUILayout.PropertyField(connection.FindProperty("channelList"));connection.ApplyModifiedProperties();
                if(Application.isPlaying&&GUILayout.Button("Connect muted"))experiment.vibration.Connect();
            }
            EditorGUILayout.HelpBox(experiment.vibration.Status,MessageType.None);
            EditorGUILayout.Space();EditorGUILayout.LabelField("Stage: "+study.Stage,EditorStyles.boldLabel);EditorGUILayout.HelpBox(study.Status,MessageType.Info);
            using(new EditorGUI.DisabledScope(!Application.isPlaying))
            {
                using(new EditorGUI.DisabledScope(study.Active))
                {if(GUILayout.Button("Practice (separately labelled)"))study.BeginPractice();if(GUILayout.Button("Begin study"))study.BeginStudy();}
                if(study.Stage==StudyStage.AwaitStimulus&&GUILayout.Button("Start stimuli [Space]"))study.StartStimuli();
                if(study.Measurement!=null&&(study.Stage==StudyStage.Baseline||study.Stage==StudyStage.PostMeasurement))
                {
                    EditorGUILayout.LabelField("Responses",study.Measurement.Count.ToString());
                    if(study.Measurement.Settings.method==DriftMethod.RandomizedRuler)
                    {rulerAnswer=EditorGUILayout.FloatField("Number verbally reported",rulerAnswer);if(GUILayout.Button("Record spoken ruler number"))study.SubmitRuler(rulerAnswer);}
                    if(study.Measurement.Settings.method==DriftMethod.AlternatingStaircase)
                    {if(GUILayout.Button("Probe is LEFT of felt finger"))study.SubmitJudgement(false);if(GUILayout.Button("Probe is RIGHT of felt finger"))study.SubmitJudgement(true);}
                }
                if(study.Stage==StudyStage.Rest)
                {EditorGUILayout.LabelField("Rest remaining",study.RestRemaining.ToString("F1")+" s");using(new EditorGUI.DisabledScope(study.RestRemaining>0))if(GUILayout.Button("Real hand unchanged — next trial / finish"))study.NextTrial(true);}
                study.operatorNotes=EditorGUILayout.TextField("Deviations / operator notes",study.operatorNotes);
                if(GUILayout.Button("STOP OUTPUT + ABORT STUDY")){study.Abort("Operator abort");experiment.vibration.Stop("operator_abort");}
            }
            if(!string.IsNullOrEmpty(study.RecordPath))EditorGUILayout.SelectableLabel(study.RecordPath,EditorStyles.wordWrappedLabel,GUILayout.Height(45));
            EditorGUILayout.EndScrollView();
        }
    }
}
