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
    }
}