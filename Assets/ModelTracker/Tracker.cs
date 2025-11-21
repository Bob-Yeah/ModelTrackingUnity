using ModelTracker;
using Newtonsoft.Json;
using OpenCVForUnity.CoreModule;
using OpenCVForUnity.ImgcodecsModule;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace ModelTracker
{
    struct Frame
    {
        public Mat img;
        public Mat objMask;
        public Mat colorProb;
        public Pose pose;
        public float err;
    };

    public class Tracker
    {
        public void reset()
        {

        }


    }
}