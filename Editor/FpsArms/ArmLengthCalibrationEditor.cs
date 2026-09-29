using UnityEditor;
using UnityEngine;

namespace AvatarExperiments.FpsArms.Editor
{
    [CustomEditor(typeof(ArmLengthCalibration))]
    public sealed class ArmLengthCalibrationEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var calibration = (ArmLengthCalibration)target;
            EditorGUILayout.HelpBox("Measure LEFT only; the result is applied to BOTH arms. Hands: hold both pinches for 1 second, release, touch LEFT elbow with RIGHT index fingertip and hold still for 1.5 seconds. Controllers: both grips to start, RIGHT controller at LEFT elbow, RIGHT trigger to confirm. Hands and fingers scale with the forearms.", MessageType.Info);
            using (new EditorGUI.DisabledScope(!Application.isPlaying))
            {
                if (GUILayout.Button("Fit Both from Left - Hands")) calibration.BeginHandCalibration();
                if (GUILayout.Button("Fit Both from Left - Controllers")) calibration.BeginControllerCalibration();
                if (GUILayout.Button("Cancel Fitting")) calibration.CancelCalibration();
                if (GUILayout.Button("Reset Saved Elbow Fit")) calibration.ResetCalibration();
            }
        }
    }
}
