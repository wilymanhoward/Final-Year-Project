// Smoother depth occlusion edges for FYP/OccludedProp and FYP/OccludedUI (user, 2026-11-04: "the cutout looks pixelated").
// The Quest depth texture is far lower resolution than the screen, so Meta's soft occlusion (one sample, ~6 depth pixel
// blend) still shows the depth pixels as steps. Here the same Meta soft-occlusion function
// (com.meta.xr.sdk.core/Shaders/EnvironmentDepth/EnvironmentOcclusion.cginc, CalculateEnvironmentDepthSoftOcclusion) is
// averaged over a centre + 4 rotated-grid taps, _FypOcclusionSoftness depth texels apart: wider, smoother edges, 5 samples
// instead of 1. Softness 0 = exactly Meta's own result. The global is set by DepthOcclusionMonitor from
// ExperimentConfig.occlusionSoftness. Include after EnvironmentOcclusionURP.hlsl.
#ifndef FYP_OCCLUSION_INCLUDED
#define FYP_OCCLUSION_INCLUDED

float _FypOcclusionSoftness;

float FypEnvironmentOcclusion(float3 worldPos, float bias)
{
#if defined(SOFT_OCCLUSION)
    // Same reprojection and bias as Meta's CalculateEnvironmentDepthOcclusion.
    const float4 depthSpace = mul(_EnvironmentDepthReprojectionMatrices[unity_StereoEyeIndex], float4(worldPos, 1.0));
    const float2 uv = (depthSpace.xy / depthSpace.w + 1.0f) * 0.5f;
    float linearDepth = (1.0f / ((depthSpace.z / depthSpace.w) + _EnvironmentDepthZBufferParams.y)) * _EnvironmentDepthZBufferParams.x;
    linearDepth -= bias * linearDepth * UNITY_NEAR_CLIP_VALUE;

    float centre = CalculateEnvironmentDepthSoftOcclusion(uv, linearDepth);
    if (_FypOcclusionSoftness <= 0.0f) return centre;
    const float2 o = _FypOcclusionSoftness * _PreprocessedEnvironmentDepthTexture_TexelSize.xy;
    float sum = 2.0f * centre;
    sum += CalculateEnvironmentDepthSoftOcclusion(uv + o * float2( 0.5f,  1.0f), linearDepth);
    sum += CalculateEnvironmentDepthSoftOcclusion(uv + o * float2(-1.0f,  0.5f), linearDepth);
    sum += CalculateEnvironmentDepthSoftOcclusion(uv + o * float2(-0.5f, -1.0f), linearDepth);
    sum += CalculateEnvironmentDepthSoftOcclusion(uv + o * float2( 1.0f, -0.5f), linearDepth);
    return sum / 6.0f;
#elif defined(HARD_OCCLUSION)
    return META_DEPTH_GET_OCCLUSION_VALUE_WORLDPOS(worldPos, bias);
#else
    return 1.0f;
#endif
}

#if defined(HARD_OCCLUSION) || defined(SOFT_OCCLUSION)
#define FYP_OCCLUDE_OUTPUT_PREMULTIPLY(input, output, zBias) output *= FypEnvironmentOcclusion(input.posWorld, zBias)
#else
#define FYP_OCCLUDE_OUTPUT_PREMULTIPLY(input, output, zBias) output = output
#endif

#endif
