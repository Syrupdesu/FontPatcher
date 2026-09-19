// Builds Unifont-based TMP font assets (Normal / Transmit) as AssetBundles for
// LC-FontPatcher. Run headless:
//   Unity -batchmode -nographics -quit -projectPath <unity-project>
//     -executeMethod BuildUnifontBundles.BuildAll -logFile <log>
// Environment:
//   LCFP_REPO_ROOT  repo root; built Windows bundles are copied to
//                   <repo>/assets/FontPatcher/<variant dir>/<bundle name>
//   LCFP_OSX_OUT    directory that receives the same bundles built for macOS
//                   (StandaloneOSX) so they can be loaded again inside this
//                   editor for validation.
using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore;
using UnityEngine.TextCore.LowLevel;

public static class BuildUnifontBundles
{
    // LC-FontPatcher README: with the game font sampled at 90, line height is 98.1
    // (1.09em) and ascent is 72 (0.8em); descent follows as 1.09 - 0.8 = 0.29em.
    // Normalizing our font to the same ratios keeps mixed Latin (game font) and
    // CJK (fallback font) lines on one baseline with one line height.
    const float AscentEm = 72f / 90f;
    const float LineHeightEm = 98.1f / 90f;
    const float DescentEm = LineHeightEm - AscentEm;

    const int AtlasSize = 1024;
    const string FontFileName = "unifont-18.0.01.otf";

    class Variant
    {
        public string BundleName;     // file name in the config folder = fallback order
        public string DirName;        // sub folder under assets/FontPatcher/ (or test-bundles/)
        public string FontAssetDir;   // Assets/Fonts/<dir> holding this variant's font copy
        public int SamplingPointSize; // 80 = 16px bitmap grid x5; 16 = native 1:1
        public int Padding;
        public FilterMode FilterMode;
        public bool ShipToConfig;     // true: repo assets/ (shipped in the Thunderstore package)
    }

    static readonly Variant[] Variants =
    {
        new Variant { BundleName = "00 zh", DirName = "default", FontAssetDir = "default",
                      SamplingPointSize = 80, Padding = 8, FilterMode = FilterMode.Point, ShipToConfig = true },
        new Variant { BundleName = "01 zh bilinear", DirName = "zh-bilinear", FontAssetDir = "zh-bilinear",
                      SamplingPointSize = 80, Padding = 8, FilterMode = FilterMode.Bilinear, ShipToConfig = false },
        new Variant { BundleName = "02 zh native", DirName = "zh-native", FontAssetDir = "zh-native",
                      SamplingPointSize = 16, Padding = 2, FilterMode = FilterMode.Point, ShipToConfig = false },
    };

    public static void BuildAll()
    {
        EditorApplication.Exit(BuildAllInner());
    }

    static int BuildAllInner()
    {
        try
        {
            ImportTmpEssentialResources();

            foreach (Variant v in Variants)
            {
                ImportFont(v);
                CreateVariantAssets(v);
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            VerifySavedAssets();

            foreach (Variant v in Variants)
            {
                SetBundleName($"Assets/Fonts/{v.FontAssetDir}/{FontFileName}", v.BundleName);
                SetBundleName($"Assets/Generated/{v.DirName}/Normal.asset", v.BundleName);
                SetBundleName($"Assets/Generated/{v.DirName}/Normal Material.asset", v.BundleName);
                SetBundleName($"Assets/Generated/{v.DirName}/Transmit.asset", v.BundleName);
                SetBundleName($"Assets/Generated/{v.DirName}/Transmit Material.asset", v.BundleName);
            }
            AssetDatabase.RemoveUnusedAssetBundleNames();

            BuildFor(BuildTarget.StandaloneWindows64, "Assets/Builds/win64");
            BuildFor(BuildTarget.StandaloneOSX, "Assets/Builds/osx");

            string repoRoot = Environment.GetEnvironmentVariable("LCFP_REPO_ROOT");
            string osxOut = Environment.GetEnvironmentVariable("LCFP_OSX_OUT");
            foreach (Variant v in Variants)
            {
                if (!string.IsNullOrEmpty(repoRoot))
                {
                    string destRoot = v.ShipToConfig
                        ? Path.Combine(repoRoot, "assets", "FontPatcher")
                        : Path.Combine(repoRoot, "test-bundles");
                    string destDir = Path.Combine(destRoot, v.DirName);
                    Directory.CreateDirectory(destDir);
                    File.Copy(Path.Combine("Assets/Builds/win64", v.BundleName),
                              Path.Combine(destDir, v.BundleName), overwrite: true);
                    File.Copy(Path.Combine("Assets/Builds/win64", v.BundleName + ".manifest"),
                              Path.Combine(destDir, v.BundleName + ".manifest"), overwrite: true);
                }
                if (!string.IsNullOrEmpty(osxOut))
                {
                    Directory.CreateDirectory(osxOut);
                    File.Copy(Path.Combine("Assets/Builds/osx", v.BundleName),
                              Path.Combine(osxOut, v.BundleName), overwrite: true);
                }
            }

            Debug.Log("[BuildUnifontBundles] done");
            return 0;
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            return 1;
        }
    }

    static void ImportTmpEssentialResources()
    {
        // The build-bundles.sh driver extracts the TMP Essential Resources package into
        // Assets before Unity starts (ImportPackage is unreliable in batchmode). Verify.
        Shader mobileBitmap = Shader.Find("TextMeshPro/Mobile/Bitmap");
        if (mobileBitmap == null)
            throw new System.Exception(
                "Shader 'TextMeshPro/Mobile/Bitmap' not found; TMP Essential Resources missing from Assets");
        Debug.Log("[BuildUnifontBundles] TMP shaders available");

        // Deterministic rebuild of generated assets and bundles.
        FileUtil.DeleteFileOrDirectory("Assets/Generated");
        FileUtil.DeleteFileOrDirectory("Assets/Generated.meta");
        AssetDatabase.Refresh();
    }

    static void ImportFont(Variant v)
    {
        string path = $"Assets/Fonts/{v.FontAssetDir}/{FontFileName}";
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var importer = (TrueTypeFontImporter)AssetImporter.GetAtPath(path);
        if (importer == null) throw new System.Exception($"No font importer for {path}");
        importer.includeFontData = true; // the source font data must be embedded in the bundle
        importer.SaveAndReimport();
        if (AssetDatabase.LoadAssetAtPath<Font>(path) == null)
            throw new System.Exception($"Font asset missing after import: {path}");
    }

    static void CreateVariantAssets(Variant v)
    {
        string fontPath = $"Assets/Fonts/{v.FontAssetDir}/{FontFileName}";
        Font font = AssetDatabase.LoadAssetAtPath<Font>(fontPath);
        string outDir = $"Assets/Generated/{v.DirName}";
        Directory.CreateDirectory(outDir);

        foreach (string kind in new[] { "Normal", "Transmit" })
        {
            TMP_FontAsset fontAsset = TMP_FontAsset.CreateFontAsset(
                font, v.SamplingPointSize, v.Padding,
                GlyphRenderMode.RASTER, // 1-bit bitmap, no hinting: Unifont's pixel grid, verbatim
                AtlasSize, AtlasSize,
                AtlasPopulationMode.Dynamic, enableMultiAtlasSupport: true);
            if (fontAsset == null)
                throw new System.Exception($"CreateFontAsset failed for {v.BundleName}/{kind}");
            if (fontAsset.sourceFontFile == null)
                throw new System.Exception($"sourceFontFile not set for {v.BundleName}/{kind}");

            fontAsset.name = kind;
            fontAsset.isMultiAtlasTexturesEnabled = true;

            // Materialize the first atlas page so the bundle ships a real texture.
            // Texture2D.Resize leaves the content UNDEFINED — it must be cleared to
            // transparent explicitly, or every glyph quad paints a solid box of text
            // color (the atlas background is part of each glyph quad's UV rect).
            // TMP only calls ResetAtlasTexture itself when the page is still 0x0.
            Texture2D texture = fontAsset.atlasTextures[0];
            if (texture.width != AtlasSize || texture.height != AtlasSize)
            {
                texture.Reinitialize(AtlasSize, AtlasSize, TextureFormat.Alpha8, false);
            }
            texture.SetPixelData(new byte[AtlasSize * AtlasSize], 0);
            texture.Apply(false, false);
            texture.name = kind + " Atlas";
            texture.filterMode = v.FilterMode;
            texture.wrapMode = TextureWrapMode.Clamp;

            // The upstream kr/jp bundles use a TMP Distance Field material with
            // _GradientScale = padding + 1 (UnityPy-measured reference: 9+1=10,
            // _WeightBold 0.75, RASTER bitmap atlas, Alpha8). TMP's fallback material
            // derivation (TMP_MaterialManager.GetFallbackMaterial) copies exactly these
            // values onto a clone of the game font's material; a Mobile/Bitmap material
            // without them fails the HasProperty check and makes TMP clone *our*
            // material instead, which renders text through the wrong pipeline.
            Shader sdfShader = Shader.Find("TextMeshPro/Distance Field");
            if (sdfShader == null)
                throw new System.Exception("Shader 'TextMeshPro/Distance Field' not found");
            Material sdfMaterial = new Material(sdfShader);
            sdfMaterial.SetTexture(ShaderUtilities.ID_MainTex, texture);
            sdfMaterial.SetFloat(ShaderUtilities.ID_GradientScale, v.Padding + 1);
            sdfMaterial.SetFloat(ShaderUtilities.ID_TextureWidth, AtlasSize);
            sdfMaterial.SetFloat(ShaderUtilities.ID_TextureHeight, AtlasSize);
            sdfMaterial.SetFloat(ShaderUtilities.ID_WeightNormal, fontAsset.normalStyle);
            sdfMaterial.SetFloat(ShaderUtilities.ID_WeightBold, fontAsset.boldStyle);
            fontAsset.material = sdfMaterial;

            // faceInfo is a struct: copy, adjust, write back.
            FaceInfo fi = fontAsset.faceInfo;
            fi.ascentLine = AscentEm * fi.pointSize;
            fi.descentLine = -DescentEm * fi.pointSize;
            fi.lineHeight = LineHeightEm * fi.pointSize;
            fontAsset.faceInfo = fi;

            // Persist the font asset first, then the texture as its sub-asset, and only then
            // the material: CreateAsset serializes the object at that instant, so saving the
            // material while the texture is still non-persistent would serialize _MainTex as
            // null (which NRE'd TMP_MaterialManager.GetFallbackMaterial in-game).
            AssetDatabase.CreateAsset(fontAsset, $"{outDir}/{kind}.asset");
            AssetDatabase.AddObjectToAsset(texture, fontAsset);
            AssetDatabase.CreateAsset(fontAsset.material, $"{outDir}/{kind} Material.asset");
            EditorUtility.SetDirty(fontAsset);
            EditorUtility.SetDirty(fontAsset.material);

            Debug.Log($"[BuildUnifontBundles] {v.BundleName}/{kind}: pointSize={fi.pointSize} " +
                      $"ascent={fi.ascentLine} descent={fi.descentLine} lineHeight={fi.lineHeight} " +
                      $"filter={v.FilterMode} mode={fontAsset.atlasPopulationMode} " +
                      $"material={sdfShader.name} gradientScale={v.Padding + 1}");
        }
    }

    static void VerifySavedAssets()
    {
        // Reload every persisted font asset/material from disk and assert the
        // references that in-game fallback rendering depends on.
        foreach (Variant v in Variants)
        {
            foreach (string kind in new[] { "Normal", "Transmit" })
            {
                string dir = $"Assets/Generated/{v.DirName}";
                var fa = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>($"{dir}/{kind}.asset");
                var mat = AssetDatabase.LoadAssetAtPath<Material>($"{dir}/{kind} Material.asset");
                if (fa == null || mat == null)
                    throw new System.Exception($"Reload failed for {v.BundleName}/{kind}");
                if (mat.GetTexture(ShaderUtilities.ID_MainTex) == null)
                    throw new System.Exception(
                        $"{v.BundleName}/{kind}: material._MainTex is null after save " +
                        "(would NRE TMP_MaterialManager.GetFallbackMaterial in-game)");
                if (mat.GetTexture(ShaderUtilities.ID_MainTex) != fa.atlasTextures[0])
                    throw new System.Exception($"{v.BundleName}/{kind}: material._MainTex != atlasTextures[0]");
                if (fa.sourceFontFile == null)
                    throw new System.Exception($"{v.BundleName}/{kind}: sourceFontFile is null after save");
                if (mat.shader.name != "TextMeshPro/Distance Field")
                    throw new System.Exception($"{v.BundleName}/{kind}: unexpected shader {mat.shader.name}");
                if (mat.GetFloat(ShaderUtilities.ID_GradientScale) != v.Padding + 1)
                    throw new System.Exception(
                        $"{v.BundleName}/{kind}: _GradientScale {mat.GetFloat(ShaderUtilities.ID_GradientScale)} != padding+1");
                if (!mat.HasProperty(ShaderUtilities.ID_GradientScale))
                    throw new System.Exception($"{v.BundleName}/{kind}: material lacks _GradientScale");

                // The shipped atlas page must be fully transparent: Texture2D.Resize
                // leaves undefined content, and a non-zero background paints a solid
                // box behind every glyph quad.
                var readable = new Texture2D(AtlasSize, AtlasSize, TextureFormat.Alpha8, false);
                Graphics.CopyTexture(fa.atlasTextures[0], readable);
                byte[] raw = readable.GetRawTextureData();
                int nonzero = 0;
                foreach (byte b in raw)
                    if (b != 0) nonzero++;
                UnityEngine.Object.DestroyImmediate(readable);
                if (nonzero != 0)
                    throw new System.Exception(
                        $"{v.BundleName}/{kind}: shipped atlas has {nonzero} non-zero background texels");

                Debug.Log($"[BuildUnifontBundles] verified {v.BundleName}/{kind}: " +
                          $"material._MainTex=OK shader={mat.shader.name} gradientScale={v.Padding + 1} " +
                          $"atlasTextures={fa.atlasTextures.Length} sourceFont=OK atlasContent=clear");
            }
        }
    }

    static void SetBundleName(string assetPath, string bundleName)
    {
        AssetImporter importer = AssetImporter.GetAtPath(assetPath);
        if (importer == null) throw new System.Exception($"No importer for {assetPath}");
        importer.SetAssetBundleNameAndVariant(bundleName, null);
    }

    static void BuildFor(BuildTarget target, string outputDir)
    {
        if (Directory.Exists(outputDir)) Directory.Delete(outputDir, true);
        Directory.CreateDirectory(outputDir);
        AssetBundleManifest manifest = BuildPipeline.BuildAssetBundles(
            outputDir, BuildAssetBundleOptions.ChunkBasedCompression, target);
        if (manifest == null)
            throw new System.Exception($"BuildAssetBundles failed for {target}");
        foreach (string bundleName in manifest.GetAllAssetBundles())
        {
            Debug.Log($"[BuildUnifontBundles] built [{target}] {bundleName}: " +
                      $"{new FileInfo(Path.Combine(outputDir, bundleName)).Length} bytes, " +
                      $"deps: [{string.Join(", ", manifest.GetAllDependencies(bundleName))}]");
        }
    }
}
