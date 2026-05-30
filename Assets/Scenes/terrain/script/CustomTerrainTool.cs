using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;

[ExecuteAlways]



public class CustomTerrainTool : MonoBehaviour
{
    [System.Serializable]
    public class TerrainInfo
    {
        public List<CustomTerrainLayer> TerrainLayers;
        public maskInfo maskMaps;
    }
    [System.Serializable]
    public class maskInfo
    {
        public Texture2D mask;
        public Vector4 tilingOffset = new Vector4(1,1,0,0); // xy: tiling, zw: offset
    }

    [Header("Terrain Layers")]
    public List<TerrainInfo> terrainInfos;

    // public List<CustomTerrainLayer> TerrainLayers;
    // [Header("Mask Map")]
    // public List<maskInfo> maskMaps = new List<maskInfo>();

    public Material material;
    public ComputeShader TerrainRenderPass;

    [Header("Compute Shader Kernel Name")]
    [SerializeField] private string kernelName = "CombineTerrain";

    private Material mat;
    private int kernel;
    //--显存资源
    private Texture2DArray RTArrayDiff;
    private Texture2DArray RTArrayNormal;
    private Texture2DArray RTArrayRoughness;
    private RenderTexture outMRO; 
    private RenderTexture outDiff; 
    private RenderTexture outNormal;

    private RenderTexture FirstDiff;
    private RenderTexture FirstNormal;
    private RenderTexture FirstMRO;
    private int LayerCount = 0;
    private int width, height;
    private bool Initialized=false;

    // Start is called before the first frame update
    private void OnEnable() 
    {
        Initialize();
        drawOnce();

        // CombineLayers();
    }

    private void OnDisable() 
    {
        ReleaseResources();
    }

    private void ReleaseResources()
    {
        Initialized = false;

        if(RTArrayDiff != null){ DestroyImmediate(RTArrayDiff);RTArrayDiff = null;}
        if(RTArrayNormal != null){ DestroyImmediate(RTArrayNormal);RTArrayNormal = null;}
        if(RTArrayRoughness != null){ DestroyImmediate(RTArrayRoughness);RTArrayRoughness = null;}

        if(outMRO != null){outMRO.Release();DestroyImmediate(outMRO);outMRO = null; }
        if(outDiff != null){outDiff.Release();DestroyImmediate(outDiff);outDiff = null; }
        if(outNormal != null){outNormal.Release();DestroyImmediate(outNormal);outNormal = null; }
        
    }

    private bool Initialize()
    {
        ReleaseResources();
        if(terrainInfos == null || terrainInfos.Count == 0)
        {
            Debug.LogError("CustomTerrainTool: 请至少在第一个 TerrainInfo 中指定一个有效的 CustomTerrainLayer！");
            return false;
        }
        mat = material;
        List<CustomTerrainLayer> TerrainLayers = terrainInfos[0].TerrainLayers;
        if (!TerrainRenderPass || TerrainLayers == null || TerrainLayers.Count == 0 || !mat ) return false;

        if (TerrainLayers[0].DiffuseMap == null) return false;
        width = TerrainLayers[0].DiffuseMap.width;
        height = TerrainLayers[0].DiffuseMap.height;

        if (!SameWH(TerrainLayers))
        {
            Debug.LogError("CustomTerrainTool: 所有地形层的纹理分辨率必须保持一致！");
            return false;
        }

        // LayerCount = TerrainLayers.Count;
        TextureFormat diffFormat = TerrainLayers[0].DiffuseMap.format;
        TextureFormat normalFormat = TerrainLayers[0].normalMap.format;
        // Debug.Log($"CustomTerrainTool: 使用的法线图格式是 {normalFormat}");
        TextureFormat roughnessFormat = TerrainLayers[0].roughnessMap.format;

        RTArrayDiff = new Texture2DArray(width, height, 4, diffFormat, false);
        RTArrayNormal = new Texture2DArray(width, height, 4, normalFormat, false);
        RTArrayRoughness = new Texture2DArray(width, height, 4, roughnessFormat, false);
        RTArrayDiff.wrapMode = TextureWrapMode.Repeat;
        RTArrayNormal.wrapMode = TextureWrapMode.Repeat;

        outMRO     = CreateRenderTexture(width, height, Color.black);
        outDiff    = CreateRenderTexture(width, height, Color.black);
        outNormal  = CreateRenderTexture(width, height, Color.black);
        FirstDiff  = CreateRenderTexture(width, height, Color.black);
        FirstNormal = CreateRenderTexture(width, height,  new Color(0f, 0f, 0f, 0f));
        FirstMRO   = CreateRenderTexture(width, height, Color.black);

        try
        {
            kernel = TerrainRenderPass.FindKernel(kernelName);
            Initialized = true;
        }
        catch
        {
            Debug.LogError($"CustomTerrainTool: 在 Compute Shader 中找不到名为 '{kernelName}' 的核函数！");
            Initialized = false;
        }

        return Initialized;
    }

    void CopyTex(List<CustomTerrainLayer> TerrainLayers)
    {
        int layerCount = TerrainLayers.Count;
        for(int i = 0; i<layerCount; i++)
        {
            Debug.Log($"CustomTerrainTool: 正在复制第 {i} 层纹理到 GPU，DiffuseMap 是 {TerrainLayers[i].DiffuseMap.name}，NormalMap 是 {TerrainLayers[i].normalMap.name}，RoughnessMap 是 {TerrainLayers[i].roughnessMap.name}");
            Graphics.CopyTexture(TerrainLayers[i].DiffuseMap, 0, 0, RTArrayDiff, i, 0);
            Graphics.CopyTexture(TerrainLayers[i].normalMap,  0, 0, RTArrayNormal, i, 0);
            Graphics.CopyTexture(TerrainLayers[i].roughnessMap, 0, 0, RTArrayRoughness, i, 0);
        }
    }

    #if UNITY_EDITOR
    private void EnforceNormalMapSettings(Texture2D tex)
    {
        if (tex == null) return;

        string path = AssetDatabase.GetAssetPath(tex);
        if (string.IsNullOrEmpty(path)) return;

        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) return;

        // 如果该图没有被勾选为 Normal Map，或者它的独立平台压缩格式不正确
        if (importer.textureType != TextureImporterType.NormalMap)
        {
            Debug.LogWarning($"CustomTerrainTool: 检测到 {tex.name} 不是法线图格式，正在自动将其转换为 DXT5nm...");
            
            // 强制修改为法线图类型
            importer.textureType = TextureImporterType.NormalMap;
            // importer.convertToNormalMap = false; // 如果本身已经是法线蓝图，不需要从高度图生成
            
            // 刷新并重新导入资产
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        }
    }
    #endif

    private RenderTexture CreateRenderTexture(int w, int h, Color clearColor)
    {
        RenderTexture rt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        rt.enableRandomWrite = true;
        rt.Create();

        // 清除为指定颜色
        RenderTexture oldRT = RenderTexture.active;
        RenderTexture.active = rt;
        GL.Clear(true, true, clearColor);
        RenderTexture.active = oldRT;

        return rt;
    }
    
    private bool SameWH(List<CustomTerrainLayer> TerrainLayers)
    {
        foreach(var l in TerrainLayers)
        {
            if (l == null || l.DiffuseMap == null || l.normalMap == null) return false;

            if(l.DiffuseMap.width!= width || l.DiffuseMap.height!=height || l.normalMap.width!= width || l.normalMap.height != height)
            {
                return false;
            }
        }
        return true;
    }

    #if UNITY_EDITOR
    private void OnValidate()
    {
        // 规避特殊生命周期冲突：确保只在非编译期且物体处于激活状态时执行
        if (gameObject.activeInHierarchy)
        {
            // 延迟一帧或直接调用初始化，让 Compute Shader 重新绑定并执行
            // 这里加个保护：只有当主要资源（如 ComputeShader）存在时才触发
            if (TerrainRenderPass != null)
            {
                // 强制重新执行初始化与内存构建
                Initialize();
                drawOnce();
            }
        }
    }
    #endif

    // export assets and assign to material
    public void CombineLayers()
    {
        Texture2D savedDiff = RenderTextureToTexture2D(outDiff);
        Texture2D savedNormal = RenderTextureToTexture2D(outNormal);
        Texture2D savedMRO = RenderTextureToTexture2D(outMRO);

        string folderPath = "Assets/Scenes/terrain/CombinedTextures/";
        if (!Directory.Exists(folderPath))
            Directory.CreateDirectory(folderPath);

        string diffPath = folderPath + "Combined_Diffuse.png";
        string normalPath = folderPath + "Combined_Normal.png";
        string mroPath = folderPath + "Combined_MRO.png";

        SaveTextureAsPNG(savedDiff,diffPath);
        SetTextureNotSRGB(diffPath);
        AssetDatabase.Refresh();

        SaveTextureAsPNG(savedNormal, normalPath);
        SetTextureNotSRGB(normalPath); // 确保法线图不被当作 sRGB 处理
        AssetDatabase.Refresh();

        SaveTextureAsPNG(savedMRO, mroPath);
        SetTextureNotSRGB(mroPath); // 确保法线图不被当作 sRGB 处理
        AssetDatabase.Refresh();
        Debug.Log($"CustomTerrainTool: 已保存合成纹理到 {folderPath}");

        Texture2D combinedDiff = AssetDatabase.LoadAssetAtPath<Texture2D>(diffPath);
        Texture2D combinedNormal = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
        Texture2D combinedMRO = AssetDatabase.LoadAssetAtPath<Texture2D>(mroPath);

        string layerPath = $"{folderPath}/CombinedLayer.asset";
        CustomTerrainLayer layer = AssetDatabase.LoadAssetAtPath<CustomTerrainLayer>(layerPath);
        if (layer == null)
        {
            layer = ScriptableObject.CreateInstance<CustomTerrainLayer>();
            AssetDatabase.CreateAsset(layer, layerPath);
        }

        layer.DiffuseMap = combinedDiff;
        layer.normalMap = combinedNormal;  
        layer.roughnessMap = combinedMRO;
        AssetDatabase.SaveAssets();

        if (mat != null)
        {
            mat.SetTexture("_MainTex", combinedDiff);
            mat.SetTexture("_NormalMap", combinedNormal);
            mat.SetTexture("_MRO", combinedMRO);
            EditorUtility.SetDirty(mat);
        }
        Debug.Log($"地形纹理已保存到 {folderPath}，CustomTerrainLayer 已更新，材质已赋值。");

        DestroyImmediate(savedDiff);
        DestroyImmediate(savedNormal);
        DestroyImmediate(savedMRO);
    }

    void drawOnce()
    {
        if(!Initialized) return;

        RenderTexture tempDiff = null;
        RenderTexture tempNormal = null;
        RenderTexture tempMRO = null;

        foreach(var info in terrainInfos)
        {
            // var info = terrainInfos[0];
            CopyTex(info.TerrainLayers);
            
            TerrainRenderPass.SetTexture(kernel, "_DiffTexs", RTArrayDiff);
            TerrainRenderPass.SetTexture(kernel, "_NormalTexs", RTArrayNormal);
            TerrainRenderPass.SetTexture(kernel, "_mask", info.maskMaps.mask); 
            TerrainRenderPass.SetTexture(kernel, "_RoughnessTexs", RTArrayRoughness);
            TerrainRenderPass.SetTexture(kernel, "_FirstDiff", FirstDiff);
            TerrainRenderPass.SetTexture(kernel, "_FirstNormal", FirstNormal);
            TerrainRenderPass.SetTexture(kernel, "_FirstMRO", FirstMRO);
            TerrainRenderPass.SetVector( "_tillingOffset", info.maskMaps.tilingOffset);
            TerrainRenderPass.SetInt( "_layerCount", info.TerrainLayers.Count);
            Debug.Log($"CustomTerrainTool: 正在处理 TerrainInfo，包含 {info.TerrainLayers.Count} 层，使用的 Mask 是 {info.maskMaps.mask.name}，tilingOffset 是 {info.maskMaps.tilingOffset}");
            TerrainRenderPass.SetTexture(kernel, "_outDiff", outDiff);
            TerrainRenderPass.SetTexture(kernel, "_outNormal", outNormal);
            TerrainRenderPass.SetTexture(kernel, "_outMRO", outMRO);

            int threadGroupsX = Mathf.CeilToInt(width / 8f);
            int threadGroupsY = Mathf.CeilToInt(height / 8f);
            TerrainRenderPass.Dispatch(kernel, threadGroupsX, threadGroupsY, 1);

            tempDiff = FirstDiff;
            tempNormal = FirstNormal;
            tempMRO = FirstMRO;

            FirstDiff = outDiff;
            FirstNormal = outNormal;
            FirstMRO = outMRO;

            outDiff = tempDiff;
            outNormal = tempNormal;
            outMRO = tempMRO;
        }

        if (terrainInfos.Count % 2 == 0)
        {
            // 偶数次交换后，最终结果在 _First* 中，需要交换回来
            tempDiff = FirstDiff;
            tempNormal = FirstNormal;
            tempMRO = FirstMRO;

            FirstDiff = outDiff;
            FirstNormal = outNormal;
            FirstMRO = outMRO;

            outDiff = tempDiff;
            outNormal = tempNormal;
            outMRO = tempMRO;
        }
        mat.SetTexture("_MRO",outMRO);
        mat.SetTexture("_MainTex",outDiff);
        mat.SetTexture("_NormalMap",outNormal);
    }

    void SetTextureNotSRGB(string assetPath)
    {
        TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (importer != null)
        {
            importer.sRGBTexture = false;   // 不勾选 sRGB
            importer.SaveAndReimport();
        }
    }

    Texture2D RenderTextureToTexture2D(RenderTexture rt)
    {
        Texture2D tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
        RenderTexture.active = rt;
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply();
        RenderTexture.active = null;
        return tex;
    }

    // 辅助方法：保存 Texture2D 为 PNG
    void SaveTextureAsPNG(Texture2D tex, string path)
    {
        byte[] bytes = tex.EncodeToPNG();
        File.WriteAllBytes(path, bytes);
    }
}
