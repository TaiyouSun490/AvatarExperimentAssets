using System;
using UnityEditor;
using UnityEngine;

namespace AvatarExperiments.FpsArms.Editor
{
    public static class FpsArmSmokeTests
    {
        [MenuItem("Tools/Avatar Experiments/FPS Arms/Run smoke tests (no hardware)")]
        public static void Run()
        {
            var root=new GameObject("Temporary FPS arm tests");
            try
            {
                var head=Child(root.transform,"Head",new Vector3(0,1.65f,0));
                int poses=0;
                foreach(bool left in new[]{false,true})
                foreach(bool helpers in new[]{false,true})
                {
                    var arm=MakeArm(root.transform,head,left,helpers);
                    arm.Initialize();Check(arm.TwistRelaxer.IsReady,"Direct and helper chains supported");
                    var shoulder=head.position+arm.shoulderOffset;
                    arm.wristTarget.position=shoulder+new Vector3(left?-.05f:.05f,-.18f,.35f);
                    float upper=arm.UpperLength,forearm=arm.ForearmLength;
                    arm.wristTarget.rotation=Quaternion.identity;arm.Solve();
                    float palm=Vector3.Distance(arm.indexBase.position,arm.littleBase.position);
                    Check(arm.SetSegmentLengths(upper*1.1f,forearm*1.2f),"Segment scaling");
                    arm.Solve();
                    Check(Mathf.Abs(Vector3.Distance(arm.indexBase.position,arm.littleBase.position)-palm*1.2f)<.0001f,"Hand inherits forearm size");
                    var axes=(arm.wrist.position-arm.elbow.position).normalized;
                    arm.TwistRelaxer.ResetHistory();
                    for(int roll=-180;roll<=180;roll+=3)
                    {
                        arm.wristTarget.rotation=Quaternion.AngleAxis(roll,axes);
                        arm.relaxTwist=false;arm.Solve();
                        var e=arm.elbow.position;var w=arm.wrist.position;var q=arm.HandFrame();
                        arm.relaxTwist=true;arm.Solve();
                        Check(Vector3.Distance(e,arm.elbow.position)<.0001f&&Vector3.Distance(w,arm.wrist.position)<.0001f,"Relaxer endpoint preservation");
                        Check(Quaternion.Angle(q,arm.HandFrame())<.1f,"Relaxer hand orientation");
                        Check(arm.WristError<.001f,"IK wrist target");
                        arm.Solve();Check(Vector3.Distance(w,arm.wrist.position)<.0001f,"Repeated solve");
                        poses++;
                    }
                    UnityEngine.Object.DestroyImmediate(arm.gameObject);
                }
                var fit=new ArmLengthCalibration.Fit{valid=true,upper=.3f,forearm=.25f,
                    hint=new Vector3(-.45f,-.8f,0).normalized,reachAxis=Vector3.forward};
                var pair=ArmLengthCalibration.MirrorLeftFit(fit);
                Check(pair.left.upper==pair.right.upper&&pair.right.hint.x==-pair.left.hint.x,"Left-only bilateral fit");
                var pole=StableElbowPole.CorrectReferenceBend(Vector3.forward,Vector3.up,true);
                Check(pole.y<0,"Inverted pole correction");
                var sample=new TrackedJointSample();sample.Capture(new[]{Vector3.one},new[]{true},1);
                Check(sample.TryGet(0,1.1f,out _)&&!sample.TryGet(0,1.3f,out _),"Measured sample expiry");
                var hold=new ElbowCalibrationHold();bool complete=false;
                for(int i=0;i<95;i++)complete=hold.Tick(true,Vector3.zero,Vector3.forward,Vector3.up,Vector3.forward,1f/60f,1.5f);
                Check(complete,"Bare-hand stable hold");
                Debug.Log($"FPS_ARMS_SMOKE_PASS {poses} poses, direct/helper chains, mirrored arms, hand scaling, calibration and raw samples. No device or PlayerPrefs writes.");
            }
            finally{UnityEngine.Object.DestroyImmediate(root);}
        }
        static TrackedArmAvatar MakeArm(Transform root,Transform head,bool left,bool helpers)
        {
            var go=new GameObject("Test arm");go.transform.SetParent(root,false);
            var arm=go.AddComponent<TrackedArmAvatar>();arm.head=head;arm.leftHand=left;
            arm.shoulderOffset=new Vector3(left?-.19f:.19f,-.2f,-.05f);
            arm.elbowHint=new Vector3(left?-.45f:.45f,-.8f,-.25f);
            arm.model=Child(go.transform,"Model",Vector3.zero);arm.model.localScale=new Vector3(left?-1:1,1,1);
            arm.upperArm=Child(arm.model,"Upper",Vector3.zero);
            var upperParent=helpers?Child(arm.upperArm,"Upper helper",Vector3.up*.15f):arm.upperArm;
            arm.elbow=Child(upperParent,"Elbow",Vector3.up*(helpers?.15f:.3f));
            var lowerParent=helpers?Child(arm.elbow,"Forearm helper",Vector3.forward*.125f):arm.elbow;
            arm.wrist=Child(lowerParent,"Wrist",Vector3.forward*(helpers?.125f:.25f));
            arm.indexBase=Child(arm.wrist,"Index",new Vector3(-.02f,0,.05f));
            arm.middleBase=Child(arm.wrist,"Middle",new Vector3(0,0,.06f));
            arm.littleBase=Child(arm.wrist,"Little",new Vector3(.02f,0,.05f));
            arm.wristTarget=Child(go.transform,"Target",Vector3.zero);return arm;
        }
        static Transform Child(Transform parent,string name,Vector3 position)
        {var t=new GameObject(name).transform;t.SetParent(parent,false);t.localPosition=position;return t;}
        static void Check(bool condition,string label){if(!condition)throw new Exception("FPS arm test failed: "+label);}
    }
}
