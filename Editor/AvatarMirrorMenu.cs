using UnityEditor;
using UnityEngine;

namespace AvatarExperiments.Editor
{
    public static class AvatarMirrorMenu
    {
        [MenuItem("Tools/Avatar Experiments/Create full-height mirror")]
        public static void Create()
        {
            if(Application.isPlaying) { Debug.LogWarning("Stop Play before creating a persistent mirror."); return; }
            var setup=Object.FindAnyObjectByType<AvatarExperimentSetup>();
            var camera=setup!=null?setup.XrCamera:Camera.main;
            Vector3 forward=camera!=null?Vector3.ProjectOnPlane(camera.transform.forward,Vector3.up).normalized:Vector3.forward;
            if(forward.sqrMagnitude<.5f)forward=Vector3.forward;
            Vector3 position=camera!=null?camera.transform.position:Vector3.zero;
            position.y=(setup!=null && setup.TrackingOrigin!=null?setup.TrackingOrigin.position.y:0)+1.1f;
            var shader=Resources.Load<Shader>("AvatarExperiments/Shaders/PlanarMirror");
            if(shader==null) { Debug.LogError("Packaged mirror shader missing.");return; }
            const string folder="Assets/AvatarExperimentsGenerated";
            if(!AssetDatabase.IsValidFolder(folder))AssetDatabase.CreateFolder("Assets","AvatarExperimentsGenerated");
            const string path=folder+"/Mirror.mat";
            var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(material==null) {material=new Material(shader);AssetDatabase.CreateAsset(material,path);}
            var go=GameObject.CreatePrimitive(PrimitiveType.Quad);go.name="Avatar Experiment Mirror";
            Undo.RegisterCreatedObjectUndo(go,"Create avatar mirror");
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.layer=PlanarMirror.MirrorLayer;
            go.transform.SetPositionAndRotation(position+forward*2,Quaternion.LookRotation(forward,Vector3.up));
            go.transform.localScale=new Vector3(1.5f,2.1f,1);
            go.GetComponent<Renderer>().sharedMaterial=material;
            go.AddComponent<PlanarMirror>();
            Selection.activeGameObject=go;
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(go.scene);
            Debug.Log("Mirror created. Save the scene. Reflective face points along local -Z; layer 31 is excluded from reflection.");
        }
    }
}
