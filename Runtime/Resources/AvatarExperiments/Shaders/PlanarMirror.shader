Shader "AvatarExperiments/URP/PlanarMirror"
{
    Properties
    {
        _MirrorLeft("Left eye", 2D) = "black" {}
        _MirrorRight("Right eye", 2D) = "black" {}
        _Tint("Reflection tint", Color) = (.943, 1.081, 1.058, 1)
        _EdgeColor("Snake mirror edge", Color) = (.08, .65, .5, .14)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Name "Mirror"
            Tags { "LightMode"="UniversalForward" }
            Cull Back ZWrite On ZTest LEqual
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MirrorLeft); SAMPLER(sampler_MirrorLeft);
            TEXTURE2D(_MirrorRight); SAMPLER(sampler_MirrorRight);
            CBUFFER_START(UnityPerMaterial)
                half4 _Tint, _EdgeColor;
            CBUFFER_END
            float4x4 _MirrorLeftVP, _MirrorRightVP;
            float _MirrorReady;
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float2 uv : TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };
            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.uv = input.uv;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half3 color = half3(.025, .045, .05);
                if(_MirrorReady > .5)
                {
                    bool right = unity_StereoEyeIndex != 0;
                    float4 clipPos = mul(right ? _MirrorRightVP : _MirrorLeftVP, float4(input.positionWS, 1));
                    float2 uv = clipPos.xy / clipPos.w * .5 + .5;
                    // GPU RT projection is Y-flipped on D3D; texture UV is top-origin.
                    #if UNITY_UV_STARTS_AT_TOP
                    uv.y = 1 - uv.y;
                    #endif
                    color = right ? SAMPLE_TEXTURE2D(_MirrorRight, sampler_MirrorRight, uv).rgb
                                  : SAMPLE_TEXTURE2D(_MirrorLeft, sampler_MirrorLeft, uv).rgb;
                    color *= _Tint.rgb;
                }
                float2 edgeUv = abs(input.uv * 2 - 1);
                half edge = saturate(max(edgeUv.x, edgeUv.y) * 5 - 4);
                return half4(lerp(color, _EdgeColor.rgb, edge * _EdgeColor.a), 1);
            }
            ENDHLSL
        }
        // The mirror must be present in the depth texture so real fluid behind
        // the panel cannot be composited over its reflected image.
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ColorMask R ZWrite On Cull Back
            HLSLPROGRAM
            #pragma vertex VertDepth
            #pragma fragment FragDepth
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct A { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };
            V VertDepth(A i) { V o=(V)0; UNITY_SETUP_INSTANCE_ID(i); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o); o.positionCS=TransformObjectToHClip(i.positionOS.xyz); return o; }
            half FragDepth(V i) : SV_Target { return i.positionCS.z; }
            ENDHLSL
        }
    }
}
