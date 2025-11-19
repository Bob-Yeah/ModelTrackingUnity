using ModelTracker;
using Newtonsoft.Json;
using OpenCVForUnity.CoreModule;
using OpenCVForUnity.ImgcodecsModule;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;


[ExecuteInEditMode]
public class TemplateBuilder : MonoBehaviour
{
    [SerializeField]
    public GameObject targetModel;


    [Header("渲染快照设置")]
    [SerializeField] private int _snapshotCount = 200; // 默认12个快照点
    [SerializeField] private int _contourPointCountByView = 200; // 默认每个视图200个轮廓点

    [SerializeField] private float _sphereRadiusScale = 2.5f; // 默认球体半径
    [SerializeField] private int _textureSize = 1024; // 默认纹理大小
    [SerializeField] private Color _backgroundColor = Color.black; // 背景颜色
    
    [SerializeField] private float _cameraVFov = 45f; // 相机的Vertical Fov
    [SerializeField] private float _cameraNearPlane = 0.003f; // 相机的近平面
    [SerializeField] private float _cameraFarPlane = 100f; // 相机的近平面

    [Header("保存设置")]
    [Tooltip("保存路径，默认为Assets/DepthMaps")]
    public string savePath = "Assets/DepthMaps";
    [Tooltip("文件名前缀")]
    public string fileNamePrefix = "r_channel";
    public bool SaveImages = true;

    public void GenerateTemplate()
    {
        if (targetModel == null && targetModel.GetComponent<MeshFilter>() == null 
            && targetModel.transform.position == Vector3.zero)
        {
            Debug.LogWarning("targetModel的设置有问题，未设置、下面没有MeshFilter、不处在原点都不正确");
            return;
        }

        int targetLayer = LayerMask.NameToLayer("Template");
        targetModel.layer = targetLayer;

        // 计算模型的中心点
        Renderer[] renderers = targetModel.GetComponents<Renderer>();
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
        float effectiveRadius = modelSize / 2.0f * _sphereRadiusScale;

        // 创建临时相机
        GameObject cameraObj = new GameObject("SnapshotCamera");
        Camera snapshotCamera = cameraObj.AddComponent<Camera>();
        
        // 设置相机性质
        SetupCamera(ref snapshotCamera, targetLayer);

        // 设置纹理和材质
        // 创建深度渲染所需的纹理
        RenderTexture depthRenderTexture = new RenderTexture(_textureSize, _textureSize, 24, RenderTextureFormat.ARGB32);
        // 使用32位浮点型纹理存储深度信息
        Texture2D depthTexture = new Texture2D(_textureSize, _textureSize, TextureFormat.RFloat, false);

        // 保存原始材质并创建临时的深度着色器材质
        Dictionary<Renderer, Material[]> originalMaterials = new Dictionary<Renderer, Material[]>();
        Material depthMaterial = CreateDepthMaterial();

        // 替换所有渲染器的材质为深度材质
        foreach (Renderer renderer in targetModel.GetComponents<Renderer>())
        {
            originalMaterials[renderer] = renderer.sharedMaterials;
            renderer.sharedMaterials = new Material[] { depthMaterial };
        }

        // 设置反投影的参数
        float fov = snapshotCamera.fieldOfView * Mathf.Deg2Rad;
        float aspectRatio = (float)_textureSize / (float)_textureSize;
        float focalLength = (float)(_textureSize / 2.0f / Mathf.Tan(fov / 2.0f));
        float cx = (float)_textureSize / 2.0f;
        float cy = (float)_textureSize / 2.0f;
        // 构建相机内参矩阵K
        Matx33f K = new Matx33f(
            focalLength, 0, cx,
            0, focalLength, cy,
            0, 0, 1
        );

        // 设置不同的相机位置，开始渲染
        List<Vector3> cameraPos = GenerateSphericalPoints(_snapshotCount, effectiveRadius, modelCenter);
        for (int i = 0; i < _snapshotCount; ++i)
        {
            RenderExecute(ref cameraObj,cameraPos[i], modelCenter, depthRenderTexture, depthTexture);

        }

        // 恢复原始材质
        foreach (var kvp in originalMaterials)
        {
            kvp.Key.sharedMaterials = kvp.Value;
        }

        // 清理资源
        RenderTexture.active = null;
        DestroyImmediate(depthRenderTexture);
        DestroyImmediate(depthTexture);
        DestroyImmediate(depthMaterial);
        DestroyImmediate(cameraObj);



#if UNITY_EDITOR
        // 刷新AssetDatabase以显示新创建的深度图资源
        UnityEditor.AssetDatabase.Refresh();
        UnityEditor.EditorUtility.DisplayDialog("渲染完成", $"成功渲染{cameraPos.Count}个深度图", "确定");
#endif
    }

    public void SetupCamera(ref Camera renderCam, int RenderLayer)
    {
        renderCam.fieldOfView = _cameraVFov;
        renderCam.backgroundColor = _backgroundColor;
        renderCam.clearFlags = CameraClearFlags.SolidColor;
        renderCam.targetTexture = new RenderTexture(_textureSize, _textureSize, 24);
        renderCam.cullingMask = 1 << RenderLayer;
    }

    public void RenderExecute(ref GameObject cameraObj, Vector3 camPos, Vector3 modelCenter, RenderTexture rt, Texture2D tex)
    {
        // 设置相机位置和朝向
        cameraObj.transform.position = camPos;
        cameraObj.transform.LookAt(modelCenter);
        Camera renderCam = cameraObj.GetComponent<Camera>();
        // 渲染到RenderTexture（彩色图）
        RenderTexture.active = rt;
        renderCam.targetTexture = rt;

        // 清除渲染目标
        GL.Clear(false, true, new Color(0, 0, 0, 0));

        renderCam.Render();

        tex.ReadPixels(new UnityEngine.Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply();

    }

    public void SamplingAndUnproject(GameObject cameraObj, Texture2D depthTex, Matx33f K)
    {
        // 获取相机外参（旋转矩阵R和平移向量t）
        Matrix4x4 cameraToWorld = cameraObj.transform.localToWorldMatrix;
        Matrix4x4 worldToCamera = cameraToWorld.inverse;

        // 构建旋转矩阵R
        Matx33f R = new Matx33f(
            worldToCamera.m00, worldToCamera.m01, worldToCamera.m02,
            worldToCamera.m10, worldToCamera.m11, worldToCamera.m12,
            worldToCamera.m20, worldToCamera.m21, worldToCamera.m22
        );

        // 构建平移向量t
        Vector3 t = new Vector3(
            worldToCamera.m03,
            worldToCamera.m13,
            worldToCamera.m23
        );

        // 提取R通道数据
        Color[] pixels = depthTex.GetPixels();
        int width = depthTex.width;
        int height = depthTex.height;

        // 创建浮点数组存储R通道数据
        float[] rChannelData = new float[width * height];
        for (int i = 0; i < pixels.Length; i++)
        {
            rChannelData[i] = pixels[i].r; // 只提取R通道值
        }
        // 创建OpenCV Mat（32位浮点，单通道）
        Mat rChannelMatOrigin = new Mat(height, width, CvType.CV_32FC1);
        // 将数据复制到Mat
        rChannelMatOrigin.put(0, 0, rChannelData);

        // 调用EdgeSampler.Sample方法进行处理
        List<CPoint> sampledPoints = EdgeSampler.Sample(rChannelMatOrigin, 0, maxPointCount, K, R, t);

        // 生成Debug小球


        //保存图片
        // 生成时间戳
        if (SaveImages)
        {
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
            string fileNamePrefix = "r_channel";
            // 生成文件名（带时间戳避免覆盖）
            string fileName = $"{fileNamePrefix}_{timestamp}.png";
            string fullPath = Path.Combine(savePath, fileName);

            // 检查目录是否可写
            if (!Directory.Exists(savePath))
            {
                Debug.LogError($"保存目录不存在: {savePath}");
            }

            // 尝试保存为PNG作为备选方案
            bool pngSuccess = Imgcodecs.imwrite(fullPath, rChannelMatOrigin * 255);
            if (pngSuccess)
            {
                Debug.Log($"作为备选方案，成功保存为PNG文件: {fileName}");
            }
        }
        
        rChannelMatOrigin.Dispose();
    }

    /// <summary>
    /// 在球面上生成均匀分布的点
    /// </summary>
    private List<Vector3> GenerateSphericalPoints(int count, float radius, Vector3 center)
    {
        List<Vector3> dirs = null;
        ModelTrackerUtils.sampleSphere(ref dirs, count);

        List<Vector3> points = new List<Vector3>();

        for (int i = 0; i < count; i++)
        {
            points.Add(center + dirs[i] * radius);
        }

        return points;
    }

    /// <summary>
    /// 创建用于渲染深度图的着色器材质
    /// </summary>
    /// <returns>深度着色器材质</returns>
    private Material CreateDepthMaterial()
    {
        // 使用外部shader文件而不是内联代码
        Shader depthShader = Shader.Find("ModelTracker/DepthOnly");

        if (depthShader == null)
        {
            Debug.LogError("找不到深度着色器文件 'ModelTracker/DepthOnly'。请确保该着色器已被正确创建在 Assets/Materials 目录下。");
            // 如果找不到shader，返回一个默认的材质
            return new Material(Shader.Find("Standard"));
        }

        Material depthMaterial = new Material(depthShader);
        return depthMaterial;
    }

    [CustomEditor(typeof(TemplateBuilder))]
    public class TemplateBuilderEditor: Editor
    {
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();
            TemplateBuilder builder = (TemplateBuilder)target;
            if (GUILayout.Button("生成Template"))
            {
                builder.GenerateTemplate();
            }

            if (GUILayout.Button("保存Template Json"))
            {

            }
        }
    }
}
