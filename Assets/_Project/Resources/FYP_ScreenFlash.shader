// Full-view red edge vignette (ContaminationFlash). Positions are written straight to clip space, so it covers the
// whole view of each eye without a camera-attached mesh. Loaded with Resources.Load, hence this folder.
Shader "FYP/ScreenFlash"
{
    Properties
    {
        _Color ("Colour", Color) = (0.9, 0.0, 0.0, 1)
        _Intensity ("Intensity", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags { "Queue"="Overlay" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Intensity;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 ndc : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.ndc = v.uv * 2.0 - 1.0;
                o.positionCS = float4(o.ndc, 0.0, 1.0);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float edge = smoothstep(0.35, 1.25, length(i.ndc));
                return half4(_Color.rgb, edge * _Intensity);
            }
            ENDHLSL
        }
    }
}
