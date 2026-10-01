// Translucent hand with a brighter rim. Unlit, single-pass-instanced safe, URP.
Shader "FYP/GhostHand"
{
    Properties
    {
        _BodyColor ("Body Colour", Color) = (0.08, 0.07, 0.07, 1)
        _BodyAlpha ("Body Alpha", Range(0, 1)) = 0.3
        _OutlineColor ("Outline Colour", Color) = (1, 1, 1, 1)
        _OutlineWidth ("Outline Width", Range(0.02, 1)) = 0.2
        _OutlineSharpness ("Outline Sharpness", Range(0.5, 8)) = 1.5
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }

        // Depth prepass so overlapping fingers don't stack into a brighter blob.
        Pass
        {
            Name "DepthPrepass"
            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                return o;
            }

            half4 frag(Varyings i) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "Ghost"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BodyColor;
                half _BodyAlpha;
                half4 _OutlineColor;
                half _OutlineWidth;
                half _OutlineSharpness;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 viewDirWS : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

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
                float facing = saturate(dot(normalize(i.normalWS), normalize(i.viewDirWS)));
                // 1 at the silhouette edge, 0 where the surface faces the eye. The outline only covers the outer _OutlineWidth of that range.
                float edge = 1.0 - facing;
                half outline = pow(saturate((edge - (1.0 - _OutlineWidth)) / _OutlineWidth), _OutlineSharpness);
                half3 color = lerp(_BodyColor.rgb, _OutlineColor.rgb, outline);
                half alpha = lerp(_BodyAlpha, 1.0, outline);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
}
