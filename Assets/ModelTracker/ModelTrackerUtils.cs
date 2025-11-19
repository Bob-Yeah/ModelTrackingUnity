using UnityEngine;
using UnityEngine.UI;
using OpenCVForUnity.CoreModule;
using OpenCVForUnity.ImgprocModule;
using OpenCVForUnity.Calib3dModule;
using OpenCVForUnity.UnityUtils;
using System.Collections.Generic;
using System;
using System.IO;

namespace ModelTracker
{
    public static class ModelTrackerUtils
    {
        // 获取点集的边界框
        public static OpenCVForUnity.CoreModule.Rect getBoundingBox2D(List<Vector2> points)
        {
            if (points == null || points.Count == 0)
            {
                return new OpenCVForUnity.CoreModule.Rect(0, 0, 0, 0);
            }

            float minX = float.MaxValue;
            float minY = float.MaxValue;
            float maxX = float.MinValue;
            float maxY = float.MinValue;

            foreach (var point in points)
            {
                minX = Mathf.Min(minX, point.x);
                minY = Mathf.Min(minY, point.y);
                maxX = Mathf.Max(maxX, point.x);
                maxY = Mathf.Max(maxY, point.y);
            }

            int x = Mathf.FloorToInt(minX);
            int y = Mathf.FloorToInt(minY);
            int width = Mathf.CeilToInt(maxX - minX);
            int height = Mathf.CeilToInt(maxY - minY);

            return new OpenCVForUnity.CoreModule.Rect(x, y, width, height);
        }

        public static void DecomposeRT(Mat modelMat, ref Matx33f R, ref Vector3 t)
        {
            Mat rvec = new Mat(3, 1, CvType.CV_32FC1);
            Mat tvec = new Mat(3, 1, CvType.CV_32FC1);
            Mat Rmat = new Mat(3, 3, CvType.CV_32FC1);
            Calib3d.decomposeProjectionMatrix(modelMat, new Mat(), Rmat, tvec, rvec, new Mat(), new Mat(), new Mat());
            R = new Matx33f(Rmat);
            t = new Vector3((float)tvec.get(0, 0)[0], (float)tvec.get(1, 0)[0], (float)tvec.get(2, 0)[0]);
        }

        public static void rectAppend(ref OpenCVForUnity.CoreModule.Rect rect, int _left, int _top, int _right, int _bottom)
        {
            int right = rect.x + rect.width + _right, bottom = rect.y + rect.height + _bottom;

            rect.x -= _left; rect.y -= _top;

            rect.width = right - rect.x;
            if (rect.width < 0)
                rect.width = 0;

            rect.height = bottom - rect.y;
            if (rect.height < 0)
                rect.height = 0;
        }

        public static OpenCVForUnity.CoreModule.Rect rectOverlapped(OpenCVForUnity.CoreModule.Rect rect1, OpenCVForUnity.CoreModule.Rect rect2)
        {
            // 创建空矩形作为默认返回值
            OpenCVForUnity.CoreModule.Rect emptyRect = new OpenCVForUnity.CoreModule.Rect(0, 0, 0, 0);

            // 计算两个矩形的边界
            int left1 = rect1.x;
            int top1 = rect1.y;
            int right1 = rect1.x + rect1.width;
            int bottom1 = rect1.y + rect1.height;

            int left2 = rect2.x;
            int top2 = rect2.y;
            int right2 = rect2.x + rect2.width;
            int bottom2 = rect2.y + rect2.height;

            // 计算重叠区域
            int overlapLeft = Mathf.Max(left1, left2);
            int overlapTop = Mathf.Max(top1, top2);
            int overlapRight = Mathf.Min(right1, right2);
            int overlapBottom = Mathf.Min(bottom1, bottom2);

            // 检查是否有重叠
            if (overlapLeft >= overlapRight || overlapTop >= overlapBottom)
            {
                return emptyRect; // 没有重叠，返回空矩形
            }

            // 返回重叠区域的矩形
            return new OpenCVForUnity.CoreModule.Rect(
                overlapLeft,
                overlapTop,
                overlapRight - overlapLeft,
                overlapBottom - overlapTop
            );
        }

        /// <summary>
        /// 保存Mat到文件
        /// </summary>
        static public void SaveMatToFile(Mat mat, string filename)
        {
            try
            {
                // 创建临时纹理
                Texture2D tempTexture = new Texture2D(mat.cols(), mat.rows(), TextureFormat.RGB24, false);
                OpenCVForUnity.UnityUtils.Utils.matToTexture2D(mat, tempTexture);
                tempTexture.Apply();

                // 保存到文件
                byte[] bytes = tempTexture.EncodeToPNG();
                string path = Path.Combine(Application.dataPath, "..", filename);
                File.WriteAllBytes(path, bytes);

                Debug.Log($"图片已保存到: {path}");

                // 释放临时纹理
#if UNITY_EDITOR
                UnityEngine.Object.DestroyImmediate(tempTexture);
#else
                Destroy(tempTexture);
#endif
            }
            catch (System.Exception e)
            {
                Debug.LogError($"保存图片时发生错误：{e.Message}");
            }
        }
        
        /// <summary>
        /// 使用黄金螺旋算法在球面上均匀采样N个点
        /// </summary>
        /// <param name="vecs">存储采样点的List</param>
        /// <param name="N">采样点数量</param>
        public static void sampleSphere(ref List<Vector3> vecs, int N)
        {
            if (vecs == null)
                vecs = new List<Vector3>();
            vecs.Clear();
            if (N > 1)
            {
                vecs.Capacity = N; // 设置容量以避免频繁重新分配
                double phi = (Math.Sqrt(5.0) - 1) / 2; // 黄金比例倒数
                for (int n = 0; n < N; ++n)
                {
                    double z = (2.0 * n) / (N - 1) - 1.0;
                    double r = Math.Sqrt(1.0 - z * z);
                    double theta = 2.0 * Math.PI * n * phi;
                    double x = r * Math.Cos(theta);
                    double y = r * Math.Sin(theta);
                    
                    // 创建向量并归一化
                    Vector3 point = new Vector3((float)x, (float)y, (float)z).normalized;
                    vecs.Add(point);
                }
            }
        }
    }
    
}