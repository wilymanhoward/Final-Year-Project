// Partner marker (PartnerMarker): translucent unlit colour drawn over virtual props (ZTest Always), so the partner stays
// visible behind a desk or display case without depth occlusion. Loaded with Resources.Load, hence this folder.
Shader "FYP/PartnerMarker"
{
    Properties
    {
        _Color ("Colour", Color) = (0.2, 0.85, 1.0, 0.45)
    }

    SubShader
    {
        Tags { "Queue"="Transparent+50" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Back
            // Each pixel only once (stencil bit 128 is free in URP forward; cleared every frame with the depth buffer):
            // overlapping fingers / palm read as one translucent layer instead of brighter patches.
            Stencil
            {
                Ref 128
                ReadMask 128
                WriteMask 128
                Comp NotEqual
                Pass Replace
            }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float3 viewDirWS : TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.viewDirWS = GetWorldSpaceNormalizeViewDir(positionWS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                // Brighter rim so the shape reads as a solid object, not a flat disc.
                half rim = 1.0 - saturate(dot(normalize(i.normalWS), normalize(i.viewDirWS)));
                return half4(_Color.rgb + rim * 0.4, saturate(_Color.a + rim * 0.4));
            }
            ENDHLSL
        }
    }
}
