using UnityEditor;
using UnityEngine;

/// <summary>
/// 确保 TagManager 中存在 CollisionLayers 所需层（Hider / Seeker / HiderItem）。
/// 外部改 YAML 后若 Play 仍报缺层，用菜单 Peekaboo/Ensure Collision Layers，或重启 Editor。
/// </summary>
[InitializeOnLoad]
public static class CollisionLayerSetup
{
    const string TagManagerPath = "ProjectSettings/TagManager.asset";

    static readonly string[] RequiredLayers =
    {
        CollisionLayers.Hider,
        CollisionLayers.Seeker,
        CollisionLayers.HiderItem,
    };

    static CollisionLayerSetup()
    {
        EditorApplication.delayCall += () => EnsureLayers();
    }

    [MenuItem("Peekaboo/Ensure Collision Layers")]
    public static void EnsureLayersMenu()
    {
        if (EnsureLayers())
            Debug.Log("[CollisionLayerSetup] Hider / Seeker / HiderItem 已就绪。");
    }

    [MenuItem("Peekaboo/Round All Scene Box Colliders")]
    public static void RoundAllSceneBoxCollidersMenu()
    {
        BoxCollider2D[] cols = Object.FindObjectsOfType<BoxCollider2D>(true);
        int count = 0;
        for (int i = 0; i < cols.Length; i++)
        {
            BoxCollider2D col = cols[i];
            if (col == null)
                continue;

            bool isActor = col.GetComponent<RoomPlayer>() != null ||
                           col.GetComponent<InvestigableObject>() != null;
            Undo.RecordObject(col, "Round Box Collider");
            CollisionLayers.ApplyColliderRounding(col, useMinScale: isActor);
            EditorUtility.SetDirty(col);
            count++;
        }

        Debug.Log($"[CollisionLayerSetup] 已为 {count} 个 BoxCollider2D 应用圆角（世界半径 {GameConstants.ColliderEdgeRadiusWorld}）。");
    }


    public static bool EnsureLayers()
    {
        Object[] assets = AssetDatabase.LoadAllAssetsAtPath(TagManagerPath);
        if (assets == null || assets.Length == 0)
        {
            Debug.LogError($"[CollisionLayerSetup] 无法加载 {TagManagerPath}");
            return false;
        }

        var tagManager = new SerializedObject(assets[0]);
        SerializedProperty layers = tagManager.FindProperty("layers");
        if (layers == null || !layers.isArray)
        {
            Debug.LogError("[CollisionLayerSetup] TagManager.layers 无效。");
            return false;
        }

        bool changed = false;
        foreach (string name in RequiredLayers)
        {
            if (LayerMask.NameToLayer(name) >= 0)
                continue;

            int existingIndex = FindLayerIndex(layers, name);
            if (existingIndex >= 0)
            {
                // YAML 已有名字，但 NameToLayer 未刷新：强制写回以同步引擎表
                layers.GetArrayElementAtIndex(existingIndex).stringValue = name;
                changed = true;
                Debug.Log($"[CollisionLayerSetup] 刷新已有 Layer「{name}」(index {existingIndex})。");
                continue;
            }

            if (!TryAssignLayer(layers, name))
            {
                Debug.LogError($"[CollisionLayerSetup] 无空闲 Layer 槽可写入「{name}」。");
                return false;
            }

            changed = true;
            Debug.Log($"[CollisionLayerSetup] 已添加 Layer「{name}」。");
        }

        if (changed)
        {
            tagManager.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
        }

        foreach (string name in RequiredLayers)
        {
            if (LayerMask.NameToLayer(name) < 0)
            {
                Debug.LogWarning(
                    $"[CollisionLayerSetup] 「{name}」仍不可用。请打开 Edit → Project Settings → Tags and Layers 确认，或重启 Unity。");
                return false;
            }
        }

        return true;
    }

    static int FindLayerIndex(SerializedProperty layers, string name)
    {
        for (int i = 0; i < layers.arraySize; i++)
        {
            SerializedProperty slot = layers.GetArrayElementAtIndex(i);
            if (slot != null && slot.stringValue == name)
                return i;
        }

        return -1;
    }

    /// <summary>优先 User Layer 8–31，其次 6–7。</summary>
    static bool TryAssignLayer(SerializedProperty layers, string name)
    {
        if (TryFillEmpty(layers, 8, layers.arraySize, name))
            return true;
        return TryFillEmpty(layers, 6, 8, name);
    }

    static bool TryFillEmpty(SerializedProperty layers, int start, int end, string name)
    {
        int last = Mathf.Min(end, layers.arraySize);
        for (int i = start; i < last; i++)
        {
            SerializedProperty slot = layers.GetArrayElementAtIndex(i);
            if (slot == null || !string.IsNullOrEmpty(slot.stringValue))
                continue;

            slot.stringValue = name;
            return true;
        }

        return false;
    }
}
