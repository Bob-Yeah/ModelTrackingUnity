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
    public static class Utils
    {
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
    }
    
}