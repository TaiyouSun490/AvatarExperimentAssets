using UnityEngine;
using UnityEngine.InputSystem;

namespace AvatarExperiments.VirtualHand
{
    // Enabled only by an explicit desktop preview or the separate generated PC scene.
    [DefaultExecutionOrder(-25)]
    public sealed class TabletopDesktopPreview : MonoBehaviour
    {
        public TabletopExperiment experiment;
        TabletopTrackedBrush brush;
        Camera view;
        MeshCollider handSurface;
        Vector3 fallbackTip;

        public void Initialize(TabletopExperiment target)
        {
            experiment = target; brush = target.trackedBrush; view = Camera.main;
            var head = view.GetComponent<TabletopHeadTracking>(); if (head) head.enabled = false;
            brush.automaticSampling = false; experiment.desktopPreview = true; experiment.controllerControls = false;
            experiment.ResetTrial(); brush.tipOffset = Vector3.zero; brush.ConfirmMeasuredTip();
            experiment.hand.Select(experiment.settings.appearance);
            double now = Time.realtimeSinceStartupAsDouble;
            for (int i = 0; i < 3; i++)
            {
                brush.Sample(now - .03 + i * .01, new Pose(brush.trackingSpace.InverseTransformPoint(brush.visualLandmarks[i].position), Quaternion.identity));
                brush.CaptureLandmark();
            }
            fallbackTip = brush.visualLandmarks[0].position;
            handSurface = experiment.hand.gameObject.AddComponent<MeshCollider>();
            handSurface.sharedMesh = experiment.hand.surface.sharedMesh;
            Physics.SyncTransforms();
            Vector3 center = experiment.hand.skin.bounds.center;
            view.transform.position = center + new Vector3(0, .65f, -.6f);
            view.transform.LookAt(center); view.fieldOfView = 45; view.nearClipPlane = .01f;
            Debug.Log("TABLETOP_PC_PREVIEW_READY mouse-driven brush preview; optional condition-based ESP32 vibration");
        }
        void Update()
        {
            if (!experiment || !brush || !view) return;
            var mouse = Mouse.current; Vector3 tip = fallbackTip;
            if (mouse != null)
            {
                Vector2 pointer = mouse.position.ReadValue();
                if (view.pixelRect.Contains(pointer))
                {
                    var ray = view.ScreenPointToRay(pointer);
                    if (handSurface.Raycast(ray, out var hit, 2)) tip = hit.point;
                    else if (new Plane(experiment.hand.transform.up, fallbackTip).Raycast(ray, out float distance)) tip = ray.GetPoint(distance);
                }
                if (!mouse.leftButton.isPressed) tip += experiment.hand.transform.up * .035f;
            }
            brush.Sample(Time.realtimeSinceStartupAsDouble, new Pose(brush.trackingSpace.InverseTransformPoint(brush.Ready?brush.MapToReal(tip):tip), Quaternion.identity));
            if (!experiment.Running) brush.Preview();
            var keyboard = Keyboard.current;
            if (keyboard != null && !experiment.Running && (!experiment.study||!experiment.study.Active))
            {
                if (keyboard.digit1Key.wasPressedThisFrame) experiment.settings.synchrony = Synchrony.Synchronous;
                if (keyboard.digit2Key.wasPressedThisFrame) experiment.settings.synchrony = Synchrony.Asynchronous;
                if (keyboard.digit3Key.wasPressedThisFrame) ChangeAppearance();
                if (keyboard.digit4Key.wasPressedThisFrame) ChangeVibration();
            }
        }
        void ChangeAppearance()
        {
            for(int i=0;i<3;i++)
            {
                experiment.settings.appearance = (MeltAppearance)(((int)experiment.settings.appearance + 1) % 3);
                if(experiment.hand.HasAppearance(experiment.settings.appearance))break;
            }
            experiment.hand.Select(experiment.settings.appearance);
        }
        void ChangeVibration()
        {
            experiment.settings.vibration=(VibrationCondition)(((int)experiment.settings.vibration+1)%4);
        }
        void OnGUI()
        {
            if (!experiment || (experiment.study&&experiment.study.Active)) return;
            var previous = GUI.matrix;
            float scale = Mathf.Clamp(Screen.height / 900f, 1, 1.6f); GUI.matrix = Matrix4x4.Scale(Vector3.one * scale);
            GUILayout.BeginArea(new Rect(18, 18, 410, 590), GUI.skin.box);
            GUILayout.Label("TABLETOP MELTING - PC PREVIEW");
            GUILayout.Label("Move mouse: move brush   Hold left button: touch");
            GUILayout.Label("Space: start    R: reset    Esc: abort");
            GUILayout.Label("1: sync    2: async    3: change melt shader");
            GUILayout.Label("4: vibration condition");
            GUILayout.Space(8);
            GUILayout.Label(experiment.Phase + "   " + experiment.Elapsed.ToString("F1") + " s");
            GUILayout.Label(experiment.Status);
            GUILayout.Label(experiment.settings.synchrony + "   " + experiment.settings.appearance);
            GUI.enabled = !experiment.Running&&(experiment.settings.vibration==VibrationCondition.None||(experiment.vibration&&experiment.vibration.Connected));
            if (GUILayout.Button("Start trial [Space]")) experiment.StartTrial();
            GUI.enabled=!experiment.Running;
            if (GUILayout.Button("Switch sync / async")) experiment.settings.synchrony = experiment.settings.synchrony == Synchrony.Synchronous ? Synchrony.Asynchronous : Synchrony.Synchronous;
            if (GUILayout.Button("Change melt shader [3]")) ChangeAppearance();
            GUILayout.Label("Vibration: "+experiment.settings.vibration);
            if(GUILayout.Button("Change vibration condition [4]"))ChangeVibration();
            if(experiment.settings.vibration==VibrationCondition.MeltDelayed)
            {
                GUILayout.Label("Vibration delay: "+experiment.settings.vibrationDelay.ToString("F2")+" s");
                experiment.settings.vibrationDelay=GUILayout.HorizontalSlider(experiment.settings.vibrationDelay,0,Mathf.Min(5,experiment.settings.observation));
            }
            GUILayout.Label("Strength: "+experiment.settings.vibrationAmplitude.ToString("F2"));
            experiment.settings.vibrationAmplitude=GUILayout.HorizontalSlider(experiment.settings.vibrationAmplitude,0,1);
            if(experiment.vibration)
            {
                if(GUILayout.Button(experiment.vibration.Connected?"ESP32 connected (muted until trial)":"Connect ESP32: "+experiment.vibration.portName+" / CH"+experiment.vibration.channelList))experiment.vibration.Connect();
                GUILayout.Label(experiment.vibration.Connected?"ESP32: CONNECTED":experiment.vibration.Connecting?"ESP32: CONNECTING":"ESP32: NOT CONNECTED - connect before vibration trials");
                GUILayout.Label(experiment.vibration.Status);
            }
            GUI.enabled = true;
            if(experiment.vibration)
            {
                GUILayout.Label(experiment.vibration.Active?"OUTPUT: VIBRATING":"OUTPUT: OFF");
                if(GUILayout.Button("Stop vibration + abort trial")){experiment.AbortTrial();experiment.vibration.Stop("operator_stop");}
            }
            if (GUILayout.Button("Reset [R]")) experiment.ResetTrial();
            GUILayout.Label("Mouse brush preview - physical brush contact is not measured.");
            GUILayout.EndArea(); GUI.matrix = previous;
        }
        void OnDestroy() { if (handSurface) Destroy(handSurface); }
    }
}
