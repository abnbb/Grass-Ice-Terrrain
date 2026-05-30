using UnityEngine;
using UnityEditor;
using System.IO;

[CustomEditor(typeof(CustomTerrainTool))]
public class CustomTerrainToolEditor : Editor
{
    private CustomTerrainTool tool;
    private Object combinedLayerAsset;

    private void OnEnable()
    {
        tool = (CustomTerrainTool)target;
        RefreshCombinedLayerReference();
    }

    public override void OnInspectorGUI()
    {

        // 绘制所有 public 字段（默认 Inspector）
        DrawDefaultInspector();

        EditorGUILayout.Space(10);
        if (GUILayout.Button("开始绘制（合成纹理）", GUILayout.Height(30)))
        {
            tool.CombineLayers();
            RefreshCombinedLayerReference();
            // 刷新资源数据库，确保新生成的内容立刻可见
            AssetDatabase.Refresh();
        }

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("生成的组合地形层资产", EditorStyles.boldLabel);
        
        if (combinedLayerAsset != null)
        {
            EditorGUILayout.ObjectField("Combined Layer", combinedLayerAsset, typeof(CustomTerrainLayer), false);
        }
        else
        {
            EditorGUILayout.HelpBox("尚未生成资产，请点击上方按钮进行合成。", MessageType.Info);
        }
    }

    private void RefreshCombinedLayerReference()
    {
        string folderPath = "Assets/Scenes/terrain/CombinedTextures/";
        string assetPath = folderPath + "CombinedLayer.asset";
        combinedLayerAsset = AssetDatabase.LoadAssetAtPath<CustomTerrainLayer>(assetPath);
    }
}