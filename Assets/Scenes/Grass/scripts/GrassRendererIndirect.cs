using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;

[ExecuteAlways]
public class GrassRendererIndirect : MonoBehaviour
{
    [Header("数据")]
    public GrassMatrixAsset matrixAsset;
    public Mesh grassMesh;
    public Material grassMaterial; // 使用上面的 Shader

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

    [Header("包围盒（模型空间）")]
    public Vector3 boundsCenter = new Vector3(0, 0.15f, 0);   // 默认草高约0.3米，中心在中间
    public Vector3 boundsExtents = new Vector3(0.1f, 0.15f, 0.1f); // 半尺寸

    [Header("GPU变换")]
    public bool enableAnima = true;
    public ComputeShader grassComputeShader;
    public int rotationSpeed = 15;
    public Color newcolor= Color.green;
    public Vector3 offset=Vector3.zero;

    // GPU 资源
    // private ComputeShader cullingCompute;
    private ComputeBuffer allMatricesBuffer;   // 所有实例的矩阵\
    private int totalCount;
    private Matrix4x4[] allMatrices;
    private Matrix4x4[] visibleArray;
    private int visibleCount = 0;
    private GraphicsBuffer indirectArgsBuffer;
    private uint[] indirectArgs = new uint[5];

    private int kernel;

    private ComputeBuffer intputBuffer;
    private ComputeBuffer outputBuffer;
    private ComputeBuffer newColorBuffer;

    // // Hi‑Z 相关
    // private RenderTexture hiZTexture;
    // private Material hiZMaterial;

    // 初始化标志
    private bool initialized = false;

    void OnEnable()
    {
        // cullingCompute = Resources.Load<ComputeShader>("GrassCulling");
        // hiZMaterial = new Material(Shader.Find("Hidden/HiZBuild"));
        Initialize();
    }

    void OnDisable()
    {
        ReleaseBuffers();
        // if (hiZTexture) { hiZTexture.Release(); DestroyImmediate(hiZTexture); }
    }

    void Initialize()
    {
        ReleaseBuffers();
        if (matrixAsset == null || matrixAsset.matrices == null || matrixAsset.matrices.Length == 0) return;
        
        if(!grassComputeShader) return;

        totalCount = matrixAsset.matrices.Length;
        allMatrices = matrixAsset.matrices;

        visibleArray = new Matrix4x4[totalCount];

        allMatricesBuffer = new ComputeBuffer(totalCount, sizeof(float) * 16);
        intputBuffer = new ComputeBuffer(totalCount, sizeof(float)*16);
        outputBuffer = new ComputeBuffer(totalCount, sizeof(float)*16);
        newColorBuffer = new ComputeBuffer(totalCount, sizeof(float)*4);

        intputBuffer.SetData(allMatrices);
        indirectArgsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1, indirectArgs.Length * sizeof(uint));
        indirectArgs[0] = grassMesh.GetIndexCount(0);
        indirectArgs[1] = 0;//count
        indirectArgs[2] = 0;
        indirectArgs[3] = 0;
        indirectArgs[4] = 0;

        kernel = grassComputeShader.FindKernel("AnimateGrass");
        initialized = true;
    }

    void ReleaseBuffers()
    {
        allMatricesBuffer?.Release();
        indirectArgsBuffer?.Release();
        intputBuffer?.Release();
        outputBuffer?.Release();
        newColorBuffer?.Release();
    }

    void Update()
    {
        if (!initialized) return;

        Camera cam = Camera.main;
        if (cam == null) return;
        Vector3 camPos = cam.transform.position;

        Matrix4x4[] sourceMatrices;
        if (enableAnima)
        {
            grassComputeShader.SetBuffer(kernel, "_InputMatrices", intputBuffer);
            grassComputeShader.SetBuffer(kernel, "_OutputMatrices", outputBuffer);
            grassComputeShader.SetBuffer(kernel, "_OutputColors", newColorBuffer);
            grassComputeShader.SetFloat("_RotationSpeed", rotationSpeed);
            grassComputeShader.SetFloat("_Time", Time.time);
            grassComputeShader.SetVector("_NewColor", newcolor);
            grassComputeShader.SetVector("_Offset", offset);

            int threadGroups = Mathf.CeilToInt(totalCount / 64.0f);
            grassComputeShader.Dispatch(kernel, threadGroups, 1, 1);

            outputBuffer.GetData(visibleArray);
            sourceMatrices = visibleArray;
            grassMaterial.EnableKeyword("_NEWCOLOR");
        }else{
            sourceMatrices = allMatrices;
            grassMaterial.DisableKeyword("_NEWCOLOR");
        }


        Plane[] frustumPlanes = GeometryUtility.CalculateFrustumPlanes(cam);

        Matrix4x4 refMatrix;
        Vector3 worldCenter;
        Vector3 worldMin;
        Vector3 worldMax;
        visibleCount = 0;
        for(int i = 0; i < totalCount;i++)
        {
            Matrix4x4 mat = sourceMatrices[i];
            Vector3 pos = new Vector3(mat.m03, mat.m13, mat.m23);

            // 距离剔除（水平距离）
            float dx = pos.x - camPos.x;
            float dz = pos.z - camPos.z;
            if (dx * dx + dz * dz > cullDistance) continue;

            worldCenter = mat.MultiplyPoint(boundsCenter);
            worldMin = worldCenter-boundsExtents;
            worldMax = worldCenter+boundsExtents;
            bool visibleInFrustum = true;

            foreach(Plane plane in frustumPlanes)
            {
                Vector3 pVertex;
                pVertex.x = plane.normal.x >= 0 ? worldMax.x : worldMin.x;
                pVertex.y = plane.normal.y >= 0 ? worldMax.y : worldMin.y;
                pVertex.z = plane.normal.z >= 0 ? worldMax.z : worldMin.z;

                if (plane.GetDistanceToPoint(pVertex) < 0)
                {
                    visibleInFrustum = false;
                    break;
                }
            }
            if (!visibleInFrustum) 
            {
                // Debug.Log("invisible");
                continue;
            }
            else{
                // Debug.Log("visible");
                visibleArray[visibleCount++] = mat;
            }
            
        }

        if (visibleCount == 0) return;

        allMatricesBuffer.SetData(visibleArray, 0,0,visibleCount);

        indirectArgs[1] = (uint)visibleCount;
        indirectArgsBuffer.SetData(indirectArgs);

        // 4. 绑定可见矩阵到材质
        grassMaterial.SetBuffer("_WorldMatrixBuffer", allMatricesBuffer);
        grassMaterial.SetBuffer("_NewColor", newColorBuffer);

        // 5. 间接绘制（Bounds 需要足够大）
        Graphics.DrawMeshInstancedIndirect(
            grassMesh, 0, grassMaterial,
            new Bounds(Vector3.zero, Vector3.one * 100000),
            indirectArgsBuffer, 0, null, castShadows, receiveShadows, layer
        );
    }

    #if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        // 1. 画一个半透明立方体表示模型空间包围盒（放在一个实例的位置）
        if (ShowGizmo && matrixAsset != null && matrixAsset.matrices != null && matrixAsset.matrices.Length > 0)
        {
            // 取第一个实例的矩阵，仅用于参考位置
            Matrix4x4 refMatrix = matrixAsset.matrices[1];
            Vector3 worldCenter = refMatrix.MultiplyPoint(boundsCenter);
            // 粗略处理缩放：取三个轴的平均缩放
            Vector3 scale = refMatrix.lossyScale;
            Vector3 worldExtents = Vector3.Scale(boundsExtents, scale);

            Gizmos.color = new Color(1, 0, 0, 0.5f);
            Gizmos.DrawCube(worldCenter, worldExtents * 2);

            // 2. 再画几个实例的包围盒，展示覆盖范围
            // Gizmos.color = new Color(0, 1, 0, 0.1f);
            // for (int i = 1; i < Mathf.Min(matrixAsset.matrices.Length, 50); i += 5)
            // {
            //     Matrix4x4 mat = matrixAsset.matrices[i];
            //     Vector3 wc = mat.MultiplyPoint(boundsCenter);
            //     Vector3 ws = Vector3.Scale(boundsExtents, mat.lossyScale);
            //     Gizmos.DrawWireCube(wc, ws * 2);
            // }
        }

        // 3. 在组件位置也绘制一个参考框
        // Gizmos.color = Color.yellow;
        // Gizmos.DrawWireCube(transform.TransformPoint(boundsCenter), boundsExtents * 2);
    }
    #endif
    }