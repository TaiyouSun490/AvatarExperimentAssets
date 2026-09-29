using System;
using UnityEngine;
using UnityEngine.XR.Hands;

namespace AvatarExperiments.FpsArms
{
    /// <summary>Use the opposite index fingertip or controller as an elbow probe.</summary>
    [DefaultExecutionOrder(300)]
    public sealed class ArmLengthCalibration : MonoBehaviour
    {
        [Tooltip("Unique ID for this participant/avatar setup. Shared by left and right arms.")]
        public string calibrationProfile="default";
        string PreferenceKey=>"AvatarExperiments.FpsArms."+calibrationProfile+".v1";
        public TrackedHandsAvatarRig rig;
        [Tooltip("Probe point relative to the controller grip pose, in metres. Zero uses its tracked grip centre.")]
        public Vector3 elbowProbeOffset;
        public const string IdleInstructions="Hands: hold both pinches for 1 second. Controllers: hold both grips.";
        [SerializeField] string status=IdleInstructions;
        public string Status=>status;
        public bool IsCalibrating=>step!=Step.Idle;
        enum Step { Idle, LeftElbow }
        Step step;
        float gripTime,triggerTime,stepStarted,messageUntil;
        bool gripLatched,triggerReleased;
        bool useHands;
        float pinchTime;
        bool pinchLatched;
        readonly ElbowCalibrationHold handHold=new ElbowCalibrationHold();
        Vector3 sampleElbow,sampleWrist,sampleHead;
        TextMesh message;
        [Serializable] public class Fit { public bool valid; public float upper,forearm; public Vector3 hint,reachAxis; }
        [Serializable] public class SavedFit { public Fit left=new Fit(),right=new Fit(); }
        SavedFit saved=new SavedFit();
        Vector3 originalLeftHint,originalRightHint;
        Vector3 originalLeftAxis,originalRightAxis;

        void Start()
        {
            if(!rig)rig=GetComponent<TrackedHandsAvatarRig>();
            if(!rig){enabled=false;return;}
            status=IdleInstructions;
            originalLeftHint=rig.leftArm.elbowHint;originalRightHint=rig.rightArm.elbowHint;
            originalLeftAxis=rig.leftArm.poleReferenceAxis;originalRightAxis=rig.rightArm.poleReferenceAxis;
            // v1 reach-only measurements are intentionally not loaded.
            if(!PlayerPrefs.HasKey(PreferenceKey))return;
            try
            {
                var read=JsonUtility.FromJson<SavedFit>(PlayerPrefs.GetString(PreferenceKey));
                if(read==null || read.left==null || !read.left.valid)return;
                // Left is the shared size reference, including previously saved left fits.
                saved=MirrorLeftFit(read.left);Load(rig.leftArm,saved.left);Load(rig.rightArm,saved.right);
            }
            catch(ArgumentException){status="Saved elbow fit was invalid. Fit again.";}
        }
        static void Load(TrackedArmAvatar arm,Fit fit)
        {
            if(!fit.valid || !arm.SetSegmentLengths(fit.upper,fit.forearm))return;
            // Old fits saved a pole vector without the reach frame. Keep their lengths,
            // but use the default pole until the elbow is sampled again.
            if(fit.reachAxis.sqrMagnitude>.5f && fit.hint.sqrMagnitude>.5f)
                arm.SetPoleReference(fit.reachAxis,fit.hint);
        }
        void Update()
        {
            if(!rig)return;
            bool lc=rig.TryGetController(true,out _,out _,out float lg);
            bool rc=rig.TryGetController(false,out _,out _,out float rg);
            bool both=lc&&rc&&lg>.8f&&rg>.8f;
            if(!both){gripTime=0;gripLatched=false;}
            else if(!gripLatched&&!IsCalibrating)
            {
                gripTime+=Time.unscaledDeltaTime;
                if(gripTime>=1){gripLatched=true;BeginControllerCalibration();}
            }
            bool pinches=rig.inputMode!=TrackedHandsAvatarRig.InputMode.ControllersOnly&&rig.LeftPinching&&rig.RightPinching;
            if(!pinches){pinchTime=0;pinchLatched=false;}
            else if(!pinchLatched&&!IsCalibrating)
            {pinchTime+=Time.unscaledDeltaTime;if(pinchTime>=1){pinchLatched=true;BeginHandCalibration();}}
#if ENABLE_LEGACY_INPUT_MANAGER
            if(Input.GetKeyDown(KeyCode.C))BeginCalibration();
#endif
            if(IsCalibrating)MeasureElbow();
            if(message)message.gameObject.SetActive(IsCalibrating||Time.unscaledTime<messageUntil);
        }
        [ContextMenu("Start Elbow Calibration (Play Mode)")]
        public void BeginCalibration()
        {
            if(rig&&rig.inputMode!=TrackedHandsAvatarRig.InputMode.HandsOnly&&rig.TryGetController(false,out _,out _,out _))BeginControllerCalibration();
            else BeginHandCalibration();
        }
        public void BeginHandCalibration(){Begin(true);}
        public void BeginControllerCalibration(){Begin(false);}
        void Begin(bool hands)
        {
            if(!Application.isPlaying||!rig)return;
            useHands=hands;step=Step.LeftElbow;stepStarted=Time.unscaledTime;triggerTime=0;triggerReleased=false;handHold.Reset();
        }
        void MeasureElbow()
        {
            if(Time.unscaledTime-stepStarted>60){Finish("Fitting incomplete. No changes saved.\nPrevious calibration kept. Try again.");return;}
            if(useHands){MeasureHandElbow();return;}
            var arm=rig.leftArm;
            const string instruction="LEFT elbow: touch it with RIGHT controller.\nKeep LEFT wrist tracked; hold RIGHT trigger.";
            if(!rig.HeadTracked || !arm.tracked || !rig.TryGetController(false,out var probe,out float trigger,out _))
            {triggerTime=0;triggerReleased=false;Show(instruction+"\nWaiting for wrist and controller tracking...");return;}
            if(trigger<.2f){triggerReleased=true;triggerTime=0;}
            Vector3 elbow=probe.position+probe.rotation*elbowProbeOffset;
            Vector3 wrist=arm.wristTarget.position;
            if(!triggerReleased || trigger<.8f){triggerTime=0;Show(instruction+"\nUse the grip centre as the elbow point.");return;}
            if(triggerTime==0 || Vector3.Distance(elbow,sampleElbow)>.02f || Vector3.Distance(wrist,sampleWrist)>.02f ||
                Vector3.Distance(rig.head.transform.position,sampleHead)>.02f)
            {triggerTime=0;sampleElbow=elbow;sampleWrist=wrist;sampleHead=rig.head.transform.position;}
            triggerTime+=Time.unscaledDeltaTime;
            Show(instruction+$"\nHold still: {Mathf.Clamp01(triggerTime/.5f):P0}");
            if(triggerTime<.5f)return;
            if(!TryCreateFit(arm,elbow,wrist,out var fit))
            {triggerReleased=false;triggerTime=0;Show("Fit rejected: check elbow and wrist points.\nRelease the trigger and try again.");return;}
            SaveLeftForBoth(fit);
        }
        void MeasureHandElbow()
        {
            var arm=rig.leftArm;
            const string instruction="Touch LEFT elbow with RIGHT index fingertip.";
            float preparation=3-(Time.unscaledTime-stepStarted);
            if(preparation>0||rig.LeftPinching||rig.RightPinching)
            {handHold.Reset();Show($"Release pinches. Prepare: {Mathf.CeilToInt(Mathf.Max(0,preparation))}\n{instruction}\nBend your elbow; keep both hands visible.");return;}
            // Read only measured wrist/fingertip joints; do not use a frozen avatar pose.
            if(!rig.TryGetTrackedJoint(true,XRHandJointID.Wrist,out var wrist)||
                !rig.TryGetTrackedJoint(false,XRHandJointID.IndexTip,out var elbow))
            {handHold.Reset();Show(instruction+"\nWaiting for wrist and fingertip tracking.\nKeep both hands in view.");return;}
            if(!TryMeasureElbow(arm,elbow,wrist,out _,out _,out _,out _))
            {handHold.Reset();Show(instruction+"\nBend elbow slightly and adjust your touch point.");return;}
            bool complete=handHold.Tick(true,elbow,wrist,rig.head.transform.position,rig.head.transform.forward,Time.unscaledDeltaTime,1.5f);
            Show(instruction+$"\nHold fingertip and wrist still: {handHold.Progress:P0}");
            if(!complete)return;
            if(!TryCreateFit(arm,handHold.Elbow,handHold.Wrist,out var fit)){handHold.Reset();return;}
            SaveLeftForBoth(fit);
        }
        public static bool TryCreateFit(TrackedArmAvatar arm,Vector3 elbow,Vector3 wrist,out Fit fit)
        {
            fit=null;
            if(!TryMeasureElbow(arm,elbow,wrist,out var upper,out var forearm,out var hint,out var axis))return false;
            fit=new Fit{valid=true,upper=upper,forearm=forearm,hint=hint,reachAxis=axis};return true;
        }
        public static SavedFit MirrorLeftFit(Fit left)
        {
            if(left==null)return new SavedFit();
            return new SavedFit
            {
                left=new Fit{valid=left.valid,upper=left.upper,forearm=left.forearm,hint=left.hint,reachAxis=left.reachAxis},
                right=new Fit{valid=left.valid,upper=left.upper,forearm=left.forearm,
                    hint=new Vector3(-left.hint.x,left.hint.y,left.hint.z),
                    reachAxis=new Vector3(-left.reachAxis.x,left.reachAxis.y,left.reachAxis.z)}
            };
        }
        void SaveLeftForBoth(Fit fit)
        {
            var pair=MirrorLeftFit(fit);
            if(!ApplyPair(rig,pair)){Finish("Fit rejected. Previous calibration kept.");return;}
            saved=pair;
            PlayerPrefs.SetString(PreferenceKey,JsonUtility.ToJson(saved));PlayerPrefs.Save();
            Debug.Log($"HAND_ELBOW_CALIBRATION_SHARED_SAVED source={(useHands?"hands":"controllers")} upper={fit.upper:F4} forearm={fit.forearm:F4}");
            handHold.Reset();
            Finish($"LEFT FIT APPLIED TO BOTH ARMS\nUpper arm: {fit.upper*100:F1} cm / Forearm: {fit.forearm*100:F1} cm\nHands and fingers scaled with forearms.");
        }
        public static bool ApplyPair(TrackedHandsAvatarRig rig,SavedFit pair)
        {
            if(!rig||pair==null||!ValidFit(rig.leftArm,pair.left)||!ValidFit(rig.rightArm,pair.right))return false;
            Load(rig.leftArm,pair.left);Load(rig.rightArm,pair.right);
            return true;
        }
        static bool ValidFit(TrackedArmAvatar arm,Fit fit)
        {
            if(fit==null||!fit.valid||!arm.CanSetSegmentLengths(fit.upper,fit.forearm))return false;
            float cross=Vector3.Cross(fit.hint,fit.reachAxis).sqrMagnitude;
            return cross>.01f&&!float.IsInfinity(cross)&&!float.IsNaN(cross);
        }
        public static bool TryFitElbow(TrackedArmAvatar arm,Vector3 elbowPoint,Vector3 wristPoint,out Vector3 hint)
        {
            if(!TryMeasureElbow(arm,elbowPoint,wristPoint,out float upper,out float forearm,out hint,out var axis))return false;
            arm.SetSegmentLengths(upper,forearm);arm.SetPoleReference(axis,hint);return true;
        }
        public static bool TryMeasureElbow(TrackedArmAvatar arm,Vector3 elbowPoint,Vector3 wristPoint,out float upper,out float forearm,out Vector3 hint,out Vector3 axis)
        {
            upper=forearm=0;hint=axis=default;
            var forward=Vector3.ProjectOnPlane(arm.head.forward,Vector3.up);
            if(forward.sqrMagnitude<.01f)return false;
            var yaw=Quaternion.LookRotation(forward,Vector3.up);
            var shoulder=arm.head.position+yaw*arm.shoulderOffset;
            upper=Vector3.Distance(shoulder,elbowPoint);forearm=Vector3.Distance(elbowPoint,wristPoint);
            var bend=Vector3.ProjectOnPlane(elbowPoint-shoulder,wristPoint-shoulder);
            if(bend.sqrMagnitude<.0004f || !arm.CanSetSegmentLengths(upper,forearm))return false;
            hint=Quaternion.Inverse(yaw)*bend.normalized;
            axis=Quaternion.Inverse(yaw)*(wristPoint-shoulder).normalized;
            hint=StableElbowPole.CorrectReferenceBend(axis,hint,arm.leftHand);return true;
        }
        [ContextMenu("Reset Saved Elbow Fit")]
        public void ResetCalibration()
        {
            PlayerPrefs.DeleteKey(PreferenceKey);PlayerPrefs.Save();saved=new SavedFit();
            if(rig)
            {
                rig.leftArm.ResetArmLength();rig.rightArm.ResetArmLength();
                if(originalLeftHint.sqrMagnitude>0)rig.leftArm.SetPoleReference(originalLeftAxis,originalLeftHint);
                if(originalRightHint.sqrMagnitude>0)rig.rightArm.SetPoleReference(originalRightAxis,originalRightHint);
            }
            Finish("Elbow fitting reset to model defaults.");
        }
        public void CancelCalibration(){Finish("Fitting cancelled. No changes saved.\nPrevious calibration kept.");}
        void Finish(string text){step=Step.Idle;handHold.Reset();gripLatched=pinchLatched=true;messageUntil=Time.unscaledTime+5;status=text;if(Application.isPlaying)Show(text);}
        void Show(string text)
        {
            if(IsCalibrating)text="MEASURE LEFT - APPLY TO BOTH\n"+text;
            status=text;
            if(!message)
            {
                var go=new GameObject("Elbow Calibration Instructions");go.transform.SetParent(rig.head.transform,false);
                go.transform.localPosition=new Vector3(0,.08f,.65f);
                message=go.AddComponent<TextMesh>();message.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                message.GetComponent<MeshRenderer>().sharedMaterial=message.font.material;
                message.fontSize=36;message.characterSize=.008f;message.anchor=TextAnchor.MiddleCenter;message.alignment=TextAlignment.Center;
            }
            message.text=text;
        }
        void OnDisable(){step=Step.Idle;if(message)message.gameObject.SetActive(false);}
        void OnDestroy(){if(message)Destroy(message.gameObject);}
    }
}
