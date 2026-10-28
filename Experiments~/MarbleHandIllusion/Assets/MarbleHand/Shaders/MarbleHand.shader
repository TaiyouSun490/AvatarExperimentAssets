Shader "MarbleHand/SkinToStone"
{
    Properties
    {
        [MainTexture] _BaseMap("Skin", 2D) = "white" {}
        _Marble("Marble blend", Range(0,1)) = 0
        _Wrist("Wrist", Vector) = (0,0,0,0)
        _Forward("Hand forward", Vector) = (0,0,1,0)
        _CuffLength("Visible forearm length", Float) = .10
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForward" }
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST, _Wrist, _Forward;
                float _Marble, _CuffLength;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 positionCS:SV_POSITION; float3 world:TEXCOORD0; float3 normal:TEXCOORD1;
                float2 uv:TEXCOORD2; UNITY_VERTEX_OUTPUT_STEREO
            };
            Varyings Vert(Attributes input)
            {
                Varyings o; UNITY_SETUP_INSTANCE_ID(input); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.world = TransformObjectToWorld(input.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.world);
                o.normal = TransformObjectToWorldNormal(input.normalOS);
                o.uv = TRANSFORM_TEX(input.uv, _BaseMap); return o;
            }
            float Hash(float2 p) { return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453); }
            float Noise(float2 p)
            {
                float2 i=floor(p),f=frac(p); f=f*f*(3-2*f);
                return lerp(lerp(Hash(i),Hash(i+float2(1,0)),f.x),lerp(Hash(i+float2(0,1)),Hash(i+1),f.x),f.y);
            }
            half4 Frag(Varyings input):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                clip(dot(input.world-_Wrist.xyz,_Forward.xyz)+_CuffLength);
                // UV-anchored veins stay attached as the skin and fingers move.
                float2 p=input.uv*14;
                float warp=Noise(p*.8)*2+Noise(p*2.2)*.5+Noise(p*5.1)*.18;
                float veins=1-smoothstep(.02,.15,abs(sin(p.x*1.7+p.y*.8+warp*3)));
                float grain=Noise(p*18)*.025;
                half3 marble=lerp(half3(.84,.86,.83)+grain,half3(.20,.25,.25),veins*.65);
                half3 skin=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,input.uv).rgb;
                half3 normal=normalize(input.normal);
                Light light=GetMainLight();
                half3 diffuse=half3(.42,.45,.48)+light.color*saturate(dot(normal,light.direction))*.6;
                half3 view=normalize(GetWorldSpaceViewDir(input.world));
                float spec=pow(saturate(dot(normal,normalize(view+light.direction))),70)*.4*_Marble;
                return half4(lerp(skin,marble,_Marble)*diffuse+light.color*spec,1);
            }
            ENDHLSL
        }
    }
}
