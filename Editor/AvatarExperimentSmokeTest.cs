using System;
using UnityEditor;
using UnityEngine;

namespace AvatarExperiments.Editor
{
    public static class AvatarExperimentSmokeTest
    {
        [MenuItem("Tools/Avatar Experiments/Run math smoke tests")]
        public static void Run()
        {
            var origin=new GameObject("Temporary calibration test");
            try
            {
                origin.transform.SetPositionAndRotation(new Vector3(2,3,4),Quaternion.Euler(0,80,0));
                Check(VrCalibrationMenu.TryWorldRay(Vector3.forward,Quaternion.identity,origin.transform,out var ray),"Controller ray valid");
                Check(Vector3.Distance(ray.origin,origin.transform.TransformPoint(Vector3.forward))<.0001f,"Origin applied once");
                Check(Vector3.Angle(ray.direction,origin.transform.forward)<.01f,"Controller direction");
                Check(!VrCalibrationMenu.TryWorldRay(new Vector3(float.NaN,0,0),Quaternion.identity,null,out _),"Invalid ray rejected");
                var reference=Quaternion.Euler(25,40,70);
                var basis=Quaternion.Euler(10,50,20);
                var rest=Quaternion.Euler(5,15,25);
                Check(Quaternion.Angle(XRHandHumanoidFingerDriver.RetargetLocal(reference,reference,basis,rest),rest)<.01f,"Rest finger reference");
                Check(XRHandHumanoidFingerDriver.Straight(Vector3.zero,Vector3.right,Vector3.right*2,.8f),"Straight finger");
                Check(!XRHandHumanoidFingerDriver.Straight(Vector3.zero,Vector3.right,Vector3.right+Vector3.up,.8f),"Bent finger");
                Check(Resources.Load<Shader>("AvatarExperiments/Shaders/WorldSpaceUI")!=null,"Packaged UI shader");
                Debug.Log("AvatarExperimentAssets smoke tests: PASS (8 checks). No user settings changed.");
            }
            finally { UnityEngine.Object.DestroyImmediate(origin); }
        }
        private static void Check(bool condition,string label)
        { if(!condition)throw new InvalidOperationException("Avatar experiments test failed: "+label); }
    }
}
