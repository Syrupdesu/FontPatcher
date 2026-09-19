using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using TMPro;
using HarmonyLib;

namespace FontPatcher;

[HarmonyPatch]
class FontLoader
{
    class FontBundle
    {
        public string BundleName;
        public TMP_FontAsset Normal;
        public TMP_FontAsset Transmit;
    }

    static List<FontBundle> fontBundles = new();
    static Regex normalRegex;
    static Regex transmitRegex;

    public static void Load()
    {
        // Initialize regexes before anything that can throw: Harmony patches run even when
        // font loading failed, and must never see a null regex (NRE for every patched font).
        normalRegex = new Regex(Plugin.configNormalRegexPattern.Value);
        transmitRegex = new Regex(Plugin.configTransmitRegexPattern.Value);

        try
        {
            string configPath = Path.GetDirectoryName(Plugin.Instance.Config.ConfigFilePath);
            string fontsPath = Path.Combine(configPath, Plugin.configFontAssetPath.Value);
            Plugin.LogInfo($"Font path: {fontsPath}");

            DirectoryInfo di = new DirectoryInfo(fontsPath);
            if (!di.Exists)
            {
                Plugin.LogError($"Font directory not found: {fontsPath}");
                return;
            }
            FileInfo[] fileInfos = di.GetFiles("*");
            Array.Sort(fileInfos, (a, b) => string.CompareOrdinal(a.Name, b.Name));

            int sucessCount = 0;
            int failCount = 0;
            foreach (FileInfo info in fileInfos)
            {
                // Unity writes a "<bundle>.manifest" (and a summary manifest) next to built
                // AssetBundles; they are not bundles and only produce noise if loaded.
                if (info.Extension.Equals(".manifest", StringComparison.OrdinalIgnoreCase) ||
                    info.Extension.Equals(".meta", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    AssetBundle bundle = AssetBundle.LoadFromFile(info.FullName);
                    Plugin.LogInfo($"[{info.Name}] loaded");

                    FontBundle tmp = new()
                    {
                        Normal = bundle.LoadAsset<TMP_FontAsset>(ResourcePath.NormalFont),
                        Transmit = bundle.LoadAsset<TMP_FontAsset>(ResourcePath.TransmitFont)
                    };

                    if (tmp.Normal)
                    {
                        tmp.BundleName = info.Name;
                        tmp.Normal.name = $"{info.Name}(Normal)";
                        EnsureMainTexture(tmp.Normal);
                        DiagnoseFontAsset(tmp.Normal, $"{info.Name}(Normal)");
                        Plugin.LogInfo($"[{info.Name}] Normal font found ({tmp.Normal.name})");
                    }
                    if (tmp.Transmit)
                    {
                        tmp.BundleName = info.Name;
                        tmp.Transmit.name = $"{info.Name}(Transmit)";
                        EnsureMainTexture(tmp.Transmit);
                        DiagnoseFontAsset(tmp.Transmit, $"{info.Name}(Transmit)");
                        Plugin.LogInfo($"[{info.Name}] Transmit font found ({tmp.Transmit.name})");
                    }

                    if (tmp.BundleName == null)
                    {
                        throw new Exception($"Not included recognizable font");
                    }

                    fontBundles.Add(tmp);
                    sucessCount += 1;
                }
                catch (Exception e)
                {
                    Plugin.LogError($"[{info.Name}] load failed: {e.Message}");
                    failCount += 1;
                }
            }

            StringBuilder stringBuilder = new();
            stringBuilder.Append($"{sucessCount} fonts loaded");
            if (failCount > 0) stringBuilder.Append($", {failCount} fonts load failed");
            Plugin.LogInfo(stringBuilder.ToString());
        }
        catch (Exception e)
        {
            Plugin.LogError(e.ToString());
        }
    }

    // Re-bind the material's main texture if it was serialized as null (older bundles
    // built before the asset-creation order was fixed). A null _MainTex makes
    // TMP_MaterialManager.GetFallbackMaterial throw a NullReferenceException.
    static void EnsureMainTexture(TMP_FontAsset font)
    {
        if (!font.material) return;
        if (font.material.GetTexture(ShaderUtilities.ID_MainTex)) return;
        if (font.atlasTextures == null || font.atlasTextures.Length == 0 || !font.atlasTextures[0]) return;

        font.material.mainTexture = font.atlasTextures[0];
        Plugin.LogInfo($"[{font.name}] material._MainTex was null; re-bound to atlasTextures[0]");
    }

    static void DiagnoseFontAsset(TMP_FontAsset font, string label)
    {
        var sb = new StringBuilder();
        sb.Append($"[{label}] material={(font.material ? "ok" : "NULL")}");
        if (font.material)
        {
            Material m = font.material;
            sb.Append($", shader={(m.shader ? m.shader.name : "NULL")}");
            sb.Append($", _MainTex={(m.GetTexture(ShaderUtilities.ID_MainTex) ? "ok" : "NULL")}");
            sb.Append($", _GradientScale={DescribeFloat(m, ShaderUtilities.ID_GradientScale)}");
            sb.Append($", _TextureWidth={DescribeFloat(m, ShaderUtilities.ID_TextureWidth)}");
            sb.Append($", _TextureHeight={DescribeFloat(m, ShaderUtilities.ID_TextureHeight)}");
            sb.Append($", _WeightNormal={DescribeFloat(m, ShaderUtilities.ID_WeightNormal)}");
            sb.Append($", _WeightBold={DescribeFloat(m, ShaderUtilities.ID_WeightBold)}");
            sb.Append($", _FaceDilate={DescribeFloat(m, ShaderUtilities.ID_FaceDilate)}");
            sb.Append($", _UnderlayDilate={DescribeFloat(m, ShaderUtilities.ID_UnderlayDilate)}");
        }
        sb.Append($", atlasTextures={(font.atlasTextures != null ? font.atlasTextures.Length : 0)}");
        if (font.atlasTextures != null && font.atlasTextures.Length > 0 && font.atlasTextures[0])
            sb.Append($", atlasPage0={font.atlasTextures[0].width}x{font.atlasTextures[0].height}/{font.atlasTextures[0].filterMode}");
        else
            sb.Append(", atlasPage0=NULL");
        sb.Append($", populationMode={font.atlasPopulationMode}");
        sb.Append($", sourceFontFile={(font.sourceFontFile ? "ok" : "NULL")}");
        Plugin.LogInfo(sb.ToString());
    }

    // Returns the float value or "absent" — GetFloat silently returns 0 for missing
    // shader properties, which has already bitten us once (see _GradientScale).
    static string DescribeFloat(Material m, int propertyId)
    {
        return m.HasProperty(propertyId) ? m.GetFloat(propertyId).ToString("0.##") : "absent";
    }

    // Log what TMP actually derives for in-game fallback rendering.
    [HarmonyPostfix, HarmonyPatch(typeof(TMP_MaterialManager), "GetFallbackMaterial",
        new[] { typeof(Material), typeof(Material) })]
    static void LogDerivedFallbackMaterial(Material sourceMaterial, Material targetMaterial, Material __result)
    {
        if (__result == null) return;

        Plugin.LogInfo(
            $"[fallback material] source={(sourceMaterial && sourceMaterial.shader ? sourceMaterial.shader.name : "NULL")} " +
            $"target={(targetMaterial && targetMaterial.shader ? targetMaterial.shader.name : "NULL")} " +
            $"-> shader={(__result.shader ? __result.shader.name : "NULL")} " +
            $"_GradientScale={DescribeFloat(__result, ShaderUtilities.ID_GradientScale)} " +
            $"_FaceDilate={DescribeFloat(__result, ShaderUtilities.ID_FaceDilate)} " +
            $"_OutlineWidth={DescribeFloat(__result, ShaderUtilities.ID_OutlineWidth)} " +
            $"_UnderlayDilate={DescribeFloat(__result, ShaderUtilities.ID_UnderlayDilate)} " +
            $"_MainTex={(__result.GetTexture(ShaderUtilities.ID_MainTex) ? "ok" : "NULL")}");
    }

    [HarmonyPrefix, HarmonyPatch(typeof(TMP_FontAsset), "Awake")]
    static void PatchFontAwake(TMP_FontAsset __instance)
    {
        __instance.material.SetFloat("_UnderlayDilate", 1f);
        __instance.material.SetFloat("_UnderlayOffsetX", 0.1f);
        string fontName = __instance.name;

        if (normalRegex.IsMatch(fontName))
        {
            if (!Plugin.configNormalIngameFont.Value)
            {
                DisableFont(__instance);
            }

            int patchCount = 0;
            foreach (FontBundle bundle in fontBundles)
            {
                if (!bundle.Normal) continue;
                if (__instance.fallbackFontAssetTable.Contains(bundle.Normal)) continue;

                __instance.fallbackFontAssetTable.Add(bundle.Normal);
                patchCount += 1;
            }

            if (patchCount > 0)
            {
                Plugin.LogInfo($"[{fontName}] font patched (Normal): +{patchCount} fallback(s), total={__instance.fallbackFontAssetTable.Count}");
            }
            return;
        }

        if (transmitRegex.IsMatch(fontName))
        {
            if (!Plugin.configTransmitIngameFont.Value)
            {
                DisableFont(__instance);
            }

            int patchCount = 0;
            foreach (FontBundle bundle in fontBundles)
            {
                if (!bundle.Transmit) continue;
                if (__instance.fallbackFontAssetTable.Contains(bundle.Transmit)) continue;

                __instance.fallbackFontAssetTable.Add(bundle.Transmit);
                patchCount += 1;
            }

            if (patchCount > 0)
            {
                Plugin.LogInfo($"[{fontName}] font patched (Transmit): +{patchCount} fallback(s), total={__instance.fallbackFontAssetTable.Count}");
            }
            return;
        }

        Plugin.LogWarning($"[{fontName}] not patched");
    }

    [HarmonyPostfix, HarmonyPatch(typeof(TMP_Text), "font", MethodType.Setter)]
    static void PatchTextFontSetter(TMP_FontAsset value)
    {
        if (value == null) return;

        PatchFontAwake(value);
    }

    // Dynamic multi-atlas font assets create new atlas pages at runtime with Unity's default
    // filter mode (Bilinear), which would make bundle fonts inconsistent after the first page
    // fills up. Keep new pages using the same sampling settings as the first page.
    [HarmonyPostfix, HarmonyPatch(typeof(TMP_FontAsset), "SetupNewAtlasTexture")]
    static void PatchSetupNewAtlasTexture(TMP_FontAsset __instance)
    {
        if (__instance.atlasTextures == null || __instance.atlasTextures.Length == 0 || !__instance.atlasTextures[0])
        {
            return;
        }

        FilterMode filterMode = __instance.atlasTextures[0].filterMode;
        TextureWrapMode wrapMode = __instance.atlasTextures[0].wrapMode;
        foreach (Texture2D texture in __instance.atlasTextures)
        {
            if (!texture) continue;
            texture.filterMode = filterMode;
            texture.wrapMode = wrapMode;
        }
    }

    [HarmonyPrefix, HarmonyPatch(typeof(TextMeshProUGUI), "Awake")]
    static void PatchTextAwake(TextMeshProUGUI __instance)
    {
        if (__instance.font == null) return;

        PatchFontAwake(__instance.font);
    }

    static void DisableFont(TMP_FontAsset font)
    {
        font.characterLookupTable.Clear();
        font.atlasPopulationMode = AtlasPopulationMode.Static;
    }
}