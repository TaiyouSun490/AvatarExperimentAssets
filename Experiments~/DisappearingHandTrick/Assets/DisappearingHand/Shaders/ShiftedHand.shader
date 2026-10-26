Shader "DisappearingHand/ShiftedHand"
{
    Properties
    {
        [MainTexture] _BaseMap("Skin", 2D) = "white" {}
        _BaseColor("Tint", Color) = (1,1,1,1)
        _Visible("Visible", Float) = 1
        _Fade("Fade to black", Range(0,1)) = 1
        _Wrist("Physical wrist", Vector) = (0,0,0,0)
        _Forward("Hand forward", Vector) = (0,0,1,0)
        _Shift("Visual displacement", Vector) = (0,0,0,0)
        _Rotation("Visual rotation quaternion", Vector) = (0,0,0,1)
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
                float4 _BaseMap_ST, _BaseColor, _Wrist, _Forward, _Shift, _Rotation;
                float _Visible, _CuffLength, _Fade;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 positionCS:SV_POSITION;
                float3 rawWS:TEXCOORD0;
                float3 normalWS:TEXCOORD1;
                float2 uv:TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            float3 RotateVisual(float3 p)
            {
                return p + 2 * cross(_Rotation.xyz, cross(_Rotation.xyz, p) + _Rotation.w * p);
            }
            Varyings Vert(Attributes input)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.rawWS = TransformObjectToWorld(input.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(_Wrist.xyz + _Shift.xyz + RotateVisual(o.rawWS - _Wrist.xyz));
                o.normalWS = RotateVisual(TransformObjectToWorldNormal(input.normalOS));
                o.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return o;
            }
            half4 Frag(Varyings input):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                clip(_Visible - .5);
                // Remove the proximal arm without changing bones or tracking coordinates.
                clip(dot(input.rawWS - _Wrist.xyz, _Forward.xyz) + _CuffLength);
                Light light = GetMainLight();
                half3 normal = normalize(input.normalWS);
                half3 illumination = half3(.47,.49,.52) + light.color * saturate(dot(normal, light.direction)) * .65;
                return half4(SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,input.uv).rgb * _BaseColor.rgb * illumination * _Fade, 1);
            }
            ENDHLSL
        }
        // No shadow/depth-only passes: a hidden hand must leave no shadow or depth silhouette.
    }
}
