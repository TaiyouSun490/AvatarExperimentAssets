using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Management;

namespace AvatarExperiments.VirtualHand.Editor
{
    // PC scenes must remain usable when the same project also has an active VR loader.
    [InitializeOnLoad]
    static class VirtualHandPCPlayMode
    {
        const string Key="AvatarExperiments.VirtualHand.PCPlay";
        static VirtualHandPCPlayMode(){EditorApplication.playModeStateChanged+=Changed;}
        static void Changed(PlayModeStateChange state)
        {
            var settings=XRGeneralSettings.Instance;
            if(state==PlayModeStateChange.ExitingEditMode)
            {
                var experiment=Object.FindFirstObjectByType<TabletopExperiment>();
                if(!experiment||!experiment.desktopPreview||!settings)return;
                SessionState.SetBool(Key,true);SessionState.SetBool(Key+".WasEnabled",settings.InitManagerOnStart);
                settings.InitManagerOnStart=false;
            }
            else if(state==PlayModeStateChange.EnteredEditMode&&SessionState.GetBool(Key,false))
            {
                if(settings)settings.InitManagerOnStart=SessionState.GetBool(Key+".WasEnabled",true);
                SessionState.SetBool(Key,false);
            }
        }
    }
}
