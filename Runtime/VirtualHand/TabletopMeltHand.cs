using UnityEngine;

namespace AvatarExperiments.VirtualHand
{
    /// <summary>Common tabletop deformation for a user-supplied static hand mesh and optional surface materials.</summary>
    public sealed class TabletopMeltHand:MonoBehaviour
    {
        public MeshFilter surface;
        public MeshRenderer skin;
        public Transform tabletop;
        public Material viscousMaterial,meltingTestMaterial,meltShaderMaterial;
        [Header("Spread on tabletop")]
        [Min(1)] public float finalWidthScale=2.25f;
        [Min(1)] public float finalLengthScale=1.55f;
        [Min(.001f)] public float finalThickness=.004f;
        [Min(.01f)] public float flowResponse=.22f;
        public float Amount{get;private set;}
        public MeltAppearance Appearance{get;private set;}
        Mesh original,runtime;
        Material originalMaterial,material;
        Vector3[] rest,positions,velocities;
        MaterialPropertyBlock block;

        public bool HasAppearance(MeltAppearance mode) => Source(mode) != null;
        Material Source(MeltAppearance mode) => mode==MeltAppearance.Viscous?viscousMaterial:mode==MeltAppearance.MeltingTest?meltingTestMaterial:meltShaderMaterial;
        public void ValidateConfiguration(MeltAppearance mode)
        {
            if(!surface||!surface.sharedMesh||!skin||!Source(mode))
                throw new System.InvalidOperationException("Assign a readable static hand mesh, renderer and material for the selected appearance.");
            if(!surface.sharedMesh.isReadable)throw new System.InvalidOperationException("Enable Read/Write on the hand mesh importer.");
            if(!float.IsFinite(finalWidthScale)||!float.IsFinite(finalLengthScale)||!float.IsFinite(finalThickness)||!float.IsFinite(flowResponse)||
                finalWidthScale<1||finalLengthScale<1||finalThickness<.001f||flowResponse<.01f)
                throw new System.InvalidOperationException("Invalid tabletop deformation settings.");
            if(Vector3.Distance(transform.lossyScale,Vector3.one)>.001f)
                throw new System.InvalidOperationException("Bake the hand at metre scale (1,1,1), local +Y upward.");
        }
        void Awake(){if(surface&&skin)Initialize();}
        void Initialize()
        {
            if(runtime)return;
            ValidateConfiguration(MeltAppearance.Viscous);
            original=surface.sharedMesh;originalMaterial=skin.sharedMaterial;
            runtime=Instantiate(original);runtime.name="Tabletop Hand (Runtime)";runtime.MarkDynamic();
            surface.sharedMesh=runtime;rest=original.vertices;positions=(Vector3[])rest.Clone();velocities=new Vector3[rest.Length];
            block=new MaterialPropertyBlock();Select(MeltAppearance.Viscous);
        }
        public void Select(MeltAppearance mode)
        {
            ValidateConfiguration(mode);
            if(!runtime){Initialize();}
            Appearance=mode;
            if(material)Destroy(material);
            Material source=Source(mode);
            material=new Material(source){name=source.name+" (Trial Runtime)"};skin.sharedMaterial=material;
            ResetShape();
        }
        public void ResetShape()
        {
            Amount=0;
            if(!runtime)return;
            System.Array.Copy(rest,positions,rest.Length);System.Array.Clear(velocities,0,velocities.Length);
            runtime.vertices=rest;runtime.normals=original.normals;runtime.tangents=original.tangents;
            // GPU displacement needs conservative CPU culling bounds.
            var bounds=original.bounds;bounds.Expand(.4f);runtime.bounds=bounds;
            ApplyProperties();
        }
        public void SetAmount(float value){Amount=Mathf.Clamp01(value);ApplyProperties();}
        void ApplyProperties()
        {
            if(!material)return;
            block.Clear();
            if(Appearance==MeltAppearance.Viscous)
            {
                block.SetFloat("_GeometryMeltAmount",0);block.SetFloat("_Viscosity",0);
                block.SetFloat("_SurfaceMeltAmount",Amount);
                if(material.HasProperty("_Smoothness"))block.SetFloat("_Smoothness",Mathf.Lerp(material.GetFloat("_Smoothness"),.92f,Amount));
                block.SetVector("_MeltAxisWS",transform.forward);block.SetVector("_MeltGravityWS",Vector3.down);
            }
            else if(Appearance==MeltAppearance.MeltingTest)
            {
                // This shader uses an OBJECT-SPACE height, not a 0..1 amount.
                // All appearances share the same table-constrained CPU surface.
                // Keep the original shaders/materials intact and avoid double deformation.
                block.SetFloat("_Melting",original.bounds.max.y+.04f);
                block.SetFloat("_NormalSpreadScale",0);
                block.SetFloat("_NoiseScale",0);block.SetFloat("_NoiseSpeed",0);
            }
            else
            {
                block.SetFloat("_MeltRate",.0001f);
                block.SetVector("_ObjectBasePos",transform.position);
                block.SetFloat("_ObjectHeight",Mathf.Max(.01f,original.bounds.max.y*transform.lossyScale.y));
                block.SetFloat("_ObjectMeltHeight",.001f);block.SetFloat("_SpreadPower",0);block.SetFloat("_TiltAngle",0);
            }
            skin.SetPropertyBlock(block);
        }
        void LateUpdate()
        {
            StepSurface(Time.unscaledDeltaTime);
        }
        public void StepSurface(float deltaTime)
        {
            if(!runtime||Amount<=0||deltaTime<=0)return;
            float dt=Mathf.Min(deltaTime,.033f);
            var bounds=original.bounds;
            float collapse=Mathf.SmoothStep(0,1,Amount);
            float flow=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.08f,1,Amount));
            float width=Mathf.Lerp(1,finalWidthScale,flow),length=Mathf.Lerp(1,finalLengthScale,flow);
            for(int i=0;i<rest.Length;i++)
            {
                Vector3 p=rest[i];
                Vector3 target=p;
                // Continuous, coherent squash/spread: keep a thin layer above the desk,
                // expand in BOTH desk axes, and preserve the same mapping at UV seams.
                float height=Mathf.InverseLerp(bounds.min.y,bounds.max.y,p.y);
                target.x=bounds.center.x+(p.x-bounds.center.x)*width;
                target.z=bounds.center.z+(p.z-bounds.center.z)*length;
                Vector3 world=transform.TransformPoint(target);
                float desk=tabletop?tabletop.position.y:0;
                world.y=Mathf.Lerp(world.y,desk+.001f+height*finalThickness,collapse);
                target=transform.InverseTransformPoint(world);
                positions[i]=Vector3.SmoothDamp(positions[i],target,ref velocities[i],flowResponse,float.PositiveInfinity,dt);
                world=transform.TransformPoint(positions[i]);world.y=Mathf.Max(desk,world.y);positions[i]=transform.InverseTransformPoint(world);
            }
            runtime.vertices=positions;runtime.RecalculateNormals();runtime.RecalculateTangents();runtime.RecalculateBounds();
        }
        void OnDestroy()
        {
            if(surface)surface.sharedMesh=original;
            if(skin){skin.sharedMaterial=originalMaterial;skin.SetPropertyBlock(null);}
            if(runtime)Destroy(runtime);if(material)Destroy(material);
        }
    }
}
