using OpenCVForUnity.CoreModule;
using OpenCVForUnity.ImgprocModule;
using UnityEngine;
using System.Collections.Generic;

namespace ModelTracker
{
    // 根据深度图进行轮廓的采样
    // 边缘采样器类
    public class EdgeSampler
    {
        //// 从深度图中提取渲染掩码
        //public static Mat GetRenderMask(Mat depth)
        //{
        //    Mat mask = new Mat();
        //    Imgproc.threshold(depth, mask, 0, 255, Imgproc.THRESH_BINARY | Imgproc.THRESH_OTSU);
        //    mask.convertTo(mask, CvType.CV_8UC1);
        //    return mask;
        //}

        //// 计算矩阵的步长
        //private static int StepC(Mat mat)
        //{
        //    return (int)(mat.step() / mat.elemSize());
        //}

        //private static bool get3D(Mat depthMask, OpenCVForUnity.CoreModule.Rect roiRect, Projector prj, int x, int y, ref Vector3 P, ref float _z)
        //{
        //    float[] z_data = new float[1];
        //    if (depthMask.get(y, x, z_data) > 0)
        //    {
        //        float z = z_data[0];
        //        if (z != 0)
        //        {
        //            P = prj.Unproject((float)(x + roiRect.x), (float)(y + roiRect.y), z);
        //            _z = z;
        //            return true;
        //        }
        //    }
        //    return false;
        //}
        
        //// 深度图ROI区域的采样
        //private static void _Sample(OpenCVForUnity.CoreModule.Rect roiRect, Mat depthMat, Projector prj, List<CPoint> c3d, int nSamples)
        //{   
        //    Mat depthMask = GetRenderMask(depthMat);

        //    List<List<Point>> contours = new List<List<Point>>();
        //    List<MatOfPoint> contours_mop = new List<MatOfPoint>();
        //    Imgproc.findContours(depthMask, contours_mop, new Mat(), Imgproc.RETR_EXTERNAL, Imgproc.CHAIN_APPROX_NONE);

        //    // 转换为List<Point>格式
        //    foreach (var mop in contours_mop)
        //    {
        //        List<Point> contour = mop.toList();
        //        contours.Add(contour);
        //        mop.Dispose();
        //    }

            

        //    // 计算总点数和跳过步长
        //    int npt = 0;
        //    foreach (var c in contours)
        //        npt += c.Count;
        //    int nSkip = npt / nSamples;

        //    const int smoothWSZ = 15;
        //    int hwsz = smoothWSZ / 2;

        //    foreach (var c in contours)
        //    {
        //        // 计算轮廓面积，确保轮廓是逆时针方向
        //        double area = Imgproc.contourArea(new MatOfPoint(c.ToArray()));
        //        if (area < 0) // 顺时针方向
        //            c.Reverse(); // 反转成逆时针方向

        //        // 创建点矩阵并进行平滑处理
        //        Mat cimg = new Mat(1, c.Count, CvType.CV_32SC2);
        //        for (int i = 0; i < c.Count; i++)
        //        {
        //            cimg.put(0, i, c[i].x, c[i].y);
        //        }

        //        Mat smoothed = new Mat();
        //        Imgproc.boxFilter(cimg, smoothed, CvType.CV_32FC2, new Size(smoothWSZ, 1));

        //        // 采样轮廓点
        //        for (int i = nSkip / 2; i < c.Count; i += nSkip)
        //        {
        //            CPoint P = new CPoint();
        //            float depth = 0;
        //            if (get3D(c[i].x, c[i].y, ref P.center, ref depth))
        //            {
        //                Vector2 n = new Vector2(0, 0);

        //                // 计算法线向量
        //                float[] pt_i = new float[2];
        //                smoothed.get(0, i, pt_i);
        //                Vector2 pt_i_p = new Vector2(pt_i[0], pt_i[1]);

        //                if (i - hwsz >= 0)
        //                {
        //                    float[] pt_prev = new float[2];
        //                    smoothed.get(0, i - hwsz, pt_prev);
        //                    Vector2 pt_prev_p = new Vector2(pt_prev[0], pt_prev[1]);
        //                    n += new Vector2(pt_i_p.x - pt_prev_p.x, pt_i_p.y - pt_prev_p.y);
        //                }

        //                if (i + hwsz < smoothed.cols())
        //                {
        //                    float[] pt_next = new float[2];
        //                    smoothed.get(0, i + hwsz, pt_next);
        //                    Vector2 pt_next_p = new Vector2(pt_next[0], pt_next[1]);
        //                    n += new Vector2(pt_next_p.x - pt_i_p.x, pt_next_p.y - pt_i_p.y);
        //                }

        //                // 归一化法线
        //                float norm = (float)Mathf.Sqrt(n.x * n.x + n.y * n.y);
        //                if (norm > 0)
        //                    n = new Vector2(n.x / norm, n.y / norm);

        //                // 旋转法线90度（逆时针）
        //                n = new Vector2(-n.y, n.x);

        //                // 计算法线端点
        //                Vector2 q = new Vector2((float)c[i].x, (float)c[i].y) + n;
        //                q.x += roiRect.x;
        //                q.y += roiRect.y;

        //                // 获取法线向量的3D点
        //                P.normal = prj.Unproject(q.x, q.y, depth);
        //                c3d.Add(P);
        //            }
        //        }

        //        // 释放资源
        //        cimg.Dispose();
        //        smoothed.Dispose();
        //    }

        //    // 释放资源
        //    depthMask.Dispose();
        //}

        //// 公开的采样方法
        //public static void Sample(List<CPoint> c3d, Mat depthBuffer, Matx33f K, Mat transformMat, int nSamples, OpenCVForUnity.CoreModule.Rect roiRect)
        //{
        //    Matx33f R = new Matx33f();
        //    Vector3 t = new Vector3();
        //    Utils.DecomposeRT(transformMat, ref R, ref t);
        //    Projector prj = new Projector(K, R, t);
        //    _Sample(roiRect, depthBuffer, prj, c3d, nSamples);
        //}
    }
}
