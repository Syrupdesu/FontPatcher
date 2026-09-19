// Reproduces the in-game "solid box per CJK glyph" rendering through the exact
// fallback material path, and verifies the fix: renders a fallback-driven text mesh
// with the material TMP_MaterialManager actually derives, reads back the pixels and
// measures how much of each glyph quad is opaque. A coverage ratio near 1 means the
// whole quad (including padding) is painted (the box bug); a low ratio means only
// the glyph strokes are painted.
//   Unity -batchmode -quit -projectPath <proj> -executeMethod ProbeBoxRender.Run
// Environment:
//   LCFP_OSX_OUT   directory with the macOS bundles
//   LCFP_BOX_REPORT output file for the report
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;

public static class ProbeBoxRender
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
            string reportPath = Environment.GetEnvironmentVariable("LCFP_BOX_REPORT");
            string bundleFile = Path.Combine(osxOut, "00 zh");

            var sb = new StringBuilder();
            bool ok = true;

            AssetBundle bundle = AssetBundle.LoadFromFile(bundleFile);
            TMP_FontAsset fa = bundle != null ? bundle.LoadAsset<TMP_FontAsset>("Normal") : null;
            if (fa == null)
            {
                Console.Error.WriteLine("bundle/Normal load failed");
                return 1;
            }
            TMP_FontAsset runtime = UnityEngine.Object.Instantiate(fa);
            var _ = runtime.atlasTexture;
            // Match the validation flow: populate the glyph before layout (the game adds
            // glyphs the same way on demand; the material derivation is identical).
            runtime.TryAddCharacters("中", out string missing0, false);

            // Same fallback construction as the game: empty static primary + our asset.
            var primary = TMP_FontAsset.CreateFontAsset(runtime.sourceFontFile, 80, 8,
                UnityEngine.TextCore.LowLevel.GlyphRenderMode.RASTER, 512, 512,
                AtlasPopulationMode.Static);
            primary.characterLookupTable.Clear();
            primary.fallbackFontAssetTable = new List<TMP_FontAsset> { runtime };

            var go = new GameObject("box-probe");
            var text = go.AddComponent<TextMeshPro>();
            text.font = primary;
            text.fontSize = 80;
            text.text = "中";
            text.ForceMeshUpdate(true, true);
            sb.AppendLine($"textInfo.characterCount: {text.textInfo.characterCount}");

            MeshRenderer renderer = go.GetComponent<MeshRenderer>();
            Material derived = renderer != null ? renderer.sharedMaterial : null;
            sb.AppendLine($"derived material: {(derived != null ? derived.name : "null")}");
            sb.AppendLine($"derived shader: {(derived && derived.shader ? derived.shader.name : "null")}");
            sb.AppendLine($"derived _GradientScale: {(derived && derived.HasProperty(ShaderUtilities.ID_GradientScale) ? derived.GetFloat(ShaderUtilities.ID_GradientScale).ToString() : "NO PROPERTY")}");
            MeshFilter mf0 = go.GetComponent<MeshFilter>();
            sb.AppendLine($"mesh: {(mf0 && mf0.sharedMesh ? $"{mf0.sharedMesh.vertexCount} verts, bounds={mf0.sharedMesh.bounds.extents}" : "null")}");
            sb.AppendLine($"renderer bounds: {(renderer != null ? renderer.bounds.extents.ToString() : "null")}");

            double coverage = RenderGlyphCoverage(go, renderer, sb, out string pngPath);
            sb.AppendLine($"rendered frame: {pngPath}");
            sb.AppendLine($"glyph quad opaque coverage: {coverage:F3}");
            // Crisp bitmap strokes fill well under half of the quad; a full box fills ~all of it.
            bool noBox = coverage < 0.60;
            sb.AppendLine($"{(noBox ? "PASS" : "FAIL")}: glyph quad coverage {(noBox ? "< 0.60 (no box)" : ">= 0.60 (BOX!)")}");
            ok &= noBox;

            UnityEngine.Object.DestroyImmediate(go);
            UnityEngine.Object.DestroyImmediate(primary);
            bundle.Unload(true);

            string report = (ok ? "RESULT: PASS\n" : "RESULT: FAIL\n") + sb.ToString();
            Debug.Log("[ProbeBoxRender]\n" + report);
            if (!string.IsNullOrEmpty(reportPath))
                File.WriteAllText(reportPath, report);
            return ok ? 0 : 1;
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            return 1;
        }
    }

    // Renders the text mesh with a solid color on black, orthographic camera tightly
    // framing the mesh bounds, and returns the fraction of the mesh's bounding rect
    // covered by non-black pixels.
    static double RenderGlyphCoverage(GameObject go, MeshRenderer renderer, StringBuilder sb, out string pngPath)
    {
        pngPath = "";
        MeshFilter mf = go.GetComponent<MeshFilter>();
        Mesh mesh = mf != null ? mf.sharedMesh : null;
        if (mesh == null || mesh.vertexCount == 0) return 0;

        // Sample the atlas page 0 background via a raw GPU-side copy (blitting an
        // Alpha8 texture produces bogus RGBA). The shipped page must be all-zero;
        // glyph pixels added at runtime become 255.
        Texture2D atlas = renderer.sharedMaterial != null
            ? renderer.sharedMaterial.GetTexture(ShaderUtilities.ID_MainTex) as Texture2D
            : null;
        if (atlas != null)
        {
            var readable = new Texture2D(atlas.width, atlas.height, TextureFormat.Alpha8, false);
            Graphics.CopyTexture(atlas, readable);
            byte[] raw = readable.GetRawTextureData();
            var counts = new System.Collections.Generic.Dictionary<int, int>();
            foreach (byte val in raw)
            {
                counts.TryGetValue(val, out int n);
                counts[val] = n + 1;
            }
            var top = new System.Collections.Generic.List<(int val, int count)>();
            foreach (var kv in counts) top.Add((kv.Key, kv.Value));
            top.Sort((a, b2) => b2.count.CompareTo(a.count));
            var parts = new System.Collections.Generic.List<string>();
            for (int i = 0; i < Math.Min(4, top.Count); i++)
                parts.Add($"alpha={top[i].val} x{top[i].count}");
            sb.AppendLine($"atlas content (raw copy, {atlas.width}x{atlas.height}): {string.Join(", ", parts)}");
            UnityEngine.Object.DestroyImmediate(readable);
        }

        int size = 256;
        var rt = new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32);
        var camGo = new GameObject("cam");
        var cam = camGo.AddComponent<Camera>();
        cam.backgroundColor = Color.black;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.orthographic = true;
        cam.targetTexture = rt;
        cam.aspect = 1f;

        Bounds b = new Bounds();
        {
            Vector3[] v = mesh.vertices;
            sb.AppendLine($"mesh verts: [{string.Join("; ", v)}]");
            foreach (Vector3 p in v) b.Encapsulate(p);
        }
        sb.AppendLine($"mesh vertex bounds: center={b.center} extents={b.extents}");
        if (b.extents.sqrMagnitude < 1e-8f)
        {
            sb.AppendLine("FAIL: mesh has no real vertices (layout produced no geometry)");
            return 0;
        }
        Vector3 center = b.center;
        float half = Mathf.Max(b.extents.x, b.extents.y) * 1.05f + 0.01f;
        cam.transform.position = center + Vector3.back * 10f;
        cam.orthographicSize = half;
        cam.Render();

        RenderTexture.active = rt;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.ReadPixels(new Rect(0, 0, size, size), 0, 0);
        tex.Apply();
        RenderTexture.active = null;
        cam.targetTexture = null;

        pngPath = $"Assets/BoxProbe/{go.name}-{size}.png";
        Directory.CreateDirectory("Assets/BoxProbe");
        File.WriteAllBytes(pngPath, tex.EncodeToPNG());
        AssetDatabase.ImportAsset(pngPath);

        Color32[] px = tex.GetPixels32();
        int lit = 0;
        foreach (Color32 p in px)
            if (p.a > 8) lit++; // TMP atlases are Alpha8: shape lives in the alpha channel

        UnityEngine.Object.DestroyImmediate(tex);
        UnityEngine.Object.DestroyImmediate(camGo);
        rt.Release();
        UnityEngine.Object.DestroyImmediate(rt);

        // coverage relative to the quad area (mesh bounds projected onto the square frame)
        float rectW = (b.extents.x * 2f) / (half * 2f);
        float rectH = (b.extents.y * 2f) / (half * 2f);
        double quadFraction = (double)rectW * rectH;
        double litFraction = (double)lit / (size * size);
        return quadFraction > 0 ? litFraction / quadFraction : 0;
    }
}
