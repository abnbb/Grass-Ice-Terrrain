using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;

#if UNITY_EDITOR
using UnityEditor;
#endif
[ExecuteAlways]
public class GrassRendererAlways : MonoBehaviour
{
    [Header("数据")]
    public GrassMatrixAsset matrixAsset;          // 保存了所有矩阵的资产
    public Mesh grassMesh;                        // 草模型网格
    public Material grassMaterial;                // 草材质（需启用 GPU Instancing）

    [Header("剔除设置")]
    public float cullDistance = 80f;              // 超过此距离不渲染
    public bool enableCulling = true;

    [Header("绘制设置")]
    public ShadowCastingMode castShadows = ShadowCastingMode.On;
    public bool receiveShadows = true;
    public int layer = 0;

    // 内部缓存
    private Matrix4x4[] allMatrices;              // 全部原始矩阵
    private int totalInstanceCount;
    private int batchSize = 1023;                 // 单次绘制上限

    // 每帧临时可见列表（避免GC，保留引用）
    private List<Matrix4x4> visibleList = new List<Matrix4x4>();
    private Matrix4x4[][] batchArrays;            // 预分配的分批数组

    private bool isSetup = false;


    private void OnEnable() {
        Setup();
    #if UNITY_EDITOR
        if (!Application.isPlaying)
            EditorApplication.update += EditorUpdate;
    #endif
    }

    private void OnDisable() {
    #if UNITY_EDITOR
        if (!Application.isPlaying)
            EditorApplication.update -= EditorUpdate;
    #endif
    }

    void Setup()
    {
        if (matrixAsset == null || matrixAsset.matrices == null || matrixAsset.matrices.Length == 0)
        {
            Debug.LogError("GrassRenderer: 没有有效的矩阵资产。");
            enabled = false;
            return;
        }

        allMatrices = matrixAsset.matrices;
        totalInstanceCount = allMatrices.Length;

        // 预分配最大可能的批次数目（最坏情况：全部可见，每批 1023 个）
        int maxBatches = Mathf.CeilToInt(totalInstanceCount / (float)batchSize);
        batchArrays = new Matrix4x4[maxBatches][];
        for (int i = 0; i < maxBatches; i++)
        {
            int size = Mathf.Min(batchSize, totalInstanceCount - i * batchSize);
            batchArrays[i] = new Matrix4x4[size];
        }
        isSetup = true;
    }

    void EditorUpdate()
    {
        if (Application.isPlaying) 
            return;

        if (!isSetup && matrixAsset != null)
            Setup();

        drawGrass();
        SceneView.RepaintAll(); // 强制刷新场景视图
    }

    void Update()
    {
        if (Application.isPlaying)
            drawGrass();
    }


    void drawGrass()
    {
        if (!isSetup || grassMesh == null || grassMaterial == null)
            return;

        // 1. 距离剔除（CPU）
        Camera cam = Application.isPlaying ? Camera.main : GetSceneViewCamera();
        Vector3 camPos = cam.transform.position;

        visibleList.Clear();
        if (enableCulling)
        {
            for (int i = 0; i < totalInstanceCount; i++)
            {
                Matrix4x4 mat = allMatrices[i];
                Vector3 pos = new Vector3(mat.m03, mat.m13, mat.m23);
                if (Vector3.Distance(camPos, pos) <= cullDistance)
                {
                    visibleList.Add(mat);
                }
            }
        }
        else
        {
            // 不剔除，全部添加（注意：全部绘制可能导致超高 DC，仅用于测试）
            visibleList.AddRange(allMatrices);
        }

        int visibleCount = visibleList.Count;
        if (visibleCount == 0) return;

        // 2. 分批填充预分配数组
        int batchCount = Mathf.CeilToInt(visibleCount / (float)batchSize);
        for (int b = 0; b < batchCount; b++)
        {
            int start = b * batchSize;
            int count = Mathf.Min(batchSize, visibleCount - start);
            Matrix4x4[] batchArray = batchArrays[b];

            // 复制可见矩阵到批次数组
            for (int i = 0; i < count; i++)
            {
                batchArray[i] = visibleList[start + i];
            }

            // 3. 绘制本批实例
            Graphics.DrawMeshInstanced(
                grassMesh,
                0,
                grassMaterial,
                batchArray,
                count,
                null,
                castShadows,
                receiveShadows,
                layer
            );
        }
    }
    Camera GetSceneViewCamera()
    {
        #if UNITY_EDITOR
        if (SceneView.lastActiveSceneView != null)
            return SceneView.lastActiveSceneView.camera;
        #endif
        return Camera.main;
    }
    void OnDrawGizmosSelected()
    {
        // 在编辑器中可视化剔除距离
        if (enableCulling)
        {
            Gizmos.color = Color.yellow;
            Vector3 camPos = Camera.main ? Camera.main.transform.position : Vector3.zero;
            Gizmos.DrawWireSphere(camPos, cullDistance);
        }
    }
}