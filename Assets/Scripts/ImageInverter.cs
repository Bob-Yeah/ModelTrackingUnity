using UnityEngine;
using UnityEngine.UI;
using OpenCVForUnity.CoreModule;
using OpenCVForUnity.ImgprocModule;
using OpenCVForUnity.UnityUtils;
using System.Collections;

/// <summary>
/// 图像反色处理器 - 使用OpenCV for Unity实现图像反色效果
/// </summary>
public class ImageInverter : MonoBehaviour
{
    /// <summary>
    /// 输入图像组件，用于读取源纹理
    /// </summary>
    public RawImage inputRawImage;
    
    /// <summary>
    /// 输出图像组件，用于显示处理后的纹理
    /// </summary>
    public RawImage outputRawImage;
    
    /// <summary>
    /// 是否每帧更新处理
    /// </summary>
    public bool updateEveryFrame = true;
    
    /// <summary>
    /// 处理帧率（当updateEveryFrame为false时使用）
    /// </summary>
    public float processingRate = 0.1f; // 100ms更新一次
    
    private Mat sourceMat;
    private Mat destinationMat;
    private WebCamTexture inputWebCamTexture; // 处理摄像头纹理的情况
    
    private void Start()
    {
        // 检查输入和输出组件是否已配置
        if (inputRawImage == null)
        {
            Debug.LogError("请在Inspector中设置输入RawImage组件");
            return;
        }
        
        if (outputRawImage == null)
        {
            Debug.LogError("请在Inspector中设置输出RawImage组件");
            return;
        }
        
        // 如果不每帧更新，启动协程定期处理
        if (!updateEveryFrame)
        {
            StartCoroutine(ProcessImageCoroutine());
        }
    }
    
    private void Update()
    {
        // 每帧更新处理
        if (updateEveryFrame)
        {
            ProcessImage();
        }
    }
    
    /// <summary>
    /// 协程：定期处理图像
    /// </summary>
    private IEnumerator ProcessImageCoroutine()
    {
        while (true)
        {
            ProcessImage();
            yield return new WaitForSeconds(processingRate);
        }
    }
    
    /// <summary>
    /// 处理图像：读取输入纹理，转换为Mat，进行反色处理，再转换回纹理
    /// </summary>
    public void ProcessImage()
    {
        if (inputRawImage == null || outputRawImage == null)
            return;
        
        // 检查输入纹理是否存在
        if (inputRawImage.texture == null)
            return;
        
        try
        {
            // 检查输入纹理类型（WebCamTexture或普通Texture2D）
            if (inputRawImage.texture is WebCamTexture)
            {
                inputWebCamTexture = inputRawImage.texture as WebCamTexture;
                // 确保WebCamTexture已经初始化并有数据
                if (!inputWebCamTexture.isPlaying || inputWebCamTexture.width <= 16 || inputWebCamTexture.height <= 16)
                    return;
            }
            
            // 从Texture创建Mat
            sourceMat = new Mat();
            destinationMat = new Mat();
            
            // 将Unity纹理转换为OpenCV的Mat
            if (inputRawImage.texture is WebCamTexture)
            {
                sourceMat = new Mat(inputWebCamTexture.height, inputWebCamTexture.width, CvType.CV_8UC3);
                Utils.webCamTextureToMat(inputWebCamTexture, sourceMat);
            }
            else
            {
                Texture2D texture2D = inputRawImage.texture as Texture2D;
                sourceMat = new Mat(texture2D.height, texture2D.width, CvType.CV_8UC3);
                Utils.texture2DToMat(texture2D, sourceMat);
            }
            
            // 执行反色处理
            // 反色处理：255 - 像素值
            Core.bitwise_not(sourceMat, destinationMat);
            
            // 将处理后的Mat转换回Texture
            Texture processedTexture;
            if (inputRawImage.texture is WebCamTexture)
            {
                // 对于WebCamTexture，创建一个新的Texture2D
                Texture2D outputTexture2D = new Texture2D(destinationMat.cols(), destinationMat.rows(), TextureFormat.RGB24, false);
                Utils.matToTexture2D(destinationMat, outputTexture2D);
                processedTexture = outputTexture2D;
            }
            else
            {
                // 对于普通Texture，转换回Texture2D
                processedTexture = new Texture2D(destinationMat.cols(), destinationMat.rows(), TextureFormat.RGB24, false);
                Utils.matToTexture2D(destinationMat, processedTexture as Texture2D);
            }
            
            // 赋值给输出RawImage
            outputRawImage.texture = processedTexture;
        }
        catch (System.Exception e)
        {
            Debug.LogError("图像处理错误: " + e.Message);
        }
        finally
        {
            // 释放Mat资源，避免内存泄漏
            if (sourceMat != null)
                sourceMat.Dispose();
            
            if (destinationMat != null)
                destinationMat.Dispose();
        }
    }
    
    /// <summary>
    /// 手动触发一次图像处理
    /// </summary>
    public void TriggerProcess()
    {
        ProcessImage();
    }
    
    private void OnDestroy()
    {
        // 确保资源被正确释放
        if (sourceMat != null)
            sourceMat.Dispose();
        
        if (destinationMat != null)
            destinationMat.Dispose();
    }
}