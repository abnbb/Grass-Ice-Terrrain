using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

public class GrassMatrixTool : EditorWindow
{
    private Transform grassHolder;          // 草的父物体
    private float cullDistance = 50f;       // CPU 剔除用距离（GPU 方案可替换）
    private Vector2 scrollPos;

    private float maxScaleValue = 4.0f;   // 解压最大缩放值

    [MenuItem("Tools/Grass Matrix Tool")]
    public static void ShowWindow()
    {
        GetWindow<GrassMatrixTool>("Grass Matrix Tool");
    }

    private void OnGUI()
    {
        scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

        GUILayout.Label("草矩阵提取与剔除工具", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        // 父物体选择
        grassHolder = (Transform)EditorGUILayout.ObjectField("草父物体", grassHolder, typeof(Transform), true);
        if (grassHolder == null)
        {
            GameObject holder = GameObject.Find("GrassHolder_Plane");
            if (holder != null)
                grassHolder = holder.transform;
        }

        if (grassHolder != null)
        {
            EditorGUILayout.HelpBox($"当前父物体: {grassHolder.name}，子物体数量: {grassHolder.childCount}", MessageType.Info);
        }
        else
        {
            EditorGUILayout.HelpBox("未找到 GrassHolder_Plane，请手动拖入。", MessageType.Warning);
        }

        EditorGUILayout.Space();

        // 提取原始矩阵
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("提取原始矩阵并保存", GUILayout.Height(30)))
        {
            if (grassHolder == null || grassHolder.childCount == 0)
            {
                EditorUtility.DisplayDialog("错误", "父物体为空或没有子物体。", "确定");
            }
            else
            {
                SaveRawMatrices();
            }
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space();

        //压缩数据
        GUILayout.Label("压缩数据提取 (float4)", EditorStyles.boldLabel);
        maxScaleValue = EditorGUILayout.FloatField("最大缩放值", maxScaleValue);
        EditorGUILayout.HelpBox("每个实例存储为 float4：\n" +
            "• xyz = 世界位置\n" +
            "• w = 打包的 [旋转Y(高16位) + 缩放(低16位)]", MessageType.Info);

        if (GUILayout.Button("提取并保存打包数据", GUILayout.Height(25)))
        {
            if (grassHolder == null || grassHolder.childCount == 0)
                EditorUtility.DisplayDialog("错误", "父物体为空或没有子物体。", "确定");
            else
                SavePackedData();
        }

        EditorGUILayout.Space();

        GUILayout.Label("GPU 剔除（预留）", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("执行剔除并保存", GUILayout.Height(30)))
        {
            if (grassHolder == null || grassHolder.childCount == 0)
            {
                EditorUtility.DisplayDialog("错误", "父物体为空或没有子物体。", "确定");
                return;
            }

            string sourcePath = EditorUtility.OpenFilePanel("选择原始矩阵资产", "Assets", "asset");
            if (string.IsNullOrEmpty(sourcePath))
                return;

            GrassMatrixAsset sourceAsset = AssetDatabase.LoadAssetAtPath<GrassMatrixAsset>(
                "Assets" + sourcePath.Substring(Application.dataPath.Length));
            if (sourceAsset == null)
            {
                EditorUtility.DisplayDialog("错误", "未找到有效的 GrassMatrixAsset。", "确定");
                return;
            }

            string savePath = EditorUtility.SaveFilePanelInProject("保存剔除后矩阵", "GrassMatrices_Culled", "asset", "选择保存位置");
            if (string.IsNullOrEmpty(savePath))
                return;
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox("GPU 剔除流程：\n1. 先将剔除逻辑写入 ComputeShader。\n2. 在 PerformGPUCulling() 中实现调度。\n3. 当前已预留接口。", MessageType.None);

        EditorGUILayout.EndScrollView();
    }

    /// <summary>
    /// 提取并保存所有原始矩阵
    /// 
    /// 
    /// </summary>
    private void SaveRawMatrices()
    {
        Matrix4x4[] matrices = new Matrix4x4[grassHolder.childCount];
        for (int i = 0; i < grassHolder.childCount; i++)
        {
            matrices[i] = grassHolder.GetChild(i).localToWorldMatrix;
        }

        string path = EditorUtility.SaveFilePanelInProject("保存原始矩阵", "GrassMatrices_Raw", "asset", "选择保存位置");
        if (string.IsNullOrEmpty(path))
            return;

        GrassMatrixAsset asset = ScriptableObject.CreateInstance<GrassMatrixAsset>();
        asset.matrices = matrices;

        AssetDatabase.CreateAsset(asset, path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog("成功", $"已保存 {matrices.Length} 个变换矩阵到:\n{path}", "确定");
        Selection.activeObject = asset;
    }

    private void SavePackedData()
    {
        int count = grassHolder.childCount;
        Vector4[] packedArray = new Vector4[count];

        for (int i = 0; i < count; i++)
        {
            Matrix4x4 m = grassHolder.GetChild(i).localToWorldMatrix;
            Vector3 pos = new Vector3(m.m03, m.m13, m.m23);

            // 提取 Y 轴旋转：假设模型本地 Z+ 为前向
            Vector3 forward = new Vector3(m.m02, m.m12, m.m22);
            float rotY = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;

            // 提取统一缩放（取 X 轴，假设各轴相等）
            float scale = m.lossyScale.x;

            // 打包
            float packed = PackRotationScale(rotY, scale, maxScaleValue);
            packedArray[i] = new Vector4(pos.x, pos.y, pos.z, packed);
        }

        string path = EditorUtility.SaveFilePanelInProject("保存打包数据", "GrassPacked", "asset", "选择保存位置");
        if (string.IsNullOrEmpty(path)) return;

        PackedGrassAsset asset = ScriptableObject.CreateInstance<PackedGrassAsset>();
        asset.packedData = packedArray;
        asset.maxScale = maxScaleValue;
        AssetDatabase.CreateAsset(asset, path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        EditorUtility.DisplayDialog("成功", $"已保存 {count} 个压缩实例数据到:\n{path}\n最大缩放={maxScaleValue}", "确定");
        Selection.activeObject = asset;
    }

    // 打包工具函数（与 Shader 端一致）
    private static float PackRotationScale(float rotationY, float scale, float maxScale)
    {
        uint rot16 = (uint)(Mathf.Repeat(rotationY / 360f, 1f) * 65535.0f) & 0xFFFF;
        uint scale16 = (uint)(Mathf.Clamp01(scale / maxScale) * 65535.0f) & 0xFFFF;
        uint packed = (rot16 << 16) | scale16;
        return System.BitConverter.Int32BitsToSingle((int)packed);
    }

}