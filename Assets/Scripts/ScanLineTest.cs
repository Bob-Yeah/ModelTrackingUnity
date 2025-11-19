using UnityEngine;
using UnityEngine.UI;
using System.IO;
#if UNITY_EDITOR
using UnityEditor;
#endif
using OpenCVForUnity.CoreModule;
using OpenCVForUnity.Calib3dModule;
using OpenCVForUnity.ImgprocModule;
using OpenCVForUnity.UtilsModule;
using OpenCVForUnity.UnityUtils;
using ModelTracker;
//using System.Numerics;
using System.Collections.Generic;
using System.Linq;

#if UNITY_EDITOR
[CustomEditor(typeof(ScanLineTest))]
public class ScanLineTestEditor : Editor
{
    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();
        
        ScanLineTest testScript = (ScanLineTest)target;
        
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("编辑器模式按钮", EditorStyles.boldLabel);
        
        if (GUILayout.Button("加载图片 (编辑器模式)"))
        {
            testScript.LoadAndDisplayImage();
        }
        
        if (GUILayout.Button("计算扫描线 (编辑器模式)"))
        {
            testScript.StartScanLineComputation();
        }
    }
}
#endif
/// <summary>
/// 扫描线测试脚本，用于加载图片并准备扫描线计算
/// </summary>
public class ScanLineTest : MonoBehaviour
{
    [Header("图片设置")]
    //[Tooltip("图片文件路径")]
    // 注释掉硬编码的图片路径，改为通过文件浏览器选择
    // public string imagePath = ""; // 在Inspector中设置的图片路径
    
    public Camera targetCamera;

    // 当前加载的纹理
    private Texture2D loadedTexture;

    private Optimizer optimizer;
    
    /// <summary>
    /// 初始化组件和按钮事件
    /// </summary>
    private void Awake()
    {        
        optimizer = new Optimizer();
    }
    
    /// <summary>
    /// 加载并显示图片
    /// </summary>
    public void LoadAndDisplayImage()
    {
#if UNITY_EDITOR
        // 打开文件浏览器，让用户选择图片文件
        string imagePath = EditorUtility.OpenFilePanel(
            "选择图片文件", 
            Application.dataPath, 
            "jpg,png,bmp,tga");
        
        // 检查用户是否取消了选择
        if (string.IsNullOrEmpty(imagePath))
        {
            Debug.Log("用户取消了文件选择");
            return;
        }
        
        // 检查文件是否存在
        if (!File.Exists(imagePath))
        {
            Debug.LogError($"找不到图片文件：{imagePath}");
            return;
        }
#else
        Debug.LogWarning("文件浏览器功能仅在编辑器模式下可用");
        return;
#endif
        
        try
        {
            // 释放旧纹理资源
            if (loadedTexture != null)
            {
                #if UNITY_EDITOR
                DestroyImmediate(loadedTexture);
                #else
                Destroy(loadedTexture);
                #endif
                loadedTexture = null;
            }
            
            // 加载图片字节数据
            byte[] imageData = File.ReadAllBytes(imagePath);
            
            // 创建纹理并加载数据
            loadedTexture = new Texture2D(2, 2);
            bool loadSuccess = loadedTexture.LoadImage(imageData);
            
            if (loadSuccess)
            {
                Debug.Log($"成功加载图片：{Path.GetFileName(imagePath)}, 尺寸: {loadedTexture.width}x{loadedTexture.height}");
            }
            else
            {
                Debug.LogError("图片数据加载失败！");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"加载图片时发生错误：{e.Message}");
        }
    }

    /// <summary>
    /// 开始扫描线计算
    /// </summary>
    public void StartScanLineComputation()
    {
        // 检查是否已加载图片
        if (loadedTexture == null)
        {
            Debug.LogWarning("请先加载图片！");
            return;
        }

        Debug.Log("开始扫描线计算");

        try
        {
            int width = loadedTexture.width;
            int height = loadedTexture.height;

            // 把loadedTexture转换为Mat
            Mat mat = new Mat(loadedTexture.height, loadedTexture.width, CvType.CV_8UC3);
            OpenCVForUnity.UnityUtils.Utils.texture2DToMat(loadedTexture, mat);

            // 相机相关
            // 在编辑器模式下，如果没有相机，使用默认相机参数
            Camera cam = null;
            Matx33f K = new Matx33f();
            
            if (targetCamera != null || Camera.main != null)
            {
                cam = targetCamera != null ? targetCamera : Camera.main;
                // 计算相机内参矩阵K
                float fov = cam.fieldOfView * Mathf.Deg2Rad;
                float aspectRatio = (float)width / (float)height;
                float focalLength = (float)(height / 2.0f / Mathf.Tan(fov / 2.0f));
                float cx = (float)width / 2.0f;
                float cy = (float)height / 2.0f;
                
                // 构建相机内参矩阵K
                K = new Matx33f(
                    focalLength, 0, cx,
                    0, focalLength, cy,
                    0, 0, 1
                );
            }
            else
            {
                // 使用默认相机参数
                Debug.LogWarning("未找到相机，使用默认相机参数");
                float focalLength = Mathf.Max(width, height);
                float cx = (float)width / 2.0f;
                float cy = (float)height / 2.0f;
                
                K = new Matx33f(
                    focalLength, 0, cx,
                    0, focalLength, cy,
                    0, 0, 1
                );
            }

            // 构建默认的旋转矩阵R和平移向量t
            Matx33f R = Matx33f.eye(); // 单位矩阵
            UnityEngine.Vector3 t = new UnityEngine.Vector3(0, 0, 5); // 默认平移

            // 如果有相机，使用相机的外参
            if (cam != null)
            {
                // 获取相机外参（旋转矩阵R和平移向量t）
                UnityEngine.Matrix4x4 cameraToWorld = cam.transform.localToWorldMatrix;
                UnityEngine.Matrix4x4 worldToCamera = cameraToWorld.inverse;

                // 构建旋转矩阵R
                R = new Matx33f(
                    worldToCamera.m00, worldToCamera.m01, worldToCamera.m02,
                    worldToCamera.m10, worldToCamera.m11, worldToCamera.m12,
                    worldToCamera.m20, worldToCamera.m21, worldToCamera.m22
                );

                // 构建平移向量t
                t = new UnityEngine.Vector3(
                    worldToCamera.m03,
                    worldToCamera.m13,
                    worldToCamera.m23
                );
            }

            ModelTracker.Projector prj_test = new ModelTracker.Projector(K, R, t);

            // 获得3D轮廓点
            List<UnityEngine.Vector3> points = Get3DContourPoints();
            
            // 如果没有找到轮廓点，使用默认的测试点
            if (points.Count == 0)
            {
                Debug.LogWarning("未找到Sphere对象，使用默认测试点");
                // 添加一些默认测试点
                points.Add(new UnityEngine.Vector3(-1, 0, 0));
                points.Add(new UnityEngine.Vector3(1, 0, 0));
                points.Add(new UnityEngine.Vector3(0, -1, 0));
                points.Add(new UnityEngine.Vector3(0, 1, 0));
            }
            
            // 投影到2D平面
            List<UnityEngine.Vector2> points2D = new List<UnityEngine.Vector2>();
            foreach (var point in points)
            {
                Vector2 p2d = prj_test.Project(point);
                points2D.Add(p2d);
            }

            // 可视化2D轮廓点
            Mat mat_vis_cp = new Mat(mat.rows(), mat.cols(), CvType.CV_8UC3);
            foreach (var p2d in points2D)
            {
                Imgproc.circle(mat_vis_cp, new Point((int)p2d.x, (int)p2d.y), 5, new Scalar(255, 255, 255), -1);
            }
            
            // 保存可视化结果
            ModelTrackerUtils.SaveMatToFile(mat_vis_cp, "mat_vis_cp.png");

            // 获得ROI
            OpenCVForUnity.CoreModule.Rect roi = ModelTrackerUtils.getBoundingBox2D(points2D);
            
            // 确保ROI在图像范围内
            roi.x = Mathf.Max(0, roi.x);
            roi.y = Mathf.Max(0, roi.y);
            roi.width = Mathf.Min(width - roi.x, roi.width);
            roi.height = Mathf.Min(height - roi.y, roi.height);

            // 实现扫描线计算逻辑
            Mat mat1 = new Mat(mat.rows(), mat.cols(), CvType.CV_32FC1);
            Core.extractChannel(mat, mat1, 0);
            mat1.convertTo(mat1, CvType.CV_32FC1, 1.0 / 255.0);
            
            // 确保optimizer已初始化
            if (optimizer == null)
            {
                optimizer = new Optimizer();
            }
            

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            optimizer.computeScanLines(mat1, roi);
            stopwatch.Stop();
            Debug.Log($"computeScanLines 耗时: {stopwatch.ElapsedMilliseconds} ms");

            //可视化扫描线
            Mat mat1_vis = new Mat(mat1.rows(), mat1.cols(), CvType.CV_8UC3);
            optimizer.visualizeScanLines(mat1_vis);
            
            // 保存扫描线可视化结果
            ModelTrackerUtils.SaveMatToFile(mat1_vis, "mat1_vis.png");
            
            Debug.Log("扫描线计算完成");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"扫描线计算过程中发生错误：{e.Message}\n{e.StackTrace}");
        }
    }
    
    
    
    public List<UnityEngine.Vector3> Get3DContourPoints()
    {
        // 搜索Scene中所有的名为Sphere的GameObject，并获取其Transform组件的全局位置Vector3，组成List返回
        List<UnityEngine.Vector3> points = new List<UnityEngine.Vector3>();
        foreach (var sphere in GameObject.FindObjectsOfType<GameObject>().Where(obj => obj.name == "Sphere"))
        {
            points.Add(sphere.transform.position);
        }
        return points;
    }
    /// <summary>
    /// 清理资源
    /// </summary>
    private void OnDestroy()
    {
        // 释放纹理资源
        if (loadedTexture != null)
        {
            Destroy(loadedTexture);
            loadedTexture = null;
        }
    }
}