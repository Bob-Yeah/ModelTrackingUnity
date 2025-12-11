using System.Collections.Generic;
using System.Diagnostics;
using Unity.Sentis;
using UnityEngine;
using System.Runtime.InteropServices;
using System.Collections;
using UnityEngine.Rendering;

public class SentisInferGPU : MonoBehaviour
{
    public TMPro.TextMeshProUGUI FPStext;
    public ModelAsset modelAsset;
    [SerializeField]
    Texture2D inputTexture;

    [SerializeField]
    RenderTexture resultRT;
    [SerializeField]
    RenderTexture resultRTTest;

    Unity.Sentis.Model runtimeModel;
    Worker worker;
    //ReadOnlySpan<float> results;
    //WebCamTextureToMatHelper webCamTextureToMatHelper;
    Tensor<float> inputTensor;
    Tensor<float> outputTensor;


    [SerializeField]
    ComputeShader preProcessCompute;

    [SerializeField]
    ComputeShader postProcessSoftmaxCompute;

    [SerializeField]
    ComputeShader sortingCompute;

    [SerializeField]
    ComputeShader NMSCompute;

    [SerializeField]
    ComputeShader visualizeCompute;

    [SerializeField]
    ComputeShader visualizeComputeGPU;

    [SerializeField]
    ComputeShader visualizeSoftmaxTestCompute;

    public int imgSize = 320;
    public int anchorsCount = 2125;
    public int reg_max = 7;
    public float conf_threshold = 0.35f;
    public int num_classes = 1;
    public float nms_threshold = 0.6f;
    public int topK = 10;
    public List<string> classNames = new List<string>() { "Brush" };

    //////////////////////////////////////////////////


    private int preProcessKernelHandle;

    private int postProcessSoftmaxKernelHandle;

    private int visualizeKernelHandle;
    private int visualizeGPUKernelHandle;

    private int m_ComputeInputIndex;
    private int m_ComputeOutputIndex;

    private ComputeBuffer detectionDataBuffer;
    private ComputeBuffer SoftMaxOutputBuffer;
    private ComputeBuffer SortingOutputBuffer;
    private ComputeBuffer NMSOutputBuffer;
    private ComputeBuffer NMSSuppressedBuffer;
    private ComputeBuffer scoreIndexPairs;

    private int clearKernel;
    private int nmsKernel;

    /// <summary>
    /// Performance Metrics
    /// </summary>
    int frameIdx = 0;
    long totalInferenceTime = 0;
    long totalSoftmaxTime = 0;
    long totalNMSTime = 0;
    long totalVisualizeTime = 0;
    long totalDownloadTime = 0;
    float totalInference = 0;
    float totalSoftmax = 0;
    float totalNMS = 0;
    float totalVisualize = 0;
    float totalDownload = 0;

    Stopwatch sw;

    private bool readGPUData = false;
    float currentTime;

    [StructLayout(LayoutKind.Sequential)]
    public struct DetectionData
    {
        public float x1;
        public float y1;
        public float x2;
        public float y2;
        public float conf;
        public float cls;

        // sizeof(DetectionData)
        public const int Size = 6 * sizeof(float);

        public DetectionData(int x1, int y1, int x2, int y2, float conf, int cls)
        {
            this.x1 = x1;
            this.y1 = y1;
            this.x2 = x2;
            this.y2 = y2;
            this.conf = conf;
            this.cls = cls;
        }

        public override string ToString()
        {
            return "x1:" + x1.ToString() + " y1:" + y1.ToString() + " x2:" + x2.ToString() + " y2:" + y2.ToString() + " conf:" + conf.ToString() + "  cls:" + cls.ToString();
        }
    };

    int modelLayerCount = 0;
    public int framesToExectute = 2;

    // Start is called before the first frame update
    IEnumerator Start()
    {
        // 关闭垂直同步（关键！）
        QualitySettings.vSyncCount = 0;

        // 设置目标帧率为120 FPS
        Application.targetFrameRate = 90;

        // Load Model
        Unity.Sentis.Model sourceModel = Unity.Sentis.ModelLoader.Load(modelAsset);
        //// Create a functional graph that runs the input model
        FunctionalGraph graph = new FunctionalGraph();
        FunctionalTensor[] inputs = graph.AddInputs(sourceModel);
        FunctionalTensor[] outputs = Functional.Forward(sourceModel, inputs);
        runtimeModel = graph.Compile(outputs);
        modelLayerCount = runtimeModel.layers.Count;
        // Create an engine
        worker = new Worker(runtimeModel, BackendType.CPU);
        //results = new float[70125];

        // 初始化后处理compute shader
        postProcessSoftmaxKernelHandle = postProcessSoftmaxCompute.FindKernel("CSMain");

        m_ComputeInputIndex = Shader.PropertyToID("_input");
        m_ComputeOutputIndex = Shader.PropertyToID("_result");

        int datastride = sizeof(float);
        SoftMaxOutputBuffer = new ComputeBuffer(anchorsCount * 6, datastride, ComputeBufferType.Default);
        SortingOutputBuffer = new ComputeBuffer(anchorsCount * 6, datastride, ComputeBufferType.Default);

        // 初始化可视化compute shader
        if (visualizeCompute != null)
        {
            visualizeKernelHandle = visualizeCompute.FindKernel("CSMain");
            if (visualizeKernelHandle < 0)
            {
                UnityEngine.Debug.LogError("Failed to find CSMain kernel in VisualizeDetection compute shader!");
            }
        }
        else
        {
            UnityEngine.Debug.LogWarning("VisualizeDetection compute shader is not assigned!");
        }

        if (visualizeComputeGPU != null)
        {
            visualizeGPUKernelHandle = visualizeComputeGPU.FindKernel("CSMain");
            if (visualizeGPUKernelHandle < 0)
            {
                UnityEngine.Debug.LogError("Failed to find CSMain kernel in VisualizeDetectionGPU compute shader!");
            }
        }
        else
        {
            UnityEngine.Debug.LogWarning("VisualizeDetection compute shader is not assigned!");
        }




        sw = Stopwatch.StartNew();
        preProcessKernelHandle = preProcessCompute.FindKernel("CSMain");


        ///////////////////////////////////
        clearKernel = NMSCompute.FindKernel("ClearOutput");
        nmsKernel = NMSCompute.FindKernel("NMSKernel");

        NMSOutputBuffer = new ComputeBuffer(256 * 6 + 1, sizeof(float));
        NMSSuppressedBuffer = new ComputeBuffer(256, sizeof(int));

        scoreIndexPairs = new ComputeBuffer(Mathf.NextPowerOfTwo(anchorsCount), sizeof(float) * 2); //4096

        //ModelQuantizer.QuantizeWeights(QuantizationType.Float16, ref runtimeModel);

        //// Serialize the quantized model to a file.
        //ModelWriter.Save("nanodet_fp16.onnx", runtimeModel);

        //Preprocess
        preProcessCompute.SetTexture(preProcessKernelHandle, "_InputTexture", inputTexture);
        preProcessCompute.SetTexture(preProcessKernelHandle, "Result", resultRT);

        preProcessCompute.SetVector("_Mean", new Vector4(123.675f, 116.28f, 103.53f, 0));
        preProcessCompute.SetVector("_Std", new Vector4(58.395f, 57.12f, 57.375f, 0));
        preProcessCompute.SetInt("_FrameIdx", tick);
        DispatchCompute();

        inputTensor = TextureConverter.ToTensor(resultRT, 320, 320, 3);

        while (true)
        {
            yield return new WaitForEndOfFrame();
            UnityEngine.Debug.Log($"end of frame:{tick}");
        }

        yield return null;
    }
    void DispatchCompute()
    {
        uint threadGroupsX, threadGroupsY, threadGroupsZ;
        preProcessCompute.GetKernelThreadGroupSizes(preProcessKernelHandle, out threadGroupsX, out threadGroupsY, out threadGroupsZ);

        int groupsX = Mathf.CeilToInt((float)inputTexture.width / threadGroupsX);
        int groupsY = Mathf.CeilToInt((float)inputTexture.height / threadGroupsY);

        preProcessCompute.Dispatch(preProcessKernelHandle, groupsX, groupsY, 1);
    }

    void DispatchPostProcessSoftmax()
    {
        uint threadGroupsX, threadGroupsY, threadGroupsZ;
        postProcessSoftmaxCompute.GetKernelThreadGroupSizes(postProcessSoftmaxKernelHandle, out threadGroupsX, out threadGroupsY, out threadGroupsZ);

        int groupsX = Mathf.CeilToInt((float)anchorsCount / threadGroupsX);

        postProcessSoftmaxCompute.Dispatch(postProcessSoftmaxKernelHandle, groupsX, 1, 1);
    }

    int tick = 0;
    float elapsed = 0;
    float fps = 0;

    int actualOutputCount = 0;

    bool executionStarted = false;

    

    // Update is called once per frame
    void Update()
    {
        tick++;
        elapsed += Time.deltaTime;
        if (elapsed >= 1f)
        {
            fps = tick / elapsed;
            tick = 0;
            elapsed = 0;
        }
        UnityEngine.Debug.Log($"start update tick:{tick}");
        FPStext.text = $"FPS: {fps:F0}";
        UpdateDetection();
    }
    IEnumerator executionSchedule;
    void UpdateDetection()
    {
        if (readGPUData) return;
        if (!executionStarted)
        {
            ////Preprocess
            //preProcessCompute.SetTexture(preProcessKernelHandle, "_InputTexture", inputTexture);
            //preProcessCompute.SetTexture(preProcessKernelHandle, "Result", resultRT);

            //preProcessCompute.SetVector("_Mean", new Vector4(123.675f, 116.28f, 103.53f, 0));
            //preProcessCompute.SetVector("_Std", new Vector4(58.395f, 57.12f, 57.375f, 0));
            //preProcessCompute.SetInt("_FrameIdx", tick);
            //DispatchCompute();

            //Inference
            sw.Restart();
            //inputTensor = TextureConverter.ToTensor(resultRT, 320, 320, 3);
            // Run the model with the input data
            //worker.Schedule(inputTensor);
            executionSchedule = worker.ScheduleIterable(inputTensor);
            executionStarted = true;
            // shape:(1,2125,33)
            sw.Stop();
            totalInferenceTime += sw.ElapsedMilliseconds;
            totalInference += 1;
            UnityEngine.Debug.Log($"Inference Time: {totalInferenceTime / (double)(totalInference)} ms");
        }

        bool hasMoreWork = false;
        int layersToRun = (modelLayerCount + framesToExectute) / framesToExectute; // round up
        for (int i = 0; i < layersToRun; i++)
        {
            hasMoreWork = executionSchedule.MoveNext();
            if (!hasMoreWork)
                break;
        }

        if (hasMoreWork)
        {
            UnityEngine.Debug.Log($"hasMoreWork: {tick}; current layers: {layersToRun}, total layers:{modelLayerCount}");
            return;
        }
            

        // Get the result
        outputTensor = worker.PeekOutput() as Tensor<float>;
        UnityEngine.Debug.Log($"outputTensor backend: {outputTensor.backendType}");
        sw.Restart();
        float[] results = outputTensor.DownloadToArray();
        sw.Stop();
        UnityEngine.Debug.Log($"read result: {sw.ElapsedMilliseconds} ms, results: {results.Length}");
        executionStarted = false;
        return;

        //Post process GPU -- Stage1 Softmax
        sw.Restart();
        var gpuTensorOut = ComputeTensorData.Pin(outputTensor);
        // The fastest path is to dispatch compute directly on this tensor's compute buffer.
        postProcessSoftmaxCompute.SetBuffer(postProcessSoftmaxKernelHandle, m_ComputeInputIndex, gpuTensorOut.buffer);
        postProcessSoftmaxCompute.SetBuffer(postProcessSoftmaxKernelHandle, m_ComputeOutputIndex, SoftMaxOutputBuffer);
        postProcessSoftmaxCompute.SetInt("numClasses", num_classes);
        postProcessSoftmaxCompute.SetInt("imgSize", imgSize);
        postProcessSoftmaxCompute.SetFloat("confThreshold", conf_threshold);
        DispatchPostProcessSoftmax();
        sw.Stop();
        totalSoftmaxTime += sw.ElapsedMilliseconds;
        totalSoftmax++;
        UnityEngine.Debug.Log($"Softmax result: {SoftMaxOutputBuffer.count}");
        UnityEngine.Debug.Log($"Softmax Stage Time: {totalSoftmaxTime / (double)(totalSoftmax)} ms");


        // testing softmax: no problem!
        //int softmaxTestKernelHandle = visualizeSoftmaxTestCompute.FindKernel("CSMain");
        //visualizeSoftmaxTestCompute.SetTexture(softmaxTestKernelHandle, "outputTexture", resultRT);
        //visualizeSoftmaxTestCompute.SetBuffer(softmaxTestKernelHandle, "inputBuffer", SoftMaxOutputBuffer);
        //int vstc_threadGroupX = Mathf.CeilToInt(2125 / 128f);
        //visualizeSoftmaxTestCompute.Dispatch(softmaxTestKernelHandle, vstc_threadGroupX, 1, 1);


        sw.Restart();
        // Post process GPU -- Stage2 NMS
        // 数据排序，从2125中找到前256个分数最大的框；Todo Test：应该是满足需求了？

        sortingCompute.SetInt("numDetections", anchorsCount);
        sortingCompute.SetInt("stride", 6);

        // 内核索引
        int initKernel = sortingCompute.FindKernel("InitializeSortData");
        int sortKernel = sortingCompute.FindKernel("BitonicSortStep");
        int outputKernel = sortingCompute.FindKernel("WriteSortedOutput");
        int padded_count = Mathf.NextPowerOfTwo(anchorsCount);
        // 设置缓冲区
        sortingCompute.SetBuffer(initKernel, "inputBuffer", SoftMaxOutputBuffer);
        sortingCompute.SetInt("PADDED_COUNT", padded_count);
        sortingCompute.SetBuffer(initKernel, "scoreIndexPairs", scoreIndexPairs);

        sortingCompute.SetBuffer(sortKernel, "scoreIndexPairs", scoreIndexPairs);

        sortingCompute.SetBuffer(outputKernel, "inputBuffer", SoftMaxOutputBuffer);
        sortingCompute.SetBuffer(outputKernel, "outputBuffer", SortingOutputBuffer);
        sortingCompute.SetBuffer(outputKernel, "scoreIndexPairs", scoreIndexPairs);

        // 执行初始化
        sortingCompute.Dispatch(initKernel, Mathf.CeilToInt(padded_count / 256f), 1, 1);

        // 执行排序
        // Bitonic sort: log2(paddedSize) stages
        int logN = 0;
        for (int t = padded_count; t > 1; t >>= 1) logN++;

        for (int stage_idx = 1; stage_idx <= logN; stage_idx++)
        {
            for (int pass_idx = 0; pass_idx < stage_idx; pass_idx++)
            {
                sortingCompute.SetInt("stage_idx", stage_idx);
                sortingCompute.SetInt("pass_idx", pass_idx);
                sortingCompute.SetBuffer(sortKernel, "scoreIndexPairs", scoreIndexPairs);
                sortingCompute.Dispatch(sortKernel, Mathf.CeilToInt(padded_count / 64f), 1, 1);
            }
        }

        // 写入结果
        sortingCompute.Dispatch(outputKernel, Mathf.CeilToInt(anchorsCount / 256f), 1, 1);
        UnityEngine.Debug.Log($"SortingOutputBuffer result: {SortingOutputBuffer.count}");

        // 清除标记缓冲区
        //NMSCompute.SetBuffer(clearKernel, "outputCount", NMSOutputCountBuffer);
        //NMSCompute.Dispatch(clearKernel, 1, 1, 1);

        // 256个结果的NMS
        NMSCompute.SetFloat("nmsThreshold", nms_threshold);
        NMSCompute.SetInt("numDetections", 256);
        NMSCompute.SetBuffer(nmsKernel, "inputBuffer", SortingOutputBuffer);
        NMSCompute.SetBuffer(nmsKernel, "outputBuffer", NMSOutputBuffer);
        NMSCompute.SetBuffer(nmsKernel, "suppressedBuffer", NMSSuppressedBuffer);
        //NMSCompute.SetBuffer(nmsKernel, "outputCount", NMSOutputCountBuffer);
        NMSCompute.Dispatch(nmsKernel, 1, 1, 1);

        sw.Stop();
        totalNMSTime += sw.ElapsedMilliseconds;
        totalNMS++;
        UnityEngine.Debug.Log($"NMS: {totalNMSTime / (double)(totalNMS)} ms");

        VisualizeDetectionsGPU();

        executionStarted = false;
        //AsyncGPUReadbackRequest request = AsyncGPUReadback.Request(NMSOutputBuffer, (1 + (topK) * 6) * sizeof(float), 0, OnCompleteReadback);

        //readGPUData = true;
    }

    void OnCompleteReadback(AsyncGPUReadbackRequest request)
    {
        if (!request.done)
        {
            UnityEngine.Debug.Log("readback hasnt done yet");
            return;
        }


        if (request.hasError)
        {
            UnityEngine.Debug.Log("readback error");
        }
        else
        {
            if (currentTime == 0)
            {
                currentTime = Time.realtimeSinceStartup;
            }
            else
            {
                UnityEngine.Debug.Log("Actual Interval:" + (Time.realtimeSinceStartup - currentTime) * 1000);
                currentTime = Time.realtimeSinceStartup;
            }
            sw.Restart();
            float[] outputData = request.GetData<float>().ToArray();
            UnityEngine.Debug.Log("actualOutputCount:" + outputData[0]);
            actualOutputCount = Mathf.Min((int)outputData[0],topK);
            UnityEngine.Debug.Log("actualOutputCount:" + actualOutputCount);
            UnityEngine.Debug.Log("outputData:"+ outputData.Length);
            //NMSOutputBuffer.GetData(outputData, 0, 0, actualOutputCount * 6);

            List<DetectionData> results = new List<DetectionData>();
            for (int i = 0; i < actualOutputCount; i++)
            {
                int baseIdx = 1 + i * 6;
                results.Add(new DetectionData
                {
                    x1 = outputData[baseIdx],
                    y1 = outputData[baseIdx + 1],
                    x2 = outputData[baseIdx + 2],
                    y2 = outputData[baseIdx + 3],
                    conf = outputData[baseIdx + 4],
                    cls = outputData[baseIdx + 5]
                });
            }
            UnityEngine.Debug.Log($"Results: {results[results.Count - 1].ToString()}");
            sw.Stop();
            totalDownloadTime += sw.ElapsedMilliseconds;
            totalDownload++;
            UnityEngine.Debug.Log($"Download: {totalDownloadTime / (double)(totalDownload)} ms");

            //sw.Restart();
            //// 使用compute shader可视化检测结果
            //VisualizeDetections(results.ToArray());
            //sw.Stop();
            //totalVisualizeTime += sw.ElapsedMilliseconds;
            //totalVisualize++;
            //UnityEngine.Debug.Log($"Visualize Time: {totalVisualizeTime / (double)(totalVisualize)} ms");
            readGPUData = false;
        }
    }

    void OnDisable()
    {
        // Tell the GPU we're finished with the memory the engine used
        worker?.Dispose();

        // 释放Tensor资源
        inputTensor?.Dispose();
        outputTensor?.Dispose();
        // 释放ComputeBuffer资源
        detectionDataBuffer?.Release();
        SoftMaxOutputBuffer?.Release();
        NMSOutputBuffer?.Release();
        NMSSuppressedBuffer?.Release();
        SortingOutputBuffer?.Release();
        scoreIndexPairs?.Release();
    }

    // 直接可视化Compute Buffer
    private void VisualizeDetectionsGPU()
    {
        sw.Restart();
        if (visualizeComputeGPU == null || visualizeGPUKernelHandle < 0)
        {
            UnityEngine.Debug.LogError("VisualizeDetectionGPU compute shader is not properly initialized!");
            return;
        }
        // 设置compute shader参数
        visualizeComputeGPU.SetTexture(visualizeGPUKernelHandle, "inputTexture", resultRT);
        visualizeComputeGPU.SetBuffer(visualizeGPUKernelHandle, "detections", NMSOutputBuffer);
        visualizeComputeGPU.SetInt("frameIdx", frameIdx);
        frameIdx++;
        // 执行compute shader
        visualizeComputeGPU.Dispatch(visualizeGPUKernelHandle, 1, 1, 1);
        sw.Stop();
        totalVisualizeTime += sw.ElapsedMilliseconds;
        totalVisualize++;
        UnityEngine.Debug.Log($"Visualize Time: {totalVisualizeTime / (double)(totalVisualize)} ms");
    }


    // 使用compute shader可视化检测结果
    private void VisualizeDetections(DetectionData[] detections)
    {
        if (visualizeCompute == null || visualizeKernelHandle < 0)
        {
            UnityEngine.Debug.LogError("VisualizeDetection compute shader is not properly initialized!");
            return;
        }

        if (detections == null || detections.Length == 0)
        {
            UnityEngine.Debug.Log("No detections to visualize.");
            return;
        }

        //for (int i = 0; i < detections.Length; i++)
        //{
        //    UnityEngine.Debug.Log(detections[i].ToString());
        //}

        // 创建检测结果缓冲区
        if (detectionDataBuffer != null) detectionDataBuffer.Release();
        detectionDataBuffer = new ComputeBuffer(detections.Length, DetectionData.Size);
        detectionDataBuffer.SetData(detections);

        // 设置compute shader参数
        visualizeCompute.SetTexture(visualizeKernelHandle, "inputTexture", resultRT);
        visualizeCompute.SetTexture(visualizeKernelHandle, "outputTexture", resultRTTest);
        visualizeCompute.SetBuffer(visualizeKernelHandle, "detections", detectionDataBuffer);
        visualizeCompute.SetInt("detectionCount", detections.Length);
        visualizeCompute.SetInt("frameIdx", frameIdx);
        frameIdx++;

        // 计算线程组数量
        int width = resultRT.width;
        int height = resultRT.height;
        int threadGroupX = Mathf.CeilToInt(width / 8.0f);
        int threadGroupY = Mathf.CeilToInt(height / 8.0f);

        // 执行compute shader
        visualizeCompute.Dispatch(visualizeKernelHandle, threadGroupX, threadGroupY, 1);

        // 释放缓冲区
        detectionDataBuffer.Release();
        detectionDataBuffer = null;
    }

    
    public string getClassLabel(float id)
    {
        int classId = (int)id;
        string className = string.Empty;
        if (classNames != null && classNames.Count != 0)
        {
            if (classId >= 0 && classId < classNames.Count)
            {
                className = classNames[classId];
            }
        }
        if (string.IsNullOrEmpty(className))
            className = classId.ToString();

        return className;
    }

}
