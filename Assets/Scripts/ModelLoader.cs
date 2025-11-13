using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System.IO;
using System.Linq;
using System;
using Newtonsoft.Json;

[ExecuteInEditMode]
public class ModelLoader : MonoBehaviour
{
    // ===========================================================
    // 相机标定相关
    // ===========================================================
    [Header("相机标定设置")]
    [Tooltip("是否使用相机标定文件")]
    [SerializeField] private bool _useCameraCalibration = true;
    [Tooltip("标定文件路径")]
    [SerializeField] private string _calibrationFilePath = "CalibrationData/camera_calibration.json";
    
    /// <summary>
    /// 标定数据类，用于JSON序列化/反序列化
    /// 与CameraCalibrator中的CalibrationData保持一致
    /// </summary>
    [System.Serializable]
    private class CalibrationData
    {
        public double[] cameraMatrix;     // 3x3相机矩阵
        public double[] distCoeffs;       // 畸变系数
        public int imageWidth;            // 图像宽度
        public int imageHeight;           // 图像高度
        public double rmsError;           // RMS误差
        public int frameCount;            // 使用的帧数
        public int chessboardWidth;       // 棋盘格宽度
        public int chessboardHeight;      // 棋盘格高度
        public float squareSize;          // 棋盘格方块大小
        public string timestamp;          // 标定时间戳
        public string unityVersion;       // Unity版本
        public string platform;           // 平台信息
    }
    
    /// <summary>
    /// 从标定文件加载相机参数并应用到相机
    /// </summary>
    /// <param name="camera">要应用参数的相机</param>
    private void ApplyCameraCalibration(Camera camera)
    {
        if (!_useCameraCalibration)
        {
            Debug.Log("未启用相机标定，使用默认相机参数");
            return;
        }
        
        try
        {
            // 构建完整的标定文件路径
            string fullPath = _calibrationFilePath;
            if (!Path.IsPathRooted(fullPath))
            {
                // 如果是相对路径，尝试在项目根目录下查找
                fullPath = Path.Combine(Application.dataPath, "..", fullPath);
            }
            
            // 检查文件是否存在
            if (!File.Exists(fullPath))
            {
                // 尝试查找最新的标定文件
                string calibDir = Path.Combine(Application.dataPath, "..", "CalibrationData");
                if (Directory.Exists(calibDir))
                {
                    string[] calibFiles = Directory.GetFiles(calibDir, "camera_calibration_*.json");
                    if (calibFiles.Length > 0)
                    {
                        // 按修改时间排序，取最新的
                        Array.Sort(calibFiles, (a, b) => File.GetLastWriteTime(b).CompareTo(File.GetLastWriteTime(a)));
                        fullPath = calibFiles[0];
                        Debug.Log("使用最新的标定文件: " + fullPath);
                    }
                    else
                    {
                        Debug.LogWarning("未找到相机标定文件: " + _calibrationFilePath);
                        return;
                    }
                }
                else
                {
                    Debug.LogWarning("标定目录不存在: " + calibDir);
                    return;
                }
            }
            
            // 读取并解析标定文件
            string jsonData = File.ReadAllText(fullPath);
            CalibrationData calibData = JsonConvert.DeserializeObject<CalibrationData>(jsonData);
            
            if (calibData == null || calibData.cameraMatrix == null || calibData.cameraMatrix.Length < 9)
            {
                Debug.LogError("标定文件格式错误，无法解析相机矩阵");
                return;
            }
            
            // 从相机矩阵中提取内参
            // 相机矩阵格式: [fx, 0, cx, 0, fy, cy, 0, 0, 1]
            double fx = calibData.cameraMatrix[0]; // 焦距x
            double fy = calibData.cameraMatrix[4]; // 焦距y
            double cx = calibData.cameraMatrix[2]; // 主点x
            double cy = calibData.cameraMatrix[5]; // 主点y
            
            int imageWidth = calibData.imageWidth > 0 ? calibData.imageWidth : 1280;
            int imageHeight = calibData.imageHeight > 0 ? calibData.imageHeight : 720;
            
            Debug.Log($"成功加载相机标定数据: fx={fx}, fy={fy}, cx={cx}, cy={cy}");
            Debug.Log($"标定图像分辨率: {imageWidth}x{imageHeight}, RMS误差: {calibData.rmsError}");
            
            // 在Unity中设置等效的相机参数
            // 计算等效的视场角(FOV)
            // 注意：Unity的FOV是垂直方向的，而标定文件中的焦距通常对应于特定分辨率
            float aspectRatio = (float)imageWidth / (float)imageHeight;
            float verticalFOV = CalculateVerticalFOV(fy, imageHeight);
            
            // 设置相机的视场角
            camera.fieldOfView = verticalFOV;
            Debug.Log($"设置相机垂直FOV: {verticalFOV}度，宽高比: {aspectRatio}");
            
            // 计算主点偏移量
            // Unity相机默认主点在图像中心，需要计算偏移比例
            float principalPointOffsetX = (float)((cx / imageWidth) - 0.5f);
            float principalPointOffsetY = (float)((cy / imageHeight) - 0.5f);
            Debug.Log($"主点偏移比例: X={principalPointOffsetX}, Y={principalPointOffsetY}");
            
            // 应用主点偏移
            // Unity相机不直接支持设置主点偏移，但可以通过以下方式处理：
            // 1. 保存主点偏移参数，供着色器或后处理使用
            ApplyPrincipalPointOffset(camera, principalPointOffsetX, principalPointOffsetY, imageWidth, imageHeight);
            
            // 注意：完整的标定应用还需要考虑畸变系数
            // 畸变校正通常在着色器或后处理阶段进行
        }
        catch (Exception e)
        {
            Debug.LogError("加载和应用相机标定数据时出错: " + e.Message);
            Debug.LogError("堆栈跟踪: " + e.StackTrace);
        }
    }
    
    /// <summary>
    /// 根据焦距和图像高度计算垂直视场角
    /// </summary>
    /// <param name="focalLength">焦距(像素)</param>
    /// <param name="imageHeight">图像高度(像素)</param>
    /// <returns>垂直视场角(度)</returns>
    private float CalculateVerticalFOV(double focalLength, int imageHeight)
    {
        // 使用标准的FOV计算公式: 2 * arctan(imageHeight/(2*focalLength))
        double halfHeight = imageHeight / 2.0;
        double radians = 2.0 * Math.Atan(halfHeight / focalLength);
        return (float)(radians * 180.0 / Math.PI);
    }
    
    /// <summary>
    /// 应用主点偏移
    /// 注意：Unity相机不直接支持设置主点偏移，这里使用几种可能的方法来模拟
    /// </summary>
    /// <param name="camera">目标相机</param>
    /// <param name="offsetX">X方向主点偏移比例(-0.5到0.5)</param>
    /// <param name="offsetY">Y方向主点偏移比例(-0.5到0.5)</param>
    /// <param name="imageWidth">图像宽度</param>
    /// <param name="imageHeight">图像高度</param>
    private void ApplyPrincipalPointOffset(Camera camera, float offsetX, float offsetY, int imageWidth, int imageHeight)
    {
        // 方法1：通过设置camera的projectionMatrix来应用主点偏移
        // 这种方法会修改投影矩阵，适用于需要精确相机参数的情况
        Matrix4x4 projMatrix = camera.projectionMatrix;
        
        // 计算投影矩阵中需要修改的偏移量
        // 投影矩阵中的平移分量对应于主点偏移
        float projOffsetX = 2.0f * offsetX;
        float projOffsetY = 2.0f * offsetY;
        
        // 修改投影矩阵的平移分量
        projMatrix.m02 += projOffsetX;
        projMatrix.m12 += projOffsetY;
        
        // 应用修改后的投影矩阵
        camera.projectionMatrix = projMatrix;
        
        Debug.Log($"已应用主点偏移到相机投影矩阵: X={offsetX}, Y={offsetY}");
        
        // 方法2：保存主点偏移数据供着色器使用
        // 可以通过shader.SetFloat等方式将这些参数传递给着色器
        // 这种方法更适合处理畸变校正等复杂情况
        
        // 方法3：对于简单应用，可以通过调整相机位置来模拟主点偏移
        // 但这种方法不是真正的主点偏移，只是近似模拟
    }
#if UNITY_EDITOR
    [Header("OBJ模型加载器")]
    [SerializeField] private string _objFilePath = "";
    [SerializeField] private GameObject _loadedModel;
    [SerializeField] private Material _defaultMaterial;
    [SerializeField] private bool _createMaterials = true;
    [SerializeField] private bool _centerModel = true;
    
    [Header("渲染快照设置")]
    [SerializeField] private int _snapshotCount = 12; // 默认12个快照点
    [SerializeField] private float _sphereRadius = 5f; // 默认球体半径
    [SerializeField] private int _textureSize = 1024; // 默认纹理大小
    [SerializeField] private bool _includeRotation = true; // 是否包含绕Y轴的旋转
    [SerializeField] private bool _useTransparentBackground = false; // 是否使用透明背景
    [SerializeField] private Color _backgroundColor = Color.black; // 背景颜色

    #region Editor功能按钮
    [CustomEditor(typeof(ModelLoader))]
    public class ModelLoaderEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();
            
            ModelLoader loader = (ModelLoader)target;
            
            EditorGUILayout.Space();
            EditorGUILayout.BeginVertical("box");
            
            GUILayout.Label("模型加载操作", EditorStyles.boldLabel);
            
            // 打开文件选择器按钮
            if (GUILayout.Button("📁 选择OBJ文件"))
            {
                string path = EditorUtility.OpenFilePanel("选择OBJ模型文件", Application.dataPath, "obj");
                if (!string.IsNullOrEmpty(path))
                {
                    // 转换为Unity相对路径
                    if (path.StartsWith(Application.dataPath))
                    {
                        path = "Assets" + path.Substring(Application.dataPath.Length);
                    }
                    
                    Undo.RecordObject(loader, "选择OBJ文件");
                    loader._objFilePath = path;
                    EditorUtility.SetDirty(loader);
                }
            }
            
            // 加载模型按钮
            if (GUILayout.Button("🚀 加载模型"))
            {
                loader.LoadOBJModel();
            }
            
            // 清除加载的模型
            if (GUILayout.Button("🗑️ 清除模型"))
            {
                loader.ClearModel();
            }
            
            // 保存为预制体按钮
            if (GUILayout.Button("💾 保存为预制体") && loader._loadedModel != null)
            {
                loader.SaveAsPrefab();
            }
            
            EditorGUILayout.EndVertical();
            
            // 渲染快照操作区域
            EditorGUILayout.Space();
            EditorGUILayout.BeginVertical("box");
            
            GUILayout.Label("渲染快照操作", EditorStyles.boldLabel);
            
            // 渲染快照按钮
            if (GUILayout.Button("📸 渲染快照") && loader._loadedModel != null)
            {
                string savePath = EditorUtility.SaveFolderPanel("选择保存快照的文件夹", Application.dataPath, "Snapshots");
                if (!string.IsNullOrEmpty(savePath))
                {
                    loader.RenderSnapshots(savePath);
                }
            }
            else if (loader._loadedModel == null)
            {
                EditorUtility.DisplayDialog("提示", "请先加载模型再进行快照渲染", "确定");
            }
            
            // 快照设置
            loader._snapshotCount = EditorGUILayout.IntSlider("快照数量", loader._snapshotCount, 4, 36);
            loader._sphereRadius = EditorGUILayout.FloatField("球面半径", loader._sphereRadius);
            loader._textureSize = EditorGUILayout.IntPopup("纹理大小", loader._textureSize, 
                new string[] { "512x512", "1024x1024", "2048x2048" }, 
                new int[] { 512, 1024, 2048 });
            loader._includeRotation = EditorGUILayout.Toggle("包含Y轴旋转", loader._includeRotation);
            loader._useTransparentBackground = EditorGUILayout.Toggle("透明背景", loader._useTransparentBackground);
            if (!loader._useTransparentBackground)
            {
                loader._backgroundColor = EditorGUILayout.ColorField("背景颜色", loader._backgroundColor);
            }
            
            EditorGUILayout.EndVertical();
        }
    }
    #endregion

    #region OBJ加载功能
    public void LoadOBJModel()
    {
        if (string.IsNullOrEmpty(_objFilePath))
        {
            Debug.LogError("未选择OBJ文件路径");
            return;
        }

        // 获取完整文件路径
        //string fullPath = Path.Combine(Application.dataPath, _objFilePath.Substring(7));
        string fullPath = _objFilePath;
        
        if (!File.Exists(fullPath))
        {
            Debug.LogError($"文件不存在: {fullPath}");
            return;
        }

        // 清除现有模型
        ClearModel();

        try
        {
            Debug.Log($"开始加载OBJ模型: {_objFilePath}");
            
            // 创建模型根节点
            _loadedModel = new GameObject(Path.GetFileNameWithoutExtension(_objFilePath));
            _loadedModel.transform.SetParent(transform, false);
            
            // 读取OBJ文件内容
            string[] lines = File.ReadAllLines(fullPath);
            
            List<Vector3> vertices = new List<Vector3>();
            List<Vector2> uvs = new List<Vector2>();
            List<Vector3> normals = new List<Vector3>();
            List<List<int>> faceIndices = new List<List<int>>();
            
            // 解析OBJ文件
            foreach (string line in lines)
            {
                string[] parts = line.Split(new char[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) continue;
                
                switch (parts[0])
                {
                    case "v": // 顶点
                        if (parts.Length >= 4)
                        {
                            vertices.Add(new Vector3(
                                float.Parse(parts[1]) / 1000f,
                                float.Parse(parts[2]) / 1000f,
                                float.Parse(parts[3]) / 1000f
                            ));
                        }
                        break;
                    case "vt": // 纹理坐标
                        if (parts.Length >= 3)
                        {
                            uvs.Add(new Vector2(
                                float.Parse(parts[1]),
                                1 - float.Parse(parts[2]) // Unity的UV坐标系Y轴翻转
                            ));
                        }
                        break;
                    case "vn": // 法线
                        if (parts.Length >= 4)
                        {
                            normals.Add(new Vector3(
                                float.Parse(parts[1]),
                                float.Parse(parts[2]),
                                float.Parse(parts[3])
                            ));
                        }
                        break;
                    case "f": // 面
                        List<int> face = new List<int>();
                        for (int i = 1; i < parts.Length; i++)
                        {
                            // 格式: v/vt/vn 或 v//vn 或 v/vt 或 v
                            string[] indices = parts[i].Split('/');
                            if (indices.Length > 0)
                            {
                                int vertexIndex = int.Parse(indices[0]) - 1; // OBJ索引从1开始
                                face.Add(vertexIndex);
                            }
                        }
                        faceIndices.Add(face);
                        break;
                }
            }
            
            // 创建网格
            CreateMeshFromData(_loadedModel, vertices, uvs, normals, faceIndices);
            
            // 居中模型
            if (_centerModel)
            {
                CenterModel(_loadedModel);
            }
            
            Debug.Log($"OBJ模型加载成功: {_loadedModel.name}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"加载OBJ模型时出错: {e.Message}\n{e.StackTrace}");
            ClearModel();
        }
    }

    private void CreateMeshFromData(GameObject parent, List<Vector3> vertices, List<Vector2> uvs, List<Vector3> normals, List<List<int>> faceIndices)
    {
        // 对于简单的OBJ文件，直接创建一个网格
        GameObject meshObj = new GameObject("Mesh");
        meshObj.transform.SetParent(parent.transform, false);
        
        MeshFilter meshFilter = meshObj.AddComponent<MeshFilter>();
        MeshRenderer meshRenderer = meshObj.AddComponent<MeshRenderer>();
        
        Mesh mesh = new Mesh();
        mesh.name = "OBJMesh";
        
        // 准备三角形数据
        List<int> triangles = new List<int>();
        
        foreach (List<int> face in faceIndices)
        {
            // 处理三角形和面四边形
            if (face.Count >= 3)
            {
                // 三角化
                for (int i = 1; i < face.Count - 1; i++)
                {
                    triangles.Add(face[0]);
                    triangles.Add(face[i]);
                    triangles.Add(face[i + 1]);
                }
            }
        }
        
        // 设置网格数据
        mesh.vertices = vertices.ToArray();
        mesh.triangles = triangles.ToArray();
        
        // 如果有UV数据，设置UV
        if (uvs.Count > 0)
        {
            mesh.uv = uvs.ToArray();
        }
        
        // 计算法线（如果没有提供法线数据）
        if (normals.Count > 0)
        {
            mesh.normals = normals.ToArray();
        }
        else
        {
            mesh.RecalculateNormals();
        }
        
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();
        
        meshFilter.mesh = mesh;
        
        // 设置材质
        if (_createMaterials)
        {
            if (_defaultMaterial == null)
            {
                // 创建默认材质
                _defaultMaterial = new Material(Shader.Find("Standard"));
                _defaultMaterial.name = "DefaultOBJMaterial";
            }
            meshRenderer.material = _defaultMaterial;
        }
    }

    private void CenterModel(GameObject model)
    {
        Renderer[] renderers = model.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return;
        
        Bounds bounds = renderers[0].bounds;
        foreach (Renderer renderer in renderers)
        {
            bounds.Encapsulate(renderer.bounds);
        }
        
        Vector3 centerOffset = -bounds.center;
        model.transform.position += centerOffset;
        
        Debug.Log($"模型已居中，偏移量: {centerOffset}");
    }
    #endregion

    #region 工具函数
    public void ClearModel()
    {
        if (_loadedModel != null)
        {
#if UNITY_EDITOR
            DestroyImmediate(_loadedModel);
#else
            Destroy(_loadedModel);
#endif
            _loadedModel = null;
        }
    }

    public void SaveAsPrefab()
    {
        if (_loadedModel == null)
        {
            Debug.LogError("没有加载的模型可以保存");
            return;
        }
        
        string savePath = EditorUtility.SaveFilePanelInProject(
            "保存为预制体", 
            _loadedModel.name + "_Prefab", 
            "prefab", 
            "请选择保存预制体的位置"
        );
        
        if (!string.IsNullOrEmpty(savePath))
        {
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(_loadedModel, savePath);
            if (prefab != null)
            {
                Debug.Log($"预制体保存成功: {savePath}");
            }
            else
            {
                Debug.LogError("预制体保存失败");
            }
        }
    }
    
    /// <summary>
    /// 渲染模型快照
    /// </summary>
    /// <param name="saveDirectory">保存目录</param>
    public void RenderSnapshots(string saveDirectory)
    {
        if (_loadedModel == null)
        {
            Debug.LogError("没有加载的模型可以渲染快照");
            return;
        }
        
        // 创建保存目录
        if (!Directory.Exists(saveDirectory))
        {
            Directory.CreateDirectory(saveDirectory);
        }
        
        // 计算模型的中心点
        Renderer[] renderers = _loadedModel.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
        {
            Debug.LogError("模型没有渲染器组件");
            return;
        }
        
        Bounds bounds = renderers[0].bounds;
        foreach (Renderer renderer in renderers)
        {
            bounds.Encapsulate(renderer.bounds);
        }
        
        Vector3 modelCenter = bounds.center;
        float modelSize = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
        
        // 调整球体半径以适应模型大小
        float effectiveRadius = _sphereRadius;
        
        // 创建临时相机
        GameObject cameraObj = new GameObject("SnapshotCamera");
        Camera snapshotCamera = cameraObj.AddComponent<Camera>();
        
        // 首先设置默认FOV，后面可能会被标定数据覆盖
        snapshotCamera.fieldOfView = 45f;
        snapshotCamera.backgroundColor = _useTransparentBackground ? new Color(0, 0, 0, 0) : _backgroundColor;
        snapshotCamera.clearFlags = _useTransparentBackground ? CameraClearFlags.SolidColor : CameraClearFlags.SolidColor;
        snapshotCamera.targetTexture = new RenderTexture(_textureSize, _textureSize, 24);
        
        // 尝试应用相机标定参数
        ApplyCameraCalibration(snapshotCamera);
        
        // 创建RenderTexture和Texture2D用于截图
        RenderTexture renderTexture = new RenderTexture(_textureSize, _textureSize, 24);
        Texture2D screenshotTexture = new Texture2D(_textureSize, _textureSize, _useTransparentBackground ? TextureFormat.RGBA32 : TextureFormat.RGB24, false);
        
        // 生成球面上的均匀分布点
        List<Vector3> cameraPositions = GenerateSphericalPoints(_snapshotCount, effectiveRadius, modelCenter);
        
        Debug.Log($"开始渲染{cameraPositions.Count}个快照...");
        
        for (int i = 0; i < cameraPositions.Count; i++)
        {
            // 设置相机位置和朝向
            cameraObj.transform.position = cameraPositions[i];
            cameraObj.transform.LookAt(modelCenter);
            
            // 渲染到RenderTexture
            RenderTexture.active = renderTexture;
            snapshotCamera.targetTexture = renderTexture;
            snapshotCamera.Render();
            
            // 读取像素数据
            screenshotTexture.ReadPixels(new Rect(0, 0, renderTexture.width, renderTexture.height), 0, 0);
            screenshotTexture.Apply();
            
            // 保存为PNG文件
            byte[] bytes = screenshotTexture.EncodeToPNG();
            string fileName = $"snapshot_{i.ToString("D3")}.png";
            string filePath = Path.Combine(saveDirectory, fileName);
            File.WriteAllBytes(filePath, bytes);
            
            Debug.Log($"快照已保存: {filePath}");
        }
        
        // 清理资源
        RenderTexture.active = null;
        DestroyImmediate(renderTexture);
        DestroyImmediate(screenshotTexture);
        DestroyImmediate(cameraObj);
        
        Debug.Log($"所有快照渲染完成，共{cameraPositions.Count}个文件保存在: {saveDirectory}");
        
#if UNITY_EDITOR
        EditorUtility.DisplayDialog("渲染完成", $"成功渲染{cameraPositions.Count}个快照\n保存位置: {saveDirectory}", "确定");
#endif
    }
    
    /// <summary>
    /// 在球面上生成均匀分布的点
    /// 使用斐波那契球面点分布算法
    /// </summary>
    private List<Vector3> GenerateSphericalPoints(int count, float radius, Vector3 center)
    {
        List<Vector3> points = new List<Vector3>();
        
        if (_includeRotation)
        {
            // 使用斐波那契球面点分布
            float phi = Mathf.PI * (3 - Mathf.Sqrt(5)); // 黄金角
            
            for (int i = 0; i < count; i++)
            {
                float y = 1 - (i / (float)(count - 1)) * 2; // y从1到-1
                float radius_at_y = Mathf.Sqrt(1 - y * y); // 在该y值处的圆半径
                
                float theta = phi * i; // 黄金角增量旋转
                
                float x = Mathf.Cos(theta) * radius_at_y;
                float z = Mathf.Sin(theta) * radius_at_y;
                
                points.Add(center + new Vector3(x, y, z) * radius);
            }
        }
        else
        {
            // 只在赤道平面上均匀分布
            float angleStep = 2 * Mathf.PI / count;
            
            for (int i = 0; i < count; i++)
            {
                float angle = i * angleStep;
                float x = Mathf.Cos(angle) * radius;
                float z = Mathf.Sin(angle) * radius;
                
                points.Add(center + new Vector3(x, 0, z));
            }
        }
        
        return points;
    }
    #endregion
#endif
}