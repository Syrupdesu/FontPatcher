// Loads the macOS builds of the Unifont font bundles inside this editor and
// exercises the full dynamic pipeline (LoadFromFile -> LoadAsset -> Awake ->
// on-demand glyph rasterization from the embedded source font), then reports.
//   Unity -batchmode -nographics -quit -projectPath <unity-project>
//     -executeMethod ValidateUnifontBundles.Run -logFile <log>
// Environment:
//   LCFP_OSX_OUT            directory containing the macOS bundles
//   LCFP_VALIDATION_REPORT  path of the report file to write
//   LCFP_HANZI_FILE         optional UTF-8 file with the full 8105-char table;
//                           when set, all characters are added and measured
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore;
using UnityEngine.TextCore.LowLevel;

public static class ValidateUnifontBundles
{
    public static void Run()
    {
        EditorApplication.Exit(RunInner());
    }

    static int RunInner()
    {
        try
        {
            string osxOut = Environment.GetEnvironmentVariable("LCFP_OSX_OUT");
            string reportPath = Environment.GetEnvironmentVariable("LCFP_VALIDATION_REPORT");
            string hanziFile = Environment.GetEnvironmentVariable("LCFP_HANZI_FILE");

            var report = new StringBuilder();
            bool allOk = true;

            // Control: run one layout BEFORE touching any bundle. TMP_Settings in this
            // project is incomplete (tar-extracted essentials), so isolate bundle-related
            // breakage from pre-existing state.
            try
            {
                var settings = TMP_Settings.instance;
                string leading;
                try { leading = TMP_Settings.leadingCharacters != null ? "ok" : "null"; }
                catch (System.Exception e) { leading = $"THROWS {e.GetType().Name}"; }
                Debug.Log($"[ValidateUnifontBundles] pre-bundle TMP_Settings={settings.name} leading={leading}");

                var go = new GameObject("pre-bundle-control");
                var probeText = go.AddComponent<TextMeshPro>();
                probeText.text = "布局控制 test123";
                string detail = TryForceLayout(probeText);
                UnityEngine.Object.DestroyImmediate(go);
                report.AppendLine($"CONTROL pre-bundle layout: {detail}");
                Debug.Log($"[ValidateUnifontBundles] {report}");
            }
            catch (System.Exception e)
            {
                report.AppendLine($"CONTROL pre-bundle layout EXCEPTION: {e.GetType().Name}: {e.Message}");
                Debug.LogException(e);
            }

            foreach (string bundleFile in Directory.GetFiles(osxOut))
            {
                if (bundleFile.EndsWith(".manifest")) continue;
                allOk &= ValidateBundle(bundleFile, hanziFile, report);
            }

            report.Insert(0, allOk ? "RESULT: PASS\n" : "RESULT: FAIL\n");
            string text = report.ToString();
            Debug.Log("[ValidateUnifontBundles] report:\n" + text);
            if (!string.IsNullOrEmpty(reportPath))
                File.WriteAllText(reportPath, text);
            return allOk ? 0 : 1;
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            return 1;
        }
    }

    static bool ValidateBundle(string bundleFile, string hanziFile, StringBuilder report)
    {
        report.AppendLine($"=== {Path.GetFileName(bundleFile)} ===");
        bool ok = true;

        AssetBundle bundle = AssetBundle.LoadFromFile(bundleFile);
        if (bundle == null)
        {
            report.AppendLine("FAIL: AssetBundle.LoadFromFile returned null");
            return false;
        }

        try
        {
            foreach (string kind in new[] { "Normal", "Transmit" })
            {
                TMP_FontAsset fa = bundle.LoadAsset<TMP_FontAsset>(kind);
                if (fa == null)
                {
                    report.AppendLine($"FAIL: LoadAsset<{kind}> returned null");
                    ok = false;
                    continue;
                }
                ok &= ValidateFontAsset(fa, kind, hanziFile, report);
            }
        }
        finally
        {
            bundle.Unload(true);
        }
        return ok;
    }

    static bool ValidateFontAsset(TMP_FontAsset fa, string kind, string hanziFile, StringBuilder report)
    {
        bool ok = true;

        ok &= Check(report, $"{kind} name", fa.name == kind, fa.name);
        ok &= Check(report, $"{kind} atlasPopulationMode==Dynamic", fa.atlasPopulationMode == AtlasPopulationMode.Dynamic,
                    fa.atlasPopulationMode.ToString());
        ok &= Check(report, $"{kind} multiAtlas enabled", fa.isMultiAtlasTexturesEnabled,
                    fa.isMultiAtlasTexturesEnabled.ToString());
        ok &= Check(report, $"{kind} sourceFontFile != null (embedded font data)", fa.sourceFontFile != null,
                    fa.sourceFontFile ? fa.sourceFontFile.name : "null");
        ok &= Check(report, $"{kind} material != null", fa.material != null,
                    fa.material ? fa.material.shader.name : "null");
        ok &= Check(report, $"{kind} material._MainTex == atlasTextures[0]",
                    fa.material != null && fa.material.GetTexture(ShaderUtilities.ID_MainTex) == fa.atlasTextures[0],
                    fa.material != null ? (fa.material.GetTexture(ShaderUtilities.ID_MainTex) == null ? "null!" : "ok") : "no material");

        Texture2D tex = fa.atlasTextures != null && fa.atlasTextures.Length > 0 ? fa.atlasTextures[0] : null;
        ok &= Check(report, $"{kind} atlas page 0 exists", tex != null, "null");
        if (tex != null)
        {
            ok &= Check(report, $"{kind} atlas page 0 size 1024x1024", tex.width == 1024 && tex.height == 1024,
                        $"{tex.width}x{tex.height}");
            report.AppendLine($"{kind} atlas filterMode: {tex.filterMode}");
        }

        FaceInfo fi = fa.faceInfo;
        report.AppendLine($"{kind} metrics: pointSize={fi.pointSize} ascent={fi.ascentLine} " +
                          $"descent={fi.descentLine} lineHeight={fi.lineHeight} (ratios " +
                          $"{fi.ascentLine / fi.pointSize:F3} / {fi.lineHeight / fi.pointSize:F3})");
        ok &= Check(report, $"{kind} ascent ratio ~0.800", Mathf.Abs(fi.ascentLine / fi.pointSize - 0.8f) < 0.01f,
                    (fi.ascentLine / fi.pointSize).ToString("F3"));
        ok &= Check(report, $"{kind} lineHeight ratio ~1.090", Mathf.Abs(fi.lineHeight / fi.pointSize - 1.09f) < 0.01f,
                    (fi.lineHeight / fi.pointSize).ToString("F3"));

        // TMP 3.0.7's SetupNewAtlasTexture has an editor-only branch that NREs on
        // bundle-loaded (persistent) assets and would also try AssetDatabase writes;
        // a player build compiles it out. Exercise the runtime path on an in-memory
        // copy instead, which is what the game effectively runs.
        TMP_FontAsset runtime = UnityEngine.Object.Instantiate(fa);
        UnityEngine.Object.DestroyImmediate(fa); // bundle contents stay valid; we no longer need the original
        var _ = runtime.atlasTexture; // populate TMP's cached first-page reference

        // --- Dynamic glyph generation, BMP sample (common + rare + fullwidth + symbols) ---
        string sample = "中文测试：常见简体字；生僻字（龘彟燚），全角ＡＢＣａｂｃ１２３，标点！？……——【】《》、·" +
                        "Latin 123 $ € ￥ mixed Hello 臺灣高雄 Cover全部简体" +
                        "露骨的麻将三缺一爵士庆典赢取勋章并非偶然";
        bool added = runtime.TryAddCharacters(sample, out string missing, false);
        ok &= Check(report, $"{kind} TryAddCharacters(BMP sample) all added", added && string.IsNullOrEmpty(missing),
                    $"ok={added} missing='{missing}'");
        int lookupCount = runtime.characterLookupTable.Count;
        report.AppendLine($"{kind} characterLookupTable count after BMP sample: {lookupCount}");

        // --- Non-BMP via uint[] overload (TMP text parsing feeds codepoints, not chars).
        // U+20164/U+235CB/U+28C4F/U+2CE93 are 通用规范汉字表 entries (CJK Ext B/F) covered
        // by Unifont; U+1F600 is absent from Unifont and must be reported missing.
        uint[] nonBmp = { 0x20164, 0x235CB, 0x28C4F, 0x2CE93 };
        bool nonBmpAdded = runtime.TryAddCharacters(nonBmp, out uint[] nonBmpMissing, false);
        ok &= Check(report, $"{kind} TryAddCharacters(non-BMP table chars planes 2/3) added", nonBmpAdded && (nonBmpMissing == null || nonBmpMissing.Length == 0),
                    $"ok={nonBmpAdded} missing=[{(nonBmpMissing == null ? "" : string.Join(" ", nonBmpMissing.Select(u => $"U+{u:X}")))}]");
        uint[] absent = { 0x1F600 };
        runtime.TryAddCharacters(absent, out uint[] absentMissing, false);
        ok &= Check(report, $"{kind} genuinely absent char reported missing (U+1F600)", absentMissing != null && absentMissing.Length == 1,
                    $"missing=[{(absentMissing == null ? "" : string.Join(" ", absentMissing.Select(u => $"U+{u:X}")))}]");

        // --- Optional: full 通用规范汉字表 (8105 chars) ---
        if (!string.IsNullOrEmpty(hanziFile))
        {
            uint[] all = ReadHanziFile(hanziFile);
            int batch = 500;
            var allMissing = new List<uint>();
            for (int i = 0; i < all.Length; i += batch)
            {
                uint[] slice = all.Skip(i).Take(batch).ToArray();
                if (!runtime.TryAddCharacters(slice, out uint[] miss, false))
                    allMissing.AddRange(miss ?? slice);
            }
            ok &= Check(report, $"{kind} full hanzi table ({all.Length} chars) all added", allMissing.Count == 0,
                        allMissing.Count == 0 ? "0 missing" : $"missing {allMissing.Count}: " +
                        string.Join(" ", allMissing.Take(50).Select(u => $"U+{u:X}")));
            report.AppendLine($"{kind} characterLookupTable count after full table: {runtime.characterLookupTable.Count}");
        }

        // --- Atlas usage / memory ---
        int pages = runtime.atlasTextures.Count(t => t != null);
        long texBytes = pages * 1024L * 1024L; // Alpha8 = 1 byte/texel
        report.AppendLine($"{kind} atlas pages in use: {pages} (Alpha8, ~{texBytes / 1024 / 1024} MB texture memory)");

        // --- Full text layout through the game's fallback path ---
        // A primary font with an emptied lookup table + our asset as its fallback drives
        // TextMeshProUGUI.SetArraySizes -> TMP_MaterialManager.GetFallbackMaterial(source,
        // target) with our material as target — the exact code path that NRE'd in-game
        // when _MainTex was serialized as null.
        var go = new GameObject("tmp-validate");
        // The mesh-based TextMeshPro component (not UGUI) works headless; it shares
        // TMP_Text.SetArraySizes / GetFallbackMaterial with the UGUI variant the game uses.
        try
        {
            var text = go.AddComponent<TextMeshPro>();
            string sampleText = "中文测试ＡＢＣ123 fallback-path";

            // Case A: our font as the text's own font (no fallback involved).
            text.font = runtime;
            text.text = sampleText;
            string detailA = TryForceLayout(text);
            ok &= Check(report, $"{kind} layout with our font as primary (ForceMeshUpdate)",
                        ParseChars(detailA) > 0, detailA);

            // Case B: empty primary font + our asset as fallback (the game's path).
            var primary = TMP_FontAsset.CreateFontAsset(runtime.sourceFontFile, 80, 8,
                GlyphRenderMode.RASTER, 512, 512, AtlasPopulationMode.Static);
            primary.name = "primary-empty";
            primary.characterLookupTable.Clear();
            primary.fallbackFontAssetTable = new List<TMP_FontAsset> { runtime };

            text.font = primary;
            text.text = sampleText;
            string detailB = TryForceLayout(text);
            ok &= Check(report, $"{kind} full layout via fallback (ForceMeshUpdate)",
                        ParseChars(detailB) > 0, detailB);

            // Informational: the mesh renderer's primary material (glyphs render via
            // submesh materials derived per font asset).
            MeshRenderer renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null && renderer.sharedMaterial != null)
            {
                report.AppendLine($"{kind} info: primary mesh material shader = {renderer.sharedMaterial.shader.name}");
            }

            UnityEngine.Object.DestroyImmediate(primary);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(go);
        }
        return ok;
    }

    static bool Check(StringBuilder report, string label, bool condition, string detail)
    {
        report.AppendLine($"{(condition ? "PASS" : "FAIL")}: {label} ({detail})");
        return condition;
    }

    static string TryForceLayout(TextMeshPro text)
    {
        try
        {
            text.ForceMeshUpdate(true, true);
            TMP_TextInfo info = text.textInfo;
            int fallbackChars = 0;
            for (int i = 0; i < info.characterCount; i++)
            {
                if (info.characterInfo[i].fontAsset != null && info.characterInfo[i].fontAsset != text.font)
                    fallbackChars++;
            }
            return $"chars={info.characterCount} meshVerts={text.mesh.vertexCount} viaFallback={fallbackChars}";
        }
        catch (System.Exception e)
        {
            return $"EXCEPTION {e.GetType().Name}: {e.Message}";
        }
    }

    static int ParseChars(string detail)
    {
        var m = System.Text.RegularExpressions.Regex.Match(detail ?? "", "chars=(\\d+)");
        return m.Success ? int.Parse(m.Groups[1].Value) : 0;
    }

    // Renders the text mesh orthographically on black and measures the fraction of the
    // glyph quads' bounding area covered by non-transparent pixels. Only strokes should
    // paint (bitmap coverage atlas); a solid quad means the atlas background leaks.
    static double RenderBoxCoverage(GameObject go)
    {
        MeshFilter mf = go.GetComponent<MeshFilter>();
        Mesh mesh = mf != null ? mf.sharedMesh : null;
        MeshRenderer renderer = go.GetComponent<MeshRenderer>();
        if (mesh == null || renderer == null || mesh.vertexCount == 0) return 0;

        var b = new Bounds();
        foreach (Vector3 p in mesh.vertices) b.Encapsulate(p);
        if (b.extents.sqrMagnitude < 1e-8f) return 0;

        int size = 256;
        var rt = new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32);
        var camGo = new GameObject("box-probe-cam");
        var cam = camGo.AddComponent<Camera>();
        cam.backgroundColor = Color.clear;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.orthographic = true;
        cam.targetTexture = rt;
        cam.aspect = 1f;
        float half = Mathf.Max(b.extents.x, b.extents.y) * 1.05f + 0.01f;
        cam.transform.position = b.center + Vector3.back * 10f;
        cam.orthographicSize = half;
        cam.Render();

        RenderTexture.active = rt;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.ReadPixels(new Rect(0, 0, size, size), 0, 0);
        tex.Apply();
        RenderTexture.active = null;
        cam.targetTexture = null;

        Color32[] px = tex.GetPixels32();
        int lit = 0;
        foreach (Color32 p in px)
            if (p.a > 8) lit++;

        UnityEngine.Object.DestroyImmediate(tex);
        UnityEngine.Object.DestroyImmediate(camGo);
        rt.Release();
        UnityEngine.Object.DestroyImmediate(rt);

        double quadFraction = (double)(b.extents.x * 2f) / (half * 2f) * (double)(b.extents.y * 2f) / (half * 2f);
        double litFraction = (double)lit / (size * size);
        return quadFraction > 0 ? litFraction / quadFraction : 0;
    }

    static uint[] ReadHanziFile(string path)
    {
        string text = File.ReadAllText(path);
        var set = new SortedSet<uint>();
        for (int i = 0; i < text.Length; i++)
        {
            if (char.IsWhiteSpace(text[i])) continue;
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                set.Add((uint)char.ConvertToUtf32(text[i], text[i + 1]));
                i++;
            }
            else
            {
                set.Add(text[i]);
            }
        }
        return set.ToArray();
    }
}
