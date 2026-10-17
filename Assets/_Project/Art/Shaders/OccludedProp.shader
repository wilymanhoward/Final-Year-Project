// FYP/OccludedProp: the shader of every prop / clue / floor / decal material (PropAssets), with Meta's environment depth
// occlusion (Depth API, user decision session 6): virtual pixels behind a real surface (e.g. the partner walking behind
// a desk) fade out so the passthrough shows. Look = URP Simple Lit with the palette texture and no specular/shadows
// (diffuse main light + ambient SH), or unlit (_Lighting = 0) for the hover shell, zone outline and blob shadow.
//
// Occlusion follows Meta's sample shaders in com.meta.xr.sdk.core/Shaders/EnvironmentDepth (OcclusionLit.shader,
// URP/OcclusionUnlit.shader): keywords HARD_OCCLUSION / SOFT_OCCLUSION (set globally by EnvironmentDepthManager),
// URP/EnvironmentOcclusionURP.hlsl, META_DEPTH_VERTEX_OUTPUT / META_DEPTH_INITIALIZE_VERTEX_OUTPUT /
// META_DEPTH_OCCLUDE_OUTPUT_PREMULTIPLY. The macro multiplies the whole colour (incl. alpha) by the visibility, so the
// output is premultiplied (Blend One OneMinusSrcAlpha) for opaque and transparent materials alike: an occluded pixel
// becomes (0,0,0,0) and the passthrough underneath shows.
// _EnvironmentDepthBias pulls the surface towards the viewer by that fraction of its distance (Vulkan), so things lying
// on the real floor (floor, decals, blob shadows) don't flicker against it.
Shader "FYP/OccludedProp"
{
    Properties
    {
        [MainTexture] _BaseMap("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        [MaterialToggle] _Lighting("Lit (diffuse main light + ambient)", Float) = 1
        _EnvironmentDepthBias("Environment Depth Bias", Float) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull", Float) = 2
        [MaterialToggle] _ZWrite("ZWrite", Float) = 1
    }

    SubShader
    {
        PackageRequirements { "com.unity.render-pipelines.universal": "14.0" }
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardOccluded"
            Tags { "LightMode" = "UniversalForward" }
            Blend One OneMinusSrcAlpha, One OneMinusSrcAlpha
            ZWrite [_ZWrite]
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ HARD_OCCLUSION SOFT_OCCLUSION
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.meta.xr.sdk.core/Shaders/EnvironmentDepth/URP/EnvironmentOcclusionURP.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                META_DEPTH_VERTEX_OUTPUT(2)
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _Lighting;
                float _EnvironmentDepthBias;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.vertex.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.normalWS = TransformObjectToWorldNormal(input.normal);
                META_DEPTH_INITIALIZE_VERTEX_OUTPUT(output, input.vertex.xyz);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 color = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;
                half3 n = normalize(input.normalWS);
                Light mainLight = GetMainLight();
                half3 lit = mainLight.color * saturate(dot(n, mainLight.direction)) + SampleSH(n);
                color.rgb *= lerp(half3(1, 1, 1), lit, _Lighting);
                color.rgb *= color.a;   // premultiplied

                META_DEPTH_OCCLUDE_OUTPUT_PREMULTIPLY(input, color, _EnvironmentDepthBias);
                return color;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
