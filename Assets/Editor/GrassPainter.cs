using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

public class GrassPainter : EditorWindow
{
    // 要刷的草模型预制体
    private GameObject grassPrefab;
    // 父物体，用来组织生成的草
    private Transform grassHolder;

    // 用于射线检测的图层遮罩
    private LayerMask paintLayer = ~0; // 默认所有层

    // 笔刷参数
    private float brushRadius = 2f;
    private float density = 5f; // 每平方米数量，Plane 模式可以设高一些
    private float minNormalY = 0.5f; // 法线Y分量最小值(过滤陡峭面，1为完全水平)

    // 随机缩放
    private float scaleMin = 0.8f;
    private float scaleMax = 1.2f;

    // 随机旋转
    private bool randomRotationY = true;
    private float rotationYRange = 360f;

    // 随机倾斜（让草不完全垂直于表面）
    private bool enableRandomTilt = true;
    private float maxTiltAngle = 15f;

    // 性能提示
    private bool useGPUInstancing = true;

    private float minSpacing = 0.3f; // 两棵草之间的最小距离
    private bool preventOverlap = true; // 是否防止重叠

    // ---------- RT 绘制相关 ----------
    private RenderTexture grassRT;                 // 绘制的目标 RT
    private Vector3 rtWorldOrigin = Vector3.zero;  // RT 对应的世界原点
    private Vector2 rtWorldSize = new Vector2(10f, 10f); // RT 覆盖的世界区域大小（X = 宽，Y = 长）
    private float brushHarness = 0.8f;             // 笔刷软硬(0-1)
    private Material brushMaterial;               // 用于绘制的材质
    private const string brushShaderName = "Hidden/GrassBrush";
    // --------------------------------

    // 持久化相关
    private Texture2D persistentTexture;   // 绑定的持久化纹理资源
    private string persistentTexturePath = "Assets/Scenes/Grass/Assets/GrassMaskTexture.asset";

    [MenuItem("Tools/Grass Painter (Plane Mode)")]
    public static void ShowWindow()
    {
        GetWindow<GrassPainter>("Grass Painter (Plane)");
    }

    private void OnEnable()
    {
        SceneView.duringSceneGui += OnSceneGUI;
        // 尝试查找绘制 Shader 并创建材质
        Shader shader = Shader.Find(brushShaderName);
        if (shader != null)
        {
            brushMaterial = new Material(shader);
            brushMaterial.hideFlags = HideFlags.HideAndDontSave;
        }
        else
        {
            Debug.LogWarning($"未找到 Shader '{brushShaderName}'，RT 绘制功能将不可用。");
        }

        // 加载持久化纹理资源
        persistentTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(persistentTexturePath);
        
        // 恢复 RT 内容（如果有 persistentTexture 且 RT 不为空就复制过去）
        if (grassRT != null && persistentTexture != null)
        {
            // 只有 RT 是黑色且 persistentTexture 有内容时才恢复
            // 简单判断：如果 RT 没有被绘制过，可以通过像素检测，这里先无条件恢复
            Graphics.Blit(persistentTexture, grassRT);
        }
    }

    private void OnDisable()
    {
        SceneView.duringSceneGui -= OnSceneGUI;
        if (brushMaterial != null)
            DestroyImmediate(brushMaterial);
    }

    private void OnGUI()
    {
        GUILayout.Label("草地笔刷设置 (Plane模式)", EditorStyles.boldLabel);

        EditorGUI.BeginChangeCheck();
        grassPrefab = (GameObject)EditorGUILayout.ObjectField("草模型预制体", grassPrefab, typeof(GameObject), false);
        if (EditorGUI.EndChangeCheck())
        {
            if (grassPrefab != null && useGPUInstancing)
            {
                Debug.Log("提示：确保草材质启用了 'Enable GPU Instancing'");
            }
        }

        grassHolder = (Transform)EditorGUILayout.ObjectField("父物体", grassHolder, typeof(Transform), true);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("图层设置", EditorStyles.boldLabel);
        paintLayer = EditorGUILayout.LayerField("绘画图层", (int)Mathf.Log(paintLayer.value, 2));
        paintLayer = 1 << paintLayer;

        EditorGUILayout.Space();
        brushRadius = EditorGUILayout.Slider("笔刷半径", brushRadius, 0.1f, 20f);
        density = EditorGUILayout.Slider("密度(每平方米)", density, 0.1f, 20f);

        EditorGUILayout.LabelField("密度控制", EditorStyles.boldLabel);
        density = EditorGUILayout.Slider("基础密度(每平方米)", density, 0.1f, 20f);

        preventOverlap = EditorGUILayout.Toggle("防止重叠", preventOverlap);
        if (preventOverlap)
        {
            minSpacing = EditorGUILayout.Slider("最小间距", minSpacing, 0.05f, 2f);
            EditorGUILayout.HelpBox(
                "启用后，新生成的草会与周围已存在的草保持最小间距。\n" +
                "持续涂抹同一区域不会导致密度无限增长。",
                MessageType.Info);
        }

        EditorGUILayout.LabelField("表面过滤", EditorStyles.boldLabel);
        minNormalY = EditorGUILayout.Slider("最小法线Y (0=陡坡可, 1=仅平地)", minNormalY, 0f, 1f);
        if (minNormalY < 0.5f)
            EditorGUILayout.HelpBox("法线Y值过低，草可能种在垂直面或倒悬面", MessageType.Warning);

        EditorGUILayout.LabelField("缩放", EditorStyles.boldLabel);
        scaleMin = EditorGUILayout.Slider("最小缩放", scaleMin, 0.1f, 5f);
        scaleMax = EditorGUILayout.Slider("最大缩放", scaleMax, 0.1f, 5f);

        EditorGUILayout.LabelField("旋转（法线对齐后的随机）", EditorStyles.boldLabel);
        randomRotationY = EditorGUILayout.Toggle("随机Y轴旋转", randomRotationY);
        if (randomRotationY)
            rotationYRange = EditorGUILayout.Slider("旋转范围", rotationYRange, 0f, 360f);

        enableRandomTilt = EditorGUILayout.Toggle("随机倾斜", enableRandomTilt);
        if (enableRandomTilt)
        {
            maxTiltAngle = EditorGUILayout.Slider("最大倾斜角度", maxTiltAngle, 0f, 90f);
            EditorGUILayout.HelpBox("草会随机偏离表面法线一定角度，更自然。", MessageType.None);
        }

        EditorGUILayout.Space();
        useGPUInstancing = EditorGUILayout.Toggle("提示使用GPU Instancing", useGPUInstancing);

        // ========== RT 绘制设置 ==========
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("RT 绘制设置", EditorStyles.boldLabel);

        grassRT = (RenderTexture)EditorGUILayout.ObjectField("目标 RenderTexture", grassRT, typeof(RenderTexture), false);
        if (grassRT != null)
        {
            // 自动设为全局属性，供其他 Shader 使用
            Shader.SetGlobalTexture("_GrassORSoilRT", grassRT);
        }

        rtWorldOrigin = EditorGUILayout.Vector3Field("世界原点", rtWorldOrigin);
        rtWorldSize = EditorGUILayout.Vector2Field("世界尺寸 (X, Z)", rtWorldSize);
        brushHarness = EditorGUILayout.Slider("笔刷软硬", brushHarness, 0f, 1f);

        if (brushMaterial == null)
        {
            EditorGUILayout.HelpBox($"未找到 Shader '{brushShaderName}'，请确认该 Shader 存在于项目中。", MessageType.Warning);
        }
        else
        {
            EditorGUILayout.HelpBox("RT 绘制已就绪。绘画 / 擦除时自动向 RT 绘制标记。", MessageType.Info);
        }

        if (GUILayout.Button("手动保存 RT 到资源"))
        {
            SaveRTToPersistentAsset();
            Debug.Log("RT 已手动保存。");
        }

        if (GUILayout.Button("清除持久化 RT 数据"))
        {
            if (persistentTexture != null)
            {
                // 清空纹理为黑色
                Color[] black = new Color[persistentTexture.width * persistentTexture.height];
                for (int i = 0; i < black.Length; i++) black[i] = Color.black;
                persistentTexture.SetPixels(black);
                persistentTexture.Apply();
                EditorUtility.SetDirty(persistentTexture);
                AssetDatabase.SaveAssets();
                
                // 同步清空 grassRT
                if (grassRT != null)
                    Graphics.Blit(Texture2D.blackTexture, grassRT);
            }
            Debug.Log("持久化 RT 数据已清除。");
        }

        // ===================================

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "使用方法:\n" +
            "1. 拖入草预制体，设置父物体和图层\n" +
            "2. 在Scene视图中按住Ctrl+左键绘画\n" +
            "3. 按住Ctrl+Shift+左键擦除\n" +
            "4. 确保目标平面有Collider并处于正确图层",
            MessageType.Info);

        EditorGUILayout.Space();
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("清除所有草"))
        {
            if (EditorUtility.DisplayDialog("确认", "确定要清除所有草吗？", "确定", "取消"))
            {
                ClearAllGrass();
            }
        }

        if (GUILayout.Button("为所有草启用GPU Instancing"))
        {
            EnableGPUInstancingOnAllGrass();
        }
        EditorGUILayout.EndHorizontal();
    }

    private void OnSceneGUI(SceneView sceneView)
    {
        if (grassPrefab == null)
            return;

        // 自动创建或查找父物体
        if (grassHolder == null)
        {
            GameObject holder = GameObject.Find("GrassHolder_Plane");
            if (holder == null)
            {
                holder = new GameObject("GrassHolder_Plane");
                Undo.RegisterCreatedObjectUndo(holder, "Create Grass Holder");
            }
            grassHolder = holder.transform;
        }

        // 确保 RT 全局属性持续有效（因为 Play 或重新编译可能失效）
        if (grassRT != null)
            Shader.SetGlobalTexture("_GrassORSoilRT", grassRT);

        Event e = Event.current;
        Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
        RaycastHit hit;

        Vector3? paintPoint = null;
        Vector3? paintNormal = null;
        Vector2 paintUV= new Vector2(0,0);

        if (Physics.Raycast(ray, out hit, 1000f, paintLayer))
        {
            if (hit.normal.y >= minNormalY)
            {
                paintPoint = hit.point;
                paintNormal = hit.normal;
                paintUV = hit.textureCoord;
            }
        }

        // 绘制笔刷预览
        if (paintPoint.HasValue)
        {
            Handles.color = new Color(0, 1, 0, 0.3f);
            Handles.DrawSolidDisc(paintPoint.Value, paintNormal.Value, brushRadius);
            Handles.color = Color.green;
            Handles.DrawWireDisc(paintPoint.Value, paintNormal.Value, brushRadius);

            // Ctrl+左键绘画
            if (e.control && !e.shift && (e.type == EventType.MouseDown || e.type == EventType.MouseDrag) && e.button == 0)
            {
                PaintGrass(paintPoint.Value, paintNormal.Value);
                DrawOnRT(paintUV, isGrass: true);   // 绘制草地标记
                e.Use();
            }

            // Ctrl+Shift+左键擦除
            if (e.control && e.shift && (e.type == EventType.MouseDown || e.type == EventType.MouseDrag) && e.button == 0)
            {
                EraseGrass(paintPoint.Value);
                DrawOnRT(paintUV, isGrass: false);  // 擦除草地标记
                e.Use();
            }
        }

        sceneView.Repaint();
    }

    /// <summary>
    /// 在 grassRT 上绘制笔刷形状（使用 Hidden/GrassBrush Shader）
    /// </summary>
    private void DrawOnRT(Vector2 texCoords, bool isGrass)
    {
        if (grassRT == null || brushMaterial == null)
            return;

        // 将世界坐标映射到 RT UV 空间
        Vector2 uv =  texCoords;

        // 边界检查，超出 RT 范围则不画
        if (uv.x < 0f || uv.x > 1f || uv.y < 0f || uv.y > 1f)
            return;
        // 计算笔刷在 UV 空间的大小 (取 X 轴对应世界宽度)
        float brushSizeUV = brushRadius/rtWorldSize.x;

        // 设置 Shader 参数
        brushMaterial.SetVector("_pointUV", uv);
        brushMaterial.SetFloat("_BrushSize", brushSizeUV);
        brushMaterial.SetFloat("_BrushHarness", brushHarness);
        brushMaterial.SetFloat("_isGrass", isGrass ? 1f : 0f);

        // 使用临时 RT 避免同时读写
        RenderTexture tempRT = RenderTexture.GetTemporary(grassRT.descriptor);
        Graphics.Blit(grassRT, tempRT);
        Graphics.Blit(tempRT, grassRT, brushMaterial);
        RenderTexture.ReleaseTemporary(tempRT);

        // SaveRTToPersistentAsset();
    }

    private void PaintGrass(Vector3 center, Vector3 normal)
    {
        int pointsToCreate = Mathf.RoundToInt(brushRadius * brushRadius * Mathf.PI * density);

        List<Vector3> existingPositions = new List<Vector3>();
        if (preventOverlap)
        {
            for (int i = 0; i < grassHolder.childCount; i++)
            {
                Transform child = grassHolder.GetChild(i);
                float distance = Vector3.Distance(
                    new Vector3(child.position.x, 0, child.position.z),
                    new Vector3(center.x, 0, center.z));
                if (distance <= brushRadius + minSpacing)
                    existingPositions.Add(child.position);
            }
        }

        int createdCount = 0;
        int maxAttempts = pointsToCreate * 3;

        for (int i = 0; i < maxAttempts && createdCount < pointsToCreate; i++)
        {
            float angle = Random.Range(0f, Mathf.PI * 2f);
            float distance = Mathf.Sqrt(Random.Range(0f, 1f)) * brushRadius;
            Vector2 offset2D = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance;

            Vector3 tangent, binormal;
            if (Mathf.Abs(normal.y) < 0.999f)
                tangent = Vector3.Cross(normal, Vector3.up).normalized;
            else
                tangent = Vector3.Cross(normal, Vector3.forward).normalized;
            binormal = Vector3.Cross(normal, tangent).normalized;

            Vector3 offset = tangent * offset2D.x + binormal * offset2D.y;
            Vector3 samplePoint = center + offset;

            if (preventOverlap)
            {
                bool tooClose = false;
                Vector3 samplePointXZ = new Vector3(samplePoint.x, 0, samplePoint.z);
                foreach (Vector3 existingPos in existingPositions)
                {
                    Vector3 existingXZ = new Vector3(existingPos.x, 0, existingPos.z);
                    if (Vector3.Distance(samplePointXZ, existingXZ) < minSpacing)
                    {
                        tooClose = true;
                        break;
                    }
                }
                if (tooClose)
                    continue;
            }

            RaycastHit hit;
            if (Physics.Raycast(samplePoint + normal * 1f, -normal, out hit, 2f, paintLayer))
            {
                if (hit.normal.y < minNormalY)
                    continue;

                GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(grassPrefab);
                instance.transform.position = hit.point;

                Quaternion baseRotation = Quaternion.FromToRotation(Vector3.up, hit.normal);
                Quaternion yRotation = Quaternion.identity;
                if (randomRotationY)
                    yRotation = Quaternion.Euler(0, Random.Range(0, rotationYRange), 0);
                Quaternion tiltRotation = Quaternion.identity;
                if (enableRandomTilt)
                {
                    float pitch = Random.Range(-maxTiltAngle, maxTiltAngle);
                    float roll = Random.Range(-maxTiltAngle, maxTiltAngle);
                    tiltRotation = Quaternion.Euler(pitch, 0, roll);
                }

                instance.transform.rotation = baseRotation * yRotation * tiltRotation;
                instance.transform.localScale = Vector3.one * Random.Range(scaleMin, scaleMax);
                instance.transform.parent = grassHolder;

                Undo.RegisterCreatedObjectUndo(instance, "Paint Grass");

                if (preventOverlap)
                    existingPositions.Add(instance.transform.position);

                createdCount++;
            }
        }
    }

    private void SaveRTToPersistentAsset()
    {
        if (grassRT == null)
            return;

        // 如果还没有持久化纹理资源，创建一个
        if (persistentTexture == null)
        {
            // 确保路径目录存在
            string directory = System.IO.Path.GetDirectoryName(persistentTexturePath);
            if (!System.IO.Directory.Exists(directory))
                System.IO.Directory.CreateDirectory(directory);

            persistentTexture = new Texture2D(grassRT.width, grassRT.height,TextureFormat.RGBA32, false);
            AssetDatabase.CreateAsset(persistentTexture, persistentTexturePath);
            Debug.Log($"创建持久化纹理资源：{persistentTexturePath}");
        }

        // 如果尺寸不匹配，重新调整 Texture2D
        if (persistentTexture.width != grassRT.width || persistentTexture.height != grassRT.height)
        {
            persistentTexture.Reinitialize(grassRT.width, grassRT.height);
            Debug.LogWarning("持久化纹理大小已调整，旧数据被清除。");
        }

        // 读取 RT 像素到 Texture2D
        RenderTexture previousActive = RenderTexture.active;
        RenderTexture.active = grassRT;
        persistentTexture.ReadPixels(new Rect(0, 0, grassRT.width, grassRT.height), 0, 0);
        persistentTexture.Apply();
        RenderTexture.active = previousActive;

        // 标记资源为脏，触发自动保存
        EditorUtility.SetDirty(persistentTexture);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }


    private void EraseGrass(Vector3 center)
    {
        for (int i = grassHolder.childCount - 1; i >= 0; i--)
        {
            Transform child = grassHolder.GetChild(i);
            float distance = Vector3.Distance(
                new Vector3(child.position.x, 0, child.position.z),
                new Vector3(center.x, 0, center.z));
            if (distance <= brushRadius)
            {
                Undo.DestroyObjectImmediate(child.gameObject);
            }
        }
    }

    private void ClearAllGrass()
    {
        if (grassHolder != null)
        {
            for (int i = grassHolder.childCount - 1; i >= 0; i--)
            {
                Undo.DestroyObjectImmediate(grassHolder.GetChild(i).gameObject);
            }
        }
    }

    private void EnableGPUInstancingOnAllGrass()
    {
        if (grassPrefab == null)
        {
            Debug.LogWarning("请先设置草预制体");
            return;
        }

        Renderer prefabRenderer = grassPrefab.GetComponent<Renderer>();
        if (prefabRenderer == null)
        {
            Debug.LogWarning("预制体上没有Renderer组件");
            return;
        }

        Material mat = prefabRenderer.sharedMaterial;
        if (mat != null)
        {
            mat.enableInstancing = true;
            Debug.Log($"已为材质 '{mat.name}' 启用GPU Instancing");
        }

        EditorUtility.SetDirty(mat);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("材质已更新，场景中的草会自动应用此设置");
    }
}