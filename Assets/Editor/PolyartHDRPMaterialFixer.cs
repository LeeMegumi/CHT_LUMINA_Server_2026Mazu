// -----------------------------------------------------------------------------
//  PolyartHDRPMaterialFixer
//
//  將 Polyart "Dreamscape" 套件中殘留的 URP / Built-in 材質轉換到 HDRP。
//
//  背景：本專案為純 HDRP（com.unity.render-pipelines.high-definition），
//  但 Polyart 套件內有幾個手寫 shader 是 Amplify Shader Editor 的 URP 模板
//  （Tags{"RenderPipeline"="UniversalPipeline"}），以及一個已遺失的 shader 與
//  一個 Built-in RP 內建 shader。這些在 HDRP 下都會編譯失敗 → 材質變粉紅色。
//
//  用法：Unity 選單 Tools / Polyart HDRP Fixer / ...
//        先跑「1. Report (dry run)」看報告，再跑「2. Convert」實際轉換。
//
//  轉換前請先確認專案已提交版控（材質檔會被就地改寫）。
// -----------------------------------------------------------------------------

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class PolyartHDRPMaterialFixer
{
    // ---- 目標 shader 名稱 -----------------------------------------------
    const string ShaderWingFlap = "Polyart/Dreamscape/HDRP/PA_WingFlap_HDRP";
    const string ShaderDust     = "Polyart/Dreamscape/HDRP/PA_Dust_HDRP";
    const string ShaderHDLit    = "HDRP/Lit";

    // ---- 需要處理的資產 GUID --------------------------------------------
    const string GuidButterflyBlue = "9113898edcd5182419fc96635423fd5f";
    const string GuidButterflyRed  = "28b56d09e4f42fd42b260776b3bd77f1";
    const string GuidCrow          = "a5788d3111a59814bb33e23b892bf657";
    const string GuidDustGray      = "d0d0a721218ab264ba117ae600eaa275";
    const string GuidLeafTower     = "f5f1ee502c224964f916bf1c071edd53";
    const string GuidLeavesPlanes1 = "fc2c3284ca4806045ab897900015a8f2";
    const string GuidLeavesPlanes2 = "982aa43330b06a84094f62b85491ed7c";
    const string GuidImpostorMat   = "0fcb8ec3c7c5bda41ac3d576ce368432";
    const string GuidLeavesGraph   = "e9916bdfa0455554ca20566244ee9a1e"; // Leaves.shadergraph

    // =====================================================================
    //  選單
    // =====================================================================

    [MenuItem("Tools/Polyart HDRP Fixer/1. Report (dry run)", priority = 0)]
    public static void Report() { Run(dryRun: true, includeImpostor: false); }

    [MenuItem("Tools/Polyart HDRP Fixer/2. Convert Effect + Foliage Materials", priority = 1)]
    public static void Convert()
    {
        if (!EditorUtility.DisplayDialog(
                "Polyart HDRP Fixer",
                "即將就地改寫 7 個材質檔的 shader 指向與參數。\n" +
                "建議先確認專案已提交版控。\n\n要繼續嗎？",
                "轉換", "取消"))
            return;
        Run(dryRun: false, includeImpostor: false);
    }

    [MenuItem("Tools/Polyart HDRP Fixer/3. Convert Impostor Material (stop-gap)", priority = 2)]
    public static void ConvertImpostor()
    {
        if (!EditorUtility.DisplayDialog(
                "Polyart HDRP Fixer",
                "Impostor 材質會被改指到 HDRP/Lit。\n" +
                "這只是止血：原本 URP shader 的八面體 impostor 取樣邏輯不會被保留，\n" +
                "樹的遠景替身會變成單純的貼圖平面。\n\n要繼續嗎？",
                "轉換", "取消"))
            return;
        Run(dryRun: false, includeImpostor: true, onlyImpostor: true);
    }

    // =====================================================================
    //  主流程
    // =====================================================================

    static void Run(bool dryRun, bool includeImpostor, bool onlyImpostor = false)
    {
        var log = new StringBuilder();
        log.AppendLine(dryRun ? "=== Polyart HDRP Fixer — REPORT (未修改任何檔案) ==="
                              : "=== Polyart HDRP Fixer — CONVERT ===");

        int done = 0, skipped = 0;

        if (!onlyImpostor)
        {
            foreach (var guid in new[] { GuidButterflyBlue, GuidButterflyRed, GuidCrow })
                Step(ref done, ref skipped, log, guid, dryRun, ConvertWingFlap);

            Step(ref done, ref skipped, log, GuidDustGray,  dryRun, ConvertDust);
            Step(ref done, ref skipped, log, GuidLeafTower, dryRun, ConvertLeafTower);

            foreach (var guid in new[] { GuidLeavesPlanes1, GuidLeavesPlanes2 })
                Step(ref done, ref skipped, log, guid, dryRun, ConvertLeavesPlane);
        }

        if (includeImpostor)
            Step(ref done, ref skipped, log, GuidImpostorMat, dryRun, ConvertImpostorMat);

        if (!dryRun)
        {
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        log.AppendLine($"--- 完成 {done} 個，略過 {skipped} 個 ---");
        Debug.Log(log.ToString());
    }

    static void Step(ref int done, ref int skipped, StringBuilder log,
                     string guid, bool dryRun, Func<Material, bool, string> action)
    {
        var path = AssetDatabase.GUIDToAssetPath(guid);
        if (string.IsNullOrEmpty(path))
        {
            log.AppendLine($"[跳過] 找不到 GUID {guid} 對應的資產");
            skipped++;
            return;
        }

        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            log.AppendLine($"[跳過] {path} 不是材質");
            skipped++;
            return;
        }

        string before = mat.shader != null ? mat.shader.name : "<missing shader>";
        string result;
        try { result = action(mat, dryRun); }
        catch (Exception e) { result = "錯誤：" + e.Message; }

        if (result == null) { skipped++; log.AppendLine($"[跳過] {mat.name}"); return; }

        if (!dryRun)
        {
            EditorUtility.SetDirty(mat);
            done++;
        }
        else done++;

        log.AppendLine($"[{(dryRun ? "預覽" : "已轉換")}] {mat.name}");
        log.AppendLine($"        路徑 : {path}");
        log.AppendLine($"        原本 : {before}");
        log.AppendLine($"        改為 : {result}");
    }

    // =====================================================================
    //  各類材質的轉換規則
    // =====================================================================

    // 蝴蝶 / 烏鴉：原 URP Amplify shader "Polyart/Dreamscape/Builtin/Particles/Wing Flap"
    static string ConvertWingFlap(Material mat, bool dryRun)
    {
        var shader = Shader.Find(ShaderWingFlap);
        if (shader == null) return MissingShader(ShaderWingFlap);

        var tex   = ReadTextures(mat);
        var cols  = ReadColors(mat);
        var flts  = ReadFloats(mat);

        if (dryRun) return shader.name;

        mat.shader = shader;

        SetTex(mat, "_ColorMap",    Pick(tex, "_ColorMap", "_MainTex", "_BaseMap", "_Butterfly"));
        SetTex(mat, "_GradientMap", Pick(tex, "_GradientMap"));

        var tint = PickColor(cols, Color.white, "_ColorTint", "_Color", "_BaseColor");
        tint.a = 1f;                                  // 原檔 alpha 為 0，會讓 tint 失真
        SetCol(mat, "_ColorTint", tint);

        SetFlt(mat, "_FlapFrequency", PickFloat(flts, 12f,   "_FlapFrequency"));
        SetFlt(mat, "_Intensity",     PickFloat(flts, 0.02f, "_Intensity"));
        SetFlt(mat, "_Smoothness",    PickFloat(flts, 0.3f,  "_Smoothness", "_Glossiness"));
        SetFlt(mat, "_AlphaCutoff",   PickFloat(flts, 0.5f,  "_AlphaCutoff", "_Cutoff"));

        ValidateHD(mat);
        return shader.name;
    }

    // 塵埃：原本指向一個專案裡根本不存在的 URP 粒子 shader（missing shader）
    static string ConvertDust(Material mat, bool dryRun)
    {
        var shader = Shader.Find(ShaderDust);
        if (shader == null) return MissingShader(ShaderDust);

        var tex  = ReadTextures(mat);
        var cols = ReadColors(mat);

        if (dryRun) return shader.name;

        mat.shader = shader;

        SetTex(mat, "_ColorMap",
               Pick(tex, "_BaseMap", "_MainTex", "_T_Dust_Particle_01", "_ColorMap"));

        // 原材質是 unlit + emission 驅動，所以用 _EmissionColor 當作可見顏色
        var tint = PickColor(cols, Color.white, "_EmissionColor", "_BaseColor", "_Color");
        tint.a = PickColor(cols, Color.white, "_BaseColor", "_Color").a;
        SetCol(mat, "_ColorTint", tint);
        SetFlt(mat, "_Intensity", 1f);

        mat.renderQueue = -1;
        ValidateHD(mat);
        return shader.name;
    }

    // 落葉：原本指向 Built-in RP 的內建 shader（unity_builtin_extra）
    static string ConvertLeafTower(Material mat, bool dryRun)
    {
        var shader = Shader.Find(ShaderHDLit);
        if (shader == null) return MissingShader(ShaderHDLit);

        var tex  = ReadTextures(mat);
        var cols = ReadColors(mat);
        var flts = ReadFloats(mat);

        if (dryRun) return shader.name + " (cutout, double sided)";

        mat.shader = shader;

        SetTex(mat, "_BaseColorMap", Pick(tex, "_MainTex", "_BaseMap", "_Leaf"));
        SetTex(mat, "_NormalMap",    Pick(tex, "_BumpMap"));
        SetCol(mat, "_BaseColor",    PickColor(cols, Color.white, "_Color", "_BaseColor"));

        SetFlt(mat, "_AlphaCutoffEnable", 1f);
        SetFlt(mat, "_AlphaCutoff",       PickFloat(flts, 0.625f, "_Cutoff", "_AlphaCutoff"));
        SetFlt(mat, "_DoubleSidedEnable", 1f);   // 葉片平面需要雙面
        SetFlt(mat, "_Smoothness",        PickFloat(flts, 0.043f, "_Glossiness", "_Smoothness"));
        SetFlt(mat, "_Metallic",          0f);
        if (Pick(tex, "_BumpMap") != null) SetFlt(mat, "_NormalScale", PickFloat(flts, 1f, "_BumpScale"));

        mat.renderQueue = -1;
        ValidateHD(mat);
        return shader.name + " (cutout, double sided)";
    }

    // 兩個葉面材質：指向一個已被刪除的 shadergraph GUID，屬性欄位與 Leaves.shadergraph 完全吻合
    static string ConvertLeavesPlane(Material mat, bool dryRun)
    {
        var graphPath = AssetDatabase.GUIDToAssetPath(GuidLeavesGraph);
        var shader = string.IsNullOrEmpty(graphPath)
            ? null
            : AssetDatabase.LoadAssetAtPath<Shader>(graphPath);

        if (shader == null) return "找不到 Leaves.shadergraph（GUID " + GuidLeavesGraph + "）";
        if (dryRun) return shader.name;

        // 屬性名稱一致，指派 shader 時 Unity 會自動保留同名欄位
        mat.shader = shader;
        mat.renderQueue = -1;
        ValidateHD(mat);
        return shader.name;
    }

    // Impostor：止血用，不保留原本的八面體 impostor 取樣
    static string ConvertImpostorMat(Material mat, bool dryRun)
    {
        var shader = Shader.Find(ShaderHDLit);
        if (shader == null) return MissingShader(ShaderHDLit);

        var tex  = ReadTextures(mat);
        var flts = ReadFloats(mat);

        if (dryRun) return shader.name + " (stop-gap)";

        mat.shader = shader;
        SetTex(mat, "_BaseColorMap", Pick(tex, "_MainTex", "_BaseMap"));
        SetTex(mat, "_NormalMap",    Pick(tex, "_NormalMap", "_BumpMap"));
        SetFlt(mat, "_AlphaCutoffEnable", 1f);
        SetFlt(mat, "_AlphaCutoff",       PickFloat(flts, 0.5f, "_Cutoff", "_AlphaCutoff"));
        SetFlt(mat, "_DoubleSidedEnable", 1f);
        SetFlt(mat, "_Smoothness",        PickFloat(flts, 0.1f, "_Smoothness", "_Glossiness"));
        SetFlt(mat, "_Metallic",          PickFloat(flts, 0f,   "_Metallic"));

        mat.renderQueue = -1;
        ValidateHD(mat);
        return shader.name + " (stop-gap)";
    }

    // =====================================================================
    //  讀取舊材質的序列化欄位
    //  （直接讀 YAML 欄位，所以即使 shader 已遺失／編譯失敗也讀得到）
    // =====================================================================

    static Dictionary<string, Texture> ReadTextures(Material mat)
    {
        var result = new Dictionary<string, Texture>();
        var so = new SerializedObject(mat);
        var arr = so.FindProperty("m_SavedProperties.m_TexEnvs");
        if (arr == null) return result;

        for (int i = 0; i < arr.arraySize; i++)
        {
            var e = arr.GetArrayElementAtIndex(i);
            var name = e.FindPropertyRelative("first").stringValue;
            var texProp = e.FindPropertyRelative("second.m_Texture");
            if (!string.IsNullOrEmpty(name) && texProp != null)
                result[name] = texProp.objectReferenceValue as Texture;
        }
        return result;
    }

    static Dictionary<string, Color> ReadColors(Material mat)
    {
        var result = new Dictionary<string, Color>();
        var so = new SerializedObject(mat);
        var arr = so.FindProperty("m_SavedProperties.m_Colors");
        if (arr == null) return result;

        for (int i = 0; i < arr.arraySize; i++)
        {
            var e = arr.GetArrayElementAtIndex(i);
            var name = e.FindPropertyRelative("first").stringValue;
            var v = e.FindPropertyRelative("second");
            if (!string.IsNullOrEmpty(name) && v != null)
                result[name] = v.colorValue;
        }
        return result;
    }

    static Dictionary<string, float> ReadFloats(Material mat)
    {
        var result = new Dictionary<string, float>();
        var so = new SerializedObject(mat);
        var arr = so.FindProperty("m_SavedProperties.m_Floats");
        if (arr == null) return result;

        for (int i = 0; i < arr.arraySize; i++)
        {
            var e = arr.GetArrayElementAtIndex(i);
            var name = e.FindPropertyRelative("first").stringValue;
            var v = e.FindPropertyRelative("second");
            if (!string.IsNullOrEmpty(name) && v != null)
                result[name] = v.floatValue;
        }
        return result;
    }

    // =====================================================================
    //  小工具
    // =====================================================================

    static Texture Pick(Dictionary<string, Texture> d, params string[] keys)
    {
        foreach (var k in keys)
            if (d.TryGetValue(k, out var t) && t != null) return t;
        return null;
    }

    static Color PickColor(Dictionary<string, Color> d, Color fallback, params string[] keys)
    {
        foreach (var k in keys)
            if (d.TryGetValue(k, out var c)) return c;
        return fallback;
    }

    static float PickFloat(Dictionary<string, float> d, float fallback, params string[] keys)
    {
        foreach (var k in keys)
            if (d.TryGetValue(k, out var f)) return f;
        return fallback;
    }

    static void SetTex(Material m, string name, Texture t)
    {
        if (t != null && m.HasProperty(name)) m.SetTexture(name, t);
    }

    static void SetCol(Material m, string name, Color c)
    {
        if (m.HasProperty(name)) m.SetColor(name, c);
    }

    static void SetFlt(Material m, string name, float f)
    {
        if (m.HasProperty(name)) m.SetFloat(name, f);
    }

    static string MissingShader(string name)
        => $"找不到 shader「{name}」——請確認 .shadergraph 已匯入且沒有編譯錯誤";

    /// <summary>
    /// 呼叫 HDMaterial.ValidateMaterial()，讓 HDRP 重新設定關鍵字、render queue、
    /// stencil 等等。用反射呼叫，避免此腳本在沒有 HDRP 的環境下編譯失敗。
    /// </summary>
    static void ValidateHD(Material mat)
    {
        var type = Type.GetType(
            "UnityEngine.Rendering.HighDefinition.HDMaterial, Unity.RenderPipelines.HighDefinition.Runtime");
        var method = type?.GetMethod("ValidateMaterial",
            BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Material) }, null);
        method?.Invoke(null, new object[] { mat });
    }
}
#endif
