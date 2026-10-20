using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace AvatarExperiments.Editor
{
    public static class DesktopSpectatorSmokeTest
    {
        [MenuItem("Tools/Avatar Experiments/Run desktop spectator smoke tests")]
        public static void Run()
        {
            var sourceObject = new GameObject("Spectator test XR camera");
            var owner = new GameObject("Spectator test owner");
            try
            {
                var source = sourceObject.AddComponent<Camera>();
                var data = source.GetUniversalAdditionalCameraData();
                data.allowXRRendering = true;
                source.stereoTargetEye = StereoTargetEyeMask.Both;
                source.transform.SetPositionAndRotation(new Vector3(2, 1.6f, 3), Quaternion.Euler(10, 30, 0));
                source.rect = new Rect(.1f, .1f, .8f, .8f);
                source.fieldOfView = 93;
                var projection = source.projectionMatrix;
                var rect = source.rect; var position = source.transform.position; var rotation = source.transform.rotation;
                var spectator = owner.AddComponent<DesktopSpectator>();
                spectator.EnableOperatorInput = false;
                spectator.Configure(source, Array.Empty<Renderer>());
                foreach (SpectatorLayout layout in Enum.GetValues(typeof(SpectatorLayout)))
                foreach (var size in new[] { new Vector2Int(1920,1080), new Vector2Int(800,1200), new Vector2Int(2560,720) })
                {
                    spectator.SetLayout(layout);
                    spectator.RefreshViews(1, size.x, size.y);
                    var third = spectator.ThirdPersonCamera; var preview = spectator.ParticipantPreviewCamera;
                    Check(third && preview, "Owned cameras created");
                    Check(!third.GetUniversalAdditionalCameraData().allowXRRendering &&
                        !preview.GetUniversalAdditionalCameraData().allowXRRendering &&
                        third.stereoTargetEye == StereoTargetEyeMask.None && preview.stereoTargetEye == StereoTargetEyeMask.None,
                        "Desktop cameras never request XR rendering");
                    Check(third.depth > source.depth && preview.depth > third.depth, "Desktop renders after the XR mirror");
                    Check(preview.enabled == (layout != SpectatorLayout.ThirdPerson), "Unused participant camera disabled");
                    Check(preview.transform.position == source.transform.position &&
                        Quaternion.Angle(preview.transform.rotation, source.transform.rotation) < .001f, "Participant pose copied");
                    Check(source.rect == rect && source.projectionMatrix == projection && source.fieldOfView == 93 &&
                        source.transform.position == position && source.transform.rotation == rotation && source.targetTexture == null &&
                        source.enabled && source.stereoTargetEye == StereoTargetEyeMask.Both && data.allowXRRendering,
                        "Tracking camera pose, projection and output unchanged");
                    Check(third.GetComponent<AudioListener>() == null && preview.GetComponent<AudioListener>() == null, "No duplicate audio listeners");
                    DesktopSpectator.Viewports(layout,size.x,size.y,.34f,out var a,out var b);
                    Check(a.xMin>=0 && a.yMin>=0 && a.xMax<=1 && a.yMax<=1 &&
                        b.xMin>=0 && b.yMin>=0 && b.xMax<=1 && b.yMax<=1, "Viewports inside desktop");
                    if(layout == SpectatorLayout.SideBySide) Check(a.xMax==b.xMin && a.width+b.width==1,"Split covers screen");
                    // The fallback standing bounds must fit even in a portrait split.
                    var bounds = new Bounds(source.transform.position + Vector3.down*.6f,new Vector3(1.2f,1.8f,1.2f));
                    for(int corner=0;corner<8;corner++)
                    {
                        var sign=new Vector3((corner&1)==0?-1:1,(corner&2)==0?-1:1,(corner&4)==0?-1:1);
                        var point=third.WorldToViewportPoint(bounds.center+Vector3.Scale(sign,bounds.extents));
                        Check(point.z>0 && point.x>0 && point.x<1 && point.y>0 && point.y<1,"Whole-scene framing includes all corners");
                    }
                }
                var oldThird=spectator.ThirdPersonCamera; var oldPreview=spectator.ParticipantPreviewCamera;
                spectator.enabled=false;
                // This is an edit-mode batch test of a runtime-only component.
                // Unity does not invoke its play-mode lifecycle here.
                Lifecycle(spectator,"OnDisable");
                Check(!oldThird && !oldPreview,"Disabling releases owned cameras");
                spectator.enabled=true;spectator.RefreshViews(1,1280,720);
                Check(spectator.ThirdPersonCamera && spectator.ParticipantPreviewCamera,"Re-enable restores views");
                Debug.Log("DESKTOP_SPECTATOR_PASS: 3 layouts x 3 aspect ratios, XR isolation, framing, input camera preservation, cleanup and re-enable.");
            }
            finally
            {
                var spectator=owner.GetComponent<DesktopSpectator>();
                if(spectator!=null)Lifecycle(spectator,"OnDisable");
                UnityEngine.Object.DestroyImmediate(owner);
                UnityEngine.Object.DestroyImmediate(sourceObject);
            }
        }
        private static void Check(bool value,string description)
        { if(!value)throw new InvalidOperationException("Desktop spectator: "+description); }
        private static void Lifecycle(DesktopSpectator target,string method) =>
            typeof(DesktopSpectator).GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(target,null);
    }
}
