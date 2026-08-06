#ifndef UI_BLUR_SAMPLE_INCLUDED
#define UI_BLUR_SAMPLE_INCLUDED

TEXTURE2D_X(_UIBlurTexture);
float4 _UIBlurTextureScale;

void SampleUIBlur_float(float2 ScreenUV, out float4 Out)
{
    float2 scale = _UIBlurTextureScale.xy;
    scale = (scale.x <= 0.0) ? float2(1.0, 1.0) : scale; // 保險：避免縮放未設定時整片塌掉

    float2 uv = clamp(ScreenUV * scale, 0.0, scale);
    Out = SAMPLE_TEXTURE2D_X_LOD(_UIBlurTexture, s_linear_clamp_sampler, uv, 0);
}
#endif