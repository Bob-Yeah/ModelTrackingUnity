using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Android;

public class OpenCamera : MonoBehaviour
{
    /// <summary>
    /// 图片组件
    /// </summary>
    public RawImage rawImage;
    /// <summary>
    /// 当前相机索引
    /// </summary>
    private int index = 0;
    /// <summary>
    /// 当前运行的相机
    /// </summary>
    private WebCamTexture currentWebCam;

    void Start()
    {
        StartCoroutine(Call());
    }

    /// <summary>
    /// 开启摄像机
    /// </summary>
    public IEnumerator Call()
    {
        // 第一步：请求摄像头权限（使用Unity的Permission API，适用于Android）
        if (!Permission.HasUserAuthorizedPermission(Permission.Camera))
        {
            Debug.Log("正在请求摄像头权限...");
            Permission.RequestUserPermission(Permission.Camera);
            
            // 等待用户响应权限请求
            yield return new WaitForSeconds(0.5f);
            
            // 再次检查权限状态
            if (!Permission.HasUserAuthorizedPermission(Permission.Camera))
            {
                Debug.LogError("未获得摄像头权限！请在设备设置中允许应用访问摄像头。");
                yield break;
            }
        }
        else
        {
            Debug.Log("摄像头权限已获取");
        }
        
        // 等待一帧，确保权限已经完全处理
        yield return null;
        
        // 第二步：获取设备列表
        WebCamDevice[] devices = WebCamTexture.devices;
        
        if (devices.Length == 0)
        {
            Debug.LogError("未检测到可用的摄像头设备");
            yield break;
        }
        
        // 记录可用摄像头信息
        Debug.Log("可用摄像头数量: " + devices.Length);
        for (int i = 0; i < devices.Length; i++)
        {
            Debug.Log("摄像头 " + i + ": " + devices[i].name);
        }
        
        bool success = false;
        
        // 第一部分：尝试使用指定设备
        try
        {
            // 确保索引有效
            index = Mathf.Clamp(index, 0, devices.Length - 1);
            Debug.Log("尝试打开摄像头: " + devices[index].name);
            
            // 创建相机贴图，使用更合理的分辨率
            int requestedWidth = Mathf.Min(Screen.width, 1280);
            int requestedHeight = Mathf.Min(Screen.height, 720);
            Debug.Log($"请求相机分辨率: {requestedWidth}x{requestedHeight}");
            
            // 在Windows平台上使用更可靠的初始化方式
            // 在Windows上直接使用设备名称有时会失败
            if (Application.platform == RuntimePlatform.WindowsEditor || Application.platform == RuntimePlatform.WindowsPlayer)
            {
                Debug.Log("Windows平台检测到，使用默认构造函数创建WebCamTexture");
                // 使用默认构造函数，Unity会自动选择合适的相机
                currentWebCam = new WebCamTexture(requestedWidth, requestedHeight, 30);
            }
            else
            {
                // 在其他平台上使用设备名称
                currentWebCam = new WebCamTexture(devices[index].name, requestedWidth, requestedHeight, 30);
            }
            
            // 注：WebCamTexture不直接提供获取相机支持的所有分辨率的API
            // 但我们可以通过创建后检查实际分辨率来了解相机使用了什么分辨率
            
            // 检查创建是否成功
            if (currentWebCam == null)
            {
                Debug.LogError("无法创建WebCamTexture对象");
            }
            else
            {
                success = true;
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError("启动摄像头时发生错误: " + e.Message);
            Debug.LogError("堆栈跟踪: " + e.StackTrace);
        }
        
        // 等待一帧并应用设置（移出try块）
        if (success && currentWebCam != null)
        {
            rawImage.texture = currentWebCam;
            
            while (true)
            {
                currentWebCam.Play();
                if (currentWebCam.isPlaying)
                {
                    break;
                }
            }
            
            // 等待一帧以确保videoRotationAngle已更新，同时让WebCamTexture初始化完成
            yield return null;
            
            // 获取并显示相机实际使用的分辨率
            // 注：当请求的分辨率与相机支持的分辨率不符时，Unity会选择最接近的支持分辨率
            int actualWidth = currentWebCam.width;
            int actualHeight = currentWebCam.height;
            Debug.Log($"相机实际使用的分辨率: {actualWidth}x{actualHeight}");
            
            // 应用旋转角度修正
            rawImage.rectTransform.localEulerAngles = new Vector3(0, 0, -currentWebCam.videoRotationAngle);
            Debug.Log("摄像头启动成功，旋转角度: " + currentWebCam.videoRotationAngle);
            
            // 分辨率处理说明：
            // 1. Unity的WebCamTexture会自动选择最接近请求分辨率的实际支持分辨率
            // 2. 不同设备和相机支持的分辨率不同，无法直接获取完整的支持列表
            // 3. 如果需要特定分辨率，建议在应用中提供几种常用分辨率选项让用户选择
            yield break;
        }
        
        // 第二部分：如果第一部分失败，尝试使用默认设置
        bool defaultSuccess = false;
        try
        {
            Debug.Log("尝试使用默认摄像头设置");
            currentWebCam = new WebCamTexture();
            defaultSuccess = true;
        }
        catch (System.Exception innerE)
        {
            Debug.LogError("默认设置也失败: " + innerE.Message);
        }
        
        // 等待一帧并应用默认设置（移出try块）
        if (defaultSuccess && currentWebCam != null)
        {
            rawImage.texture = currentWebCam;
            currentWebCam.Play();
            
            // 等待一帧以确保videoRotationAngle已更新，同时让WebCamTexture初始化完成
            yield return null;
            
            // 获取并显示相机实际使用的分辨率
            // 注：当请求的分辨率与相机支持的分辨率不符时，Unity会选择最接近的支持分辨率
            int actualWidth = currentWebCam.width;
            int actualHeight = currentWebCam.height;
            Debug.Log($"相机实际使用的分辨率: {actualWidth}x{actualHeight}");
            
            // 应用旋转角度修正
            rawImage.rectTransform.localEulerAngles = new Vector3(0, 0, -currentWebCam.videoRotationAngle);
            
            // 分辨率处理说明：
            // 1. Unity的WebCamTexture会自动选择最接近请求分辨率的实际支持分辨率
            // 2. 不同设备和相机支持的分辨率不同，无法直接获取完整的支持列表
            // 3. 如果需要特定分辨率，建议在应用中提供几种常用分辨率选项让用户选择
        }
    }
    
    /// <summary>
    /// 释放资源
    /// </summary>
    private void OnDestroy()
    {
        if (currentWebCam != null && currentWebCam.isPlaying)
        {
            currentWebCam.Stop();
            currentWebCam = null;
        }
    }
}
