using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Hands;

namespace AvatarExperiments.FpsArms.Editor
{
    public sealed class FpsArmSetupWindow : EditorWindow
    {
        FpsArmModelProfile profile;
        Camera camera;
        string profileId="fps-arm";
        [MenuItem("Tools/Avatar Experiments/FPS Arms/Create rig")]
        static void Open()=>GetWindow<FpsArmSetupWindow>("FPS Arm Setup");
        void OnGUI()
        {
            EditorGUILayout.HelpBox("Choose a model profile. Adds a separate arm rig to the current scene. Models, materials and XR project settings are not modified.",MessageType.Info);
            profile=(FpsArmModelProfile)EditorGUILayout.ObjectField("Model profile",profile,typeof(FpsArmModelProfile),false);
            camera=(Camera)EditorGUILayout.ObjectField("Existing XR camera (optional)",camera,typeof(Camera),true);
            profileId=EditorGUILayout.TextField("Calibration profile ID",profileId);
            if(camera)EditorGUILayout.HelpBox("The existing camera must already be tracked. FPS Arms will not write its pose. The camera parent is the tracking origin.",MessageType.Info);
            using(new EditorGUI.DisabledScope(!profile||!profile.modelPrefab||Application.isPlaying))
                if(GUILayout.Button("Create left and right arms"))
                    try{Selection.activeGameObject=Create(profile,camera,profileId);}
                    catch(Exception e){Debug.LogException(e);EditorUtility.DisplayDialog("FPS Arms",e.Message,"OK");}
        }
        public static GameObject Create(FpsArmModelProfile p,Camera existingCamera,string id)
        {
            if(!p||!p.modelPrefab||string.IsNullOrWhiteSpace(id))throw new ArgumentException("Model and calibration profile ID required.");
            if(existingCamera&&!existingCamera.transform.parent)throw new ArgumentException("The XR camera needs a tracking-origin parent.");
            if(p.fingers==null||p.fingers.Length==0)throw new ArgumentException("Configure the profile's finger mappings first.");
            var root=new GameObject("FPS Arm Experiment");
            try
            {
                var rig=root.AddComponent<TrackedHandsAvatarRig>();rig.enabled=false;
                rig.driveHeadPose=!existingCamera;
                rig.trackingSpace=existingCamera?existingCamera.transform.parent:Child(root.transform,"Tracking Origin");
                rig.head=existingCamera;
                if(!rig.head)
                {
                    rig.head=Child(rig.trackingSpace,"XR Camera").gameObject.AddComponent<Camera>();
                    rig.head.transform.localPosition=new Vector3(0,1.65f,0);rig.head.nearClipPlane=.03f;
                }
                rig.leftArm=BuildArm(root.transform,rig,p,true,out var left);
                rig.rightArm=BuildArm(root.transform,rig,p,false,out var right);
                rig.leftJoints=left;rig.rightJoints=right;
                var fit=root.AddComponent<ArmLengthCalibration>();fit.rig=rig;fit.calibrationProfile=id.Trim();
                rig.enabled=true;
                Undo.RegisterCreatedObjectUndo(root,"Create FPS arm experiment");
                return root;
            }
            catch{DestroyImmediate(root);throw;}
        }
        static Transform Child(Transform parent,string name)
        {var go=new GameObject(name);go.transform.SetParent(parent,false);return go.transform;}
        static TrackedArmAvatar BuildArm(Transform root,TrackedHandsAvatarRig rig,FpsArmModelProfile p,bool left,out TrackedHandsAvatarRig.JointBinding[] bindings)
        {
            var arm=Child(root,left?"Left Arm":"Right Arm").gameObject.AddComponent<TrackedArmAvatar>();
            arm.leftHand=left;arm.head=rig.head.transform;
            arm.shoulderOffset=new Vector3(left?-.19f:.19f,-.2f,-.05f);
            arm.elbowHint=new Vector3(left?-.45f:.45f,-.8f,-.25f);
            // Keep targets under our owned root, so Undo/failure never leaves objects
            // under an existing external XR origin.
            arm.wristTarget=Child(root,left?"Left Wrist Target":"Right Wrist Target");
            arm.wristTarget.position=rig.trackingSpace.TransformPoint(new Vector3(left?-.23f:.23f,1.15f,.35f));
            var model=(GameObject)PrefabUtility.InstantiatePrefab(p.modelPrefab,arm.transform);
            arm.model=model.transform;
            if(left&&p.mirrorLeft){var scale=model.transform.localScale;scale.x=-scale.x;model.transform.localScale=scale;}
            foreach(var animator in model.GetComponentsInChildren<Animator>())animator.enabled=false;
            if(p.openPose)p.openPose.SampleAnimation(model,0);
            Transform Bone(string path)
            {var t=string.IsNullOrEmpty(path)?null:model.transform.Find(path);return t?t:throw new ArgumentException("Missing model bone path: "+path);}
            arm.upperArm=Bone(p.upperArm);arm.elbow=Bone(p.elbow);arm.wrist=Bone(p.wrist);
            arm.indexBase=Bone(p.indexBase);arm.middleBase=Bone(p.middleBase);arm.littleBase=Bone(p.littleBase);
            arm.skin=string.IsNullOrEmpty(p.rendererPath)?model.GetComponentInChildren<SkinnedMeshRenderer>():Bone(p.rendererPath).GetComponent<SkinnedMeshRenderer>();
            if(!arm.skin)throw new ArgumentException("Model needs a SkinnedMeshRenderer.");
            bindings=p.fingers.Select(f=>new TrackedHandsAvatarRig.JointBinding{bone=Bone(f.bone),tip=Bone(f.tip),joint=f.joint,nextJoint=f.nextJoint}).ToArray();
            var bones=model.GetComponentsInChildren<Transform>();
            var rest=bones.Select(t=>t.localRotation).ToArray();
            var positions=bones.Select(t=>t.localPosition).ToArray();
            var scales=bones.Select(t=>t.localScale).ToArray();
            var poses=p.fingers.Select(f=>new TrackedArmAvatar.FingerPose{bone=Bone(f.bone),open=Bone(f.bone).localRotation,group=f.controllerGroup}).ToArray();
            if(p.closedPose)p.closedPose.SampleAnimation(model,0);
            for(int i=0;i<poses.Length;i++)poses[i].closed=poses[i].bone.localRotation;
            for(int i=0;i<bones.Length;i++)
            {bones[i].localRotation=rest[i];bones[i].localPosition=positions[i];bones[i].localScale=scales[i];}
            arm.fingers=poses;
            arm.model.rotation=Quaternion.Inverse(arm.HandFrame())*arm.model.rotation;
            arm.skin.updateWhenOffscreen=true;
            arm.Initialize();
            if(!arm.TwistRelaxer.IsReady)throw new ArgumentException("Use a direct shoulder-elbow-wrist chain or one intermediate bone per segment.");
            arm.Solve();return arm;
        }

        [MenuItem("Tools/Avatar Experiments/FPS Arms/Create profile for selected fp_male_hand")]
        static void CreateFpProfile()
        {
            var model=Selection.activeObject as GameObject;
            if(!model||!AssetDatabase.Contains(model)){EditorUtility.DisplayDialog("FPS Arms","Select your imported fp_male_hand model asset first.","OK");return;}
            var p=CreateFpProfile(model);
            string path=EditorUtility.SaveFilePanelInProject("Save arm profile",model.name+"_FPSArms","asset","Save model bindings");
            if(string.IsNullOrEmpty(path)){DestroyImmediate(p);return;}
            AssetDatabase.CreateAsset(p,path);Selection.activeObject=p;
        }
        public static FpsArmModelProfile CreateFpProfile(GameObject model)
        {
            var bones=model.GetComponentsInChildren<Transform>();
            string Path(string name)=>AnimationUtility.CalculateTransformPath(bones.Single(t=>t.name==name),model.transform);
            // Validate paths before creating an asset; no model or third-party data is copied.
            string upper=Path("arm_up_deform"),elbow=Path("forearm_up_deform"),wrist=Path("hand_joint");
            var p=CreateInstance<FpsArmModelProfile>();p.modelPrefab=model;
            p.upperArm=upper;p.elbow=elbow;p.wrist=wrist;
            p.indexBase=Path("index_prox");p.middleBase=Path("middle_prox");p.littleBase=Path("little_prox");
            var clips=AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(model)).OfType<AnimationClip>().ToArray();
            p.openPose=clips.FirstOrDefault(c=>c.name.EndsWith("|neutral_pose"));
            p.closedPose=clips.FirstOrDefault(c=>c.name.EndsWith("|fist_pose"));
            var fingers=new System.Collections.Generic.List<FpsArmModelProfile.Finger>();
            void Add(string bone,string tip,XRHandJointID joint,XRHandJointID next,int group)
            {fingers.Add(new FpsArmModelProfile.Finger{bone=Path(bone),tip=Path(tip),joint=joint,nextJoint=next,controllerGroup=group});}
            Add("thumb_prox","thumb_inter",XRHandJointID.ThumbMetacarpal,XRHandJointID.ThumbProximal,0);
            Add("thumb_inter","thumb_dist",XRHandJointID.ThumbProximal,XRHandJointID.ThumbDistal,0);
            Add("thumb_dist","thumb_dist_end",XRHandJointID.ThumbDistal,XRHandJointID.ThumbTip,0);
            foreach(string name in new[]{"Index","Middle","Ring","Little"})
            {
                string f=name.ToLowerInvariant();int group=name=="Index"?1:2;
                XRHandJointID Joint(string suffix)=>(XRHandJointID)Enum.Parse(typeof(XRHandJointID),name+suffix);
                Add(f+"_prox",f+"_inter",Joint("Proximal"),Joint("Intermediate"),group);
                Add(f+"_inter",f+"_dist",Joint("Intermediate"),Joint("Distal"),group);
                Add(f+"_dist",f+"_dist_end",Joint("Distal"),Joint("Tip"),group);
            }
            p.fingers=fingers.ToArray();return p;
        }
    }
}
