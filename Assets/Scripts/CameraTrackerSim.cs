using ModelTracker;
using Newtonsoft.Json;
using OpenCVForUnity.CoreModule;
using OpenCVForUnity.ImgcodecsModule;
using OpenCVForUnity.UnityUtils;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

public class CameraTrackerSim : MonoBehaviour
{
    public Camera TrackerCam;

    public Vector3 currentT = Vector3.zero;
    public Vector3 lastT = Vector3.zero;
    public Matx33f currentR = new Matx33f();
    public Matx33f lastR = new Matx33f();

    public TemplateRuntime templateRuntimeInst;

    private Matx33f K = new Matx33f();

    public UnityEngine.UI.RawImage resultImage;

    public RenderTexture inputImage;

    private Texture2D inputTexture;

    private ColorHistogram colorHistogram;
    private Texture2D colorHistogramTexture;

    private Mat inputMat;
    Frame _prvec;
    Frame _cur;


    // 相机移动轨迹相关变量
    private bool isFirstMove = true;
    private List<Vector3> cameraPath = new List<Vector3>();
    private List<Quaternion> cameraRotations = new List<Quaternion>();
    private int currentPathIndex = 0;
    private Vector3 modelCenter;
    
    public void MoveCamera()
    {
        if (templateRuntimeInst == null || templateRuntimeInst.ModelTemplate == null)
        {
            Debug.LogError("TemplateRuntime instance or ModelTemplate is not initialized.");
            return;
        }
        
        // 获取模型中心
        modelCenter = templateRuntimeInst.ModelTemplate.modelCenter;
        
        // 第一次移动时，创建轨迹
        if (isFirstMove)
        {
            CreateCameraPath();
            isFirstMove = false;
            
            // 确保当前路径索引有效
            if (cameraPath.Count > 0 && currentPathIndex < cameraPath.Count)
            {
                // 记录初始状态
                lastT = currentT;
                lastR = currentR;
            }
        }

        // 执行相机移动
        if (cameraPath.Count > 0 && currentPathIndex < cameraPath.Count)
        {
            // 平滑过渡到下一个位置和旋转
            MoveToNextPosition();
        }
        else if (cameraPath.Count > 0)
        {
            // 如果到达轨迹末尾，重新开始
            currentPathIndex = 0;
            MoveToNextPosition();
        }


        ManualRenderCamera();
        //rt2mat();
        resultImage.texture = inputTexture;
    }
    
    private void CreateCameraPath()
    {
        // 获取当前相机位置
        Vector3 currentCameraPos = TrackerCam.transform.position;
        Quaternion currentCameraRot = TrackerCam.transform.rotation;
        
        // 清除现有轨迹
        cameraPath.Clear();
        cameraRotations.Clear();
        
        // 添加起始位置和旋转
        cameraPath.Add(currentCameraPos);
        cameraRotations.Add(currentCameraRot);
        
        // 创建围绕模型中心的圆形轨迹
        int pathPoints = 200; // 轨迹点数量
        float minDistance = 0.3f;
        float maxDistance = 0.7f;
        
        for (int i = 1; i <= pathPoints; i++)
        {
            // 计算角度
            float angle = (float)i / pathPoints * Mathf.PI * 2;
            
            // 随机距离在0.3到0.7之间
            float distance = UnityEngine.Random.Range(minDistance, maxDistance);
            
            // 创建圆形轨迹（在XY平面）
            Vector3 offset = new Vector3(
                Mathf.Cos(angle) * distance,
                UnityEngine.Random.Range(-0.2f, 0.2f), // 增加一些垂直变化
                Mathf.Sin(angle) * distance
            );
            
            // 计算相机位置
            Vector3 cameraPos = modelCenter + offset;
            cameraPath.Add(cameraPos);
            
            // 计算相机朝向（看向模型中心，但添加5度以内的随机偏移）
            Vector3 lookDirection = (modelCenter - cameraPos).normalized;
            
            // 添加随机旋转偏移（最多5度）
            Quaternion randomRotation = Quaternion.Euler(
                UnityEngine.Random.Range(-5f, 5f),
                UnityEngine.Random.Range(-5f, 5f),
                UnityEngine.Random.Range(-5f, 5f)
            );
            
            // 计算最终旋转
            Quaternion targetRotation = Quaternion.LookRotation(lookDirection) * randomRotation;
            cameraRotations.Add(targetRotation);
        }
        
        // 添加一个回到起始点附近的点，使轨迹闭合
        cameraPath.Add(cameraPath[0] + UnityEngine.Random.insideUnitSphere * 0.1f);
        cameraRotations.Add(cameraRotations[0]);
        
        currentPathIndex = 0;
    }
    
    private void MoveToNextPosition()
    {
        if (currentPathIndex + 1 >= cameraPath.Count) return;
        
        // 计算当前点和下一个点之间的插值参数
        float t = 1.0f; // 直接使用1.0表示完全移动到下一个点
        
        // 获取当前目标点索引
        int nextIndex = currentPathIndex + 1;
        
        // 如果是第一个移动，从初始位置开始插值
        if (currentPathIndex == 0 && isFirstMove)
        {
            // 使用初始相机位置作为起点
            Vector3 startPosition = TrackerCam.transform.position;
            Quaternion startRotation = TrackerCam.transform.rotation;
            
            // 使用插值计算平滑过渡
            Vector3 interpolatedPosition = Vector3.Lerp(startPosition, cameraPath[nextIndex], t);
            Quaternion interpolatedRotation = Quaternion.Slerp(startRotation, cameraRotations[nextIndex], t);
            
            // 更新相机位置和旋转
            TrackerCam.transform.position = interpolatedPosition;
            TrackerCam.transform.rotation = interpolatedRotation;

            Debug.Log("First Move to: " + interpolatedPosition);
        }
        else
        {
            // 使用路径上的前一个点和当前目标点进行插值
            Vector3 startPosition = cameraPath[currentPathIndex];
            Quaternion startRotation = cameraRotations[currentPathIndex];
            
            // 使用插值计算平滑过渡
            Vector3 interpolatedPosition = Vector3.Lerp(startPosition, cameraPath[nextIndex], t);
            Quaternion interpolatedRotation = Quaternion.Slerp(startRotation, cameraRotations[nextIndex], t);
            
            // 更新相机位置和旋转
            TrackerCam.transform.position = interpolatedPosition;
            TrackerCam.transform.rotation = interpolatedRotation;
            
            Debug.Log("Move to: " + interpolatedPosition);
        }
        
        // 更新路径索引
        currentPathIndex = nextIndex;
        
        // 计算相机坐标系下的模型姿态
        UpdateCameraPose();
    }
    
    private void UpdateCameraPose()
    {
        // 获取相机的世界到相机变换矩阵
        Matrix4x4 cameraToWorld = TrackerCam.transform.localToWorldMatrix;
        Matrix4x4 worldToCamera = cameraToWorld.inverse;
        
        // 更新旋转矩阵
        currentR = new Matx33f(
            worldToCamera.m00, worldToCamera.m01, worldToCamera.m02,
            worldToCamera.m10, worldToCamera.m11, worldToCamera.m12,
            worldToCamera.m20, worldToCamera.m21, worldToCamera.m22
        );
        
        // 更新平移向量
        currentT = new Vector3(
            worldToCamera.m03,
            worldToCamera.m13,
            worldToCamera.m23
        );
        
        // 记录上一次的状态用于平滑过渡
        lastT = currentT;
        lastR = currentR;
    }
    private void rt2mat()
    {
        if (inputTexture == null)
        {
            inputTexture = new Texture2D(inputImage.width, inputImage.height, TextureFormat.RGB24, false);
        }
        
        // 保存当前的RenderTexture
        RenderTexture currentRT = RenderTexture.active;
        
        try
        {
            // 设置inputImage为活动的RenderTexture
            RenderTexture.active = inputImage;
            
            // 从RenderTexture读取像素到inputTexture
            inputTexture.ReadPixels(new UnityEngine.Rect(0, 0, inputImage.width, inputImage.height), 0, 0);
            
            // 应用像素读取操作
            inputTexture.Apply();
            
            // 使用OpenCVForUnity提供的工具函数直接将Texture2D转换为Mat
            // 这比手动循环提取像素更高效且代码更简洁
            if (inputMat == null)
            {
                // 创建与Texture2D尺寸相同的Mat对象，使用CV_8UC3格式（8位无符号整数，3通道）
                inputMat = new Mat(inputTexture.height, inputTexture.width, CvType.CV_8UC3);
            }

            // 使用Utils.texture2DToMat直接进行转换
            // 参数说明：
            // 1. 源Texture2D对象
            // 2. 目标Mat对象
            // 注意：OpenCV使用BGR格式存储颜色，而Unity使用RGB格式
            Utils.texture2DToMat(inputTexture, inputMat, false); // 不翻转y轴，刻意进行的。
            
            // 保存inputMat为PNG文件
            string savePath = Path.Combine(Application.persistentDataPath, "inputMat.png");
            Imgcodecs.imwrite(savePath, inputMat);
            Debug.Log("inputMat 已保存为 PNG 文件: " + savePath);
            Debug.Log("Texture2D successfully converted to Mat using OpenCVForUnity Utils.");
        }
        catch (Exception e)
        {
            Debug.LogError("Error converting RenderTexture to Texture2D: " + e.Message);
        }
        finally
        {
            // 恢复之前的RenderTexture
            RenderTexture.active = currentRT;
        }
    }


    public void SetupFirstGTFrame()
    {
        // init Tracker 
        Matrix4x4 cameraToWorld = TrackerCam.transform.localToWorldMatrix;
        Matrix4x4 worldToCamera = cameraToWorld.inverse;

        // 以模型的局部坐标系为世界坐标系
        // 在相机坐标系下的模型transform
        currentR = new Matx33f(
            worldToCamera.m00, worldToCamera.m01, worldToCamera.m02,
            worldToCamera.m10, worldToCamera.m11, worldToCamera.m12,
            worldToCamera.m20, worldToCamera.m21, worldToCamera.m22
        );

        currentT = new Vector3(
                    worldToCamera.m03,
                    worldToCamera.m13,
                    worldToCamera.m23
                );

        // 初始化颜色Historgram
        colorHistogram = new ColorHistogram();
        // 在相机坐标系下，模型的Pose
        ModelTracker.Pose modelPose = new ModelTracker.Pose { R = currentR, t = currentT };
        // 计算相机内参矩阵K
        float fov = TrackerCam.fieldOfView * Mathf.Deg2Rad;
        int width = TrackerCam.pixelWidth;
        int height = TrackerCam.pixelHeight;

        float focalLength = (float)(height / 2.0f / Mathf.Tan(fov / 2.0f));
        float cx = (float)width / 2.0f;
        float cy = (float)height / 2.0f;

        // 构建相机内参矩阵K
        Matx33f K = new Matx33f(
            focalLength, 0, cx,
            0, focalLength, cy,
            0, 0, 1
        );

        // 初始化颜色Historgram
        Mat img = new Mat(height, width, CvType.CV_32FC1);
        colorHistogram.update(templateRuntimeInst.ModelTemplate, ref img, ref modelPose, K, 1f);

        // mat 转换为 texture
        if (colorHistogramTexture == null)
            colorHistogramTexture = new Texture2D(width, height, TextureFormat.RFloat, false);
        // 将Mat数据拷贝到Texture2D
        Utils.matToTexture2D(img, colorHistogramTexture);
        // 赋值给输出RawImage
        resultImage.texture = colorHistogramTexture;

    }

    void Awake()
    {

    }

    void Start()
    {

    }

    void Update()
    {

    }
    
    /// <summary>
    /// 手动触发一次相机渲染
    /// </summary>
    public void ManualRenderCamera()
    {
        if (TrackerCam == null || inputImage == null)
        {
            Debug.LogError("TrackerCam or inputImage is not initialized.");
            return;
        }
        
        // 保存当前的RenderTexture
        RenderTexture currentRT = RenderTexture.active;
        
        try
        {
            // 设置相机的目标渲染纹理
            TrackerCam.targetTexture = inputImage;
            
            // 手动触发相机渲染
            TrackerCam.Render();
            
            // 处理渲染结果（使用现有的rt2mat方法）
            rt2mat();
            
            // 如果有resultImage，则显示渲染结果
            if (resultImage != null)
            {
                resultImage.texture = inputTexture;
            }
            
            Debug.Log("Camera rendering completed successfully.");
        }
        catch (Exception e)
        {
            Debug.LogError("Error during manual camera rendering: " + e.Message);
        }
        finally
        {
            // 恢复之前的RenderTexture
            RenderTexture.active = currentRT;
        }
    }
}