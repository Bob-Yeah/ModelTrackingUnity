using ModelTracker;
using Newtonsoft.Json;
using OpenCVForUnity.CoreModule;
using OpenCVForUnity.ImgcodecsModule;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using UnityEditor;
using UnityEngine;

public class TemplateRuntime : MonoBehaviour
{
    public ModelTracker.Templates ModelTemplate = null;
    public string TemplatePath = "Assets/ModelTracking/Squirrel.json";
    public Camera TrackerCamera = null;
    
    // 加载状态标志
    public bool IsTemplateLoading { get; private set; } = false;
    public bool IsTemplateLoaded { get; private set; } = false;
    
    // 用于存储点云可视化的小球对象
    private List<GameObject> pointCloudSpheres = new List<GameObject>();
    

    void Awake()
    {

    }

    void Start()
    {

    }

    void Update()
    {

    }

    // 公共方法，用于启动加载协程
    public void LoadTemplate()
    {
        if (!IsTemplateLoading && File.Exists(TemplatePath))
        {
            StartCoroutine(LoadTemplateCoroutine());
        }
        else if (!File.Exists(TemplatePath))
        {
            Debug.LogError($"模板文件不存在: {TemplatePath}");
        }
    }
    
    // 协程方法，在后台加载模板文件
    private IEnumerator LoadTemplateCoroutine()
    {
        IsTemplateLoading = true;
        IsTemplateLoaded = false;
        Debug.Log("开始后台加载模板文件...");
        
        // 保存当前路径的副本
        string path = TemplatePath;
        string jsonData = null;
        Exception loadException = null;
        ModelTracker.Templates tempTemplate = null;
        
        // 使用线程池在后台读取文件内容
        ThreadPool.QueueUserWorkItem(state => {
            try
            {
                // 读取文件内容
                jsonData = File.ReadAllText(path);
                
                // 创建新的模板对象
                tempTemplate = new ModelTracker.Templates();
                
                // 使用新的LoadFromJson方法，避免重复文件读取
                tempTemplate.LoadFromJson(jsonData);
                
                ModelTemplate = tempTemplate;
                IsTemplateLoading = false;
                IsTemplateLoaded = true;
                Debug.Log("模板文件加载完成");

                
            }
            catch (Exception e)
            {
                loadException = e;
                Debug.LogError($"加载模板文件失败: {e.Message}");
                    Debug.LogError($"堆栈跟踪: {e.StackTrace}");
                    IsTemplateLoading = false;
            }
        });
        
        // 每帧检查是否加载完成，避免阻塞主线程
        while (IsTemplateLoading)
        {
            yield return null;
        }
    }

    public void FindNearestView()
    {
        if (!IsTemplateLoaded)
        {
            Debug.LogError("模板文件未加载完成");
            return;
        }

        // 找到最近的视图
        Vector3 currentDir = TrackerCamera.transform.position - ModelTemplate.modelCenter;
        int DViewIndex = ModelTemplate.viewIndex.GetViewInDir(currentDir.normalized);
        if (DViewIndex < 0)
        {
            Debug.LogError("未找到最近的视图");
            return;
        }
        // 打印最近的视图
        
        ModelTracker.DView currentDView = ModelTemplate.views[DViewIndex];
        
        Debug.Log($"最近的视图索引: {DViewIndex}, Current Dir: {currentDir.normalized} FindNearestDir: {currentDView.viewDir}");

        // 创建DebugObject
        pointCloudSpheres.Clear();
        foreach (var point in currentDView.contourPoints3d)
        {
            GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.transform.position = ModelTemplate.modelCenter + point.center;
            sphere.transform.localScale = Vector3.one * 0.001f;
            // 设置红色材质以便于识别
            Renderer renderer = sphere.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = new Material(Shader.Find("Standard"));
                renderer.sharedMaterial.color = Color.red;
            }
            pointCloudSpheres.Add(sphere);
        }
    }
    

}