using UnityEngine;
using UnityEngine.UI;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.EventSystems;
using OpenCVForUnity.CoreModule;
using OpenCVForUnity.ImgprocModule;
using OpenCVForUnity.Calib3dModule;
using OpenCVForUnity.UnityUtils;

public class CameraCalibrationUI : MonoBehaviour
{
    // 引用相机标定管理器
    public CameraCalibrator calibrator;
    
    // UI元素
    public Button saveFrameButton;
    public Button calibrateButton;
    public Button clearDataButton;
    public Text frameCountText;
    public Text statusText;
    public Text platformInfoText;    // 显示平台信息的文本
    public Text qualityText;         // 显示标定质量评级
    public RawImage cameraFeed;
    public RawImage detectionResultImage;
    
    // 错误和警告面板
    public GameObject errorPanel;
    public Text errorText;
    public GameObject warningPanel;
    public Text warningText;
    
    // 加载指示器
    public GameObject loadingIndicator;
    
    // 棋盘格参数设置UI
    public InputField chessboardWidthInput;
    public InputField chessboardHeightInput;
    public InputField squareSizeInput;
    public Button applySettingsButton;
    
    void Start()
    {
        // 初始化UI状态
        UpdateUIState();
        
        // 显示平台信息
        ShowPlatformInfo();
        
        // 检查文件写入权限
        CheckAndShowPermissions();
        
        // 设置按钮事件
        if (saveFrameButton != null)
            saveFrameButton.onClick.AddListener(OnSaveFrameButtonClicked);
        
        if (calibrateButton != null)
            calibrateButton.onClick.AddListener(OnCalibrateButtonClicked);
        
        if (clearDataButton != null)
            clearDataButton.onClick.AddListener(OnClearDataButtonClicked);
        
        if (applySettingsButton != null)
            applySettingsButton.onClick.AddListener(ApplyChessboardSettings);
        
        // 注册标定管理器事件
        if (calibrator != null)
        {
            calibrator.OnChessboardDetectedChanged += OnChessboardDetectedChanged;
            calibrator.OnDataFrameCountChanged += OnDataFrameCountChanged;
            calibrator.OnErrorOccurred += OnErrorOccurred;
            calibrator.OnWarningOccurred += OnWarningOccurred;
            calibrator.OnStatusChanged += OnStatusChanged;
            
            // 初始化棋盘格参数输入框
            UpdateChessboardSettingsUI();
        }
        
        // 更新初始帧数显示
        UpdateFrameCountDisplay();
        
        // 初始化错误/警告UI元素
        if (errorPanel != null)
            errorPanel.SetActive(false);
        if (warningPanel != null)
            warningPanel.SetActive(false);
    }
    
    // 显示平台信息
    private void ShowPlatformInfo()
    {
        if (platformInfoText != null)
        {
            string platformName = Application.platform.ToString();
            string dataPath = Application.persistentDataPath;
            string saveDirInfo = GetPlatformSaveDirectoryInfo();
            
            platformInfoText.text = string.Format("平台: {0}\n保存目录: {1}", 
                platformName, saveDirInfo);
        }
    }
    
    // 获取平台特定的保存目录信息
    private string GetPlatformSaveDirectoryInfo()
    {
        switch (Application.platform)
        {
            case RuntimePlatform.WindowsEditor:
                return "项目目录/CalibrationData";
            case RuntimePlatform.Android:
                return "应用数据目录/Calibration";
            case RuntimePlatform.IPhonePlayer:
                return "应用数据目录/Calibration";
            default:
                return "应用数据目录/Calibration";
        }
    }
    
    // 检查并显示权限状态
    private void CheckAndShowPermissions()
    {
        Text targetText = statusText; // 使用已定义的statusText变量
        if (targetText != null && calibrator != null)
        {
            bool hasPermission = calibrator.HasWritePermission();
            string permissionStatus = hasPermission ? "✓ 文件写入权限正常" : "✗ 文件写入权限受限";
            
            // 在Android上可能需要额外的权限提示
            if (Application.platform == RuntimePlatform.Android)
            {
                permissionStatus += " (Android可能需要存储权限)";
            }
            
            targetText.text = permissionStatus;
        }
    }
    
    private void UpdateUIState()
    {
        bool hasCalibrator = calibrator != null;
        
        if (saveFrameButton != null)
            saveFrameButton.interactable = hasCalibrator && calibrator.IsChessboardDetected();
        
        if (calibrateButton != null)
            calibrateButton.interactable = hasCalibrator && calibrator.GetDataFrameCount() >= 5;
        
        if (clearDataButton != null)
            clearDataButton.interactable = hasCalibrator && calibrator.GetDataFrameCount() > 0;
        
        // 如果有标定结果，显示质量评级
        if (calibrator != null && calibrator.GetDataFrameCount() > 0 && qualityText != null)
        {
            string qualityRating = calibrator.GetCalibrationQualityRating();
            if (qualityRating != "未标定")
            {
                qualityText.text = "标定质量: " + qualityRating;
                qualityText.gameObject.SetActive(true);
            }
            else
            {
                qualityText.gameObject.SetActive(false);
            }
        }
    }
    
    private void OnChessboardDetectedChanged(bool detected)
    {
        // 更新保存帧按钮状态
        if (saveFrameButton != null)
            saveFrameButton.interactable = detected;
        
        // 更新状态文本
        if (statusText != null)
        {
            if (detected)
                statusText.text = "已检测到棋盘格！可以保存当前帧。";
            else
                statusText.text = "正在检测棋盘格...";
        }
    }
    
    private void OnDataFrameCountChanged(int count)
    {
        UpdateFrameCountDisplay();
        UpdateUIState();
    }
    
    private void UpdateFrameCountDisplay()
    {
        if (frameCountText != null && calibrator != null)
        {
            int count = calibrator.GetDataFrameCount();
            frameCountText.text = string.Format("有效数据帧数: {0}/5+", count);
            
            // 更新状态文本以反映新的帧数
            if (count > 0 && count < 5)
                statusText.text = string.Format("已收集 {0}/5+ 帧数据，请继续收集更多角度的数据。", count);
            else if (count >= 5)
                statusText.text = string.Format("已收集足够数据 ({0} 帧)，可以进行标定。", count);
        }
    }
    
    private void OnSaveFrameButtonClicked()
    {
        if (calibrator != null)
        {
            // 临时禁用按钮防止重复点击
            if (saveFrameButton != null)
                saveFrameButton.interactable = false;
            
            bool success = calibrator.SaveCurrentFrameData();
            
            // 恢复按钮状态（UI会在帧更新时正确设置状态）
            StartCoroutine(EnableButtonAfterFrame(saveFrameButton));
        }
    }
    
    // 延迟一帧后启用按钮（确保UI状态正确更新）
    private System.Collections.IEnumerator EnableButtonAfterFrame(Button button)
    {
        yield return new WaitForEndOfFrame();
        // 按钮状态会根据检测状态由UpdateUIState自动设置
        // 这里不手动设置，留给UpdateUIState处理
    }
    
    private void OnCalibrateButtonClicked()
    {
        if (calibrator != null)
        {
            int count = calibrator.GetDataFrameCount();
            
            if (count < 5)
            {
                ShowStatusMessage("数据帧数不足，请至少收集5帧不同角度的数据。", Color.red);
                return;
            }
            
            // 禁用按钮防止重复点击
            if (calibrateButton != null)
                calibrateButton.interactable = false;
            if (saveFrameButton != null)
                saveFrameButton.interactable = false;
            
            StartCoroutine(CalibrateCameraCoroutine());
        }
    }
    
    // 相机标定协程
    private System.Collections.IEnumerator CalibrateCameraCoroutine()
    {
        ShowStatusMessage("正在进行相机标定计算，请稍候...", Color.yellow);
        
        // 显示加载指示器（如果有）
        if (loadingIndicator != null)
        {
            loadingIndicator.SetActive(true);
        }
        
        // 执行标定计算（在主线程，但允许一帧来更新UI）
        yield return null;
        
        bool success = calibrator.CalibrateCamera();
        
        if (success)
        {
            // 显示标定质量信息
            string qualityRating = calibrator.GetCalibrationQualityRating();
            ShowStatusMessage("相机标定计算完成，质量评级: " + qualityRating, Color.green);
            
            // 延迟一下再保存，让用户看到质量信息
            yield return new WaitForSeconds(1f);
            
            // 尝试保存标定结果
            ShowStatusMessage("正在保存标定结果...", Color.yellow);
            bool saveSuccess = calibrator.SaveCalibrationResult("camera_calibration.json");
        }
        
        // 隐藏加载指示器
        if (loadingIndicator != null)
        {
            loadingIndicator.SetActive(false);
        }
        
        // 恢复UI交互
        UpdateUIState();
    }
    
    private void OnClearDataButtonClicked()
    {
        if (calibrator != null)
        {
            // 禁用按钮防止重复点击
            if (clearDataButton != null)
                clearDataButton.interactable = false;
            
            calibrator.ClearCalibrationData();
            ShowStatusMessage("所有标定数据已清除。", Color.blue);
            
            // 清除错误和警告面板
            if (errorPanel != null)
                errorPanel.SetActive(false);
            if (warningPanel != null)
                warningPanel.SetActive(false);
            
            // 清除质量文本
            if (qualityText != null)
                qualityText.gameObject.SetActive(false);
            
            UpdateUIState();
            
            // 恢复按钮状态
            StartCoroutine(EnableButtonAfterFrame(clearDataButton));
        }
    }
    
    // 错误处理事件
    private void OnErrorOccurred(string errorMessage)
    {
        Debug.LogError("UI接收到错误: " + errorMessage);
        
        // 显示错误面板（如果有）
        if (errorText != null && errorPanel != null)
        {
            errorText.text = "错误: " + errorMessage;
            errorPanel.SetActive(true);
            
            // 3秒后自动隐藏错误面板
            StartCoroutine(HidePanelAfterDelay(errorPanel, 5f));
        }
        
        // 也在状态栏显示错误
        ShowStatusMessage(errorMessage, Color.red);
    }
    
    // 警告处理事件
    private void OnWarningOccurred(string warningMessage)
    {
        Debug.LogWarning("UI接收到警告: " + warningMessage);
        
        // 显示警告面板（如果有）
        if (warningText != null && warningPanel != null)
        {
            warningText.text = "警告: " + warningMessage;
            warningPanel.SetActive(true);
            
            // 5秒后自动隐藏警告面板
            StartCoroutine(HidePanelAfterDelay(warningPanel, 7f));
        }
        
        // 也在状态栏显示警告
        ShowStatusMessage(warningMessage, Color.yellow);
    }
    
    // 状态更新事件
    private void OnStatusChanged(string statusMessage)
    {
        Debug.Log("UI接收到状态更新: " + statusMessage);
        
        // 在状态栏显示状态消息
        ShowStatusMessage(statusMessage, Color.white);
    }
    
    // 延迟隐藏面板
    private System.Collections.IEnumerator HidePanelAfterDelay(GameObject panel, float delay)
    {
        yield return new WaitForSeconds(delay);
        panel.SetActive(false);
    }
    
    private void ShowStatusMessage(string message, Color color)
    {
        if (statusText != null)
        {
            statusText.text = message;
            statusText.color = color;
        }
    }
    
    // 错误面板关闭按钮
    public void OnCloseErrorPanel()
    {
        if (errorPanel != null)
        {
            errorPanel.SetActive(false);
            if (calibrator != null)
            {
                calibrator.ClearError();
            }
        }
    }
    
    // 警告面板关闭按钮
    public void OnCloseWarningPanel()
    {
        if (warningPanel != null)
        {
            warningPanel.SetActive(false);
            if (calibrator != null)
            {
                calibrator.ClearWarning();
            }
        }
    }
    
    private void UpdateChessboardSettingsUI()
    {
        if (calibrator == null)
            return;
            
        if (chessboardWidthInput != null)
            chessboardWidthInput.text = calibrator.chessboardWidth.ToString();
            
        if (chessboardHeightInput != null)
            chessboardHeightInput.text = calibrator.chessboardHeight.ToString();
            
        if (squareSizeInput != null)
            squareSizeInput.text = calibrator.squareSize.ToString("F3");
    }
    
    private void ApplyChessboardSettings()
    {
        if (calibrator == null)
            return;
            
        try
        {
            if (chessboardWidthInput != null && !string.IsNullOrEmpty(chessboardWidthInput.text))
            {
                int width = int.Parse(chessboardWidthInput.text);
                if (width > 2) // 至少需要3个内角点
                    calibrator.chessboardWidth = width;
            }
            
            if (chessboardHeightInput != null && !string.IsNullOrEmpty(chessboardHeightInput.text))
            {
                int height = int.Parse(chessboardHeightInput.text);
                if (height > 2) // 至少需要3个内角点
                    calibrator.chessboardHeight = height;
            }
            
            if (squareSizeInput != null && !string.IsNullOrEmpty(squareSizeInput.text))
            {
                float size = float.Parse(squareSizeInput.text);
                if (size > 0) // 必须为正数
                    calibrator.squareSize = size;
            }
            
            ShowStatusMessage("棋盘格参数已更新。", Color.blue);
        }
        catch (Exception e)
        {
            ShowStatusMessage("参数格式错误：" + e.Message, Color.red);
        }
    }
    
    void Update()
    {
        UpdateUIState();
        UpdateDetectionDisplay();
    }
    
    private void UpdateDetectionDisplay()
    {
        if (calibrator == null || detectionResultImage == null)
            return;
        
        // 获取标定器处理后的显示图像
        Mat displayMat = calibrator.GetDisplayMat();
        if (displayMat.empty())
            return;
        
        // 创建或更新Texture2D用于显示
        Texture2D displayTexture = detectionResultImage.texture as Texture2D;
        if (displayTexture == null || 
            displayTexture.width != displayMat.cols() || 
            displayTexture.height != displayMat.rows())
        {
            displayTexture = new Texture2D(displayMat.cols(), displayMat.rows(), TextureFormat.RGB24, false);
            detectionResultImage.texture = displayTexture;
        }
        
        // 将Mat转换为Texture2D
        Utils.matToTexture2D(displayMat, displayTexture);
    }
    
    void OnDestroy()
    {
        // 取消注册事件
        if (calibrator != null)
        {
            calibrator.OnChessboardDetectedChanged -= OnChessboardDetectedChanged;
            calibrator.OnDataFrameCountChanged -= OnDataFrameCountChanged;
            calibrator.OnErrorOccurred -= OnErrorOccurred;
            calibrator.OnWarningOccurred -= OnWarningOccurred;
            calibrator.OnStatusChanged -= OnStatusChanged;
        }
    }
}