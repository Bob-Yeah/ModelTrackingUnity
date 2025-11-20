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
                //Debug.Log("执行二值化处理提取物体mask");
                Mat maskMat = new Mat();
                
                // 将32位浮点Mat转换为8位用于二值化
                Mat floatMask = new Mat();
                rChannelMat.convertTo(floatMask, CvType.CV_8UC1, 255.0);
                
                // 二值化：大于0的像素设为255（白色），否则为0（黑色）
                Imgproc.threshold(floatMask, maskMat, thresholdValue, 255, Imgproc.THRESH_BINARY);
                //Debug.Log($"二值化处理完成，阈值: {thresholdValue}");
                

                
                // 步骤3: 在mask上执行findContour轮廓检测
                //Debug.Log("开始轮廓检测");
                List<MatOfPoint> contours = new List<MatOfPoint>();
                Mat hierarchy = new Mat();
                
                // 执行轮廓检测
                Imgproc.findContours(maskMat, contours, hierarchy, Imgproc.RETR_EXTERNAL, Imgproc.CHAIN_APPROX_SIMPLE);
                Debug.Log($"轮廓检测完成，找到 {contours.Count} 个轮廓");
                
                List<Point> finalContourPoints = new List<Point>();

                foreach (MatOfPoint contour in contours)
                {
                    double area = Imgproc.contourArea(contour);

                    List<Point> _contourPoints = contour.toList();

                    if (area < 0)
                    {
                        _contourPoints.Reverse();
                    }

                    finalContourPoints.AddRange(_contourPoints);
                }


                Debug.Log($"整合轮廓数量: {finalContourPoints.Count}");

                if (finalContourPoints.Count > 3)
                {
                    //Debug.Log($"找到最大轮廓，面积: {maxArea}");

                    // 步骤4: 根据轮廓点获取对应深度值
                    Debug.Log("开始从轮廓点获取深度值");
                    List<Vector3> pointCloud = new List<Vector3>();
                    List<Vector3> smoothPointCloud = new List<Vector3>();

                    // 获取轮廓点的数组
                    //Point[] contourPoints = largestContour.toArray();
                    Point[] contourPoints = finalContourPoints.ToArray();
                    Debug.Log($"轮廓包含 {contourPoints.Length} 个点");

                    // 平滑轮廓
                    const int smoothWSZ = 15 , hwsz = smoothWSZ / 2;;

                    // 将轮廓点转换为Mat格式
                    Mat cimg = new Mat(contourPoints.Length, 2, CvType.CV_32FC1);
                    for (int i = 0; i < contourPoints.Length; i++)
                    {
                        cimg.put(i, 0, new double[] { contourPoints[i].x });
                        cimg.put(i, 1, new double[] { contourPoints[i].y });
                    }
                    
                    // 平滑处理
                    Mat smoothed = new Mat();
                    Imgproc.boxFilter(cimg, smoothed, -1, new Size(1, smoothWSZ));
                    //Debug.Log($"smoothed mat:{smoothed.size()}");
                    
                    Point[] smoothContourPoints = new Point[contourPoints.Length];
                    for (int i = 0;i < contourPoints.Length;i++)
                    {
                        float[] _x = new float[1];
                        float[] _y = new float[1];
                        smoothed.get(i, 0, _x);
                        smoothed.get(i, 1, _y);

                        smoothContourPoints[i] = new Point(_x[0], _y[0]);
                    }
                    Debug.Log($"平滑轮廓2 {smoothContourPoints.Length} 个点");
                    
                    // 为了避免处理过多点，进行降采样（可选）
                    int sampleRate = Mathf.Max(1, contourPoints.Length / maxPointCount); // 最多处理maxPointCount个点
                    
                    for (int i = 0; i < contourPoints.Length; i += sampleRate)
                    {
                        Point pt = contourPoints[i];
                        Point pt_s = smoothContourPoints[i];
                        
                        // 确保点在有效范围内
                        if (pt.x >= 0 && pt.x < width && pt.y >= 0 && pt.y < height)
                        {
                            // 从原始深度图Mat获取深度值（32位浮点）
                            double[] pixelValue = rChannelMat.get((int)pt.y, (int)pt.x);
                            float depthValue = (float)pixelValue[0];
                            Debug.Log($"采样轮廓 {pt.y}, {pt.x}, smooth: {pt_s.y}, {pt_s.x}, depth: {depthValue}");

                            //测试用
                            maskMat.put((int)pt.y, (int)pt.x, 125);
                            // 只添加有效的深度值（大于0）
                            if (depthValue > 0)
                            {
                                // 存储像素坐标和深度值
                                pointCloud.Add(new Vector3((float)pt.x, (float)(pt.y), depthValue)); //深度值是真实值
                                // 2D normal
                                Vector2 n = new Vector2();
                                n += new Vector2((float)(smoothContourPoints[(i + contourPoints.Length - hwsz) % contourPoints.Length].x - pt_s.x), (float)(smoothContourPoints[(i + contourPoints.Length - hwsz) % contourPoints.Length].y - pt_s.y));
                                n += new Vector2((float)(pt_s.x - smoothContourPoints[(i + hwsz) % contourPoints.Length].x), (float)(pt_s.y - smoothContourPoints[(i + hwsz) % contourPoints.Length].y));
                                n.Normalize();
                                n = new Vector2(n.y, -n.x);
                                Vector2 offset_p = new Vector2((float)(pt.x + n.x), (float)(pt.y + n.y));

                                // 存储平滑后的像素坐标和深度值
                                smoothPointCloud.Add(new Vector3((float)offset_p.x, (float)(offset_p.y), depthValue)); //深度值是真实值
                                
                            }
                        }
                    }
                    
                    Debug.Log($"成功获取 {pointCloud.Count} 个有效深度点");

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
                        }
                        else
                        {
                            Debug.LogError($"无法保存二值化mask: {maskFullPath}");
                        }


                        Mat mat_vis_cp = new Mat(maskMat.rows(), maskMat.cols(), CvType.CV_8UC3);
                        foreach (var p2d in smoothContourPoints)
                        {
                            Imgproc.circle(mat_vis_cp, new Point((int)p2d.x, (int)p2d.y), 1, new Scalar(255, 255, 255), -1);
                        }

                        foreach (var p2d in contourPoints)
                        {
                            Imgproc.circle(mat_vis_cp, new Point((int)p2d.x, (int)p2d.y), 1, new Scalar(0, 0, 255), -1);
                        }

                        // 生成时间戳
                        string maskFileName2 = $"mask_smooth_{timestamp}.png";
                        string maskFullPath2 = Path.Combine(savePath, maskFileName2);

                        bool maskSaveSuccess2 = OpenCVForUnity.ImgcodecsModule.Imgcodecs.imwrite(maskFullPath2, mat_vis_cp);
                        if (maskSaveSuccess2)
                        {
                            Debug.Log($"平滑轮廓mask已成功保存为PNG: {maskFullPath2}");
                        }
                        else
                        {
                            Debug.LogError($"无法保存二值化mask: {maskFullPath}");
                        }
                    }


                    // 步骤5: 使用相机参数将点云反投影到Unity空间
                    Debug.Log("开始将点云反投影到Unity空间");
                    
                    // 创建投影器
                    Projector prj = new Projector(K, R, t);
                    
                    for (int i = 0; i< pointCloud.Count; i++)
                    {
                        Vector3 point = pointCloud[i];
                        Vector3 s_point = smoothPointCloud[i];
                        // 使用Projector类的Unproject方法将2D点反投影到3D空间
                        Vector3 worldPoint = prj.Unproject(point.x, point.y, point.z); //x,y 是像素的位置

                        Vector3 worldPoint_smooth = prj.Unproject(s_point.x, s_point.y, s_point.z); //x,y 是像素的位置
                        
                        // 创建CPoint并添加到结果列表
                        CPoint cPoint = new CPoint();
                        cPoint.center = worldPoint;
                        // 暂时不计算法线，可以根据需要添加法线计算逻辑
                        cPoint.normal_offset = worldPoint_smooth; // 默认法线向上
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
                    (float)(nextPoint.x - prevPoint.x),
                    (float)(nextPoint.y - prevPoint.y)
                );
                
                // 法线是切线逆时针旋转90度
                normal = new Vector3(-tangent.y, tangent.x, 0).normalized;
            }
            
            return normal;
        }
    }
}
