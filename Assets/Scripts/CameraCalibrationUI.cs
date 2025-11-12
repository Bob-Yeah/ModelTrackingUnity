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
    public TMPro.TextMeshProUGUI frameCountText;
    public TMPro.TextMeshProUGUI statusText;
    public TMPro.TextMeshProUGUI platformInfoText;    // 显示平台信息的文本
    public TMPro.TextMeshProUGUI qualityText;         // 显示标定质量评级
    
    // 错误和警告面板
    public GameObject errorPanel;
    public TMPro.TextMeshProUGUI errorText;
    public GameObject warningPanel;
    public TMPro.TextMeshProUGUI warningText;
    
    // 加载指示器
    public GameObject loadingIndicator;
    
    
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
        
        // 注册标定管理器事件
        if (calibrator != null)
        {
            calibrator.OnChessboardDetectedChanged += OnChessboardDetectedChanged;
            calibrator.OnDataFrameCountChanged += OnDataFrameCountChanged;
            calibrator.OnErrorOccurred += OnErrorOccurred;
            calibrator.OnWarningOccurred += OnWarningOccurred;
            calibrator.OnStatusChanged += OnStatusChanged;
            
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
            string saveDirInfo = System.IO.Path.Combine(dataPath + "CalibrationData");
            
            platformInfoText.text = string.Format("Platform:{0}\nSaving Folder{1}", 
                platformName, saveDirInfo);
        }
    }
    
    // 检查并显示权限状态
    private void CheckAndShowPermissions()
    {
        TMPro.TextMeshProUGUI targetText = statusText; // 使用已定义的statusText变量
        if (targetText != null && calibrator != null)
        {
            bool hasPermission = calibrator.HasWritePermission();
            string permissionStatus = hasPermission ? "File write good" : "File write restricted";
            
            // 在Android上可能需要额外的权限提示
            if (Application.platform == RuntimePlatform.Android)
            {
                permissionStatus += " (Android maybe need more permissions)";
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
            if (qualityRating != "NotCalibrated")
            {
                qualityText.text = "Calibration Quality:" + qualityRating;
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
                statusText.text = "Detected Chessboard! Ready to save this frame";
            else
                statusText.text = "Detecting Chessboard...";
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
            frameCountText.text = string.Format("Valid Frames: {0}/5+", count);
            
            // 更新状态文本以反映新的帧数
            if (count > 0 && count < 5)
                statusText.text = string.Format("Collected {0}/5+ Frames, Collect More Frame from Different Angles", count);
            else if (count >= 5)
                statusText.text = string.Format("Collected ({0} Frames, Ready to Calibrate ", count);
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
                ShowStatusMessage("More frame needed", Color.red);
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
        ShowStatusMessage("Calibrating...", Color.yellow);
        
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
            ShowStatusMessage("Calibration Completed, Quality: " + qualityRating, Color.green);
            
            // 延迟一下再保存，让用户看到质量信息
            yield return new WaitForSeconds(1f);
            
            // 尝试保存标定结果
            ShowStatusMessage("Save Calibration Result...", Color.yellow);
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
            ShowStatusMessage("All calibration date cleared", Color.blue);
            
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
        Debug.LogError("UI receiced error:" + errorMessage);
        
        // 显示错误面板（如果有）
        if (errorText != null && errorPanel != null)
        {
            errorText.text = "Error:" + errorMessage;
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
        Debug.LogWarning("UI received warning:" + warningMessage);
        
        // 显示警告面板（如果有）
        if (warningText != null && warningPanel != null)
        {
            warningText.text = "Warning:" + warningMessage;
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
    
    
    void Update()
    {
        UpdateUIState();
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