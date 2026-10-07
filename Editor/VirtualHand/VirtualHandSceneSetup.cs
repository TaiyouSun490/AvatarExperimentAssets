using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AvatarExperiments.VirtualHand.Editor
{
    public static class VirtualHandSceneSetup
    {
        const string Menu = "Tools/Avatar Experiments/Virtual Hand/";
        [MenuItem(Menu + "Create PC experiment scene")]
        public static void CreatePC() { CreateInteractive(true, false); }
        [MenuItem(Menu + "Create VR experiment scene")]
        public static void CreateVR() { CreateInteractive(false, false); }
        [MenuItem(Menu + "Create PC scene from selected hand renderer")]
        public static void FromSelectedPC() { CreateInteractive(true, true); }
        [MenuItem(Menu + "Create VR scene from selected hand renderer")]
        public static void FromSelectedVR() { CreateInteractive(false, true); }
        static void CreateInteractive(bool desktop, bool selected)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play before creating a scene.");
            Mesh mesh = null; Material material = null;
            if (selected)
            {
                var renderer = Selection.activeGameObject ? Selection.activeGameObject.GetComponent<Renderer>() : null;
                if (!renderer) throw new InvalidOperationException("Select one hand MeshRenderer or SkinnedMeshRenderer. Pose it above world Y=0, fingers along +Z.");
                mesh = BakeSelected(renderer); material = renderer.sharedMaterial;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) { if (mesh) UnityEngine.Object.DestroyImmediate(mesh); return; }
            try { Create(desktop, "Assets/VirtualHandExperiments", mesh, material); }
            finally { if (mesh) UnityEngine.Object.DestroyImmediate(mesh); }
            TabletopOperatorWindow.Open();
        }
        /// <summary>Creates a new folder and scene; never overwrites an existing experiment or changes Build Settings/XR.</summary>
        public static TabletopExperiment Create(bool desktop, string requestedFolder, Mesh suppliedMesh = null, Material suppliedMaterial = null)
        {
            if (!requestedFolder.StartsWith("Assets/", StringComparison.Ordinal) || requestedFolder.Contains(".."))
                throw new ArgumentException("Choose an Assets subfolder.");
            Directory.CreateDirectory(Path.GetDirectoryName(requestedFolder));AssetDatabase.Refresh();
            string folder = AssetDatabase.GenerateUniqueAssetPath(requestedFolder);
            if(string.IsNullOrEmpty(folder))throw new IOException("Could not create a unique Assets folder: "+requestedFolder);
            Directory.CreateDirectory(folder); AssetDatabase.Refresh();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("Virtual Hand Experiment"); root.SetActive(false);
            var experiment = root.AddComponent<TabletopExperiment>(); experiment.desktopPreview = desktop;
            var handGo = new GameObject("Stationary virtual hand"); handGo.transform.SetParent(root.transform, false);
            handGo.transform.localPosition = new Vector3(0, 0, .45f);
            var mesh = suppliedMesh ? UnityEngine.Object.Instantiate(suppliedMesh) : CreateExampleHand();
            mesh.name = "Tabletop hand"; AssetDatabase.CreateAsset(mesh, folder + "/Hand.asset");
            var filter = handGo.AddComponent<MeshFilter>(); filter.sharedMesh = mesh;
            var skin = handGo.AddComponent<MeshRenderer>();
            var material = suppliedMaterial ? new Material(suppliedMaterial) : Lit(new Color(.64f, .38f, .25f));
            material.name = suppliedMaterial ? suppliedMaterial.name + " (experiment copy)" : "Example hand - URP Lit";
            AssetDatabase.CreateAsset(material, folder + "/Hand.mat"); skin.sharedMaterial = material;
            var hand = handGo.AddComponent<TabletopMeltHand>(); hand.surface = filter; hand.skin = skin; hand.viscousMaterial = material;hand.tabletop=root.transform;
            experiment.hand = hand;
            var placement=root.AddComponent<TabletopHandPlacement>();placement.experiment=experiment;experiment.placement=placement;
            var tableMaterial = Lit(new Color(.23f, .27f, .29f)); AssetDatabase.CreateAsset(tableMaterial, folder + "/Table.mat");
            Primitive("Table", PrimitiveType.Cube, root.transform, new Vector3(0, -.035f, .48f), new Vector3(1.4f, .07f, .95f), tableMaterial);
            Primitive("Floor", PrimitiveType.Cube, root.transform, new Vector3(0, -.785f, .5f), new Vector3(5, .07f, 5), tableMaterial);
            var stimulusMaterial = Lit(new Color(.95f, .48f, .09f)); AssetDatabase.CreateAsset(stimulusMaterial, folder + "/Brush.mat");
            var ball = Primitive("Visual brush tip", PrimitiveType.Sphere, root.transform, Vector3.zero, Vector3.one * experiment.ballRadius * 2, stimulusMaterial);
            UnityEngine.Object.DestroyImmediate(ball.GetComponent<Collider>()); ball.SetActive(false); experiment.ball = ball.transform;
            experiment.realHandAnchor = new GameObject("Real hand anchor - calibration result").transform;
            experiment.realHandAnchor.SetParent(root.transform, false);
            var tracking = new GameObject("Floor tracking origin").transform; tracking.SetParent(root.transform, false); tracking.localPosition = Vector3.down * .75f;
            var camera = new GameObject("Participant Camera").AddComponent<Camera>(); camera.tag = "MainCamera";
            camera.transform.SetParent(tracking, false); camera.transform.localPosition = new Vector3(0, 1.22f, -.05f); camera.transform.localRotation = Quaternion.Euler(35, 0, 0);
            camera.nearClipPlane = .03f; camera.farClipPlane = 30; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.08f, .1f, .12f);
            camera.gameObject.AddComponent<AudioListener>();
            var head = camera.gameObject.AddComponent<TabletopHeadTracking>(); head.trackingSpace = tracking; head.enabled = !desktop;
            var brush = root.AddComponent<TabletopTrackedBrush>(); brush.experiment = experiment; brush.trackingSpace = tracking; brush.automaticSampling = !desktop;
            experiment.trackedBrush = brush;
            // The default points are on the actual top surface. They remain editable in the generated scene.
            brush.visualLandmarks = CreateLandmarks(hand);
            var vibration = root.AddComponent<TabletopConditionVibration>(); vibration.experiment = experiment; experiment.vibration = vibration;
            var study=root.AddComponent<VirtualHandStudy>();study.experiment=experiment;experiment.study=study;
            var target=new GameObject("Drift target - right index fingertip (adjust to your mesh)").transform;target.SetParent(hand.transform,false);
            target.localPosition=new Vector3(-.03f,.016f,.138f);study.virtualTarget=target;
            var probeMaterial=new Material(Shader.Find("Universal Render Pipeline/Unlit")){color=Color.white};AssetDatabase.CreateAsset(probeMaterial,folder+"/Measurement.mat");
            var presenter=root.AddComponent<DriftPresenter>();presenter.participantCamera=camera;presenter.probeMaterial=probeMaterial;study.presenter=presenter;
            var textMaterial=new Material(Shader.Find("AvatarExperiments/VirtualHand/MeasurementText"));AssetDatabase.CreateAsset(textMaterial,folder+"/MeasurementText.mat");presenter.textMaterial=textMaterial;
            var light = new GameObject("Room Light").AddComponent<Light>(); light.transform.SetParent(root.transform); light.type = LightType.Directional;
            light.intensity = 1.5f; light.transform.rotation = Quaternion.Euler(45, -25, 0);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat; RenderSettings.ambientLight = new Color(.5f, .5f, .5f);
            root.SetActive(true);
            string path = folder + (desktop ? "/VirtualHand_PC.unity" : "/VirtualHand_VR.unity");
            EditorSceneManager.SaveScene(scene, path); AssetDatabase.SaveAssets(); Selection.activeGameObject = root;
            Debug.Log("Created virtual hand experiment: " + path);
            return experiment;
        }
        static Material Lit(Color color)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (!shader) throw new InvalidOperationException("A URP project is required.");
            return new Material(shader) { color = color };
        }
        static GameObject Primitive(string name, PrimitiveType type, Transform parent, Vector3 position, Vector3 scale, Material material)
        {
            var go = GameObject.CreatePrimitive(type); go.name = name; go.transform.SetParent(parent, false);
            go.transform.localPosition = position; go.transform.localScale = scale; go.GetComponent<Renderer>().sharedMaterial = material; return go;
        }
        static Transform[] CreateLandmarks(TabletopMeltHand hand)
        {
            var mesh = hand.surface.sharedMesh; var bounds = mesh.bounds;
            var collider = hand.gameObject.AddComponent<MeshCollider>(); collider.sharedMesh = mesh;
            // The root is inactive while setup runs, so intersect triangles directly rather than using Physics.
            var vertices = mesh.vertices; var triangles = mesh.triangles;
            var points = new[] { new Vector2(0, 0), new Vector2(0, .05f), new Vector2(.03f, .005f) };
            var result = new Transform[3];
            for (int i = 0; i < 3; i++)
            {
                var origin = new Vector3(points[i].x, bounds.max.y + .1f, points[i].y);
                float highest = float.NegativeInfinity;
                for (int t = 0; t < triangles.Length; t += 3)
                {
                    Vector3 a = vertices[triangles[t]], b = vertices[triangles[t + 1]], c = vertices[triangles[t + 2]];
                    Vector3 u = b - a, v = c - a;
                    float determinant = u.x * v.z - u.z * v.x;
                    if (Mathf.Abs(determinant) < 1e-9f) continue;
                    float x = origin.x - a.x, z = origin.z - a.z;
                    float s = (x * v.z - z * v.x) / determinant, r = (u.x * z - u.z * x) / determinant;
                    if (s >= 0 && r >= 0 && s + r <= 1) highest = Mathf.Max(highest, a.y + s * u.y + r * v.y);
                }
                if (!float.IsFinite(highest)) highest = bounds.max.y;
                var landmark = new GameObject("Hand landmark " + (i + 1)).transform; landmark.SetParent(hand.transform, false);
                landmark.localPosition = new Vector3(origin.x, highest, origin.z); result[i] = landmark;
            }
            UnityEngine.Object.DestroyImmediate(collider); return result;
        }
        public static Mesh BakeSelected(Renderer renderer)
        {
            Mesh mesh;
            if (renderer is SkinnedMeshRenderer skinned) { mesh = new Mesh(); skinned.BakeMesh(mesh); }
            else
            {
                var filter = renderer.GetComponent<MeshFilter>();
                if (!filter || !filter.sharedMesh || !filter.sharedMesh.isReadable) throw new InvalidOperationException("A readable hand mesh is required.");
                mesh = UnityEngine.Object.Instantiate(filter.sharedMesh);
            }
            if (mesh.subMeshCount != 1) { UnityEngine.Object.DestroyImmediate(mesh); throw new InvalidOperationException("Select a single-material hand mesh; split unrelated body geometry first."); }
            var vertices = mesh.vertices; var matrix = renderer.transform.localToWorldMatrix;
            var bounds = new Bounds(matrix.MultiplyPoint3x4(vertices[0]), Vector3.zero);
            for (int i = 0; i < vertices.Length; i++) { vertices[i] = matrix.MultiplyPoint3x4(vertices[i]); bounds.Encapsulate(vertices[i]); }
            Vector3 offset = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            for (int i = 0; i < vertices.Length; i++) vertices[i] -= offset;
            mesh.vertices = vertices; mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds(); return mesh;
        }
        public static Mesh CreateExampleHand()
        {
            // Original procedural mannequin geometry, not a copy of fp_male_hand or another model.
            var vertices = new List<Vector3>(); var uv = new List<Vector2>(); var indices = new List<int>();
            void Ellipsoid(Vector3 center, Vector3 radius, float angle = 0)
            {
                const int rings = 12, segments = 20; int first = vertices.Count; Quaternion rotation = Quaternion.Euler(0, angle, 0);
                for (int r = 0; r <= rings; r++) for (int s = 0; s <= segments; s++)
                {
                    float latitude = Mathf.PI * r / rings, longitude = 2 * Mathf.PI * s / segments;
                    var p = new Vector3(Mathf.Sin(latitude) * Mathf.Cos(longitude), Mathf.Cos(latitude), Mathf.Sin(latitude) * Mathf.Sin(longitude));
                    vertices.Add(center + rotation * Vector3.Scale(p, radius)); uv.Add(new Vector2((float)s / segments, (float)r / rings));
                }
                for (int r = 0; r < rings; r++) for (int s = 0; s < segments; s++)
                {
                    int a = first + r * (segments + 1) + s, b = a + segments + 1;
                    indices.Add(a); indices.Add(a + 1); indices.Add(b); indices.Add(a + 1); indices.Add(b + 1); indices.Add(b);
                }
            }
            Ellipsoid(new Vector3(0, .021f, -.095f), new Vector3(.03f, .021f, .09f));
            Ellipsoid(new Vector3(0, .023f, .02f), new Vector3(.043f, .023f, .06f));
            Ellipsoid(new Vector3(-.049f, .018f, .02f), new Vector3(.013f, .014f, .041f), -40);
            float[] xs = { -.03f, -.009f, .013f, .032f }, lengths = { .075f, .09f, .082f, .065f };
            for (int i = 0; i < 4; i++) Ellipsoid(new Vector3(xs[i], .016f, .06f + lengths[i] / 2), new Vector3(.0105f - i * .0005f, .013f, lengths[i] / 2 + .008f), 4 - i * 3);
            var mesh = new Mesh { name = "Procedural example hand" }; mesh.SetVertices(vertices); mesh.SetUVs(0, uv); mesh.SetTriangles(indices, 0);
            mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds(); return mesh;
        }
    }
}
