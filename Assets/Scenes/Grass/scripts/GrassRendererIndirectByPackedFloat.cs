using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;

[ExecuteAlways]
public class GrassRendererIndirectByPackedFloat : MonoBehaviour
{
    [Header("数据")]
    public PackedGrassAsset  PackedAsset;
    public Mesh grassMeshLOD0;
    public Material grassMaterialLOD0; // 使用上面的 Shader
    public Mesh grassMeshLOD2;
    public Material grassMaterialLOD2; // 使用上面的 Shader

    // [Header("包围盒")]
    // public Vector3 localBoundsMin = new Vector3(-0.1f, 0f, -0.1f);
    // public Vector3 localBoundsMax = new Vector3(0.1f, 0.3f, 0.1f);

    [Header("剔除设置")]
    public float cullDistance = 80f;
    public bool enableCulling = true;

    [Header("绘制设置")]
    public bool ShowGizmo = true;
    public ShadowCastingMode castShadows = ShadowCastingMode.On;
    public bool receiveShadows = true;
    public int layer = 0;
    public float LODDistance = 40f;

    [Header("包围盒（模型空间）")]
    public float boundsExtents = 0f; // 半尺寸
    public ComputeShader grassMatrix;
    public ComputeShader DepthCulling;
    public ComputeShader GenHiz;

    // GPU 资源
    // private ComputeShader cullingCompute;
    private int totalCount;
    private Vector4[] allPackedData;
    private Vector4[] visibleData;

    private Vector4[] LODData;
    private Vector4[] LODData2;

    private Vector4[] UnCulledData;
    private int visibleCount = 0;
    private GraphicsBuffer indirectArgsBuffer;
    private GraphicsBuffer indirectArgsBuffer2;
    private uint[] indirectArgs = new uint[5];
    private uint[] indirectArgs2 = new uint[5];

    private int F2Mkernel;
    private int CullingKernel;
    private float maxscale;

    private ComputeBuffer intputFloat4Buffer;
    private ComputeBuffer outputFloat4BufferLOD0;
    private ComputeBuffer outputMatrixBufferLOD0;

    private ComputeBuffer outputFloat4BufferLOD2;
    private ComputeBuffer outputMatrixBufferLOD2;

    // // Hi‑Z 相关
    // private RenderTexture hiZTexture;
    // private Material hiZMaterial;

    // 初始化标志
    private bool initialized = false;

    private void OnEnable()
    {
        // cullingCompute = Resources.Load<ComputeShader>("GrassCulling");
        // hiZMaterial = new Material(Shader.Find("Hidden/HiZBuild"));
        Initialize();
    }

    private void OnDisable()
    {
        ReleaseBuffers();
        // if (hiZTexture) { hiZTexture.Release(); DestroyImmediate(hiZTexture); }
    }

    void Initialize()
    {
        ReleaseBuffers();
        if (PackedAsset == null || PackedAsset.packedData == null || PackedAsset.packedData.Length == 0) return;
        
        if(!grassMatrix) return;

        totalCount = PackedAsset.packedData.Length;
        allPackedData = PackedAsset.packedData;

        visibleData = new Vector4[totalCount];
        UnCulledData = new Vector4[totalCount];
        LODData = new Vector4[totalCount];
        LODData2 = new Vector4[totalCount];

        intputFloat4Buffer = new ComputeBuffer(totalCount, sizeof(float)*4);

        outputFloat4BufferLOD0 = new ComputeBuffer(totalCount, sizeof(float)*4);
        outputMatrixBufferLOD0 = new ComputeBuffer(totalCount, sizeof(float)*16);

        outputFloat4BufferLOD2 = new ComputeBuffer(totalCount, sizeof(float)*4);
        outputMatrixBufferLOD2 = new ComputeBuffer(totalCount, sizeof(float)*16);

        
        indirectArgsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1, indirectArgs.Length * sizeof(uint));
        indirectArgs[0] = grassMeshLOD0.GetIndexCount(0);
        indirectArgs[1] = 0;//count
        indirectArgs[2] = 0;
        indirectArgs[3] = 0;
        indirectArgs[4] = 0;

        indirectArgsBuffer2 = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1, indirectArgs.Length * sizeof(uint));
        indirectArgs2[0] = grassMeshLOD2.GetIndexCount(0);
        indirectArgs2[1] = 0;//count
        indirectArgs2[2] = 0;
        indirectArgs2[3] = 0;
        indirectArgs2[4] = 0;

        F2Mkernel = grassMatrix.FindKernel("F2M");
        CullingKernel = DepthCulling.FindKernel("DepthCulling");
        GenHizKernel = GenHiz.FindKernel("GenHiz");
        maxscale = PackedAsset.maxScale;

        initialized = true;
    }

    void ReleaseBuffers()
    {
        indirectArgsBuffer?.Release();
        indirectArgsBuffer2?.Release();
        intputFloat4Buffer?.Release();

        outputMatrixBufferLOD0?.Release();
        outputFloat4BufferLOD0?.Release();
        outputMatrixBufferLOD2?.Release();
        outputFloat4BufferLOD2?.Release();
    }

    void Update()
    {
        if (!initialized) return;

        Camera cam = Camera.main;

        if (cam == null) return;
        
        Vector3 camPos = cam.transform.position;

        Matrix4x4 vMatrix = cam.worldToCameraMatrix;
        Matrix4x4 pMatrix = GL.GetGPUProjectionMatrix(cam.projectionMatrix, false);
        Matrix4x4 vpMatrix = pMatrix * vMatrix;
        visibleCount=0;
        // foreach(Vector4 p in allPackedData)
        // {
        //     Vector3 pos = new Vector3(p.x, p.y, p.z);

        //     // 距离剔除（水平距离）
        //     float distance = Vector3.Distance(pos,camPos);
        //     if (distance < cullDistance)
        //     {
        //         visibleData[visibleCount++]=p;
        //     }

        // }
        //剔除 距离 + 深度 +LOD划分

        Texture depthTex = Shader.GetGlobalTexture("_CameraDepthTexture");

        RenderTexture hiZ = new RenderTexture(1024, 1024, 0, RenderTextureFormat.RFloat);

        hiZ.enableRandomWrite = true;
        hiZ.useMipMap = true;
        hiZ.autoGenerateMips = false;
        hiZ.antiAliasing = 1;
        hiZ.filterMode = FilterMode.Point;
        hiZ.Create();

        Graphics.Blit(depthTex, hiZ);

        for(int mip = 0; mip<hiZ.mipmapCount-1; mip++)
        {
            int width = Mathf.Max(1, hiZ.width >> mip);
            int height = Mathf.Max(1, hiZ.height >> mip);
            int mapSize = min(width, height);

            GenHizKernel.setInt("_mip", mip);
            GenHizKernel.setInt("_mapSize", mapSize);
            GenHizKernel.SetTexture(GenHizKernel, "_destinateMap", hiZ,mip+1);
            GenHizKernel.SetTexture(GenHizKernel, "_resourceMap", hiZ);
            GenHiz.Dispatch(GenHizKernel, Mathf.CeilToInt(width/8.0f), Mathf.CeilToInt(height/8.0f), 1);
        }



        intputFloat4Buffer.SetData(allPackedData,0,0,totalCount);
        DepthCulling.SetVector("_camPos", new Vector4(camPos.x, camPos.y, camPos.z, cullDistance));
        // DepthCulling.SetFloat("_cullDistance",cullDistance);
        DepthCulling.SetBuffer(CullingKernel,"_inputBuffer",intputFloat4Buffer);
        DepthCulling.SetBuffer(CullingKernel,"_CulledVisibleBufferLOD0",outputFloat4BufferLOD0);
        DepthCulling.SetBuffer(CullingKernel,"_CulledVisibleBufferLOD2",outputFloat4BufferLOD2);
        DepthCulling.SetMatrix("_VPMatrix", vpMatrix);
        DepthCulling.SetVector("_ScreenSize", new Vector4(cam.pixelWidth, cam.pixelHeight, boundsExtents,LODDistance));
        
        if (depthTex != null)
        {
            DepthCulling.SetTexture(CullingKernel, "_CameraDepthTexture", depthTex);
        }
        int threadGroups = Mathf.CeilToInt(totalCount / 64.0f);
        DepthCulling.Dispatch(CullingKernel, threadGroups, 1, 1);

        outputFloat4BufferLOD0.GetData(LODData);
        outputFloat4BufferLOD2.GetData(LODData2);
        int lod2Count = LODData2.Length;
        int lod0Count = LODData.Length;
        // foreach(Vector4 p in UnCulledData)
        // {
        //     Vector3 pos = new Vector3(p.x, p.y, p.z);

        //     // LOD划分
        //     float distance = Vector3.Distance(pos,camPos);
        //     if (distance > LODDistance) {
        //         LODData2[lod2Count++]= p;
        //     }
        //     else
        //     {
        //         LODData[lod0Count++]= p;
        //     }
        // }

        //LOD两级
        if(lod0Count != 0)
        {
            // 浮点数据转M矩阵
            outputFloat4BufferLOD0.SetData(LODData,0,0,lod0Count);
            grassMatrix.SetBuffer(F2Mkernel, "_packedInfo", outputFloat4BufferLOD0);
            grassMatrix.SetBuffer(F2Mkernel, "_WMatrix", outputMatrixBufferLOD0);
            grassMatrix.SetFloat("_maxScale", maxscale);
            threadGroups = Mathf.CeilToInt(lod0Count / 64.0f);
            grassMatrix.Dispatch(F2Mkernel, threadGroups, 1, 1);
            
            indirectArgs[1] = (uint)lod0Count;
            indirectArgsBuffer.SetData(indirectArgs);

            // 4. 绑定可见矩阵到材质
            grassMaterialLOD0.SetBuffer("_WorldMatrixBuffer", outputMatrixBufferLOD0);
            grassMaterialLOD0.DisableKeyword("_NEWCOLOR");

            // 5. 间接绘制（Bounds 需要足够大）
            Graphics.DrawMeshInstancedIndirect(
                grassMeshLOD0, 0, grassMaterialLOD0,
                new Bounds(Vector3.zero, Vector3.one * 100000),
                indirectArgsBuffer, 0, null, castShadows, receiveShadows, layer
            );
        }
        if(lod2Count !=0)
        {
            //浮点数据转M矩阵
            outputFloat4BufferLOD2.SetData(LODData2,0,0,lod2Count); 
            grassMatrix.SetBuffer(F2Mkernel, "_packedInfo", outputFloat4BufferLOD2);
            grassMatrix.SetBuffer(F2Mkernel, "_WMatrix", outputMatrixBufferLOD2);
            grassMatrix.SetFloat("_maxScale", maxscale);
            threadGroups = Mathf.CeilToInt(lod2Count / 64.0f);
            grassMatrix.Dispatch(F2Mkernel, threadGroups, 1, 1);

            indirectArgs2[1] = (uint)lod2Count;
            indirectArgsBuffer2.SetData(indirectArgs2);

            // 4. 绑定可见矩阵到材质
            grassMaterialLOD2.SetBuffer("_WorldMatrixBuffer", outputMatrixBufferLOD2);
            grassMaterialLOD2.DisableKeyword("_NEWCOLOR");

            // 5. 间接绘制（Bounds 需要足够大）
            Graphics.DrawMeshInstancedIndirect(
                grassMeshLOD2, 0, grassMaterialLOD2,
                new Bounds(Vector3.zero, Vector3.one * 100000),
                indirectArgsBuffer2, 0, null, castShadows, receiveShadows, layer
            );
        }
    }
}