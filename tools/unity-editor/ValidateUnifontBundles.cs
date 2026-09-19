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
        return ok;
    }

    static bool Check(StringBuilder report, string label, bool condition, string detail)
    {
        report.AppendLine($"{(condition ? "PASS" : "FAIL")}: {label} ({detail})");
        return condition;
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
