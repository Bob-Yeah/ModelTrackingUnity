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

public class CameraTrackerSim : MonoBehaviour
{
    public Camera TrackerCam;

    public Vector3 currentT = Vector3.zero;
    public Vector3 lastT = Vector3.zero;
    public Matx33f currentR = new Matx33f();
    public Matx33f lastR = new Matx33f();

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

        // 
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
}