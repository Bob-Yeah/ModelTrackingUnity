using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ImageInverter测试控制器 - 提供一个简单的UI界面来测试图像反色功能
/// </summary>
public class ImageInverterTester : MonoBehaviour
{
    [Header("组件引用")]
    public RawImage sourceImage;    // 源图像显示组件
    public RawImage invertedImage;  // 反色图像显示组件
    public ImageInverter imageInverter;  // 反色处理器
    
    [Header("UI控制")]
    public Toggle continuousUpdateToggle;  // 连续更新开关
    public Slider updateRateSlider;        // 更新速率滑块
    public Button processButton;           // 手动处理按钮
    
    [Header("测试纹理")]
    public Texture2D testTexture;  // 可选的测试纹理
    
    private void Start()
    {
        // 初始化ImageInverter组件
        InitializeImageInverter();
        
        // 设置UI事件
        SetupUIEvents();
        
        // 如果提供了测试纹理，使用它
        if (testTexture != null && sourceImage != null)
        {
            sourceImage.texture = testTexture;
        }
    }
    
    /// <summary>
    /// 初始化图像反色处理器
    /// </summary>
    private void InitializeImageInverter()
    {
        // 如果没有指定ImageInverter，尝试在当前游戏对象上获取
        if (imageInverter == null)
        {
            imageInverter = GetComponent<ImageInverter>();
        }
        
        // 如果仍然没有找到，创建一个新的
        if (imageInverter == null)
        {
            imageInverter = gameObject.AddComponent<ImageInverter>();
        }
        
        // 设置输入和输出图像组件
        if (imageInverter != null)
        {
            imageInverter.inputRawImage = sourceImage;
            imageInverter.outputRawImage = invertedImage;
        }
    }
    
    /// <summary>
    /// 设置UI事件监听
    /// </summary>
    private void SetupUIEvents()
    {
        // 连续更新开关
        if (continuousUpdateToggle != null && imageInverter != null)
        {
            continuousUpdateToggle.isOn = imageInverter.updateEveryFrame;
            continuousUpdateToggle.onValueChanged.AddListener((value) => {
                if (imageInverter != null)
                {
                    imageInverter.updateEveryFrame = value;
                }
            });
        }
        
        // 更新速率滑块
        if (updateRateSlider != null && imageInverter != null)
        {
            updateRateSlider.value = imageInverter.processingRate;
            updateRateSlider.onValueChanged.AddListener((value) => {
                if (imageInverter != null)
                {
                    imageInverter.processingRate = value;
                }
            });
        }
        
        // 手动处理按钮
        if (processButton != null && imageInverter != null)
        {
            processButton.onClick.AddListener(() => {
                if (imageInverter != null)
                {
                    imageInverter.TriggerProcess();
                }
            });
        }
    }
    
    /// <summary>
    /// 公共方法：刷新ImageInverter引用
    /// </summary>
    public void RefreshImageInverter()
    {
        InitializeImageInverter();
        SetupUIEvents();
    }
    
    /// <summary>
    /// 公共方法：应用当前UI设置
    /// </summary>
    public void ApplySettings()
    {
        if (imageInverter != null)
        {
            if (continuousUpdateToggle != null)
                imageInverter.updateEveryFrame = continuousUpdateToggle.isOn;
                
            if (updateRateSlider != null)
                imageInverter.processingRate = updateRateSlider.value;
        }
    }
}