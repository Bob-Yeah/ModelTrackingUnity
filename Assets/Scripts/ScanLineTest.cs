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
using ModelTracker;
using System.Numerics;
/// <summary>
/// 扫描线测试脚本，用于加载图片并准备扫描线计算
/// </summary>
public class ScanLineTest : MonoBehaviour
{
    [Header("图片设置")]
    [Tooltip("图片文件路径")]
    // 注释掉硬编码的图片路径，改为通过文件浏览器选择
    // public string imagePath = ""; // 在Inspector中设置的图片路径
    
    [Tooltip("RawImage组件引用，用于显示加载的图片")]
    public RawImage displayImage; // 显示图片的RawImage组件
    
    [Header("UI按钮")]
    [Tooltip("用于加载图片的按钮")]
    public Button loadImageButton; // 加载图片的按钮
    
    [Tooltip("用于计算扫描线的按钮")]
    public Button computeScanLinesButton; // 计算扫描线的按钮

    // 当前加载的纹理
    private Texture2D loadedTexture;

    private Optimizer optimizer;
    
    /// <summary>
    /// 初始化组件和按钮事件
    /// </summary>
    private void Awake()
    {
        // 获取组件引用（如果未在Inspector中设置）
        if (displayImage == null)
        {
            displayImage = GetComponent<RawImage>();
        }
        
        if (loadImageButton == null)
        {
            loadImageButton = GetComponentInChildren<Button>();
        }
        
        // 注册按钮点击事件
        if (loadImageButton != null)
        {
            loadImageButton.onClick.AddListener(LoadAndDisplayImage);
        }

        if (computeScanLinesButton != null)
        {
            computeScanLinesButton.onClick.AddListener(StartScanLineComputation);
        }
        
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
            // 加载图片字节数据
            byte[] imageData = File.ReadAllBytes(imagePath);
            
            // 创建纹理并加载数据
            loadedTexture = new Texture2D(2, 2);
            bool loadSuccess = loadedTexture.LoadImage(imageData);
            
            if (loadSuccess)
            {
                // 显示图片
                if (displayImage != null)
                {
                    displayImage.texture = loadedTexture;
                    displayImage.SetNativeSize();
                    Debug.Log($"成功加载图片：{Path.GetFileName(imagePath)}");
                }
                else
                {
                    Debug.LogError("RawImage组件未设置！");
                }
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
    /// 开始扫描线计算（预留接口）
    /// </summary>
    public void StartScanLineComputation()
    {
        // 检查是否已加载图片
        if (loadedTexture == null)
        {
            Debug.LogWarning("请先加载图片！");
            return;
        }

        // 把loadedTexture转换为Mat
        Mat mat = new Mat(loadedTexture.height, loadedTexture.width, CvType.CV_8UC3);
        Utils.texture2DToMat(loadedTexture, mat);

        Debug.Log("开始扫描线计算（函数预留接口）");
        
        // 相机相关
        // 获取相机参数
        Camera cam = targetCamera != null ? targetCamera : Camera.main;
        if (cam == null)
        {
            Debug.LogError("无法获取相机");
            return;
        }

        // 计算相机内参矩阵K
        float fov = cam.fieldOfView * Mathf.Deg2Rad;
        float aspectRatio = (float)width / (float)height;
        float focalLength = (float)(height / 2.0f / Mathf.Tan(fov / 2.0f));
        float cx = (float)width / 2.0f;
        float cy = (float)height / 2.0f;
        
        // 构建相机内参矩阵K
        Matx33f K = new Matx33f(
            focalLength, 0, cx,
            0, focalLength, cy,
            0, 0, 1
        );
        
        // 获取相机外参（旋转矩阵R和平移向量t）
        Matrix4x4 cameraToWorld = cam.transform.localToWorldMatrix;
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

        ModelTracker.Projector prj_test = new ModelTracker.Projector(K, R, t);

        // 获得3D轮廓点
        List<Vector3> points = Get3DContourPoints();
        // 投影到2D平面
        List<Vector2> points2D = new List<Vector2>();
        foreach (var point in points)
        {
            Vector2 p2d = prj_test.project(point);
            points2D.Add(p2d);
        }

        // 可视化2D轮廓点，应该是上下反的，因为OpenCV的坐标系统和Unity的坐标系统不同
        // 创建一个全为0的三通道U8Mat，与Mat1有相同的大小
        Mat mat_vis_cp = new Mat(mat.rows(), mat.cols(), CvType.CV_8UC3);
        // 将Points2D每个点转换为像素的位置，int格式，在mat_vis_cp中绘制为白色点
        foreach (var p2d in points2D)
        {
            Core.circle(mat_vis_cp, new Point((int)p2d.x, (int)p2d.y), 5, new Scalar(255, 255, 255), -1);
        }
        // 把mat_vis_cp保存成一个png文件
        Utils.matToTexture2D(mat_vis_cp, loadedTexture);
        loadedTexture.Apply();
        File.WriteAllBytes("mat_vis_cp.png", loadedTexture.EncodeToPNG());

        // 获得ROI
        OpenCVForUnity.CoreModule.Rect roi = optimizer.getBoundingBox2D(points);

        // 实现扫描线计算逻辑
        // 把mat转换为1通道的Mat，取R通道即可，转换为0-1范围
        Mat mat1 = new Mat(mat.rows(), mat.cols(), CvType.CV_32FC1);
        Core.extractChannel(mat, mat1, 0);
        mat1.convertTo(mat1, CvType.CV_32FC1, 1.0 / 255.0);
        optimizer.computeScanLines(mat1, roi);

        //可视化扫描线
        // 创建一个和Mat1相同大小的Mat，用于可视化扫描线，三通道U8
        Mat mat1_vis = new Mat(mat1.rows(), mat1.cols(), CvType.CV_8UC3);
        optimizer.visualizeScanLines(mat1_vis);
        // 把mat1_vis保存成一个png文件
        Utils.matToTexture2D(mat1_vis, loadedTexture);
        loadedTexture.Apply();
        File.WriteAllBytes("mat1_vis.png", loadedTexture.EncodeToPNG());
    }
    
    public List<Vector3> Get3DContourPoints()
    {
        // 搜索Scene中所有的名为Sphere的GameObject，并获取其Transform组件的全局位置Vector3，组成List返回
        List<Vector3> points = new List<Vector3>();
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