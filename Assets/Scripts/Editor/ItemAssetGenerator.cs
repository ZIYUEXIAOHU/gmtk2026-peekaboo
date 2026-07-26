using System.Collections.Generic;
using System.IO;
using Mirror;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 一键：配置物品贴图导入参数、生成 Investigable 物品 Prefab、重写 ItemTable 条目。
/// 菜单：Peekaboo/Generate Item Assets
/// </summary>
public static class ItemAssetGenerator
{
    const string ArtRoot = "Assets/Art/Items";
    const string PrefabFolder = "Assets/Prefabs/Items";
    const string ItemTablePath = "Assets/Resources/ItemTable.asset";
    const string LegacyItemTablePath = "Assets/Data/ItemTable.asset";

    // 与 Visual_Hider 一致
    const int SortingLayerId = 1504543465;
    const int SortingOrder = 10;

    static readonly float PpuLarge = 280f;
    static readonly float PpuMiddle = 160f;
    static readonly float PpuSmall = 96f;

    struct ItemDef
    {
        public string relativePath; // under ArtRoot, e.g. Large/L-CoffeeTable.png
        public string displayName;
        public float ppu;
    }

    static readonly ItemDef[] Items =
    {
        new ItemDef { relativePath = "Large/L-CoffeeTable.png", displayName = "咖啡桌", ppu = PpuLarge },
        new ItemDef { relativePath = "Large/L-RockingHorse.png", displayName = "摇摇马", ppu = PpuLarge },
        new ItemDef { relativePath = "Large/L-StandLamp.png", displayName = "落地灯", ppu = PpuLarge },
        new ItemDef { relativePath = "Middle/M-Lamp.png", displayName = "台灯", ppu = PpuMiddle },
        new ItemDef { relativePath = "Middle/M-MusicBox.png", displayName = "音乐盒", ppu = PpuMiddle },
        new ItemDef { relativePath = "Middle/M-Rabbit.png", displayName = "兔子玩偶", ppu = PpuMiddle },
        new ItemDef { relativePath = "Middle/M-SideTable.png", displayName = "边桌", ppu = PpuMiddle },
        new ItemDef { relativePath = "Middle/M-Vase.png", displayName = "花瓶", ppu = PpuMiddle },
        new ItemDef { relativePath = "Small/S-Medicine.png", displayName = "药瓶", ppu = PpuSmall },
        new ItemDef { relativePath = "Small/S-MintRabbit.png", displayName = "薄荷兔", ppu = PpuSmall },
        new ItemDef { relativePath = "Small/S-TeaCup.png", displayName = "茶杯", ppu = PpuSmall },
    };

    [MenuItem("Peekaboo/Generate Item Assets")]
    public static void Generate()
    {
        EnsureFolders();
        EnsureItemTableLocation();

        var entries = new List<ItemTable.Entry>();

        for (int i = 0; i < Items.Length; i++)
        {
            ItemDef def = Items[i];
            string assetPath = $"{ArtRoot}/{def.relativePath}".Replace('\\', '/');
            if (!File.Exists(Path.Combine(Directory.GetCurrentDirectory(), assetPath)))
            {
                Debug.LogError($"[ItemAssetGenerator] 缺少贴图: {assetPath}");
                continue;
            }

            ConfigureTextureImport(assetPath, def.ppu);
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
            if (sprite == null)
            {
                Debug.LogError($"[ItemAssetGenerator] 无法加载 Sprite: {assetPath}");
                continue;
            }

            string baseName = Path.GetFileNameWithoutExtension(def.relativePath);
            string prefabPath = $"{PrefabFolder}/Item_{baseName}.prefab";
            GameObject prefab = CreateOrUpdateItemPrefab(prefabPath, baseName, sprite);

            entries.Add(new ItemTable.Entry
            {
                displayName = def.displayName,
                prefab = prefab,
                icon = sprite,
            });
        }

        WriteItemTable(entries);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[ItemAssetGenerator] 完成：生成 {entries.Count} 个物品 Prefab，并写入 {ItemTablePath}");
    }

    /// <summary>供 batchmode 调用：Unity.exe -batchmode -executeMethod ItemAssetGenerator.GenerateBatch -quit</summary>
    public static void GenerateBatch()
    {
        Generate();
    }

    static void EnsureFolders()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Art"))
            AssetDatabase.CreateFolder("Assets", "Art");
        if (!AssetDatabase.IsValidFolder("Assets/Art/Items"))
            AssetDatabase.CreateFolder("Assets/Art", "Items");
        if (!AssetDatabase.IsValidFolder("Assets/Prefabs"))
            AssetDatabase.CreateFolder("Assets", "Prefabs");
        if (!AssetDatabase.IsValidFolder(PrefabFolder))
            AssetDatabase.CreateFolder("Assets/Prefabs", "Items");
        if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            AssetDatabase.CreateFolder("Assets", "Resources");
    }

    static void EnsureItemTableLocation()
    {
        // 若仍在 Assets/Data，移到 Resources 并保留 GUID
        if (File.Exists(LegacyItemTablePath) && !File.Exists(ItemTablePath))
        {
            string error = AssetDatabase.MoveAsset(LegacyItemTablePath, ItemTablePath);
            if (!string.IsNullOrEmpty(error))
                Debug.LogError($"[ItemAssetGenerator] 移动 ItemTable 失败: {error}");
            else
                Debug.Log($"[ItemAssetGenerator] 已移动 ItemTable → {ItemTablePath}");
        }
    }

    static void ConfigureTextureImport(string assetPath, float ppu)
    {
        var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (importer == null) return;

        bool dirty = false;
        if (importer.textureType != TextureImporterType.Sprite)
        {
            importer.textureType = TextureImporterType.Sprite;
            dirty = true;
        }

        if (!Mathf.Approximately(importer.spritePixelsPerUnit, ppu))
        {
            importer.spritePixelsPerUnit = ppu;
            dirty = true;
        }

        if (importer.filterMode != FilterMode.Bilinear)
        {
            importer.filterMode = FilterMode.Bilinear;
            dirty = true;
        }

        if (importer.mipmapEnabled)
        {
            importer.mipmapEnabled = false;
            dirty = true;
        }

        if (dirty)
            importer.SaveAndReimport();
    }

    static GameObject CreateOrUpdateItemPrefab(string prefabPath, string baseName, Sprite sprite)
    {
        GameObject root = new GameObject($"Item_{baseName}");
        try
        {
            var sr = root.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingLayerID = SortingLayerId;
            sr.sortingOrder = SortingOrder;
            sr.color = Color.white;

            var col = root.AddComponent<BoxCollider2D>();
            // 固体碰撞；运行时由 CollisionLayers / InvestigableObject 设为 HiderItem 层
            col.isTrigger = false;
            int itemLayer = LayerMask.NameToLayer(CollisionLayers.HiderItem);
            if (itemLayer >= 0)
                root.layer = itemLayer;
            if (sprite != null)
            {
                Bounds b = sprite.bounds;
                col.size = b.size;
                col.offset = b.center;
            }

            root.AddComponent<NetworkIdentity>();
            root.AddComponent<InvestigableObject>();

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            return prefab;
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    static void WriteItemTable(List<ItemTable.Entry> entries)
    {
        ItemTable table = AssetDatabase.LoadAssetAtPath<ItemTable>(ItemTablePath);
        if (table == null)
        {
            table = ScriptableObject.CreateInstance<ItemTable>();
            AssetDatabase.CreateAsset(table, ItemTablePath);
        }

        table.items = entries;
        EditorUtility.SetDirty(table);
    }
}
