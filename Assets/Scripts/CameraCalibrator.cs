// ---------------------------------------------------------------------------------
// CameraCalibrator.cs
// 相机标定核心功能实现类
// 主要负责：
// - 棋盘格检测和角点提取
// - 相机标定算法实现
// - 标定结果保存和管理
// - 错误处理和用户反馈
// ---------------------------------------------------------------------------------
using UnityEngine;
using UnityEngine.UI;
using OpenCVForUnity.CoreModule;
using OpenCVForUnity.ImgprocModule;
using OpenCVForUnity.Calib3dModule;
using OpenCVForUnity.UnityUtils;
using System.Collections.Generic;
using System;
using System.IO;

public class CameraCalibrator : MonoBehaviour
{
    // ===========================================================
    // 棋盘格参数设置
    // ===========================================================
    [Header("棋盘格参数")]
    [Tooltip("棋盘格内角点宽度数量")]
    public int chessboardWidth = 9;  // 棋盘格内角点宽度
    [Tooltip("棋盘格内角点高度数量")]
    public int chessboardHeight = 6; // 棋盘格内角点高度
    [Tooltip("棋盘格方块实际尺寸（米）")]
    public float squareSize = 0.025f; // 棋盘格方块实际尺寸（米）
    
    // ===========================================================
    // 相机输入
    // ===========================================================
    [Header("相机设置")]
    [Tooltip("显示相机图像的RawImage组件")]
    public RawImage cameraFeed;

    // ===========================================================
    // ChessBoard检测输出
    // ===========================================================
    [Header("输出图像")]
    [Tooltip("显示输出图像的RawImage组件")]
    public RawImage resultImage;

    private WebCamTexture inputWebCamTexture; // 处理摄像头纹理的情况

    // ===========================================================
    // 标定数据存储
    // ===========================================================
    // 图像平面点集合 - 存储从图像中检测到的角点
    private List<MatOfPoint2f> imagePoints = new List<MatOfPoint2f>();
    
    // 物体平面点集合 - 存储对应图像点的实际3D世界坐标
    private List<MatOfPoint3f> objectPoints = new List<MatOfPoint3f>();
    
    // 检测到棋盘格的帧集合 - 存储用于标定的关键帧
    private List<Mat> detectedFrames = new List<Mat>();
    
    // ===========================================================
    // 标定结果存储
    // ===========================================================
    private Mat cameraMatrix = new Mat(); // 相机内参矩阵 [fx 0 cx; 0 fy cy; 0 0 1]
    private MatOfDouble distCoeffs = new MatOfDouble();   // 畸变系数 [k1 k2 p1 p2 k3 ...]
    private List<Mat> rvecs = new List<Mat>(); // 旋转向量列表
    private List<Mat> tvecs = new List<Mat>(); // 平移向量列表
    
    // ===========================================================
    // 状态管理
    // ===========================================================
    private bool isChessboardDetected = false; // 当前帧是否检测到棋盘格
    private MatOfPoint2f corners = new MatOfPoint2f(); // 当前检测到的角点
    private Mat grayMat = new Mat();  // 当前灰度帧缓存
    private Mat displayMat = new Mat(); // 当前显示帧
    
    // ===========================================================
    // 错误和警告信息管理
    // ===========================================================
    private string lastError = string.Empty;
    private string lastWarning = string.Empty;
    
    // ===========================================================
    // 事件系统
    // ===========================================================
    // 棋盘格检测状态变化事件
    public Action<bool> OnChessboardDetectedChanged;
    
    // 数据帧数变化事件
    public Action<int> OnDataFrameCountChanged;
    
    // 错误和状态事件
    public Action<string> OnErrorOccurred;    // 错误发生事件
    public Action<string> OnWarningOccurred;  // 警告发生事件
    public Action<string> OnStatusChanged;    // 状态更新事件
    
    // ===========================================================
    // 生命周期方法
    // ===========================================================
    // 更新方法 - 每帧调用
    void Update()
    {
        // 检查相机输入是否有效
        if (cameraFeed != null && cameraFeed.texture != null)
        {
            // 处理当前相机帧
            ProcessFrame();
        }
    }
    
    // 处理相机帧 - 核心图像处理逻辑
    private void ProcessFrame()
    {
        // 检查输入纹理类型（WebCamTexture或普通Texture2D）
        if (cameraFeed.texture is WebCamTexture)
        {
            inputWebCamTexture = cameraFeed.texture as WebCamTexture;
            // 确保WebCamTexture已经初始化并有数据
            if (!inputWebCamTexture.isPlaying || inputWebCamTexture.width <= 16 || inputWebCamTexture.height <= 16)
                return;
        }

        // 获取相机纹理并转换为Mat
        Mat frameMat;
        // 将Unity纹理转换为OpenCV的Mat
        if (cameraFeed.texture is WebCamTexture)
        {
            frameMat = new Mat(inputWebCamTexture.height, inputWebCamTexture.width, CvType.CV_8UC3);
            Utils.webCamTextureToMat(inputWebCamTexture, frameMat);
        }
        else
        {
            Texture2D texture2D = cameraFeed.texture as Texture2D;
            frameMat = new Mat(texture2D.height, texture2D.width, CvType.CV_8UC3);
            Utils.texture2DToMat(texture2D, frameMat);
        }
        
        // 创建帧的副本用于显示，避免修改原始帧
        frameMat.copyTo(displayMat);
        
        // 转换为灰度图用于角点检测
        Imgproc.cvtColor(frameMat, grayMat, Imgproc.COLOR_RGB2GRAY);
        
        // 设置增强的棋盘格检测参数
        // 标志说明：
        // - CALIB_CB_ADAPTIVE_THRESH: 使用自适应阈值
        // - CALIB_CB_NORMALIZE_IMAGE: 图像归一化
        // - CALIB_CB_FAST_CHECK: 快速检查是否存在棋盘格，加速检测
        int detectionFlags = Calib3d.CALIB_CB_ADAPTIVE_THRESH 
            + Calib3d.CALIB_CB_NORMALIZE_IMAGE
            + Calib3d.CALIB_CB_FAST_CHECK;
        
        // 第一步：使用默认参数尝试检测棋盘格角点
        bool found = Calib3d.findChessboardCorners(grayMat, 
            new Size(chessboardWidth, chessboardHeight), 
            corners, 
            detectionFlags);

        // 如果基本检测失败，尝试更激进的参数
        //if (!found)
        //{
        //    // 降低图像分辨率以提高检测成功率
        //    Mat resizedGray = new Mat();
        //    Imgproc.resize(grayMat, resizedGray, new Size(), 0.5, 0.5, Imgproc.INTER_AREA);

        //    // 使用更激进的检测参数
        //    int aggressiveFlags = detectionFlags;

        //    // 尝试检测
        //    found = Calib3d.findChessboardCorners(resizedGray, 
        //        new Size(chessboardWidth, chessboardHeight), 
        //        corners, 
        //        aggressiveFlags);

        //    // 如果在缩放图像上检测成功，需要将角点坐标缩放回原始尺寸
        //    if (found)
        //    {
        //        Point[] cornerArray = corners.toArray();
        //        for (int i = 0; i < cornerArray.Length; i++)
        //        {
        //            cornerArray[i].x *= 2;
        //            cornerArray[i].y *= 2;
        //        }
        //        corners.fromArray(cornerArray);
        //    }

        //    resizedGray.Dispose();
        //}

        // 如果检测到棋盘格，进行亚像素精确化
        // 使用亚像素级精确化来提高角点检测精度
        if (found)
        {
            // 使用更小的窗口和迭代次数以提高性能
            Imgproc.cornerSubPix(grayMat, corners, new Size(5, 5), new Size(-1, -1),
                new TermCriteria(TermCriteria.EPS + TermCriteria.MAX_ITER, 10, 0.01));

            // 在显示图像上绘制检测到的角点，使用彩色线条
            Calib3d.drawChessboardCorners(displayMat, 
                new Size(chessboardWidth, chessboardHeight), 
                corners, 
                found);
                
            // 绘制文字提示
            Imgproc.putText(displayMat, "Chessboard Detected", new Point(10, 30), 
                Imgproc.FONT_HERSHEY_SIMPLEX, 1.0, new Scalar(0, 255, 0), 2);
        }
        else
        {
            // 绘制提示信息
            Imgproc.putText(displayMat, "Put Chessboard ahead camera", new Point(10, 30), 
                Imgproc.FONT_HERSHEY_SIMPLEX, 1.0, new Scalar(0, 0, 255), 2);
        }
        
        // 更新状态标志
        // 只有当检测状态发生变化时才触发事件，减少不必要的UI更新
        if (isChessboardDetected != found)
        {
            isChessboardDetected = found;
            OnChessboardDetectedChanged?.Invoke(found);
            
            // 记录检测状态变化
            string statusMsg = "棋盘格检测状态: " + (found ? "检测到" : "未检测到");
            Debug.Log(statusMsg);
            
            // 触发状态变化事件
            OnStatusChanged?.Invoke(statusMsg);
            
            // 清除警告
            if (found && !string.IsNullOrEmpty(lastWarning))
            {
                lastWarning = string.Empty;
            }
        }
        
        // 更新显示
        UpdateDisplay();
        
        // 释放临时Mat
        frameMat.Dispose();
    }
    
    private void UpdateDisplay()
    {
        // 如果displayMat不为空，可以在这里实现检测结果的显示
        // 实际使用时，可以通过UI脚本中的detectionResultImage来显示
        // 这里我们先保持简单，让UI脚本来处理显示

        // 将处理后的Mat转换回Texture
        if (resultImage != null)
        {
            Texture processedTexture;
            processedTexture = new Texture2D(displayMat.cols(), displayMat.rows(), TextureFormat.RGB24, false);
            Utils.matToTexture2D(displayMat, processedTexture as Texture2D);
            // 赋值给输出RawImage
            resultImage.texture = processedTexture;
        }
    }
    
    // 获取当前处理后的显示图像
    public Mat GetDisplayMat()
    {
        return displayMat;
    }
    
    // 获取最近的错误信息
    public string GetLastError()
    {
        return lastError;
    }
    
    // 获取最近的警告信息
    public string GetLastWarning()
    {
        return lastWarning;
    }
    
    // ===========================================================
    // 错误和警告管理
    // ===========================================================
    
    /// <summary>
    /// 设置错误信息并触发错误事件
    /// </summary>
    /// <param name="errorMsg">错误消息内容</param>
    private void SetError(string errorMsg)
    {
        lastError = errorMsg;
        Debug.LogError("相机标定错误: " + errorMsg);
        OnErrorOccurred?.Invoke(errorMsg);
    }
    
    /// <summary>
    /// 设置警告信息并触发警告事件
    /// </summary>
    /// <param name="warningMsg">警告消息内容</param>
    private void SetWarning(string warningMsg)
    {
        lastWarning = warningMsg;
        Debug.LogWarning("相机标定警告: " + warningMsg);
        OnWarningOccurred?.Invoke(warningMsg);
    }
    
    /// <summary>
    /// 清除错误信息
    /// </summary>
    public void ClearError()
    {
        lastError = string.Empty;
    }
    
    /// <summary>
    /// 清除警告信息
    /// </summary>
    public void ClearWarning()
    {
        lastWarning = string.Empty;
    }
    
    // ===========================================================
    // 数据管理方法
    // ===========================================================
    
    /// <summary>
    /// 保存当前帧数据到标定数据集
    /// 只有成功检测到棋盘格的帧才能被保存用于标定
    /// </summary>
    /// <returns>是否成功保存帧数据</returns>
    public bool SaveCurrentFrameData()
    {
        ClearError();
        
        if (!isChessboardDetected || corners.empty())
        {
            string errorMsg = "无法保存帧数据：未检测到棋盘格或角点为空";
            SetError(errorMsg);
            return false;
        }
        
        try
        {
            // 验证角点数量是否正确
            int expectedCorners = chessboardWidth * chessboardHeight;
            if (corners.size().height != expectedCorners)
            {
                string warningMsg = "检测到的角点数量不匹配: " + corners.size().height + " 期望: " + expectedCorners;
                SetWarning(warningMsg);
                return false;
            }
            
            // 检查角点质量 - 通过计算角点平均距离判断检测质量
        // 角点距离过近或过远可能表示检测有误
        double avgDistance = CalculateAverageCornerDistance(corners);
        if (avgDistance < 5.0) // 角点太密集，可能是错误检测
        {
                string warningMsg = "角点质量不佳，平均距离: " + avgDistance;
                SetWarning(warningMsg);
                return false;
            }
            
            // 保存图像点
            MatOfPoint2f cornersCopy = new MatOfPoint2f();
            corners.copyTo(cornersCopy);
            imagePoints.Add(cornersCopy);
            
            // 生成对应的物体点
            MatOfPoint3f objPoints = GenerateObjectPoints();
            objectPoints.Add(objPoints);
            
            // 保存当前检测到的帧
            Mat frameCopy = new Mat();
            displayMat.copyTo(frameCopy);
            detectedFrames.Add(frameCopy);
            
            // 记录保存信息和提示
            int frameCount = imagePoints.Count;
            string statusMsg = "成功保存第 " + frameCount + " 帧标定数据，角点数: " + corners.size().height;
            
            // 根据帧数给出提示
            if (frameCount < 5)
            {
                statusMsg += " (建议至少收集5帧数据)";
            }
            else if (frameCount < 10)
            {
                statusMsg += " (从更多角度收集数据可提高精度)";
            }
            
            Debug.Log(statusMsg);
            OnStatusChanged?.Invoke(statusMsg);
            
            // 通知UI更新帧数
            OnDataFrameCountChanged?.Invoke(frameCount);
            
            return true;
        }
        catch (Exception ex)
        {
            string errorMsg = "保存帧数据时发生异常: " + ex.Message;
            SetError(errorMsg);
            Debug.LogError("异常详情: " + ex.ToString());
            
            // 尝试清理可能的不完整数据
            try
            {
                if (imagePoints.Count > 0)
                {
                    MatOfPoint2f lastPoints = imagePoints[imagePoints.Count - 1];
                    if (lastPoints != null)
                    {
                        lastPoints.Dispose();
                    }
                    imagePoints.RemoveAt(imagePoints.Count - 1);
                }
                if (objectPoints.Count > 0)
                {
                    MatOfPoint3f lastObjPoints = objectPoints[objectPoints.Count - 1];
                    if (lastObjPoints != null)
                    {
                        lastObjPoints.Dispose();
                    }
                    objectPoints.RemoveAt(objectPoints.Count - 1);
                }
                if (detectedFrames.Count > 0)
                {
                    Mat lastFrame = detectedFrames[detectedFrames.Count - 1];
                    if (lastFrame != null)
                    {
                        lastFrame.Dispose();
                    }
                    detectedFrames.RemoveAt(detectedFrames.Count - 1);
                }
                // 更新UI帧数
                OnDataFrameCountChanged?.Invoke(imagePoints.Count);
            }
            catch {}
            
            return false;
        }
    }
    
    /// <summary>
    /// 计算角点之间的平均距离
    /// 用于评估检测质量，距离异常可能表示检测错误
    /// </summary>
    /// <param name="corners">检测到的角点</param>
    /// <returns>角点之间的平均距离</returns>
    private double CalculateAverageCornerDistance(MatOfPoint2f corners)
    {
        Point[] points = corners.toArray();
        if (points.Length < 2)
            return 0;
        
        double totalDistance = 0;
        int count = 0;
        
        // 计算相邻角点间的距离
        for (int i = 0; i < points.Length - 1; i++)
        {
            for (int j = i + 1; j < points.Length; j++)
            {
                // 只计算合理范围内的相邻角点（棋盘格相邻的点）
                double dist = Mathf.Sqrt((float)((points[i].x - points[j].x) * (points[i].x - points[j].x) + (points[i].y - points[j].y) * (points[i].y - points[j].y)));
                if (dist < 100) // 限制最大距离，避免计算无关点之间的距离
                {
                    totalDistance += dist;
                    count++;
                }
            }
        }
        
        return count > 0 ? totalDistance / count : 0;
    }
    
    /// <summary>
    /// 生成棋盘格的3D物体点坐标
    /// 基于棋盘格参数生成对应的世界坐标点
    /// </summary>
    /// <returns>包含棋盘格3D点的MatOfPoint3f对象</returns>
    private MatOfPoint3f GenerateObjectPoints()
    {
        MatOfPoint3f objPts = new MatOfPoint3f();
        List<Point3> points = new List<Point3>();
        
        for (int i = 0; i < chessboardHeight; i++)
        {
            for (int j = 0; j < chessboardWidth; j++)
            {
                points.Add(new Point3(j * squareSize, i * squareSize, 0));
            }
        }
        
        objPts.fromList(points);
        return objPts;
    }
    
    /// <summary>
    /// 执行相机标定计算
    /// 使用收集的棋盘格图像点和对应物体点计算相机内参和畸变系数
    /// </summary>
    /// <returns>是否成功完成标定</returns>
    public bool CalibrateCamera()
    {
        ClearError();
        
        // 检查是否有足够的数据帧
        // 标定通常需要至少5个不同角度的视图才能获得稳定结果
        if (imagePoints.Count < 5) // 至少需要5帧数据
        {
            string warningMsg = "标定失败：数据帧数不足，需要至少5帧不同角度的数据";
            SetWarning(warningMsg);
            return false;
        }
        
        try
        {
            // 初始化相机矩阵和畸变系数
            cameraMatrix = Mat.eye(3, 3, CvType.CV_64F); // 3x3单位矩阵
            distCoeffs = new MatOfDouble(Mat.zeros(8, 1, CvType.CV_64F)); // 畸变系数初始化为零
            
            // 清除之前可能存在的旋转和平移向量
            rvecs.Clear();
            tvecs.Clear();
            
            string statusMsg = "开始相机标定计算，使用 " + imagePoints.Count + " 帧数据...";
            Debug.Log(statusMsg);
            OnStatusChanged?.Invoke(statusMsg);
            
            // 定义多种标定策略
            int[][] calibStrategies = {
                new int[] { Calib3d.CALIB_FIX_K3 },  // 固定k3畸变系数
                new int[] { 0 },                     // 完全自由标定
                new int[] { Calib3d.CALIB_FIX_ASPECT_RATIO } // 固定纵横比
            };
            string[] strategyNames = {
                "标准策略（固定k3）",
                "自由策略（全参数）",
                "固定纵横比策略"
            };
            
            // 尝试多种标定策略，选择最佳结果
            double bestRms = double.MaxValue;
            Mat bestCameraMatrix = null;
            MatOfDouble bestDistCoeffs = null;
            List<Mat> bestRvecs = null;
            List<Mat> bestTvecs = null;
            int bestStrategyIndex = -1;
            
            for (int i = 0; i < calibStrategies.Length; i++)
            {
                try
                {
                    Mat strategyCameraMatrix = Mat.eye(3, 3, CvType.CV_64F);
                    Mat strategyDistCoeffs = Mat.zeros(8, 1, CvType.CV_64F);
                    List<Mat> strategyRvecs = new List<Mat>();
                    List<Mat> strategyTvecs = new List<Mat>();
                    
                    Debug.Log("尝试标定策略: " + strategyNames[i]);
                    
                    // 将List<MatOfPoint3f>和List<MatOfPoint2f>转换为List<Mat>以符合calibrateCamera函数参数要求
                    List<Mat> objectPointsMat = new List<Mat>();
                    foreach (var points in objectPoints)
                    {
                        objectPointsMat.Add(points);
                    }
                    
                    List<Mat> imagePointsMat = new List<Mat>();
                    foreach (var points in imagePoints)
                    {
                        imagePointsMat.Add(points);
                    }
                    
                    double rms = Calib3d.calibrateCamera(objectPointsMat, imagePointsMat,
                        new Size(grayMat.cols(), grayMat.rows()),
                        strategyCameraMatrix, strategyDistCoeffs, strategyRvecs, strategyTvecs, calibStrategies[i][0]);
                    
                    // 验证结果有效性
                    if (!double.IsNaN(rms) && !double.IsInfinity(rms) && rms < bestRms)
                    {
                        // 释放之前的最佳结果
                        if (bestCameraMatrix != null) bestCameraMatrix.Dispose();
                        if (bestDistCoeffs != null) bestDistCoeffs.Dispose();
                        if (bestRvecs != null)
                        {
                            foreach (var vec in bestRvecs) vec.Dispose();
                            bestRvecs.Clear();
                        }
                        if (bestTvecs != null)
                        {
                            foreach (var vec in bestTvecs) vec.Dispose();
                            bestTvecs.Clear();
                        }
                        
                        // 更新最佳结果
                        bestRms = rms;
                        bestCameraMatrix = strategyCameraMatrix;
                        bestDistCoeffs = new MatOfDouble(strategyDistCoeffs);
                        bestRvecs = strategyRvecs;
                        bestTvecs = strategyTvecs;
                        bestStrategyIndex = i;
                        
                        Debug.Log(string.Format("策略 {0} RMS误差: {1:F3}", strategyNames[i], rms));
                    }
                    else
                    {
                        // 释放当前策略的结果
                        strategyCameraMatrix.Dispose();
                        strategyDistCoeffs.Dispose();
                        foreach (var vec in strategyRvecs) vec.Dispose();
                        foreach (var vec in strategyTvecs) vec.Dispose();
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning(string.Format("策略 {0} 执行失败: {1}", strategyNames[i], ex.Message));
                }
            }
            
            // 检查是否获得了有效的结果
            if (bestRms == double.MaxValue || bestCameraMatrix == null)
            {
                string errorMsg = "所有标定策略均失败，请检查输入数据质量";
                SetError(errorMsg);
                
                // 清理资源
                cameraMatrix.Dispose();
                distCoeffs.Dispose();
                cameraMatrix = new Mat();
                distCoeffs = new MatOfDouble();
                
                return false;
            }
            
            // 使用最佳结果
            if (!cameraMatrix.empty()) cameraMatrix.Dispose();
            if (!distCoeffs.empty()) distCoeffs.Dispose();
            foreach (var vec in rvecs) vec.Dispose();
            foreach (var vec in tvecs) vec.Dispose();
            
            cameraMatrix = bestCameraMatrix;
            distCoeffs = bestDistCoeffs;
            rvecs = bestRvecs;
            tvecs = bestTvecs;
            
            // 计算重投影误差，评估标定质量
            double reprojectionError = CalculateReprojectionError();
            
            // 生成详细的结果报告
            Debug.Log("=============================================");
            Debug.Log("相机标定完成！");
            Debug.Log(string.Format("最佳策略: {0}", strategyNames[bestStrategyIndex]));
            Debug.Log(string.Format("RMS误差: {0:F3}", bestRms));
            
            // 根据误差大小给出质量评估
            if (bestRms < 0.5)
            {
                Debug.Log("标定质量: 优秀");
            }
            else if (bestRms < 1.0)
            {
                Debug.Log("标定质量: 良好");
            }
            else if (bestRms < 2.0)
            {
                Debug.Log("标定质量: 可接受");
                SetWarning("RMS误差略高，可考虑收集更多数据提高精度");
            }
            else
            {
                Debug.Log("标定质量: 一般");
                SetWarning("RMS误差较高，建议重新标定并收集更多角度的数据");
            }
            
            Debug.Log(string.Format("平均重投影误差: {0:F3} 像素", reprojectionError));
            Debug.Log(string.Format("相机矩阵:\n{0}", cameraMatrix.dump()));
            Debug.Log(string.Format("畸变系数:\n{0}", distCoeffs.dump()));
            Debug.Log("=============================================");
            
            // 触发状态事件
            OnStatusChanged?.Invoke(string.Format("标定完成！质量: {0}, RMS: {1:F3}", 
                bestRms < 0.5 ? "优秀" : bestRms < 1.0 ? "良好" : bestRms < 2.0 ? "可接受" : "一般", 
                bestRms));
            
            return true;
        }
        catch (Exception ex)
        {
            string errorMsg = "标定计算过程中发生异常: " + ex.Message;
            SetError(errorMsg);
            Debug.LogError("异常详情: " + ex.ToString());
            
            // 清理资源
            try
            {
                if (!cameraMatrix.empty()) cameraMatrix.Dispose();
                if (!distCoeffs.empty()) distCoeffs.Dispose();
                foreach (var vec in rvecs) vec.Dispose();
                foreach (var vec in tvecs) vec.Dispose();
                cameraMatrix = new Mat();
                distCoeffs = new MatOfDouble();
                rvecs.Clear();
                tvecs.Clear();
            }
            catch {}
            
            return false;
        }
    }
    
    // 计算重投影误差，评估标定质量
    private double CalculateReprojectionError()
    {
        double totalErr = 0;
        int totalPoints = 0;
        
        List<MatOfPoint2f> reprojImagePoints = new List<MatOfPoint2f>();
        
        for (int i = 0; i < objectPoints.Count; i++)
        {
            MatOfPoint2f reprojPoints = new MatOfPoint2f();
            Calib3d.projectPoints(objectPoints[i], rvecs[i], tvecs[i], cameraMatrix, distCoeffs, reprojPoints);
            
            // 计算该帧的平均误差
            double err = Core.norm(imagePoints[i], reprojPoints, Core.NORM_L2);
            int n = objectPoints[i].rows();
            totalErr += err * err;
            totalPoints += n;
            
            reprojPoints.Dispose();
        }
        
        return Math.Sqrt(totalErr / totalPoints);
    }
    
    /// <summary>
    /// 保存标定结果到JSON文件
    /// 支持Windows和Android平台的路径处理
    /// </summary>
    /// <param name="filename">保存的文件名</param>
    /// <returns>是否成功保存结果</returns>
    public bool SaveCalibrationResult(string filename = "camera_calibration.json")
    {
        ClearError();
        
        if (cameraMatrix.empty() || distCoeffs.empty())
        {
            string errorMsg = "无法保存标定结果：标定数据为空";
            SetError(errorMsg);
            return false;
        }
        
        try
        {
            // 检查写入权限
        // 特别是在移动平台上，需要确保有适当的文件访问权限
        if (!HasWritePermission())
        {
                string errorMsg = "没有文件写入权限，无法保存标定结果";
                SetError(errorMsg);
                return false;
            }
            
            // 确保文件有.json扩展名
        if (!Path.HasExtension(filename) || Path.GetExtension(filename).ToLower() != ".json")
        {
            filename += ".json";
        }
        
        // 为保存文件生成一个安全的文件名
        // 使用时间戳确保文件名唯一性
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        filename = $"camera_calibration_{timestamp}.json";
        
        // 创建标定结果对象
            CalibrationData calibData = new CalibrationData
            {
                cameraMatrix = ConvertMatToArray(cameraMatrix),
                distCoeffs = ConvertMatToArray(distCoeffs),
                imageWidth = grayMat.cols(),
                imageHeight = grayMat.rows(),
                rmsError = CalculateReprojectionError(),
                frameCount = imagePoints.Count,
                chessboardWidth = chessboardWidth,
                chessboardHeight = chessboardHeight,
                squareSize = squareSize,
                timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                unityVersion = Application.unityVersion,
                platform = Application.platform.ToString()
            };
            
            // 序列化为JSON
            string jsonData;
            try
            {
                // 使用格式化选项使JSON文件更易读
                jsonData = JsonUtility.ToJson(calibData, true);
                // 验证JSON数据有效性
                if (string.IsNullOrEmpty(jsonData) || jsonData == "{}")
                {
                    string errorMsg = "JSON序列化失败，生成了空数据";
                    SetError(errorMsg);
                    return false;
                }
            }
            catch (Exception jsonEx)
            {
                string errorMsg = "JSON序列化失败: " + jsonEx.Message;
                SetError(errorMsg);
                return false;
            }
            
            // 获取保存路径
            // 根据平台选择合适的保存路径
            // 不同平台(Windows/Android)有不同的文件系统访问规则
            string savePath = GetSavePath(filename);
            
            // 确保目标目录存在
            string directory = Path.GetDirectoryName(savePath);
            try
            {
                if (!Directory.Exists(directory))
                {
                    Debug.Log("创建保存目录: " + directory);
                    Directory.CreateDirectory(directory);
                    
                    // 验证目录创建成功
                    if (!Directory.Exists(directory))
                    {
                        string errorMsg = "创建保存目录失败: " + directory;
                        SetError(errorMsg);
                        return false;
                    }
                }
            }
            catch (Exception dirEx)
            {
                string errorMsg = "创建保存目录异常: " + dirEx.Message;
                SetError(errorMsg);
                return false;
            }
            
            Debug.Log("准备保存标定结果到: " + savePath);
            
            // 实现多种备用写入方式
            bool saveSuccess = false;
            
            // 尝试方法1：标准文件写入
            if (!saveSuccess)
            {
                try
                {
                    File.WriteAllText(savePath, jsonData);
                    saveSuccess = File.Exists(savePath);
                    if (saveSuccess)
                    {
                        Debug.Log("方法1成功：使用File.WriteAllText写入文件");
                    }
                }
                catch (Exception ex1)
                {
                    Debug.LogWarning("方法1异常：" + ex1.Message);
                }
            }
            
            // 尝试方法2：使用StreamWriter
            if (!saveSuccess)
            {
                try
                {
                    using (FileStream fs = new FileStream(savePath, FileMode.Create, FileAccess.Write))
                    using (StreamWriter sw = new StreamWriter(fs))
                    {
                        sw.Write(jsonData);
                        sw.Flush();
                    }
                    saveSuccess = File.Exists(savePath);
                    if (saveSuccess)
                    {
                        Debug.Log("方法2成功：使用StreamWriter写入文件");
                    }
                }
                catch (Exception ex2)
                {
                    Debug.LogWarning("方法2异常：" + ex2.Message);
                }
            }
            
            // 尝试方法3：二进制写入（特别是对Android平台有用）
            if (!saveSuccess && Application.platform == RuntimePlatform.Android)
            {
                try
                {
                    byte[] bytes = System.Text.Encoding.UTF8.GetBytes(jsonData);
                    using (FileStream fs = new FileStream(savePath, FileMode.Create, FileAccess.Write))
                    {
                        fs.Write(bytes, 0, bytes.Length);
                        fs.Flush();
                    }
                    saveSuccess = File.Exists(savePath);
                    if (saveSuccess)
                    {
                        Debug.Log("方法3成功：使用二进制写入文件");
                    }
                }
                catch (Exception ex3)
                {
                    Debug.LogWarning("方法3异常：" + ex3.Message);
                }
            }
            
            // 验证保存结果
            if (!saveSuccess)
            {
                string errorMsg = string.Format("文件保存失败，所有写入方法均尝试过。平台: {0}", Application.platform);
                SetError(errorMsg);
                return false;
            }
            
            // 验证文件大小和完整性
            try
            {
                FileInfo fileInfo = new FileInfo(savePath);
                if (fileInfo.Length == 0)
                {
                    string errorMsg = "文件已保存但大小为0字节，可能写入不完整";
                    SetError(errorMsg);
                    File.Delete(savePath); // 删除空文件
                    return false;
                }
                
                // 记录详细的保存信息
                Debug.Log("=============================================");
                Debug.Log("标定结果保存成功！");
                Debug.Log(string.Format("文件路径: {0}", savePath));
                Debug.Log(string.Format("文件大小: {0} 字节 ({1:F2} KB)", fileInfo.Length, fileInfo.Length / 1024.0));
                Debug.Log(string.Format("平台: {0}", Application.platform));
                Debug.Log("=============================================");
                
                // 触发状态事件
                OnStatusChanged?.Invoke(string.Format("标定结果已成功保存到: {0}", savePath));
            }
            catch (Exception infoEx)
            {
                Debug.LogWarning("获取文件信息失败: " + infoEx.Message);
                // 即使获取文件信息失败，只要保存成功就返回true
            }
            
            return true;
        }
        catch (Exception e)
        {
            string errorMsg = "保存标定结果过程中发生异常: " + e.Message;
            SetError(errorMsg);
            Debug.LogError("异常详情: " + e.ToString());
            return false;
        }
    }
    
    /// <summary>
    /// 获取标定质量评级
    /// 基于重投影误差评估标定结果的质量
    /// </summary>
    /// <returns>质量评级文本</returns>
    public string GetCalibrationQualityRating()
    {
        if (cameraMatrix.empty() || distCoeffs.empty())
            return "未标定";
            
        double rms = CalculateReprojectionError();
        
        if (rms < 0.5)
            return "优秀 (RMS < 0.5)";
        else if (rms < 1.0)
            return "良好 (RMS < 1.0)";
        else if (rms < 2.0)
            return "可接受 (RMS < 2.0)";
        else
            return "一般 (RMS >= 2.0)";
    }
    
    /// <summary>
    /// 获取适合当前平台的文件保存路径
    /// 处理Windows和Android平台的路径差异
    /// </summary>
    /// <param name="filename">文件名</param>
    /// <returns>完整的文件保存路径</returns>
    private string GetSavePath(string filename)
    {
        string directoryPath;
        
        // 根据平台选择合适的保存目录
        switch (Application.platform)
        {
            case RuntimePlatform.WindowsEditor:
                // Windows编辑器环境下，保存到项目目录下的CalibrationData文件夹，方便访问
                directoryPath = Path.Combine(Application.dataPath, "..", "CalibrationData");
                break;
                
            case RuntimePlatform.Android:
                // Android平台下，保存到持久化数据路径的子目录
                directoryPath = Path.Combine(Application.persistentDataPath, "Calibration");
                // 对于Android 10+，可能需要使用MediaStore API保存到公共目录，但这需要额外权限
                // 这里使用应用私有目录，足够一般使用场景
                break;
                
            case RuntimePlatform.IPhonePlayer:
                // iOS平台
                directoryPath = Path.Combine(Application.persistentDataPath, "Calibration");
                break;
                
            default:
                // 其他平台默认使用持久化数据路径
                directoryPath = Path.Combine(Application.persistentDataPath, "Calibration");
                break;
        }
        
        // 确保文件名有效
        string safeFilename = MakeFilenameSafe(filename);
        
        // 组合完整路径
        string savePath = Path.Combine(directoryPath, safeFilename);
        
        // 记录保存路径（调试用）
        Debug.Log(string.Format("保存路径 (平台: {0}): {1}", Application.platform, savePath));
        
        return savePath;
    }
    
    /// <summary>
    /// 确保文件名安全有效
    /// 移除或替换文件系统不允许的字符
    /// </summary>
    /// <param name="filename">原始文件名</param>
    /// <returns>安全的文件名</returns>
    private string MakeFilenameSafe(string filename)
    {
        // 移除或替换不合法的字符
        string invalidChars = new string(Path.GetInvalidFileNameChars());
        string safeFilename = filename;
        
        foreach (char c in invalidChars)
        {
            safeFilename = safeFilename.Replace(c, '_');
        }
        
        // 限制文件名长度
        if (safeFilename.Length > 100)
        {
            string extension = Path.GetExtension(safeFilename);
            string nameWithoutExtension = Path.GetFileNameWithoutExtension(safeFilename);
            safeFilename = nameWithoutExtension.Substring(0, 90 - extension.Length) + "..." + extension;
        }
        
        return safeFilename;
    }
    
    /// <summary>
    /// 检查应用是否有文件写入权限
    /// 特别是在Android平台上需要权限检查
    /// </summary>
    /// <returns>是否有写入权限</returns>
    public bool HasWritePermission()
    {
        try
        {
            string testPath = GetSavePath("test_permission.txt");
            string testDir = Path.GetDirectoryName(testPath);
            
            // 尝试创建目录
            if (!Directory.Exists(testDir))
            {
                Directory.CreateDirectory(testDir);
            }
            
            // 尝试写入测试文件
            File.WriteAllText(testPath, "Permission test file.");
            
            // 读取回来验证
            string content = File.ReadAllText(testPath);
            
            // 删除测试文件
            File.Delete(testPath);
            
            return content == "Permission test file.";
        }
        catch (Exception e)
        {
            Debug.LogError("写入权限检查失败: " + e.Message);
            return false;
        }
    }
    
    /// <summary>
    /// 将OpenCV Mat对象转换为数组
    /// 用于JSON序列化保存
    /// </summary>
    /// <param name="mat">要转换的Mat对象</param>
    /// <returns>转换后的数组</returns>
    private double[] ConvertMatToArray(Mat mat)
    {
        int rows = mat.rows();
        int cols = mat.cols();
        double[] array = new double[rows * cols];
        
        for (int i = 0; i < rows; i++)
        {
            for (int j = 0; j < cols; j++)
            {
                array[i * cols + j] = mat.get(i, j)[0];
            }
        }
        
        return array;
    }
    
    // ===========================================================
    // 数据结构定义
    // ===========================================================
    /// <summary>
    /// 标定数据类，用于JSON序列化
    /// 存储相机标定的所有关键参数
    /// </summary>
    [System.Serializable]
    public class CalibrationData
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
    
    // ===========================================================
    // 公共接口方法
    // ===========================================================
    
    /// <summary>
    /// 获取当前有效数据帧数
    /// </summary>
    /// <returns>已收集的数据帧数</returns>
    public int GetDataFrameCount()
    {
        return imagePoints.Count;
    }
    
    /// <summary>
    /// 清除所有标定数据
    /// 释放资源并重置状态
    /// </summary>
    public void ClearCalibrationData()
    {
        Debug.Log("清除标定数据...");
        
        // 释放所有资源
        foreach (var pts in imagePoints)
            pts.Dispose();
        foreach (var pts in objectPoints)
            pts.Dispose();
        foreach (var frame in detectedFrames)
            frame.Dispose();
        foreach (var rvec in rvecs)
            rvec.Dispose();
        foreach (var tvec in tvecs)
            tvec.Dispose();
        
        // 清空列表
        imagePoints.Clear();
        objectPoints.Clear();
        detectedFrames.Clear();
        rvecs.Clear();
        tvecs.Clear();
        
        // 重置标定结果
        if (!cameraMatrix.empty())
            cameraMatrix.Dispose();
        if (!distCoeffs.empty())
            distCoeffs.Dispose();
        cameraMatrix = new Mat();
        distCoeffs = new MatOfDouble();
        
        // 通知UI更新
        OnDataFrameCountChanged?.Invoke(0);
        
        Debug.Log("标定数据已清除");
    }
    
    /// <summary>
    /// 检查当前是否检测到棋盘格
    /// </summary>
    /// <returns>是否检测到棋盘格</returns>
    public bool IsChessboardDetected()
    {
        return isChessboardDetected;
    }
    
        // ===========================================================
    // 资源管理
    // ===========================================================
    
    /// <summary>
    /// 释放资源
    /// 在对象销毁时调用，确保所有Mat对象被正确释放
    /// </summary>
    void OnDestroy()
    {
        // 释放资源
        grayMat.Dispose();
        displayMat.Dispose();
        corners.Dispose();
        ClearCalibrationData();
    }
}