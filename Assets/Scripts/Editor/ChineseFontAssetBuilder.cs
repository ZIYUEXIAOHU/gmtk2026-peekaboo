using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

/// <summary>
/// Builds a Dynamic SDF Chinese font and registers it as TMP fallback.
/// Force-rebuilds when the material/atlas is missing (common TMP Dynamic asset corruption).
/// </summary>
public static class ChineseFontAssetBuilder
{
    const string SourceFontPath = "Assets/Fonts/Resources/Fonts/NotoSansCJKsc-Regular.otf";
    const string OutputAssetPath = "Assets/Fonts/NotoSansCJKsc SDF.asset";
    const string LiberationSansPath = "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";
    const string TmpSettingsPath = "Assets/TextMesh Pro/Resources/TMP Settings.asset";

    const int AtlasSize = 2048;
    const int SamplingPointSize = 36;
    const int Padding = 5;

    [InitializeOnLoadMethod]
    static void AutoBuildIfNeeded()
    {
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            if (!File.Exists(SourceFontPath))
                return;

            TMP_FontAsset existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(OutputAssetPath);
            if (existing == null || IsBroken(existing))
            {
                ForceRebuild();
                return;
            }

            if (!IsWired(existing))
                WireFallbacks(existing);

            EnsureMatchMaterialPresetOff();
        };
    }

    [MenuItem("Tools/Fonts/Rebuild Chinese TMP Fallback")]
    public static void ForceRebuild()
    {
        Font source = AssetDatabase.LoadAssetAtPath<Font>(SourceFontPath);
        if (source == null)
        {
            Debug.LogError($"[ChineseFontAssetBuilder] Source font not found: {SourceFontPath}");
            return;
        }

        EnsureIncludeFontData(SourceFontPath);

        if (AssetDatabase.LoadAssetAtPath<Object>(OutputAssetPath) != null)
            AssetDatabase.DeleteAsset(OutputAssetPath);

        Directory.CreateDirectory("Assets/Fonts");

        // CreateFontAsset starts with a 0x0 atlas; we immediately resize to a real atlas
        // so the material/texture survive Play Mode and matchMaterialPreset lookups.
        TMP_FontAsset fontAsset = TMP_FontAsset.CreateFontAsset(
            source,
            SamplingPointSize,
            Padding,
            GlyphRenderMode.SDFAA,
            AtlasSize,
            AtlasSize,
            AtlasPopulationMode.Dynamic,
            true);

        if (fontAsset == null)
        {
            Debug.LogError("[ChineseFontAssetBuilder] CreateFontAsset failed. Check Include Font Data on the OTF importer.");
            return;
        }

        fontAsset.name = "NotoSansCJKsc SDF";
        PreparePersistentAtlasAndMaterial(fontAsset);

        AssetDatabase.CreateAsset(fontAsset, OutputAssetPath);

        if (fontAsset.atlasTexture != null)
            AssetDatabase.AddObjectToAsset(fontAsset.atlasTexture, fontAsset);

        if (fontAsset.material != null)
            AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);

        EditorUtility.SetDirty(fontAsset);
        AssetDatabase.SaveAssets();
        AssetDatabase.ImportAsset(OutputAssetPath, ImportAssetOptions.ForceUpdate);

        fontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(OutputAssetPath);
        if (fontAsset == null || IsBroken(fontAsset))
        {
            Debug.LogError("[ChineseFontAssetBuilder] Rebuild finished but material/atlas is still invalid.");
            return;
        }

        WireFallbacks(fontAsset);
        EnsureMatchMaterialPresetOff();
        Debug.Log($"[ChineseFontAssetBuilder] Rebuilt {OutputAssetPath} with {AtlasSize}x{AtlasSize} atlas.");
    }

    static void PreparePersistentAtlasAndMaterial(TMP_FontAsset fontAsset)
    {
        Texture2D atlas = fontAsset.atlasTexture;
        if (atlas == null && fontAsset.atlasTextures != null && fontAsset.atlasTextures.Length > 0)
            atlas = fontAsset.atlasTextures[0];

        if (atlas == null)
        {
            atlas = new Texture2D(AtlasSize, AtlasSize, TextureFormat.Alpha8, false);
            fontAsset.atlasTextures = new[] { atlas };
        }
        else if (atlas.width != AtlasSize || atlas.height != AtlasSize)
        {
            atlas.Reinitialize(AtlasSize, AtlasSize, TextureFormat.Alpha8, false);
        }

        atlas.name = "NotoSansCJKsc SDF Atlas";
        atlas.hideFlags = HideFlags.None;
        fontAsset.atlasTextures[0] = atlas;
        fontAsset.ClearFontAssetData(false);

        Material mat = fontAsset.material;
        if (mat == null)
        {
            mat = new Material(Shader.Find("TextMeshPro/Mobile/Distance Field"));
            fontAsset.material = mat;
        }

        mat.name = "NotoSansCJKsc SDF Material";
        mat.hideFlags = HideFlags.None;
        mat.SetTexture(ShaderUtilities.ID_MainTex, atlas);
        mat.SetFloat(ShaderUtilities.ID_TextureWidth, AtlasSize);
        mat.SetFloat(ShaderUtilities.ID_TextureHeight, AtlasSize);
        mat.SetFloat(ShaderUtilities.ID_GradientScale, Padding + 1);
        mat.SetFloat(ShaderUtilities.ID_WeightNormal, fontAsset.normalStyle);
        mat.SetFloat(ShaderUtilities.ID_WeightBold, fontAsset.boldStyle);

        fontAsset.ReadFontAssetDefinition();
    }

    static bool IsBroken(TMP_FontAsset fontAsset)
    {
        if (fontAsset == null)
            return true;

        // Unity "fake null" for destroyed objects
        if (fontAsset.material == null)
            return true;

        if (fontAsset.atlasTexture == null)
            return true;

        try
        {
            // Touch material; destroyed objects throw / act as missing.
            _ = fontAsset.material.GetTexture(ShaderUtilities.ID_MainTex);
        }
        catch
        {
            return true;
        }

        return false;
    }

    static void EnsureIncludeFontData(string fontPath)
    {
        AssetImporter importer = AssetImporter.GetAtPath(fontPath);
        if (importer == null)
            return;

        SerializedObject so = new SerializedObject(importer);
        SerializedProperty include = so.FindProperty("m_IncludeFontData");
        if (include != null && !include.boolValue)
        {
            include.boolValue = true;
            so.ApplyModifiedProperties();
            AssetDatabase.ImportAsset(fontPath, ImportAssetOptions.ForceUpdate);
        }
    }

    static bool IsWired(TMP_FontAsset cjk)
    {
        TMP_FontAsset liberation = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(LiberationSansPath);
        if (liberation?.fallbackFontAssetTable != null && liberation.fallbackFontAssetTable.Contains(cjk))
            return true;

        Object settingsAsset = AssetDatabase.LoadAssetAtPath<Object>(TmpSettingsPath);
        if (settingsAsset == null)
            return false;

        SerializedObject settingsSo = new SerializedObject(settingsAsset);
        SerializedProperty fallbacks = settingsSo.FindProperty("m_fallbackFontAssets");
        if (fallbacks == null || !fallbacks.isArray)
            return false;

        for (int i = 0; i < fallbacks.arraySize; i++)
        {
            if (fallbacks.GetArrayElementAtIndex(i).objectReferenceValue == cjk)
                return true;
        }

        return false;
    }

    static void WireFallbacks(TMP_FontAsset cjk)
    {
        if (cjk == null)
            return;

        TMP_FontAsset liberation = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(LiberationSansPath);
        if (liberation != null)
        {
            if (liberation.fallbackFontAssetTable == null)
                liberation.fallbackFontAssetTable = new List<TMP_FontAsset>();

            liberation.fallbackFontAssetTable.RemoveAll(f => f == null || f == cjk);
            liberation.fallbackFontAssetTable.Insert(0, cjk);
            EditorUtility.SetDirty(liberation);
        }

        Object settingsAsset = AssetDatabase.LoadAssetAtPath<Object>(TmpSettingsPath);
        if (settingsAsset == null)
            return;

        SerializedObject settingsSo = new SerializedObject(settingsAsset);
        SerializedProperty fallbacks = settingsSo.FindProperty("m_fallbackFontAssets");
        if (fallbacks == null || !fallbacks.isArray)
            return;

        for (int i = fallbacks.arraySize - 1; i >= 0; i--)
        {
            Object value = fallbacks.GetArrayElementAtIndex(i).objectReferenceValue;
            if (value == null || value == cjk)
                fallbacks.DeleteArrayElementAtIndex(i);
        }

        fallbacks.InsertArrayElementAtIndex(0);
        fallbacks.GetArrayElementAtIndex(0).objectReferenceValue = cjk;
        settingsSo.ApplyModifiedProperties();
        EditorUtility.SetDirty(settingsAsset);
        AssetDatabase.SaveAssets();
    }

    /// <summary>
    /// Match Material Preset creates temporary materials from the fallback font material.
    /// If that material is missing/destroyed, TMP throws MissingReferenceException.
    /// Using the fallback font's own material is more stable for CJK dynamic fonts.
    /// </summary>
    static void EnsureMatchMaterialPresetOff()
    {
        Object settingsAsset = AssetDatabase.LoadAssetAtPath<Object>(TmpSettingsPath);
        if (settingsAsset == null)
            return;

        SerializedObject settingsSo = new SerializedObject(settingsAsset);
        SerializedProperty match = settingsSo.FindProperty("m_matchMaterialPreset");
        if (match != null && match.boolValue)
        {
            match.boolValue = false;
            settingsSo.ApplyModifiedProperties();
            EditorUtility.SetDirty(settingsAsset);
            AssetDatabase.SaveAssets();
            Debug.Log("[ChineseFontAssetBuilder] Disabled TMP Match Material Preset to avoid fallback material crashes.");
        }
    }
}
