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
    
    // 加载完成事件
    public event Action OnTemplateLoadComplete;

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
                
                // 回到主线程设置ModelTemplate
                UnityMainThreadDispatcher.Instance().Enqueue(() => {
                    ModelTemplate = tempTemplate;
                    IsTemplateLoading = false;
                    IsTemplateLoaded = true;
                    Debug.Log("模板文件加载完成");
                    
                    // 触发完成事件
                    OnTemplateLoadComplete?.Invoke();
                });
            }
            catch (Exception e)
            {
                loadException = e;
                UnityMainThreadDispatcher.Instance().Enqueue(() => {
                    Debug.LogError($"加载模板文件失败: {e.Message}");
                    Debug.LogError($"堆栈跟踪: {e.StackTrace}");
                    IsTemplateLoading = false;
                });
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
        // 找到最近的视图

        // 打印最近的视图

        // 更新档期那
    }
    

}