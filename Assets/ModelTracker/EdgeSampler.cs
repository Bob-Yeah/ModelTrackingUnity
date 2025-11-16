using OpenCVForUnity.CoreModule;
using OpenCVForUnity.ImgprocModule;
using UnityEngine;
using System.Collections.Generic;
using System.IO;

namespace ModelTracker
{
    // 根据深度图进行轮廓的采样
    // 边缘采样器类
    public class EdgeSampler
    {
        // 从深度图中提取物体轮廓并采样
        public static List<CPoint> Sample(Mat rChannelMat, float thresholdValue, int maxPointCount, Matx33f K, Matx33f R, Vector3 t, string savePath = null)
        {
            List<CPoint> result = new List<CPoint>();
            int width = rChannelMat.cols();
            int height = rChannelMat.rows();
            
            try
            {
                // 步骤1: 二值化处理提取物体mask
                Debug.Log("执行二值化处理提取物体mask");
                Mat maskMat = new Mat();
                
                // 将32位浮点Mat转换为8位用于二值化
                Mat floatMask = new Mat();
                rChannelMat.convertTo(floatMask, CvType.CV_8UC1, 255.0);
                
                // 二值化：大于0的像素设为255（白色），否则为0（黑色）
                Imgproc.threshold(floatMask, maskMat, thresholdValue, 255, Imgproc.THRESH_BINARY);
                Debug.Log($"二值化处理完成，阈值: {thresholdValue}");
                
                // 步骤2: 将二值化mask保存为PNG用于debug
                if (!string.IsNullOrEmpty(savePath))
                {
                    // 确保保存目录存在
                    if (!Directory.Exists(savePath))
                    {
                        Directory.CreateDirectory(savePath);
                    }
                    
                    // 生成时间戳
                    string timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    string maskFileName = $"mask_{timestamp}.png";
                    string maskFullPath = Path.Combine(savePath, maskFileName);
                    
                    bool maskSaveSuccess = OpenCVForUnity.ImgcodecsModule.Imgcodecs.imwrite(maskFullPath, maskMat);
                    if (maskSaveSuccess)
                    {
                        Debug.Log($"二值化mask已成功保存为PNG: {maskFullPath}");
#if UNITY_EDITOR
                        UnityEditor.AssetDatabase.Refresh();
#endif
                    }
                    else
                    {
                        Debug.LogError($"无法保存二值化mask: {maskFullPath}");
                    }
                }
                
                // 步骤3: 在mask上执行findContour轮廓检测
                Debug.Log("开始轮廓检测");
                List<MatOfPoint> contours = new List<MatOfPoint>();
                Mat hierarchy = new Mat();
                
                // 执行轮廓检测
                Imgproc.findContours(maskMat, contours, hierarchy, Imgproc.RETR_EXTERNAL, Imgproc.CHAIN_APPROX_SIMPLE);
                Debug.Log($"轮廓检测完成，找到 {contours.Count} 个轮廓");
                
                // 筛选出最大的轮廓（假设是主要物体）
                MatOfPoint largestContour = null;
                double maxArea = 0;
                
                foreach (MatOfPoint contour in contours)
                {
                    double area = Imgproc.contourArea(contour);
                    if (area > maxArea)
                    {
                        maxArea = area;
                        largestContour = contour;
                    }
                }
                
                if (largestContour != null)
                {
                    Debug.Log($"找到最大轮廓，面积: {maxArea}");
                    
                    // 步骤4: 根据轮廓点获取对应深度值
                    Debug.Log("开始从轮廓点获取深度值");
                    List<Vector3> pointCloud = new List<Vector3>();
                    
                    // 获取轮廓点的数组
                    Point[] contourPoints = largestContour.toArray();
                    Debug.Log($"轮廓包含 {contourPoints.Length} 个点");
                    
                    // 为了避免处理过多点，进行降采样（可选）
                    int sampleRate = Mathf.Max(1, contourPoints.Length / maxPointCount); // 最多处理maxPointCount个点
                    
                    for (int i = 0; i < contourPoints.Length; i += sampleRate)
                    {
                        Point pt = contourPoints[i];
                        
                        // 确保点在有效范围内
                        if (pt.x >= 0 && pt.x < width && pt.y >= 0 && pt.y < height)
                        {
                            // 从原始深度图Mat获取深度值（32位浮点）
                            double[] pixelValue = rChannelMat.get((int)pt.y, (int)pt.x);
                            float depthValue = (float)pixelValue[0];
                            
                            // 只添加有效的深度值（大于0）
                            if (depthValue > 0)
                            {
                                // 存储像素坐标和深度值
                                pointCloud.Add(new Vector3((float)pt.x, (float)pt.y, depthValue));
                            }
                        }
                    }
                    
                    Debug.Log($"成功获取 {pointCloud.Count} 个有效深度点");
                    
                    // 步骤5: 使用相机参数将点云反投影到Unity空间
                    Debug.Log("开始将点云反投影到Unity空间");
                    
                    // 创建投影器
                    Projector prj = new Projector(K, R, t);
                    
                    foreach (Vector3 point in pointCloud)
                    {
                        // 使用Projector类的Unproject方法将2D点反投影到3D空间
                        Vector3 worldPoint = prj.Unproject(point.x, point.y, point.z);
                        
                        // 创建CPoint并添加到结果列表
                        CPoint cPoint = new CPoint();
                        cPoint.center = worldPoint;
                        // 暂时不计算法线，可以根据需要添加法线计算逻辑
                        cPoint.normal = Vector3.up; // 默认法线向上
                        result.Add(cPoint);
                    }
                    
                    Debug.Log($"成功将 {result.Count} 个点反投影到Unity世界空间");
                }
                
                // 释放资源
                maskMat.Dispose();
                floatMask.Dispose();
                hierarchy.Dispose();
                foreach (var contour in contours)
                {
                    contour.Dispose();
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"采样过程中发生错误: {e.Message}\n{e.StackTrace}");
            }
            
            return result;
        }
        
        // 计算轮廓法线的辅助方法
        private static Vector3 CalculateNormal(Point[] contourPoints, int index, int smoothSize = 5)
        {
            // 简化的法线计算，可以根据需要优化
            Vector3 normal = Vector3.up;
            
            if (contourPoints.Length > 2)
            {
                // 获取当前点及其前后点
                int prevIndex = (index - smoothSize + contourPoints.Length) % contourPoints.Length;
                int nextIndex = (index + smoothSize) % contourPoints.Length;
                
                Point prevPoint = contourPoints[prevIndex];
                Point currPoint = contourPoints[index];
                Point nextPoint = contourPoints[nextIndex];
                
                // 计算切线向量
                Vector2 tangent = new Vector2(
                    nextPoint.x - prevPoint.x,
                    nextPoint.y - prevPoint.y
                );
                
                // 法线是切线逆时针旋转90度
                normal = new Vector3(-tangent.y, tangent.x, 0).normalized;
            }
            
            return normal;
        }
    }
}
