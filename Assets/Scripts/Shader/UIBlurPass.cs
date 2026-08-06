using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.Experimental.Rendering;

[System.Serializable]
public class UIBlurPass : CustomPass
{
    [Range(0f, 32f)] public float radius = 8f;
    [Range(2, 16)] public int sampleCount = 9;
    [Range(1, 4)] public int downSample = 2;

    RTHandle blurBuffer, tempBuffer;
    int currentDownSample = -1;

    static readonly int ID_Tex = Shader.PropertyToID("_UIBlurTexture");
    static readonly int ID_Scale = Shader.PropertyToID("_UIBlurTextureScale");

    RTHandle Alloc(string name, int ds) => RTHandles.Alloc(
        Vector2.one / ds, TextureXR.slices,
        dimension: TextureXR.dimension,
        colorFormat: GraphicsFormat.R16G16B16A16_SFloat,
        useDynamicScale: true, name: name);

    void AllocIfNeeded()
    {
        if (blurBuffer != null && currentDownSample == downSample) return;
        blurBuffer?.Release();
        tempBuffer?.Release();
        blurBuffer = Alloc("UIBlurBuffer", downSample);
        tempBuffer = Alloc("UIBlurTemp", downSample);
        currentDownSample = downSample;
    }

    protected override void Setup(ScriptableRenderContext renderContext, CommandBuffer cmd)
        => AllocIfNeeded();

    protected override void Execute(CustomPassContext ctx)
    {
        AllocIfNeeded();

        CustomPassUtils.GaussianBlur(
            ctx, ctx.cameraColorBuffer, blurBuffer, tempBuffer,
            sampleCount, radius);

        ctx.cmd.SetGlobalTexture(ID_Tex, blurBuffer);

        // 關鍵：把 RTHandle 的有效區域比例傳給 Shader
        var s = RTHandles.rtHandleProperties.rtHandleScale;
        ctx.cmd.SetGlobalVector(ID_Scale, new Vector4(s.x, s.y, 1f, 1f));
    }

    protected override void Cleanup()
    {
        blurBuffer?.Release();
        tempBuffer?.Release();
        blurBuffer = tempBuffer = null;
        currentDownSample = -1;
    }
}