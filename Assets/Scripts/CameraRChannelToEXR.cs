using UnityEngine;
using UnityEngine.UI;
using System.IO;
using System;

#if UNITY_EDITOR
using UnityEditor;
#endif

// 导入OpenCV for Unity命名空间
using OpenCVForUnity.CoreModule;
using OpenCVForUnity.ImgcodecsModule;
using OpenCVForUnity.ImgprocModule;
using System.Collections.Generic;
using ModelTracker; // 导入ModelTracker命名空间

/// <summary>
/// 相机R通道提取与EXR保存工具
/// 功能：获取相机渲染图，提取R通道，转换为OpenCV Mat，并保存为EXR文件
/// </summary>
public class CameraRChannelToEXR : MonoBehaviour
    {
        [Header("相机设置")]
        [Tooltip("要使用的相机，如果为空则使用主相机")]
        public Camera targetCamera;
        
        [Header("保存设置")]
        [Tooltip("保存路径，默认为Assets/DepthMaps")]
        public string savePath = "Assets/DepthMaps";
        [Tooltip("文件名前缀")]
        public string fileNamePrefix = "r_channel";
        
        [Header("处理设置")]
        [Tooltip("二值化阈值")]
        [Range(0, 255)]
        public int thresholdValue = 0;
        [Tooltip("最大处理的轮廓点数量")]
        [Range(10, 500)]
        public int maxPointCount = 200;
        [Tooltip("是否可视化点云")]
        public bool visualizePointCloud = true;
        [Tooltip("点云小球大小")]
        [Range(0.0001f, 0.01f)]
        public float sphereSize = 0.001f;
        
        // 用于存储渲染结果的纹理
        private RenderTexture renderTexture;
        private Texture2D captureTexture;
        
        // 用于存储点云可视化的小球对象
        private List<GameObject> pointCloudSpheres = new List<GameObject>();
        
        /// <summary>
        /// 清除之前创建的点云小球
        /// </summary>
        private void ClearPointCloudSpheres()
        {
            foreach (GameObject sphere in pointCloudSpheres)
            {
                if (sphere != null)
                {
                    DestroyImmediate(sphere);
                }
            }
            pointCloudSpheres.Clear();
            Debug.Log("已清除之前的点云小球");
        }
    
    /// <summary>
    /// 初始化相机引用
    /// </summary>
    private void Awake()
    {
        if (targetCamera == null)
        {
            targetCamera = Camera.main;
            if (targetCamera == null)
            {
                Debug.LogError("未找到可用的相机！请在Inspector中指定相机。");
            }
        }
    }
    
    /// <summary>
    /// 释放资源
    /// </summary>
    private void OnDestroy()
    {
        ReleaseResources();
    }
    
    /// <summary>
    /// 释放纹理资源
    /// </summary>
    private void ReleaseResources()
    {
        if (renderTexture != null)
        {
            renderTexture.Release();
            renderTexture = null;
        }
        
        if (captureTexture != null)
        {
            Destroy(captureTexture);
            captureTexture = null;
        }
    }
    
    /// <summary>
    /// 捕获相机渲染图并处理
    /// </summary>
    public void CaptureAndProcess()
    {
        if (targetCamera == null)
        {
            Debug.LogError("相机未设置，无法捕获渲染图。");
            return;
        }
        
        try
        {
            // 创建或调整渲染纹理大小
            if (renderTexture == null || renderTexture.width != targetCamera.pixelWidth || renderTexture.height != targetCamera.pixelHeight)
            {
                ReleaseResources();
                renderTexture = new RenderTexture(targetCamera.pixelWidth, targetCamera.pixelHeight, 24);
                captureTexture = new Texture2D(targetCamera.pixelWidth, targetCamera.pixelHeight, TextureFormat.RGBAFloat, false);
            }
            
            // 保存当前相机的目标纹理
            RenderTexture previousTargetTexture = targetCamera.targetTexture;
            
            try
            {
                // 设置相机目标纹理并渲染
                targetCamera.targetTexture = renderTexture;
                targetCamera.Render();
                
                // 切换到渲染纹理并读取像素
                RenderTexture.active = renderTexture;
                captureTexture.ReadPixels(new UnityEngine.Rect(0, 0, renderTexture.width, renderTexture.height), 0, 0);
                captureTexture.Apply();
            }
            finally
            {
                // 恢复相机原始设置
                targetCamera.targetTexture = previousTargetTexture;
                RenderTexture.active = null;
            }
            
            // 处理纹理并保存
            ProcessAndSaveTexture();
        }
        catch (Exception e)
        {
            Debug.LogError($"捕获和处理过程中发生错误: {e.Message}\n{e.StackTrace}");
        }
    }
    
    /// <summary>
    /// 处理纹理（提取R通道并转换为OpenCV Mat）并保存
    /// </summary>
    private void ProcessAndSaveTexture()
    {        
        try
        {
            // 确保保存目录存在
            if (!Directory.Exists(savePath))
            {
                Directory.CreateDirectory(savePath);
            }
            
            // 提取R通道数据
            Color[] pixels = captureTexture.GetPixels();
            int width = captureTexture.width;
            int height = captureTexture.height;
            
            // 创建浮点数组存储R通道数据
            float[] rChannelData = new float[width * height];
            for (int i = 0; i < pixels.Length; i++)
            {
                rChannelData[i] = pixels[i].r; // 只提取R通道值
            }
            
            // 创建OpenCV Mat（32位浮点，单通道）
            Mat rChannelMat = new Mat(height, width, CvType.CV_32FC1);
            
            try
            {
                // 将数据复制到Mat
                rChannelMat.put(0, 0, rChannelData);
                
                // 生成时间戳
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                
                // 使用EdgeSampler.Sample方法替代原有的处理逻辑
                Debug.Log("调用EdgeSampler.Sample方法处理深度图");
                
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
                Matrix4x4 worldToCamera = cam.worldToCameraMatrix;
                Matrix4x4 cameraToWorld = worldToCamera.inverse;
                
                // 构建旋转矩阵R
                Matx33f R = new Matx33f(
                    cameraToWorld.m00, cameraToWorld.m01, cameraToWorld.m02,
                    cameraToWorld.m10, cameraToWorld.m11, cameraToWorld.m12,
                    cameraToWorld.m20, cameraToWorld.m21, cameraToWorld.m22
                );
                
                // 构建平移向量t
                Vector3 t = new Vector3(
                    cameraToWorld.m03, 
                    cameraToWorld.m13, 
                    cameraToWorld.m23
                );
                
                // 调用EdgeSampler.Sample方法进行处理
                List<CPoint> sampledPoints = EdgeSampler.Sample(rChannelMat, thresholdValue, maxPointCount, K, R, t, savePath);
                
                // 提取世界点用于可视化
                List<Vector3> worldPoints = new List<Vector3>();
                foreach (CPoint cPoint in sampledPoints)
                {
                    worldPoints.Add(cPoint.center);
                }
                
                Debug.Log($"成功通过EdgeSampler获取 {worldPoints.Count} 个3D点");
                    
                    // 步骤6: 创建小球可视化反投影点云结果
                    if (visualizePointCloud)
                    {
                        Debug.Log("开始创建小球可视化点云");
                        
                        // 清除之前的小球
                        ClearPointCloudSpheres();
                        
                        // 为每个反投影点创建小球
                        foreach (Vector3 worldPoint in worldPoints)
                        {
                            GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                            sphere.transform.position = worldPoint;
                            sphere.transform.localScale = new Vector3(sphereSize, sphereSize, sphereSize);
                        
                        // 设置红色材质以便于识别
                        Renderer renderer = sphere.GetComponent<Renderer>();
                        if (renderer != null)
                        {
                            renderer.sharedMaterial = new Material(Shader.Find("Standard"));
                            renderer.sharedMaterial.color = Color.red;
                        }
                        
                            // 将小球添加到列表中以便后续清除
                            pointCloudSpheres.Add(sphere);
                        }
                        
                        Debug.Log($"成功创建 {pointCloudSpheres.Count} 个小球来可视化点云");
                    }
                }
                else
                {
                    Debug.LogWarning("未找到轮廓");
                }
                
                // 生成文件名（带时间戳避免覆盖）
                string fileName = $"{fileNamePrefix}_{timestamp}.exr";
                string fullPath = Path.Combine(savePath, fileName);
                
                // 添加调试信息
                Debug.Log($"尝试保存EXR文件: {fullPath}");
                Debug.Log($"Mat信息 - 宽度: {rChannelMat.width()}, 高度: {rChannelMat.height()}, 类型: {rChannelMat.type()}");
                
                // 检查目录是否可写
                if (Directory.Exists(savePath))
                {
                    Debug.Log($"保存目录存在: {savePath}");
                }
                else
                {
                    Debug.LogError($"保存目录不存在: {savePath}");
                }
                
                // 尝试保存为EXR文件
                // 修改：尝试不使用类型参数，或使用不同的参数组合
                MatOfInt saveParams = new MatOfInt();
                
                // 尝试不同的保存方式
                bool success = false;
                
                try
                {
                    // 方法1: 不指定特殊参数
                    success = Imgcodecs.imwrite(fullPath, rChannelMat);
                    if (success)
                    {
                        Debug.Log("使用默认参数成功保存EXR文件");
                    }
                    else
                    {
                        Debug.Log("使用默认参数保存失败，尝试指定浮点类型");
                        // 方法2: 指定浮点类型
                        saveParams.fromArray(Imgcodecs.IMWRITE_EXR_TYPE_FLOAT);
                        success = Imgcodecs.imwrite(fullPath, rChannelMat, saveParams);
                        if (success)
                        {
                            Debug.Log("使用浮点类型参数成功保存EXR文件");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"保存EXR文件时发生异常: {ex.Message}\n{ex.StackTrace}");
                }
                finally
                {
                    // 释放参数资源
                    saveParams.Dispose();
                }
                
                if (success)
                {
                    Debug.Log($"R通道数据已成功保存为EXR文件: {fullPath}");
                    
                    #if UNITY_EDITOR
                    // 在Unity编辑器中刷新资源
                    AssetDatabase.Refresh();
                    #endif
                }
                else
                {
                    Debug.LogError($"无法保存EXR文件: {fullPath}");
                    // 尝试保存为PNG作为备选方案
                    string pngPath = fullPath.Replace(".exr", ".png");
                    bool pngSuccess = Imgcodecs.imwrite(pngPath, rChannelMat*255);
                    if (pngSuccess)
                    {
                        Debug.Log($"作为备选方案，成功保存为PNG文件: {pngPath}");
                        #if UNITY_EDITOR
                        AssetDatabase.Refresh();
                        #endif
                    }
                }
            }
            finally
            {
                // 释放Mat资源
                rChannelMat.Dispose();
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"处理和保存过程中发生错误: {e.Message}\n{e.StackTrace}");
        }
    }
}

#if UNITY_EDITOR
/// <summary>
/// CameraRChannelToEXR的编辑器扩展，添加Inspector按钮
/// </summary>
[CustomEditor(typeof(CameraRChannelToEXR))]
public class CameraRChannelToEXREditor : Editor
{
    public override void OnInspectorGUI()
    {
        // 绘制默认Inspector
        base.OnInspectorGUI();
        
        // 添加一个按钮
        CameraRChannelToEXR script = (CameraRChannelToEXR)target;
        if (GUILayout.Button("捕获相机R通道并保存为EXR"))
        {
            script.CaptureAndProcess();
        }
    }
}
#endif