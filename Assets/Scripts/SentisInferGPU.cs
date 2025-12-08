using System.Collections.Generic;
using System.Diagnostics;
using Unity.Sentis;
using UnityEngine;
using System.Runtime.InteropServices;

public class SentisInferGPU : MonoBehaviour
{
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
    ComputeShader NMSCompute;

    [SerializeField]
    ComputeShader visualizeCompute;

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

    private int m_ComputeInputIndex;
    private int m_ComputeOutputIndex;

    private ComputeBuffer detectionDataBuffer;
    private ComputeBuffer _outputBuffer;



    /// <summary>
    /// Performance Metrics
    /// </summary>
    int frameIdx = 0;
    long totalInferenceTime = 0;
    long totalSoftmaxTime = 0;
    long totalNMSTime = 0;
    long totalVisualizeTime = 0;
    float totalInference = 0;
    float totalSoftmax = 0;
    float totalNMS = 0;
    float totalVisualize = 0;

    int validRun = 0;
    float validRunTotal = 0f;
    int updateCount = 0;
    Stopwatch sw;

    float currentTime;


    [StructLayout(LayoutKind.Sequential)]
    public readonly struct DetectionData
    {
        public readonly float conf;
        public readonly float cls;
        public readonly float x1;
        public readonly float y1;
        public readonly float x2;
        public readonly float y2;

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

    // Start is called before the first frame update
    void Start()
    {
        // 关闭垂直同步（关键！）
        QualitySettings.vSyncCount = 0;

        // 设置目标帧率为120 FPS
        Application.targetFrameRate = 120;

        // Load Model
        Unity.Sentis.Model sourceModel = Unity.Sentis.ModelLoader.Load(modelAsset);
        //// Create a functional graph that runs the input model
        FunctionalGraph graph = new FunctionalGraph();
        FunctionalTensor[] inputs = graph.AddInputs(sourceModel);
        FunctionalTensor[] outputs = Functional.Forward(sourceModel, inputs);
        runtimeModel = graph.Compile(outputs);

        // Create an engine
        worker = new Worker(runtimeModel, BackendType.GPUCompute);
        //results = new float[70125];

        // 初始化后处理compute shader
        if (postProcessSoftmaxCompute == null)
        {
            UnityEngine.Debug.LogError("PostProcess softmax compute shader is not assigned!");
            return;
        }
        postProcessSoftmaxKernelHandle = postProcessSoftmaxCompute.FindKernel("CSMain");
        if (postProcessSoftmaxKernelHandle < 0)
        {
            UnityEngine.Debug.LogError("Failed to find CSMain kernel in PostProcess compute shader!");
            return;
        }

        m_ComputeInputIndex = Shader.PropertyToID("_input");
        m_ComputeOutputIndex = Shader.PropertyToID("_result");


        int datastride = sizeof(float);
        _outputBuffer = new ComputeBuffer(anchorsCount * 6, datastride, ComputeBufferType.Default);

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


        sw = Stopwatch.StartNew();

        preProcessKernelHandle = preProcessCompute.FindKernel("CSMain");

        currentTime = Time.realtimeSinceStartup;

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

    
    // Update is called once per frame
    void Update()
    {
        updateCount++;
        //Preprocess
        preProcessCompute.SetTexture(preProcessKernelHandle, "_InputTexture", inputTexture);
        preProcessCompute.SetTexture(preProcessKernelHandle, "Result", resultRT);

        preProcessCompute.SetVector("_Mean", new Vector4(123.675f, 116.28f, 103.53f, 0));
        preProcessCompute.SetVector("_Std", new Vector4(58.395f, 57.12f, 57.375f, 0));
        preProcessCompute.SetInt("_FrameIdx", updateCount);
        DispatchCompute();

        //Inference
        sw.Restart();
        inputTensor = TextureConverter.ToTensor(resultRT, 320, 320, 3);
        // Run the model with the input data
        worker.Schedule(inputTensor);
        // Get the result
        outputTensor = worker.PeekOutput() as Tensor<float>;
        // shape:(1,2125,33)
        sw.Stop();
        totalInferenceTime += sw.ElapsedMilliseconds;
        totalInference += 1;
        UnityEngine.Debug.Log($"Inference Time: {totalInferenceTime / (double)(totalInference)} ms");

        //Post process GPU -- Stage1 Softmax
        sw.Restart();
        var gpuTensorOut = ComputeTensorData.Pin(outputTensor);
        // The fastest path is to dispatch compute directly on this tensor's compute buffer.
        postProcessSoftmaxCompute.SetBuffer(postProcessSoftmaxKernelHandle, m_ComputeInputIndex, gpuTensorOut.buffer);
        postProcessSoftmaxCompute.SetBuffer(postProcessSoftmaxKernelHandle, m_ComputeOutputIndex, _outputBuffer);
        postProcessSoftmaxCompute.SetInt("numClasses", num_classes);
        postProcessSoftmaxCompute.SetInt("imgSize", imgSize);
        postProcessSoftmaxCompute.SetFloat("confThreshold", conf_threshold);
        DispatchPostProcessSoftmax();
        sw.Stop();
        totalSoftmaxTime += sw.ElapsedMilliseconds;
        totalSoftmax++;
        //UnityEngine.Debug.Log($"Post Process Stage1 Time: {totalDownloadTime / (double)(totalFrames)} ms");
        UnityEngine.Debug.Log($"Softmax result: {_outputBuffer.count}");
        UnityEngine.Debug.Log($"Softmax Stage Time: {totalSoftmaxTime / (double)(totalSoftmax)} ms");

        sw.Restart();
        // Post process GPU -- Stage2 NMS
        //DetectionData[] data = PassToPost(results);
        sw.Stop();
        totalNMSTime += sw.ElapsedMilliseconds;
        totalNMS++;
        UnityEngine.Debug.Log($"PostProcess Time: {totalNMSTime / (double)(totalNMS)} ms");

        ////-----------------------------------------------------------------------------
        //StringBuilder sb = new StringBuilder(512);

        //for (int i = 0; i < data.Length; ++i)
        //{
        //    var d = data[i];
        //    string label = getClassLabel(d.cls);

        //    sb.AppendFormat("-----------object {0}-----------", i + 1);
        //    sb.AppendLine();
        //    sb.AppendFormat("conf: {0:F4}", d.conf);
        //    sb.AppendLine();
        //    sb.Append("cls: ").Append(label);
        //    sb.AppendLine();
        //    sb.AppendFormat("box: {0:F0} {1:F0} {2:F0} {3:F0}", d.x1, d.y1, d.x2, d.y2);
        //    sb.AppendLine();
        //}
        //UnityEngine.Debug.Log(sb.ToString());
        ////-----------object 1-----------
        ////conf: 0.9095
        ////cls: brush
        ////box: 106 92 166 148


        ////test
        ////-----------object 1---------- -
        ////conf: 0.9078
        ////cls: Brush
        ////box: 106 92 166 148
        ////-----------------------------------------------------------------------------

        //sw.Restart();

        //// 使用compute shader可视化检测结果
        //VisualizeDetections(data);

        //sw.Stop();
        //totalVisualizeTime += sw.ElapsedMilliseconds;
        //totalVisualize++;
        //UnityEngine.Debug.Log($"Visualize Time: {totalVisualizeTime / (double)(totalVisualize)} ms");

    }

    void OnDisable()
    {
        // Tell the GPU we're finished with the memory the engine used
        worker.Dispose();

        // 释放Tensor资源
        if (inputTensor != null)
        {
            inputTensor.Dispose();
        }
        if (outputTensor != null)
        {
            outputTensor.Dispose();
        }

        // 释放ComputeBuffer资源
        if (detectionDataBuffer != null)
        {
            detectionDataBuffer.Release();
            detectionDataBuffer = null;
        }
        if (_outputBuffer != null)
        {
            _outputBuffer.Release();
        }
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

    //DetectionData[] PassToPost(ReadOnlySpan<float> result)
    //{
    //    // 使用GPU Compute Shader进行后处理
    //    DetectionData[] data = PostprocessGPU();
    //    return data;
    //}

    //protected DetectionData[] PostprocessGPU()
    //{
    //    // 创建新的缓冲区
    //    inputMatrixBuffer = new ComputeBuffer(inputWidth * inputHeight, sizeof(float));
    //    inputMatrixBuffer.SetData(inputMatrixData);

    //    stridesBuffer = new ComputeBuffer(strides.Length, sizeof(int));
    //    stridesBuffer.SetData(strides);

    //    anchorsBuffer = new ComputeBuffer(num * 2, sizeof(float));
    //    anchorsBuffer.SetData(anchorsData);

    //    projectBuffer = new ComputeBuffer(reg_max + 1, sizeof(float));
    //    projectBuffer.SetData(projectData);

    //    outputBoxesBuffer = new ComputeBuffer(maxOutputCount, 4 * sizeof(float)); // float4
    //    outputConfidencesBuffer = new ComputeBuffer(maxOutputCount, sizeof(float));
    //    outputClassIdsBuffer = new ComputeBuffer(maxOutputCount, sizeof(float));

    //    int[] outputCountData = new int[1] { 0 };
    //    outputCountBuffer = new ComputeBuffer(1, sizeof(int), ComputeBufferType.Raw);
    //    outputCountBuffer.SetData(outputCountData);

    //    // 设置Compute Shader参数
    //    postProcessCompute.SetBuffer(postProcessKernelHandle, "inputMatrix", inputMatrixBuffer);
    //    postProcessCompute.SetInt("inputWidth", inputWidth);
    //    postProcessCompute.SetInt("inputHeight", inputHeight);
    //    postProcessCompute.SetInt("inputChannels", inputChannels);
    //    postProcessCompute.SetFloat("confThreshold", conf_threshold);
    //    postProcessCompute.SetInt("numClasses", num_classes);
    //    postProcessCompute.SetInt("regMax", reg_max);
    //    postProcessCompute.SetBuffer(postProcessKernelHandle, "strides", stridesBuffer);
    //    postProcessCompute.SetInt("numStrides", strides.Length);
    //    postProcessCompute.SetBuffer(postProcessKernelHandle, "anchors", anchorsBuffer);
    //    postProcessCompute.SetInt("numAnchors", num);
    //    postProcessCompute.SetBuffer(postProcessKernelHandle, "project", projectBuffer);
    //    postProcessCompute.SetBuffer(postProcessKernelHandle, "outputBoxes", outputBoxesBuffer);
    //    postProcessCompute.SetBuffer(postProcessKernelHandle, "outputConfidences", outputConfidencesBuffer);
    //    postProcessCompute.SetBuffer(postProcessKernelHandle, "outputClassIds", outputClassIdsBuffer);
    //    postProcessCompute.SetBuffer(postProcessKernelHandle, "outputCount", outputCountBuffer);

    //    // 调度Compute Shader
    //    int threadGroupsX = Mathf.CeilToInt((float)num / 64);
    //    postProcessCompute.Dispatch(postProcessKernelHandle, threadGroupsX, 1, 1);

    //    // 读取结果
    //    outputCountBuffer.GetData(outputCountData);
    //    int actualOutputCount = outputCountData[0];

    //    float[] outputBoxesData = new float[actualOutputCount * 4];
    //    float[] outputConfidencesData = new float[actualOutputCount];
    //    float[] outputClassIdsData = new float[actualOutputCount];

    //    outputBoxesBuffer.GetData(outputBoxesData, 0, 0, actualOutputCount * 4);
    //    outputConfidencesBuffer.GetData(outputConfidencesData, 0, 0, actualOutputCount);
    //    outputClassIdsBuffer.GetData(outputClassIdsData, 0, 0, actualOutputCount);

    //    // 释放缓冲区
    //    DisposeComputeBuffers();

    //    // 准备NMS输入
    //    if (boxes_m_c4 == null || boxes_m_c4.rows() != actualOutputCount)
    //        boxes_m_c4 = new Mat(actualOutputCount, 1, CvType.CV_64FC4);
    //    if (confidences_m == null || confidences_m.rows() != actualOutputCount)
    //        confidences_m = new Mat(actualOutputCount, 1, CvType.CV_32FC1);
    //    if (class_ids_m == null || class_ids_m.rows() != actualOutputCount)
    //        class_ids_m = new Mat(actualOutputCount, 1, CvType.CV_32SC1);

    //    if (boxes == null || boxes.rows() != actualOutputCount)
    //        boxes = new MatOfRect2d(boxes_m_c4);
    //    if (confidences == null || confidences.rows() != actualOutputCount)
    //        confidences = new MatOfFloat(confidences_m);
    //    if (class_ids == null || class_ids.rows() != actualOutputCount)
    //        class_ids = new MatOfInt(class_ids_m);

    //    // 填充NMS输入数据
    //    for (int i = 0; i < actualOutputCount; i++)
    //    {
    //        // 转换为[x, y, w, h]格式
    //        float x = outputBoxesData[i * 4 + 0];
    //        float y = outputBoxesData[i * 4 + 1];
    //        float w = outputBoxesData[i * 4 + 2] - x;
    //        float h = outputBoxesData[i * 4 + 3] - y;

    //        boxes_m_c4.put(i, 0, new double[] { x, y, w, h });
    //        confidences_m.put(i, 0, outputConfidencesData[i]);
    //        class_ids_m.put(i, 0, (int)outputClassIdsData[i]);
    //    }

    //    // non-maximum suppression



    //    return results;
    //}


    //private int clearKernel;
    //private int nmsKernel;

    //private int maxDetections = 10000;
    //private int threadGroupSize = 256;

    //void Start()
    //{
    //    InitializeBuffers();

    //    // 测试性能
    //    RunPerformanceTest();
    //}

    //void InitializeBuffers()
    //{
    //    clearKernel = nmsComputeShader.FindKernel("ClearOutput");
    //    nmsKernel = nmsComputeShader.FindKernel("NMSKernel");

    //    inputBuffer = new ComputeBuffer(maxDetections, 6 * sizeof(float));
    //    outputBuffer = new ComputeBuffer(maxDetections, 6 * sizeof(float));
    //    suppressedBuffer = new ComputeBuffer(maxDetections, sizeof(int));
    //    outputCountBuffer = new ComputeBuffer(1, sizeof(int));
    //}

    ///// <summary>
    ///// 运行优化的NMS
    ///// </summary>
    //public unsafe List<float[]> RunNMSOptimized(float* detectionsPtr, int boxCount, float[] scores = null)
    //{
    //    if (boxCount > maxDetections)
    //    {
    //        Debug.LogError($"检测框数量{boxCount}超过最大值{maxDetections}");
    //        return new List<float[]>();
    //    }

    //    // 如果需要按分数排序
    //    if (scores != null)
    //    {
    //        // 排序检测框（按分数降序）
    //        SortDetectionsByScore(detectionsPtr, boxCount, scores);
    //    }

    //    // 上传数据
    //    inputBuffer.SetData(new Span<float>(detectionsPtr, boxCount * 6));

    //    // 清除标记缓冲区
    //    nmsComputeShader.SetBuffer(clearKernel, "outputCount", outputCountBuffer);
    //    nmsComputeShader.Dispatch(clearKernel, 1, 1, 1);

    //    // 设置参数
    //    nmsComputeShader.SetFloat("nmsThreshold", nmsThreshold);
    //    nmsComputeShader.SetInt("numDetections", boxCount);
    //    nmsComputeShader.SetBuffer(nmsKernel, "inputBuffer", inputBuffer);
    //    nmsComputeShader.SetBuffer(nmsKernel, "outputBuffer", outputBuffer);
    //    nmsComputeShader.SetBuffer(nmsKernel, "suppressedBuffer", suppressedBuffer);
    //    nmsComputeShader.SetBuffer(nmsKernel, "outputCount", outputCountBuffer);

    //    // 调度计算
    //    int threadGroups = Mathf.CeilToInt(boxCount / (float)threadGroupSize);
    //    nmsComputeShader.Dispatch(nmsKernel, threadGroups, 1, 1);

    //    // 获取结果
    //    return GetOutputResults();
    //}

    ///// <summary>
    ///// 按分数排序检测框（使用快速排序）
    ///// </summary>
    //private unsafe void SortDetectionsByScore(float* detectionsPtr, int boxCount, float[] scores)
    //{
    //    // 创建索引数组
    //    int[] indices = new int[boxCount];
    //    for (int i = 0; i < boxCount; i++) indices[i] = i;

    //    // 按分数降序排序索引
    //    System.Array.Sort(indices, (a, b) => scores[b].CompareTo(scores[a]));

    //    // 重新排列检测框
    //    float[] sortedDetections = new float[boxCount * 6];
    //    for (int i = 0; i < boxCount; i++)
    //    {
    //        int srcIdx = indices[i] * 6;
    //        int dstIdx = i * 6;

    //        for (int j = 0; j < 6; j++)
    //        {
    //            sortedDetections[dstIdx + j] = detectionsPtr[srcIdx + j];
    //        }
    //    }

    //    // 复制回原数组
    //    for (int i = 0; i < boxCount * 6; i++)
    //    {
    //        detectionsPtr[i] = sortedDetections[i];
    //    }
    //}

    //private List<float[]> GetOutputResults()
    //{
    //    int[] outputCount = new int[1];
    //    outputCountBuffer.GetData(outputCount);
    //    int actualOutputCount = outputCount[0];

    //    float[] outputData = new float[actualOutputCount * 6];
    //    outputBuffer.GetData(outputData, 0, 0, actualOutputCount * 6);

    //    List<float[]> results = new List<float[]>();
    //    for (int i = 0; i < actualOutputCount; i++)
    //    {
    //        int baseIdx = i * 6;
    //        results.Add(new float[]
    //        {
    //            outputData[baseIdx],
    //            outputData[baseIdx + 1],
    //            outputData[baseIdx + 2],
    //            outputData[baseIdx + 3],
    //            outputData[baseIdx + 4],
    //            outputData[baseIdx + 5]
    //        });
    //    }

    //    return results;
    //}

    ///// <summary>
    ///// 性能测试
    ///// </summary>
    //private void RunPerformanceTest()
    //{
    //    System.Random random = new System.Random();

    //    // 生成测试数据
    //    float[] testData = new float[10000 * 6];
    //    for (int i = 0; i < 10000; i++)
    //    {
    //        int baseIdx = i * 6;
    //        testData[baseIdx] = random.Next(0, 10);  // classId
    //        testData[baseIdx + 1] = (float)random.NextDouble();  // score

    //        // 随机框
    //        float left = (float)random.NextDouble() * 1000;
    //        float top = (float)random.NextDouble() * 1000;
    //        float width = 20 + (float)random.NextDouble() * 100;
    //        float height = 20 + (float)random.NextDouble() * 100;

    //        testData[baseIdx + 2] = left;
    //        testData[baseIdx + 3] = top;
    //        testData[baseIdx + 4] = left + width;
    //        testData[baseIdx + 5] = top + height;
    //    }

    //    // 运行NMS
    //    var startTime = System.DateTime.Now;

    //    unsafe
    //    {
    //        fixed (float* ptr = testData)
    //        {
    //            var results = RunNMSOptimized(ptr, 10000);
    //            Debug.Log($"优化版NMS: 输入10000个框, 输出{results.Count}个框, 耗时{(System.DateTime.Now - startTime).TotalMilliseconds:F2}ms");
    //        }
    //    }
    //}

    //void OnDestroy()
    //{
    //    inputBuffer?.Release();
    //    outputBuffer?.Release();
    //    suppressedBuffer?.Release();
    //    outputCountBuffer?.Release();
    //}


}
