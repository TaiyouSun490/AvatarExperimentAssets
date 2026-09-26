using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;
using UnityEngine.XR.Hands;

namespace AvatarExperiments
{
    /// <summary>World-space calibration UI: tracked controller aim rays + same-hand triggers.</summary>
    [DefaultExecutionOrder(-100)]
    public sealed class VrCalibrationMenu : MonoBehaviour
    {
        private sealed class Control
        {
            public string Id;
            public RectTransform Rect;
            public Image Image;
            public Action Action;
        }
        public bool IsOpen { get; private set; }
        public Canvas WorldCanvas { get; private set; }
        public string HoveredControl => hovered != null ? hovered.Id : "";
        public string PointerStatus { get; private set; } = "Controller not tracked";
        public bool DesktopPreview;
        private AvatarCalibration calibration;
        private AvatarTrackingInput input;
        private XRHandHumanoidFingerDriver fingers;
        private XrSession session;
        private Camera view;
        private Transform trackingOrigin;
        private RectTransform panel, cursor;
        private GameObject bodyPage, wristPage, fingerPage, limbPage;
        private Text status, wristValues, fingerStatus, limbStatus;
        private readonly Text[] limbValues = new Text[4];
        private readonly List<Control> controls = new();
        private readonly List<XRHandSubsystem> hands = new();
        private Font font;
        private Material uiMaterial;
        private Control hovered;
        private bool shownForSession, lastMenu, leftPinch, rightPinch, bothLatched, activeLeft;
        private bool leftTriggerArmed, rightTriggerArmed;
        private LineRenderer leftLaser, rightLaser;
        private float bothSince = -1, nextText, consumeUntil;
        private static readonly InputFeatureUsage<bool> PointerTracked = new("PointerIsTracked");
        private static readonly InputFeatureUsage<uint> PointerTracking = new("PointerTrackingState");
        private static readonly InputFeatureUsage<Vector3> PointerPosition = new("PointerPosition");
        private static readonly InputFeatureUsage<Quaternion> PointerRotation = new("PointerRotation");
        private static readonly Color Normal = new(.08f,.18f,.23f,1);
        private static readonly Color Hover = new(.03f,.5f,.62f,1);

        public void Configure(AvatarRig rig, Camera camera)
        {
            view = camera;
            trackingOrigin = rig.TrackingOrigin;
            calibration = GetComponent<AvatarCalibration>();
            input = GetComponent<AvatarTrackingInput>(); session = GetComponent<XrSession>();
            fingers = GetComponent<XRHandHumanoidFingerDriver>();
            Build();
        }
        private void Build()
        {
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var shader = Resources.Load<Shader>("AvatarExperiments/Shaders/WorldSpaceUI");
            uiMaterial = new Material(shader) { name = "VR calibration UI", hideFlags = HideFlags.DontSave };
            var go = new GameObject("VR Avatar Calibration", typeof(RectTransform), typeof(Canvas));
            go.layer = 5; go.transform.SetParent(transform, false);
            panel = (RectTransform)go.transform; panel.sizeDelta = new Vector2(720,720);
            panel.localScale = Vector3.one * .0012f;
            WorldCanvas = go.GetComponent<Canvas>(); WorldCanvas.renderMode = RenderMode.WorldSpace;
            WorldCanvas.worldCamera = view; WorldCanvas.sortingOrder = 10;
            var background = go.AddComponent<Image>(); background.material=uiMaterial; background.color = new Color(.025f,.045f,.065f,.97f); background.raycastTarget = false;
            Label(panel, "Avatar calibration", 0, -36, 680, 48, 30);
            Label(panel, "AIM with either CONTROLLER + TRIGGER to select", 0, -78, 680, 35, 21);
            AddButton(panel,"body","Body",-280,-127,134,48,()=>ShowPage(false));
            AddButton(panel,"wrists","Wrists",-140,-127,134,48,()=>ShowPage(true));
            AddButton(panel,"fingers","Fingers",0,-127,134,48,ShowFingers);
            AddButton(panel,"limbs","Lengths",140,-127,134,48,ShowLimbs);
            AddButton(panel,"close","Close",280,-127,134,48,()=>SetOpen(false));
            bodyPage = Page("Body"); wristPage = Page("Wrist angles"); fingerPage = Page("Finger calibration");
            limbPage = Page("Arm and leg lengths");
            Label(limbPage.transform,"Fit standing height first, then adjust each limb.\nArms: shoulder to wrist. Legs: hip to ankle.\nManual fit: no leg measurement from head/hands alone.",0,-223,675,110,21);
            for (int i=0;i<4;i++)
            {
                int index=i; float y=-317-i*57;
                limbValues[i]=Label(limbPage.transform,"",-115,y,415,48,22);
                AddButton(limbPage.transform,$"limb{i}minus","-1 cm",140,y,100,46,()=>calibration.AdjustLimb(index,-.01f));
                AddButton(limbPage.transform,$"limb{i}plus","+1 cm",257,y,100,46,()=>calibration.AdjustLimb(index,.01f));
            }
            AddButton(limbPage.transform,"resetLengths","Reset lengths",-170,-557,320,52,()=>calibration.ApplyLimbLengths(1,1,1,1));
            AddButton(limbPage.transform,"saveLengths","Save on this PC",170,-557,320,52,calibration.Save);
            AddButton(limbPage.transform,"previousLengths","Restore previous save",0,-615,660,46,calibration.RestorePrevious);
            limbStatus=Label(limbPage.transform,"",0,-674,675,65,19);
            AddButton(bodyPage.transform,"fit","Fit standing height (3 sec)",0,-203,660,54,calibration.BeginHeightCalibration);
            AddButton(bodyPage.transform,"smaller","Size -1%",-170,-268,320,52,()=>calibration.AdjustScale(-.01f));
            AddButton(bodyPage.transform,"larger","Size +1%",170,-268,320,52,()=>calibration.AdjustScale(.01f));
            AddButton(bodyPage.transform,"save","Save on this PC",-170,-533,320,52,calibration.Save);
            AddButton(bodyPage.transform,"reset","Reset calibration",170,-533,320,52,calibration.ResetSettings);
            Label(bodyPage.transform,"1. Fit standing height in a floor-based XR origin.\n2. Lengths: adjust arms and legs.\n3. Wrists / Fingers: align and capture open hands.\n4. Save on this PC.",0,-395,675,170,23);
            status = Label(bodyPage.transform,"",0,-629,676,118,21);

            Label(wristPage.transform,"Wrist-local angles; left and right independently",0,-184,675,35,21);
            string[] axes = { "X / Pitch", "Y / Yaw", "Z / Roll" };
            for(int side=0;side<2;side++) for(int axis=0;axis<3;axis++)
            {
                bool left=side==0; int capturedAxis=axis;
                float y=-232-(side*3+axis)*53;
                Label(wristPage.transform,(left?"Left  ":"Right  ")+axes[axis],-145,y,355,46,23);
                AddButton(wristPage.transform,$"{side}-{axis}-minus","-5",100,y,95,44,()=>calibration.AdjustWrist(left,capturedAxis,-5));
                AddButton(wristPage.transform,$"{side}-{axis}-plus","+5",220,y,95,44,()=>calibration.AdjustWrist(left,capturedAxis,5));
            }
            wristValues = Label(wristPage.transform,"",0,-555,676,53,22);
            AddButton(wristPage.transform,"resetWrists","Reset wrists",-170,-615,320,52,calibration.ResetWrists);
            AddButton(wristPage.transform,"saveWrists","Save on this PC",170,-615,320,52,calibration.Save);
            Label(wristPage.transform,"Close / reopen: right A / B, Menu / F8, or both pinches",0,-673,690,32,18);
            Label(fingerPage.transform,"1. Press Calibrate, then put controllers down.\n2. Show BOTH hands, fingers straight, thumbs open.\n3. Hold the open pose steady for 1 second.",0,-244,675,144,24);
            AddButton(fingerPage.transform,"calibrateFingers","Calibrate open hands (3 sec)",0,-361,660,58,()=>
            {if(calibration.Busy)calibration.Cancel();fingers?.BeginOpenHandCalibration();});
            AddButton(fingerPage.transform,"cancelFingers","Cancel",-170,-434,320,54,()=>fingers?.CancelCalibration());
            AddButton(fingerPage.transform,"resetFingers","Reset fingers only",170,-434,320,54,()=>fingers?.ResetCalibration());
            Label(fingerPage.transform,"Waits up to 25 seconds for hand tracking.\nSaved automatically. Wrist angles stay unchanged.\nTracking loss keeps the reference; no automatic rebasing.",0,-529,675,113,21);
            fingerStatus=Label(fingerPage.transform,"",0,-647,675,105,22);
            var dot = new GameObject("Controller ray cursor",typeof(RectTransform),typeof(Image)); dot.layer=5;
            dot.transform.SetParent(panel,false); cursor=(RectTransform)dot.transform; cursor.sizeDelta=new Vector2(12,12);
            var image=dot.GetComponent<Image>();image.material=uiMaterial;image.color=new Color(.2f,1,1,1);image.raycastTarget=false;
            leftLaser=CreateLaser("Left controller UI laser");rightLaser=CreateLaser("Right controller UI laser");
            ShowPage(false); go.SetActive(false);
        }
        private LineRenderer CreateLaser(string name)
        {
            var go=new GameObject(name);go.layer=5;go.transform.SetParent(transform,false);
            var line=go.AddComponent<LineRenderer>();line.sharedMaterial=uiMaterial;
            line.useWorldSpace=true;line.positionCount=2;line.startWidth=.002f;line.endWidth=.0012f;
            line.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;line.receiveShadows=false;
            line.enabled=false;return line;
        }
        private GameObject Page(string name)
        {
            var go=new GameObject(name,typeof(RectTransform));go.layer=5;go.transform.SetParent(panel,false);
            var rect=(RectTransform)go.transform;rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.sizeDelta=Vector2.zero;
            return go;
        }
        private static RectTransform Rect(Transform parent,string name,float x,float y,float width,float height)
        {
            var go=new GameObject(name,typeof(RectTransform));go.layer=5;go.transform.SetParent(parent,false);
            var rect=(RectTransform)go.transform;rect.anchorMin=rect.anchorMax=new Vector2(.5f,1);
            rect.anchoredPosition=new Vector2(x,y);rect.sizeDelta=new Vector2(width,height);return rect;
        }
        private Text Label(Transform parent,string text,float x,float y,float width,float height,int size)
        {
            var rect=Rect(parent,text,x,y,width,height);var label=rect.gameObject.AddComponent<Text>();
            label.font=font;label.material=uiMaterial;label.text=text;label.fontSize=size;label.alignment=TextAnchor.MiddleCenter;
            label.color=Color.white;label.raycastTarget=false;label.horizontalOverflow=HorizontalWrapMode.Wrap;
            return label;
        }
        private void AddButton(Transform parent,string id,string text,float x,float y,float width,float height,Action action)
        {
            var rect=Rect(parent,id,x,y,width,height);var image=rect.gameObject.AddComponent<Image>();
            image.material=uiMaterial;image.color=Normal;image.raycastTarget=false;
            Label(rect,text,0,-height*.5f,width-12,height,23);
            controls.Add(new Control{Id=id,Rect=rect,Image=image,Action=action});
        }
        private void ShowPage(bool wrists)
        { bodyPage.SetActive(!wrists);wristPage.SetActive(wrists);fingerPage.SetActive(false);limbPage.SetActive(false);SetHover(null);nextText=0; }
        private void ShowFingers()
        {bodyPage.SetActive(false);wristPage.SetActive(false);fingerPage.SetActive(true);limbPage.SetActive(false);SetHover(null);nextText=0;}
        private void ShowLimbs()
        {bodyPage.SetActive(false);wristPage.SetActive(false);fingerPage.SetActive(false);limbPage.SetActive(true);SetHover(null);nextText=0;}
        public void SetOpen(bool open)
        {
            IsOpen=open;
            if(panel==null)return;
            panel.gameObject.SetActive(open);
            HidePointers();
            leftTriggerArmed=rightTriggerArmed=false;
            SetHover(null); consumeUntil=Time.unscaledTime+.4f;
            if(open && view!=null)
            {
                // World-locked after opening: the menu does not stick to the user's face.
                Vector3 eye=view.transform.position, direction=view.transform.forward;
                var device=InputDevices.GetDeviceAtXRNode(XRNode.Head);
                if(trackingOrigin!=null && device.TryGetFeatureValue(CommonUsages.isTracked,out bool tracked) && tracked &&
                    device.TryGetFeatureValue(CommonUsages.devicePosition,out var p) && device.TryGetFeatureValue(CommonUsages.deviceRotation,out var q))
                {eye=trackingOrigin.TransformPoint(p);direction=trackingOrigin.rotation*q*Vector3.forward;}
                Vector3 forward=Vector3.ProjectOnPlane(direction,Vector3.up).normalized;
                if(forward.sqrMagnitude<.5f)forward=Vector3.forward;
                panel.SetPositionAndRotation(eye+forward*1.25f-Vector3.up*.06f,Quaternion.LookRotation(forward,Vector3.up));
            }
            else if(calibration!=null && calibration.Busy)calibration.Cancel();
            if(!open && fingers!=null && fingers.Busy)fingers.CancelCalibration();
            if(input!=null)input.UiOwnsGestures=open;
        }
        private void Update()
        {
            bool running=DesktopPreview || (session!=null && session.Running);
            if(!running)
            {
                if(IsOpen)SetOpen(false);
                shownForSession=false;if(input!=null)input.UiOwnsGestures=false;return;
            }
            ReadPinches();
            bool menu=MenuPressed();
            if(!shownForSession && (DesktopPreview || (InputDevices.GetDeviceAtXRNode(XRNode.Head)
                .TryGetFeatureValue(CommonUsages.isTracked,out bool headTracked) && headTracked)))
            {shownForSession=true;SetOpen(true);}
            if((menu&&!lastMenu)||Input.GetKeyDown(KeyCode.F8))ToggleMenu();
            lastMenu=menu;
            bool both=leftPinch&&rightPinch;
            if(both)
            {
                if(bothSince<0)bothSince=Time.unscaledTime;
                if(!bothLatched && Time.unscaledTime-bothSince>=1 && (fingers==null || !fingers.Busy))
                {bothLatched=true;ToggleMenu();}
            }
            else {bothSince=-1;bothLatched=false;}
            // B is also a legacy scatter input: consume the entire held menu press,
            // including after closing, so it cannot spill into liquid controls.
            if(input!=null)input.UiOwnsGestures=IsOpen||menu||both||bothLatched||Time.unscaledTime<consumeUntil;
            if(IsOpen)
            {
                UpdateControllerPointers();
                if(Time.unscaledTime>=nextText){nextText=Time.unscaledTime+.1f;RefreshText();}
            }
        }
        private void ToggleMenu()
        {
            // If the HMD was on a desk when Play started, or the user walked
            // away, the same gesture brings the panel back into view.
            bool inView=IsOpen && view!=null && Vector3.Distance(view.transform.position,panel.position)<2 &&
                Vector3.Angle(view.transform.forward,panel.position-view.transform.position)<40;
            SetOpen(!inView);
        }
        public bool Activate(string id)
        {
            if(!IsOpen)return false;
            foreach(var control in controls)
                if(control.Id==id && control.Rect.gameObject.activeInHierarchy)
                { control.Action();nextText=0;return true; }
            return false;
        }
        private void RefreshText()
        {
            status.text=$"Size x{calibration.RelativeScale:F3}\n{calibration.Status}\n{PointerStatus} | A / B / Menu / F8 to reopen";
            Vector3 l=calibration.LeftWrist,r=calibration.RightWrist;
            wristValues.text=$"Left: {l.x:F0}, {l.y:F0}, {l.z:F0} deg     Right: {r.x:F0}, {r.y:F0}, {r.z:F0} deg";
            fingerStatus.text=fingers!=null?fingers.Status:"Finger driver unavailable";
            string[] names={"Left arm", "Right arm", "Left leg", "Right leg"};
            for(int i=0;i<4;i++) limbValues[i].text=$"{names[i]}: {calibration.LimbLength(i)*100:F1} cm";
            limbStatus.text=calibration.Status;
        }
        private void UpdateControllerPointers()
        {
            bool leftValid=TryControllerRay(XRNode.LeftHand,out var leftRay,out bool leftPressed);
            bool rightValid=TryControllerRay(XRNode.RightHand,out var rightRay,out bool rightPressed);
            ProcessControllerPointers(leftValid,leftRay,leftPressed,rightValid,rightRay,rightPressed);
        }
        private bool TryControllerRay(XRNode node,out Ray ray,out bool pressed)
        {
            ray=default;pressed=false;
            var device=InputDevices.GetDeviceAtXRNode(node);
            if(!device.isValid || (device.characteristics&InputDeviceCharacteristics.Controller)==0)return false;
            Vector3 p;Quaternion q;
            if(device.TryGetFeatureValue(PointerTracked,out bool aimTracked))
            {
                // An invalid aim pose must NOT fall back to a stale grip or HMD pose.
                if(!aimTracked || !device.TryGetFeatureValue(PointerPosition,out p) || !device.TryGetFeatureValue(PointerRotation,out q))return false;
                if(device.TryGetFeatureValue(PointerTracking,out uint flags) && (flags&3u)!=3u)return false;
            }
            else
            {
                // Non-OpenXR controllers without a separate aim pose can use
                // their tracked device pose. Never use the adjusted avatar wrist.
                if(!device.TryGetFeatureValue(CommonUsages.isTracked,out bool tracked)||!tracked ||
                    !device.TryGetFeatureValue(CommonUsages.devicePosition,out p)||!device.TryGetFeatureValue(CommonUsages.deviceRotation,out q))return false;
                if(device.TryGetFeatureValue(CommonUsages.trackingState,out InputTrackingState flags) &&
                    (flags&(InputTrackingState.Position|InputTrackingState.Rotation))!=(InputTrackingState.Position|InputTrackingState.Rotation))return false;
            }
            if(!TryWorldRay(p,q,trackingOrigin,out ray))return false;
            pressed=device.TryGetFeatureValue(CommonUsages.triggerButton,out bool button)?button:
                device.TryGetFeatureValue(CommonUsages.trigger,out float trigger)&&trigger>.75f;
            return true;
        }
        public static bool TryWorldRay(Vector3 position,Quaternion rotation,Transform origin,out Ray ray)
        {
            ray=default;
            if(!float.IsFinite(position.x)||!float.IsFinite(position.y)||!float.IsFinite(position.z) ||
                !float.IsFinite(rotation.x)||!float.IsFinite(rotation.y)||!float.IsFinite(rotation.z)||!float.IsFinite(rotation.w) ||
                Quaternion.Dot(rotation,rotation)<.5f)return false;
            rotation=rotation.normalized;
            ray=new Ray(origin!=null?origin.TransformPoint(position):position,
                (origin!=null?origin.rotation:Quaternion.identity)*rotation*Vector3.forward);
            return true;
        }
        private void ProcessControllerPointers(bool leftValid,Ray leftRay,bool leftPressed,bool rightValid,Ray rightRay,bool rightPressed)
        {
            bool leftClick=TriggerEdge(leftValid,leftPressed,ref leftTriggerArmed);
            bool rightClick=TriggerEdge(rightValid,rightPressed,ref rightTriggerArmed);
            Control leftHit=null,rightHit=null;
            Vector3 leftPoint=default,rightPoint=default;float leftDistance=2,rightDistance=2;
            bool leftInside=leftValid&&RaycastPanel(leftRay,out leftHit,out leftPoint,out leftDistance);
            bool rightInside=rightValid&&RaycastPanel(rightRay,out rightHit,out rightPoint,out rightDistance);
            // A click always belongs to that controller's ray. Never activate
            // the other hand's highlighted button, including simultaneous input.
            if(rightClick)activeLeft=false;
            else if(leftClick)activeLeft=true;
            else if(activeLeft?leftInside:rightInside) { }
            else activeLeft=leftInside || (!rightValid&&leftValid);
            bool valid=activeLeft?leftValid:rightValid;
            bool inside=activeLeft?leftInside:rightInside;
            SetHover(activeLeft?leftHit:rightHit);
            cursor.gameObject.SetActive(inside);
            if(inside) {var p=panel.InverseTransformPoint(activeLeft?leftPoint:rightPoint);cursor.localPosition=new Vector3(p.x,p.y,-.2f);}
            DrawLaser(leftLaser,leftValid,leftRay,leftDistance,activeLeft);
            DrawLaser(rightLaser,rightValid,rightRay,rightDistance,!activeLeft);
            PointerStatus=valid?(activeLeft?"Left controller":"Right controller"):"Controller not tracked";
            if((activeLeft?leftClick:rightClick)&&hovered!=null && Time.unscaledTime>=consumeUntil)Activate(hovered.Id);
        }
        private static bool TriggerEdge(bool valid,bool pressed,ref bool armed)
        {
            if(!valid){armed=false;return false;}
            if(!pressed){armed=true;return false;}
            bool clicked=armed;armed=false;return clicked;
        }
        private bool RaycastPanel(Ray ray,out Control hit,out Vector3 point,out float distance)
        {
            hit=null;point=default;distance=2;
            var plane=new Plane(panel.forward,panel.position);
            if(!plane.Raycast(ray,out float d)||d<=0||d>4)return false;
            point=ray.GetPoint(d);var local=panel.InverseTransformPoint(point);
            if(!panel.rect.Contains(new Vector2(local.x,local.y)))return false;
            distance=d;
            foreach(var control in controls)
            {
                if(!control.Rect.gameObject.activeInHierarchy)continue;
                var p=control.Rect.InverseTransformPoint(point);
                if(control.Rect.rect.Contains(new Vector2(p.x,p.y))){hit=control;break;}
            }
            return true;
        }
        private static void DrawLaser(LineRenderer line,bool valid,Ray ray,float length,bool active)
        {
            if(line==null)return;
            line.enabled=valid;
            if(!valid)return;
            var color=active?new Color(.2f,1,1,.95f):new Color(.2f,.5f,.75f,.55f);
            line.startColor=line.endColor=color;
            line.SetPosition(0,ray.origin);line.SetPosition(1,ray.GetPoint(length));
        }
        private void HidePointers()
        {
            if(leftLaser!=null)leftLaser.enabled=false;
            if(rightLaser!=null)rightLaser.enabled=false;
            if(cursor!=null)cursor.gameObject.SetActive(false);
        }
        private void SetHover(Control value)
        {
            if(hovered==value)return;
            if(hovered!=null)hovered.Image.color=Normal;
            hovered=value;
            if(hovered!=null)hovered.Image.color=Hover;
        }
        private void ReadPinches()
        {
            hands.Clear();SubsystemManager.GetSubsystems(hands);
            foreach(var provider in hands)if(provider.running)
            {leftPinch=Pinched(provider.leftHand,leftPinch);rightPinch=Pinched(provider.rightHand,rightPinch);return;}
            leftPinch=rightPinch=false;
        }
        private static bool Pinched(XRHand hand,bool held) => hand.isTracked &&
            hand.GetJoint(XRHandJointID.ThumbTip).TryGetPose(out var thumb) &&
            hand.GetJoint(XRHandJointID.IndexTip).TryGetPose(out var index) &&
            Vector3.Distance(thumb.position,index.position)<(held?.04f:.025f);
        private static bool MenuPressed()
        {
            var right=InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            bool face=right.isValid && (right.characteristics&InputDeviceCharacteristics.Controller)!=0 &&
                ((right.TryGetFeatureValue(CommonUsages.primaryButton,out bool a)&&a) ||
                 (right.TryGetFeatureValue(CommonUsages.secondaryButton,out bool b)&&b));
            return face || (InputDevices.GetDeviceAtXRNode(XRNode.LeftHand)
                .TryGetFeatureValue(CommonUsages.menuButton,out bool menu)&&menu);
        }
        private void OnDisable()
        {SetOpen(false);shownForSession=false;if(input!=null)input.UiOwnsGestures=false;}
        private void OnDestroy()
        {
            if(uiMaterial!=null)Destroy(uiMaterial);if(panel!=null)Destroy(panel.gameObject);
            if(leftLaser!=null)Destroy(leftLaser.gameObject);if(rightLaser!=null)Destroy(rightLaser.gameObject);
        }
    }
}
