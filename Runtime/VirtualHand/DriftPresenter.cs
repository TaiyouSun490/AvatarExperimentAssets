using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

namespace AvatarExperiments.VirtualHand
{
    /// <summary>Black measurement field. Never shows the real reference, response endpoint, previous answer or correctness.</summary>
    public sealed class DriftPresenter : MonoBehaviour
    {
        public Material probeMaterial;
        public Material textMaterial;
        public Camera participantCamera;
        Material runtimeTextMaterial;
        Font font;
        VirtualHandStudy study;
        GameObject visuals,probe;
        TextMesh instruction;
        readonly List<Renderer> hidden=new List<Renderer>();
        readonly List<Canvas> hiddenCanvases=new List<Canvas>();
        Color oldBackground;
        CameraClearFlags oldClear;
        bool blank,held,axisHeld,ratings;
        int question,rating=4;
        int[] answers;
        double presentedAt,stableSince;
        Vector3 stablePoint;
        public bool Active=>blank;
        public void Begin(VirtualHandStudy owner)
        {
            study=owner;Blackout();ratings=false;Refresh();
        }
        void Blackout()
        {
            if(blank)return;
            if(!participantCamera)participantCamera=Camera.main;
            if(!participantCamera)throw new InvalidOperationException("Participant camera missing.");
            foreach(var renderer in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                if(renderer.enabled){hidden.Add(renderer);renderer.enabled=false;}
            foreach(var canvas in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                if(canvas.enabled){hiddenCanvases.Add(canvas);canvas.enabled=false;}
            oldBackground=participantCamera.backgroundColor;oldClear=participantCamera.clearFlags;
            participantCamera.clearFlags=CameraClearFlags.SolidColor;participantCamera.backgroundColor=Color.black;blank=true;
            visuals=new GameObject("Measurement visuals (runtime)");
            font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            runtimeTextMaterial=textMaterial?new Material(textMaterial):new Material(Shader.Find("AvatarExperiments/VirtualHand/MeasurementText"));
            runtimeTextMaterial.mainTexture=font.material.mainTexture;
            instruction=Text("Measurement instruction",visuals.transform,"",.0035f);
            instruction.transform.SetParent(participantCamera.transform,false);instruction.transform.localPosition=new Vector3(0,.13f,.65f);instruction.transform.localRotation=Quaternion.identity;
            instruction.transform.SetParent(visuals.transform,true);
        }
        public void Refresh()
        {
            if(!blank||!study||study.Measurement==null)return;
            foreach(Transform child in visuals.transform)if(child!=instruction.transform)Destroy(child.gameObject);
            probe=null;var measurement=study.Measurement;var settings=measurement.Settings;
            var space=study.experiment.trackedBrush.trackingSpace;
            if(settings.method==DriftMethod.MarkerAdjustment||settings.method==DriftMethod.AlternatingStaircase)
            {
                probe=GameObject.CreatePrimitive(PrimitiveType.Sphere);probe.name="Position judgement probe";probe.transform.SetParent(visuals.transform);
                probe.transform.localScale=Vector3.one*.004f;Destroy(probe.GetComponent<Collider>());probe.GetComponent<Renderer>().sharedMaterial=probeMaterial;
                probe.transform.position=space.TransformPoint(measurement.ProbePosition);
            }
            if(settings.method==DriftMethod.RandomizedRuler)
            {
                int count=Mathf.FloorToInt(settings.halfRange*100);
                for(int i=-count;i<=count;i++)
                {
                    Vector3 position=space.TransformPoint(measurement.Reference+measurement.Axis*(measurement.RulerShift+i*.01f));
                    var tick=GameObject.CreatePrimitive(PrimitiveType.Cube);tick.transform.SetParent(visuals.transform);tick.transform.position=position;
                    tick.transform.rotation=participantCamera.transform.rotation;tick.transform.localScale=new Vector3(.0007f,i%5==0?.012f:.006f,.0007f);
                    Destroy(tick.GetComponent<Collider>());tick.GetComponent<Renderer>().sharedMaterial=probeMaterial;
                    if(i%5==0)
                    {var label=Text("Ruler number",visuals.transform,(measurement.RulerZero+i).ToString(),.003f);label.transform.position=position+participantCamera.transform.up*.021f;label.transform.rotation=participantCamera.transform.rotation;}
                }
            }
            string controls=settings.method==DriftMethod.InvisibleControllerPointing?"Point with the LEFT controller (invisible).\nHold still, then press its trigger.":
                settings.method==DriftMethod.MarkerAdjustment?"Move the dot with LEFT stick / arrow keys.\nConfirm with LEFT trigger / Enter.":
                settings.method==DriftMethod.AlternatingStaircase?"Is the dot LEFT or RIGHT of your felt finger?\nLEFT X = left, LEFT Y = right / arrow keys.":
                "Say the number above your felt finger.\nThe experimenter records your answer.";
            instruction.text="Where do you feel your REAL "+settings.targetLabel+"?\n"+controls;
            presentedAt=Time.realtimeSinceStartupAsDouble;stableSince=presentedAt;held=true;axisHeld=true;
        }
        public void HoldBlank()
        {
            Blackout();ratings=false;
            foreach(Transform child in visuals.transform)if(child!=instruction.transform)Destroy(child.gameObject);
            probe=null;instruction.text="Please keep your real hand still.\nWait for the experimenter.";
        }
        public void BeginRatings()
        {
            HoldBlank();ratings=true;question=0;rating=4;answers=new int[VirtualHandStudy.Questions.Length];ShowQuestion();
        }
        void ShowQuestion()
        {
            var prompts=VirtualHandStudy.ParticipantQuestions;
            instruction.text=prompts[question]+"\n1: completely disagree    7: completely agree\n"+rating+"\nLEFT stick / arrows: choose; trigger / Enter: confirm";
            held=true;
        }
        void Update()
        {
            if(!blank||!study)return;
            instruction.transform.SetPositionAndRotation(participantCamera.transform.TransformPoint(new Vector3(0,.13f,.65f)),participantCamera.transform.rotation);
            if(study.Stage!=StudyStage.Baseline&&study.Stage!=StudyStage.PostMeasurement&&study.Stage!=StudyStage.Ratings)return;
            var keyboard=Keyboard.current;var device=InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            bool controller=device.isValid&&(device.characteristics&InputDeviceCharacteristics.Controller)!=0;
            bool tracked=controller&&device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.isTracked,out bool isTracked)&&isTracked&&
                device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.trackingState,out var tracking)&&
                (tracking&(InputTrackingState.Position|InputTrackingState.Rotation))==(InputTrackingState.Position|InputTrackingState.Rotation);
            device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.triggerButton,out bool trigger);
            device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primary2DAxis,out var stick);
            bool confirm=(tracked&&trigger)||(keyboard!=null&&keyboard.enterKey.isPressed);
            bool pressed=confirm&&!held;held=confirm;
            float direction=tracked?stick.x:0;
            if(keyboard!=null){if(keyboard.leftArrowKey.isPressed)direction=-1;if(keyboard.rightArrowKey.isPressed)direction=1;}
            bool move=Mathf.Abs(direction)>.5f;
            if(ratings)
            {
                if(move&&!axisHeld){rating=Mathf.Clamp(rating+(direction>0?1:-1),1,7);ShowQuestion();}
                axisHeld=move;
                if(pressed){answers[question++]=rating;if(question==answers.Length){ratings=false;study.SubmitRatings(answers);}else{rating=4;ShowQuestion();}}
                return;
            }
            var measurement=study.Measurement;if(measurement==null)return;
            if(Time.realtimeSinceStartupAsDouble-presentedAt<.3)return;
            if(measurement.Settings.method==DriftMethod.MarkerAdjustment)
            {
                if(move)measurement.Adjust(direction*.06f*Time.unscaledDeltaTime);
                if(probe)probe.transform.position=study.experiment.trackedBrush.trackingSpace.TransformPoint(measurement.ProbePosition);
                if(pressed)study.SubmitMarker();
            }
            else if(measurement.Settings.method==DriftMethod.AlternatingStaircase)
            {
                device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primaryButton,out bool x);device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.secondaryButton,out bool y);
                bool left=(tracked&&x)||(keyboard!=null&&keyboard.leftArrowKey.isPressed),right=(tracked&&y)||(keyboard!=null&&keyboard.rightArrowKey.isPressed);
                bool choice=left||right;if(choice&&!axisHeld&&left!=right)study.SubmitJudgement(right);axisHeld=choice;
            }
            else if(measurement.Settings.method==DriftMethod.InvisibleControllerPointing)
            {
                if(!tracked||!device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.devicePosition,out var position)||!device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.deviceRotation,out var rotation))
                {stableSince=Time.realtimeSinceStartupAsDouble;return;}
                var point=position+rotation*measurement.Settings.pointingTipOffset;
                if(Vector3.Distance(point,stablePoint)>.01f){stablePoint=point;stableSince=Time.realtimeSinceStartupAsDouble;}
                if(pressed&&Time.realtimeSinceStartupAsDouble-stableSince>=.3)study.SubmitPoint(point);
            }
        }
        void LateUpdate()
        {
            // Dynamic fonts can replace their atlas. Use our own world-space shader;
            // the Editor/IMGUI font material may carry a clip rectangle unrelated to VR.
            if(runtimeTextMaterial&&font)runtimeTextMaterial.mainTexture=font.material.mainTexture;
        }
        TextMesh Text(string name,Transform parent,string text,float size)
        {
            var go=new GameObject(name);go.transform.SetParent(parent,false);var mesh=go.AddComponent<TextMesh>();
            mesh.font=font;mesh.GetComponent<MeshRenderer>().sharedMaterial=runtimeTextMaterial;
            mesh.fontSize=40;mesh.characterSize=size;mesh.anchor=TextAnchor.MiddleCenter;mesh.alignment=TextAlignment.Center;mesh.color=Color.white;mesh.text=text;return mesh;
        }
        public void End()
        {
            if(!blank)return;
            foreach(var renderer in hidden)if(renderer)renderer.enabled=true;hidden.Clear();
            foreach(var canvas in hiddenCanvases)if(canvas)canvas.enabled=true;hiddenCanvases.Clear();
            if(visuals)Destroy(visuals);probe=null;instruction=null;
            if(runtimeTextMaterial)Destroy(runtimeTextMaterial);runtimeTextMaterial=null;
            if(participantCamera){participantCamera.backgroundColor=oldBackground;participantCamera.clearFlags=oldClear;}
            blank=ratings=false;
        }
        void OnDisable(){End();}
    }
}
