using UnityEngine;
using TMPro;
using UnityEditor;
using System.Collections.Generic;

public class BulkFontColorChanger : EditorWindow
{
    private Color targetColor = Color.white;
    private bool includeInactive = false;
    private bool applyToChildren = true;
    private bool applyToSelf = true;
    
    [MenuItem("Tools/批量更改字体颜色")]
    public static void ShowWindow()
    {
        GetWindow<BulkFontColorChanger>("批量更改字体颜色");
    }
    
    private void OnGUI()
    {
        GUILayout.Label("字体颜色批量修改", EditorStyles.boldLabel);
        EditorGUILayout.Space();
        
        targetColor = EditorGUILayout.ColorField("目标颜色", targetColor);
        
        EditorGUILayout.Space();
        
        includeInactive = EditorGUILayout.Toggle("包含非活跃物体", includeInactive);
        applyToChildren = EditorGUILayout.Toggle("应用到子物体", applyToChildren);
        applyToSelf = EditorGUILayout.Toggle("应用到自身", applyToSelf);
        
        EditorGUILayout.Space();
        
        if (GUILayout.Button("执行批量更改", GUILayout.Height(40)))
        {
            ApplyColorToAll();
        }
        
        if (GUILayout.Button("重置为白色", GUILayout.Height(30)))
        {
            targetColor = Color.white;
            ApplyColorToAll();
        }
        
        EditorGUILayout.Space();
        
        EditorGUILayout.HelpBox(
            "选中场景中的父物体后执行，会修改该物体及其子物体下所有 TextMeshProUGUI 的颜色。",
            MessageType.Info
        );
    }
    
    private void ApplyColorToAll()
    {
        GameObject[] selectedObjects = Selection.gameObjects;
        
        if (selectedObjects.Length == 0)
        {
            EditorUtility.DisplayDialog("提示", "请先在 Hierarchy 中选中一个父物体！", "确定");
            return;
        }
        
        int totalCount = 0;
        
        foreach (var obj in selectedObjects)
        {
            List<TextMeshProUGUI> texts = new List<TextMeshProUGUI>();
            
            if (applyToSelf)
            {
                TextMeshProUGUI selfText = obj.GetComponent<TextMeshProUGUI>();
                if (selfText != null) texts.Add(selfText);
            }
            
            if (applyToChildren)
            {
                texts.AddRange(obj.GetComponentsInChildren<TextMeshProUGUI>(includeInactive));
            }
            
            foreach (var text in texts)
            {
                Undo.RecordObject(text, "批量更改字体颜色");
                text.color = targetColor;
                EditorUtility.SetDirty(text);
            }
            
            totalCount += texts.Count;
        }
        
        Debug.Log($"✅ 已更改 {totalCount} 个文本的颜色为 {targetColor}");
        EditorUtility.DisplayDialog("完成", $"已更改 {totalCount} 个文本的颜色", "确定");
    }
}