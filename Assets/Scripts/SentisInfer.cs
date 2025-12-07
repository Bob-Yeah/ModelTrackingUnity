using OpenCVForUnity.UnityUtils;
using OpenCVForUnity.UnityUtils.Helper;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.Sentis;
using UnityEngine;
using UnityEngine.UI;

using OpenCVForUnity.CoreModule;
using OpenCVForUnity.DnnModule;
using OpenCVForUnity.ImgprocModule;
using OpenCVForUnity.UnityUtils;

using OpenCVRange = OpenCVForUnity.CoreModule.Range;
using OpenCVRect = OpenCVForUnity.CoreModule.Rect;
using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Linq;

public class SentisInfer : MonoBehaviour
{
    public ModelAsset modelAsset;
    [SerializeField]
    Texture2D inputTexture;
    public TMPro.TextMeshProUGUI resultText;

    [SerializeField]
    RenderTexture resultRT;
    [SerializeField]
    RenderTexture resultRTTest;

    Unity.Sentis.Model runtimeModel;
    Worker worker;
    public float[] results;
    //WebCamTextureToMatHelper webCamTextureToMatHelper;
    Tensor<float> inputTensor;
    TextureTransform resizeTransform;
    TextureTransform getTransform;

    Tensor<float> inputTensorBRG;

    [SerializeField]
    ComputeShader computeProducer;

    [SerializeField]
    ComputeShader computeProducerTest;

    [SerializeField]
    ComputeShader postProcessCompute;

    [SerializeField]
    ComputeShader visualizeCompute;

    private int kernelHandle;

    private int kernelHandleTest;

    private int postProcessKernelHandle;
    
    private int visualizeKernelHandle;

    private int m_ComputeInputIndex;
    private int m_ComputeOutputIndex;

    private ComputeBuffer detectionDataBuffer;
    // 结构化缓冲区
    ComputeBuffer inputMatrixBuffer;
    ComputeBuffer stridesBuffer;
    ComputeBuffer anchorsBuffer;
    ComputeBuffer projectBuffer;
    ComputeBuffer outputBoxesBuffer;
    ComputeBuffer outputConfidencesBuffer;
    ComputeBuffer outputClassIdsBuffer;
    ComputeBuffer outputCountBuffer;

    static int anchorsCount = 2125;

    private float[] postProcessResult = new float[anchorsCount];

    private ComputeBuffer _outputBuffer;

    // Start is called before the first frame update
    void Start()
    {
        // 关闭垂直同步（关键！）
        QualitySettings.vSyncCount = 0;

        // 设置目标帧率为60 FPS
        Application.targetFrameRate = 60;


        // Load Model
        Unity.Sentis.Model sourceModel = Unity.Sentis.ModelLoader.Load(modelAsset);
        //// Create a functional graph that runs the input model
        FunctionalGraph graph = new FunctionalGraph();
        FunctionalTensor[] inputs = graph.AddInputs(sourceModel);
        FunctionalTensor[] outputs = Functional.Forward(sourceModel, inputs);
        runtimeModel = graph.Compile(outputs);

        // Create an engine
        worker = new Worker(runtimeModel, BackendType.GPUCompute);
        results = new float[70125];

        // 初始化后处理compute shader
        if (postProcessCompute == null)
        {
            UnityEngine.Debug.LogError("PostProcess compute shader is not assigned!");
            return;
        }
        postProcessKernelHandle = postProcessCompute.FindKernel("CSMain");
        if (postProcessKernelHandle < 0)
        {
            UnityEngine.Debug.LogError("Failed to find CSMain kernel in PostProcess compute shader!");
            return;
        }

        m_ComputeInputIndex = Shader.PropertyToID("_input");
        m_ComputeOutputIndex = Shader.PropertyToID("_result");


        int stride = sizeof(float);
        _outputBuffer = new ComputeBuffer(anchorsCount, stride, ComputeBufferType.Default);

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

    }
    void DispatchCompute()
    {
        uint threadGroupsX, threadGroupsY, threadGroupsZ;
        computeProducer.GetKernelThreadGroupSizes(kernelHandle, out threadGroupsX, out threadGroupsY, out threadGroupsZ);

        int groupsX = Mathf.CeilToInt((float)inputTexture.width / threadGroupsX);
        int groupsY = Mathf.CeilToInt((float)inputTexture.height / threadGroupsY);

        computeProducer.Dispatch(kernelHandle, groupsX, groupsY, 1);
    }

    void DispatchComputeTest()
    {
        uint threadGroupsX, threadGroupsY, threadGroupsZ;
        computeProducerTest.GetKernelThreadGroupSizes(kernelHandleTest, out threadGroupsX, out threadGroupsY, out threadGroupsZ);

        int groupsX = Mathf.CeilToInt((float)resultRT.width / threadGroupsX);
        int groupsY = Mathf.CeilToInt((float)resultRT.height / threadGroupsY);

        computeProducerTest.Dispatch(kernelHandleTest, groupsX, groupsY, 1);
    }

    void DispatchPostProcess()
    {
        uint threadGroupsX, threadGroupsY, threadGroupsZ;
        postProcessCompute.GetKernelThreadGroupSizes(postProcessKernelHandle, out threadGroupsX, out threadGroupsY, out threadGroupsZ);

        int groupsX = Mathf.CeilToInt((float)anchorsCount / threadGroupsX);

        postProcessCompute.Dispatch(postProcessKernelHandle, groupsX, 1, 1);
    }

    long totalInferenceTime = 0;
    long totalDownloadTime = 0;
    long totalPostProcessTime = 0;
    long totalVisualizeTime = 0;
    float totalFrames = 0;
    // Update is called once per frame
    void Update()
    {
        Stopwatch sw = Stopwatch.StartNew();

        kernelHandle = computeProducer.FindKernel("CSMain");
        kernelHandleTest = computeProducerTest.FindKernel("CSMain");

        // ������������
        computeProducer.SetTexture(kernelHandle, "_InputTexture", inputTexture);
        computeProducer.SetTexture(kernelHandle, "Result", resultRT);

        computeProducer.SetVector("_Mean", new Vector4(123.675f, 116.28f, 103.53f, 0));
        computeProducer.SetVector("_Std", new Vector4(58.395f, 57.12f, 57.375f, 0));
        DispatchCompute();

        // Test
        //computeProducerTest.SetTexture(kernelHandleTest, "_InputTexture", resultRT);
        //computeProducerTest.SetTexture(kernelHandleTest, "Result", resultRTTest);

        //computeProducerTest.SetVector("_Mean", new Vector4(123.675f, 116.28f, 103.53f, 0));
        //computeProducerTest.SetVector("_Std", new Vector4(58.395f, 57.12f, 57.375f, 0));
        //DispatchComputeTest();


        inputTensor = TextureConverter.ToTensor(resultRT, 320, 320, 3);
        UnityEngine.Debug.Log($"tensor:{inputTensor.shape}");
        UnityEngine.Debug.Log(inputTensor.dataOnBackend.backendType);

        //var cpuCopyTensor = inputTensor.ReadbackAndClone();
        //UnityEngine.Debug.Log(cpuCopyTensor.dataOnBackend.backendType);
        //UnityEngine.Debug.Log($"float value at 159,159 R: {cpuCopyTensor[0, 0, 159, 159]}"); // 0-1
        //UnityEngine.Debug.Log($"float value at 159,159 G: {cpuCopyTensor[0, 1, 159, 159]}"); // 0-1
        //UnityEngine.Debug.Log($"float value at 159,159 B: {cpuCopyTensor[0, 2, 159, 159]}"); // 0-1

        //TextureConverter.RenderToTexture(inputTensor, resultRT);
        //UnityEngine.Debug.Log($"RT:{resultRT.height},{resultRT.width},{resultRT.format}");


        // Run the model with the input data
        worker.Schedule(inputTensor);

        //// Get the result
        Tensor<float> outputTensor = worker.PeekOutput() as Tensor<float>;
        UnityEngine.Debug.Log(outputTensor.dataOnBackend.backendType); 
        UnityEngine.Debug.Log(outputTensor.shape);
        // shape:(1,2125,33)
        sw.Stop();
        
        totalInferenceTime += sw.ElapsedMilliseconds;
        totalFrames += 1;
        UnityEngine.Debug.Log($"Inference Time: {totalInferenceTime / (double)(totalFrames)} ms");

        sw.Restart();
        int anchorCount = 2125;
        int perAnchorValues = 1 + 32;
        var gpuTensorOut = ComputeTensorData.Pin(outputTensor);

        // The fastest path is to dispatch compute directly on this tensor's compute buffer.
        postProcessCompute.SetBuffer(postProcessKernelHandle, m_ComputeInputIndex, gpuTensorOut.buffer);
        postProcessCompute.SetBuffer(postProcessKernelHandle, m_ComputeOutputIndex, _outputBuffer);

        DispatchPostProcess();
        // outputTensor is still pending
        // Either read back the results asynchronously or do a blocking download call
        //results = outputTensor.DownloadToArray();
        //UnityEngine.Debug.Log($"Results length: {results.Length}");

        _outputBuffer.GetData(postProcessResult);

        UnityEngine.Debug.Log($"_outputBuffer:{_outputBuffer.count}");
        sw.Stop();
        totalDownloadTime += sw.ElapsedMilliseconds;
        UnityEngine.Debug.Log($"Post Process Stage1 Time: {totalDownloadTime / (double)(totalFrames)} ms");

        //sw.Restart();
        //// ���봦������֤
        //DetectionData[] data = PassToPost(results);
        //sw.Stop();
        //totalPostProcessTime += sw.ElapsedMilliseconds;
        //UnityEngine.Debug.Log($"PostProcess Time: {totalPostProcessTime / (double)(totalFrames)} ms");
        
        //sw.Restart();
        
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
        
        //// 使用compute shader可视化检测结果
        //VisualizeDetections(data);

        ////-----------object 1-----------
        ////conf: 0.9095
        ////cls: brush
        ////box: 106 92 166 148


        ////test
        ////-----------object 1---------- -
        ////conf: 0.9078
        ////cls: Brush
        ////box: 106 92 166 148


        ////input_blob.Dispose();
        ////for (int i = 0; i < output_blob.Count; i++)
        ////{
        ////    output_blob[i].Dispose();
        ////}
        
        //sw.Stop();
        //totalVisualizeTime += sw.ElapsedMilliseconds;
        //UnityEngine.Debug.Log($"Visualize Time: {totalVisualizeTime / (double)(totalFrames)} ms");
    }

    void OnDisable()
    {
        // Tell the GPU we're finished with the memory the engine used
        worker.Dispose();
        
        // 释放结构化缓冲区
        DisposeComputeBuffers();
    }
    
    void DisposeComputeBuffers()
    {
        if (inputMatrixBuffer != null) inputMatrixBuffer.Release();
        if (stridesBuffer != null) stridesBuffer.Release();
        if (anchorsBuffer != null) anchorsBuffer.Release();
        if (projectBuffer != null) projectBuffer.Release();
        if (outputBoxesBuffer != null) outputBoxesBuffer.Release();
        if (outputConfidencesBuffer != null) outputConfidencesBuffer.Release();
        if (outputClassIdsBuffer != null) outputClassIdsBuffer.Release();
        if (outputCountBuffer != null) outputCountBuffer.Release();
        if (detectionDataBuffer != null) detectionDataBuffer.Release();
    }

    int num_classes = 1;
    int[] strides = new int[] { 8, 16, 32, 64 };
    Size input_size = new Size(320,320);
    Mat pickup_blob_numx6;
    Mat mlvl_anchors;
    bool optimize_pre_NMS = false;
    Mat boxes_m_c4;
    Mat confidences_m;
    Mat class_ids_m;
    MatOfRect2d boxes;
    MatOfFloat confidences;
    MatOfInt class_ids;
    bool class_agnostic = false;// Non-use of multi-class NMS
    float conf_threshold = 0.35f;
    float nms_threshold = 0.6f;
    int topK = 10;
    Mat project;
    bool keep_ratio = false;

    int frameIdx = 0;

    [StructLayout(LayoutKind.Sequential)]
    public readonly struct DetectionData
    {
        public readonly float x1;
        public readonly float y1;
        public readonly float x2;
        public readonly float y2;
        public readonly float conf;
        public readonly float cls;

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

    public DetectionData[] getData(Mat results)
    {
        if (results.empty())
            return new DetectionData[0];

        var dst = new DetectionData[results.rows()];
        MatUtils.copyFromMat(results, dst);

        return dst;
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

    List<string> classNames = new List<string>() { "Brush" };
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

    private Mat arange(int start, int stop)
    {
        if (start < 0 || stop < 0 || stop < start || stop == start)
            throw new ArgumentException("start < 0 || stop < 0 || stop < start || stop == start");

        float[] data = Enumerable.Range(start, stop).Select(i => (float)i).ToArray();
        Mat dst = new Mat(1, stop - start, CvType.CV_32FC1);
        dst.put(0, 0, data);

        return dst;
    }

    private void tile(Mat a, int ny, int nx, Mat dst)
    {
        if (a == null)
            throw new ArgumentNullException("a");
        if (a != null)
            a.ThrowIfDisposed();

        if (dst == null)
            throw new ArgumentNullException("dst");
        if (dst != null)
            dst.ThrowIfDisposed();
        if (dst.rows() != a.rows() * ny || dst.cols() != a.cols() * nx || dst.type() != a.type())
            throw new ArgumentException("dst.rows() != a.rows() * ny || dst.cols() != a.cols() * nx || dst.type() != a.type()");

        Core.repeat(a, ny, nx, dst);
    }

    protected void generateAnchors(out Mat mlvl_anchors)
    {
        int num = 0;

        int[] hsizes = new int[strides.Length];// stride for stride in self.strides
        int[] wsizes = new int[strides.Length];// stride for stride in self.strides
        for (int i = 0; i < strides.Length; i++)
        {
            hsizes[i] = (int)Mathf.Ceil((float)input_size.height / strides[i]);
            wsizes[i] = (int)Mathf.Ceil((float)input_size.width / strides[i]);

            num += hsizes[i] * wsizes[i];
        }

        mlvl_anchors = new Mat(num, 2, CvType.CV_32FC1);//num*2*CV_32FC1
        int index = 0;

        for (int i = 0; i < strides.Length; i++)
        {
            int feat_h = hsizes[i];
            int feat_w = wsizes[i];
            int stride = strides[i];

            // #shift_x = np.arange(0, feat_w) * stride
            // #shift_y = np.arange(0, feat_h) * stride
            Mat shift_x = arange(0, feat_w);
            Core.multiply(shift_x, Scalar.all(stride), shift_x);
            Mat shift_y = arange(0, feat_h).t();
            Core.multiply(shift_y, Scalar.all(stride), shift_y);

            // #xv, yv = np.meshgrid(shift_x, shift_y)
            Mat xv = new Mat(feat_h, feat_w, CvType.CV_32FC1);
            tile(shift_x, feat_h, 1, xv);
            Mat yv = new Mat(feat_h, feat_w, CvType.CV_32FC1);
            tile(shift_y, 1, feat_w, yv);

            // #np.stack((xv, yv), axis=-1)
            Mat xv_totalx1 = xv.reshape(1, (int)xv.total());//total*1*CV_32FC1
            Mat grid_roi = new Mat(mlvl_anchors, new OpenCVRect(0, index, 1, (int)xv.total()));//total*1*CV_32FC1
            xv_totalx1.copyTo(grid_roi);
            Mat yv_totalx1 = yv.reshape(1, (int)yv.total());//total*1*CV_32FC1
            grid_roi = new Mat(mlvl_anchors, new OpenCVRect(1, index, 1, (int)yv.total()));//total*1*CV_32FC1
            yv_totalx1.copyTo(grid_roi);

            index += feat_h * feat_w;
        }
    }
    int reg_max = 7;
    DetectionData[] PassToPost(float[] result)
    {
        generateAnchors(out mlvl_anchors);
        project = arange(0, reg_max + 1);
        Mat inputMat = CreateReshapedMat(result,1,2125,33);

        // channels:1
        //0:1,1:33,2:num: 2125
        UnityEngine.Debug.Log("output_blob_0.channels():" + inputMat.channels()); 
        int num = inputMat.size(1);
        UnityEngine.Debug.Log("0:" + inputMat.size(0) + ",1:" + inputMat.size(2) + ",2:num:" + num);
        
        Mat results = postprocess(inputMat, input_size);
        // 使用GPU Compute Shader进行后处理
        //Mat results = PostprocessGPU(inputMat, input_size);
        if (results == null)
        {
            UnityEngine.Debug.LogError("PostprocessGPU returned null results!");
            return null;
        }
        
        // scale_boxes
        //float x_factor;
        //float y_factor;
        //float x_shift;
        //float y_shift;
        //{
        //    x_factor = 1;
        //    y_factor = 1;
        //    x_shift = 0;
        //    y_shift = 0;
        //}
        //for (int i = 0; i < results.rows(); ++i)
        //{
        //    float[] results_arr = new float[4];
        //    results.get(i, 0, results_arr);
        //    float x1 = Mathf.Round(results_arr[0] * x_factor - x_shift);
        //    float y1 = Mathf.Round(results_arr[1] * y_factor - y_shift);
        //    float x2 = Mathf.Round(results_arr[2] * x_factor - x_shift);
        //    float y2 = Mathf.Round(results_arr[3] * y_factor - y_shift);

        //    results.put(i, 0, new float[] { x1, y1, x2, y2 });
        //}

        if (results.empty() || results.cols() < 6)
            return null;

        DetectionData[] data = getData(results);

        return data;
        
    }

    public Mat CreateReshapedMat(float[] data, int dim0, int dim1, int dim2)
    {
        // 1. ����һάMat (70125��Ԫ��)
        Mat flatMat = new Mat(1, data.Length, CvType.CV_32FC1);
        flatMat.put(0, 0, data);

        // 2. ����Ϊ��άMat (1x33x2125)
        int[] newDimensions = new int[] { dim0, dim1, dim2 };
        Mat reshapedMat = flatMat.reshape(1, newDimensions);

        return reshapedMat;
    }

    //protected Mat PostprocessGPU(Mat output_blob, Size original_shape)
    //{
    //    bool rescale = false;
    //    float scale_factor = 1f;

    //    Mat output_blob_0 = output_blob;

    //    if (output_blob_0.size(2) != 32 + num_classes)
    //    {
    //        UnityEngine.Debug.LogWarning("The number of classes and output shapes are different. " +
    //        "( output_blob_0.size(2):" + output_blob_0.size(2) + " != 32 + num_classes:" + num_classes + " )\n" +
    //        "When using a custom model, be sure to set the correct number of classes by loading the appropriate custom classesFile.");

    //        num_classes = output_blob_0.size(2) - 32;
    //    }

    //    int num = output_blob_0.size(1);
    //    Mat output_blob_numx112 = output_blob_0.reshape(1, num);

    //    // 准备数据用于Compute Shader
    //    int inputWidth = 33; // channels
    //    int inputHeight = 2125; // num
    //    int inputChannels = 1;
        
    //    // 创建输入矩阵数据（1x33x2125 -> 33x2125）
    //    float[] inputMatrixData = new float[inputWidth * inputHeight];
    //    for (int y = 0; y < inputHeight; y++)
    //    {
    //        for (int x = 0; x < inputWidth; x++)
    //        {
    //            float[] values = new float[1];
    //            output_blob_numx112.get(y, x, values);
    //            inputMatrixData[y * inputWidth + x] = values[0];
    //        }
    //    }
        
    //    // 创建锚点数据
    //    float[] anchorsData = new float[num * 2];
    //    mlvl_anchors.get(0, 0, anchorsData);
        
    //    // 创建project数据
    //    float[] projectData = new float[reg_max + 1];
    //    project.get(0, 0, projectData);
        
    //    // 配置Compute Buffer
    //    int maxOutputCount = num; // 最大可能的输出数量
        
    //    // 释放旧的缓冲区
    //    DisposeComputeBuffers();
        
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
    //    Mat indices = new MatOfInt();

    //    //if (class_agnostic)
    //    //{
    //    //    // NMS
    //    //    Dnn.NMSBoxes(boxes, confidences, conf_threshold, nms_threshold, indices, 1f, topK);
    //    //}
    //    //else
    //    //{
    //    //    // multi-class NMS
    //    //    Dnn.NMSBoxesBatched(boxes, confidences, class_ids, conf_threshold, nms_threshold, indices, 1f, topK);
    //    //}

    //    Mat results = new Mat(indices.rows(), 6, CvType.CV_32FC1);

    //    for (int i = 0; i < indices.rows(); ++i)
    //    {
    //        int idx = (int)indices.get(i, 0)[0];
            
    //        // 从outputBoxesData获取原始的[x1, y1, x2, y2]
    //        float x1 = outputBoxesData[idx * 4 + 0];
    //        float y1 = outputBoxesData[idx * 4 + 1];
    //        float x2 = outputBoxesData[idx * 4 + 2];
    //        float y2 = outputBoxesData[idx * 4 + 3];
    //        float conf = outputConfidencesData[idx];
    //        float cls = outputClassIdsData[idx];
            
    //        results.put(i, 0, new float[] { x1, y1, x2, y2, conf, cls });
    //    }

    //    indices.Dispose();

    //    // [
    //    //   [xyxy, conf, cls]
    //    //   ...
    //    //   [xyxy, conf, cls]
    //    // ]
    //    return results;
    //}
    
    protected Mat postprocess(Mat output_blob, Size original_shape)
    {
        bool rescale = false;
        float scale_factor = 1f;

        Mat output_blob_0 = output_blob;

        if (output_blob_0.size(2) != 32 + num_classes)
        {
            UnityEngine.Debug.LogWarning("The number of classes and output shapes are different. " +
            "( output_blob_0.size(2):" + output_blob_0.size(2) + " != 32 + num_classes:" + num_classes + " )\n" +
            "When using a custom model, be sure to set the correct number of classes by loading the appropriate custom classesFile.");

            num_classes = output_blob_0.size(2) - 32;
        }

        int num = output_blob_0.size(1);
        Mat output_blob_numx112 = output_blob_0.reshape(1, num);

        int[] hsizes = new int[strides.Length];// stride for stride in self.strides
        int[] wsizes = new int[strides.Length];// stride for stride in self.strides
        for (int i = 0; i < strides.Length; i++)
        {
            hsizes[i] = (int)Mathf.Ceil((float)input_size.height / strides[i]);
            wsizes[i] = (int)Mathf.Ceil((float)input_size.width / strides[i]);
        }


        // pre-NMS
        // Pick up rows to process by conf_threshold value and calculate scores and class_ids.
        if (pickup_blob_numx6 == null)
            pickup_blob_numx6 = new Mat(300, 6, CvType.CV_32FC1, new Scalar(0));

        Imgproc.rectangle(pickup_blob_numx6, new OpenCVRect(4, 0, 1, pickup_blob_numx6.rows()), Scalar.all(0), -1);
        int index_pickup = 0;

        int index = 0;

        for (int i = 0; i < strides.Length; i++)
        {
            int feat_h = hsizes[i];
            int feat_w = wsizes[i];
            int stride = strides[i];

            int num_anchors = feat_h * feat_w;

            Mat cls_score = new Mat(output_blob_numx112, new OpenCVRect(0, index, num_classes, num_anchors));
            Mat bbox_pred = new Mat(output_blob_numx112, new OpenCVRect(num_classes, index, 32, num_anchors));
            Mat anchors = new Mat(mlvl_anchors, new OpenCVRect(0, index, 2, num_anchors));

            if (optimize_pre_NMS)
            {
                searchAndPick(cls_score, bbox_pred, anchors, ref pickup_blob_numx6, ref index_pickup, 0, num_anchors, stride, conf_threshold);
            }
            else
            {
                pick(cls_score, bbox_pred, anchors, ref pickup_blob_numx6, ref index_pickup, 0, num_anchors, stride, conf_threshold);
            }

            index += num_anchors;
        }

        int num_pickup = pickup_blob_numx6.rows();
        Mat pickup_box_delta = pickup_blob_numx6.colRange(new OpenCVRange(0, 4));
        Mat pickup_confidence = pickup_blob_numx6.colRange(new OpenCVRange(4, 5));

        // #if rescale:
        // #    mlvl_bboxes /= scale_factor
        if (rescale)
            Core.divide(pickup_box_delta, Scalar.all(scale_factor), pickup_box_delta);


        // Convert boxes from [x1, y1, x2, y2] to [x, y, w, h] where Rect2d data style.
        // #bboxes_wh[:, 2:4] = bboxes_wh[:, 2:4] - bboxes_wh[:, 0:2]  ####xywh
        // #classIds = np.argmax(mlvl_scores, axis = 1)
        // #confidences = np.max(mlvl_scores, axis = 1)  ####max_class_confidence
        Mat xy1 = pickup_box_delta.colRange(new OpenCVRange(0, 2));
        Mat xy2 = pickup_box_delta.colRange(new OpenCVRange(2, 4));
        Core.subtract(xy2, xy1, xy2);


        if (boxes_m_c4 == null || boxes_m_c4.rows() != num_pickup)
            boxes_m_c4 = new Mat(num_pickup, 1, CvType.CV_64FC4);
        if (confidences_m == null || confidences_m.rows() != num_pickup)
            confidences_m = new Mat(num_pickup, 1, CvType.CV_32FC1);

        if (boxes == null || boxes.rows() != num_pickup)
            boxes = new MatOfRect2d(boxes_m_c4);
        if (confidences == null || confidences.rows() != num_pickup)
            confidences = new MatOfFloat(confidences_m);

        // non-maximum suppression
        Mat boxes_m_c1 = boxes_m_c4.reshape(1, num_pickup);
        pickup_box_delta.convertTo(boxes_m_c1, CvType.CV_64F);
        pickup_confidence.copyTo(confidences_m);

        MatOfInt indices = new MatOfInt();

        if (class_agnostic)
        {
            // NMS
            Dnn.NMSBoxes(boxes, confidences, conf_threshold, nms_threshold, indices, 1f, topK);
        }
        else
        {
            Mat pickup_class_ids = pickup_blob_numx6.colRange(new OpenCVRange(5, 6));

            if (class_ids_m == null || class_ids_m.rows() != num_pickup)
                class_ids_m = new Mat(num_pickup, 1, CvType.CV_32SC1);
            if (class_ids == null || class_ids.rows() != num_pickup)
                class_ids = new MatOfInt(class_ids_m);

            pickup_class_ids.convertTo(class_ids_m, CvType.CV_32S);

            // multi-class NMS
            Dnn.NMSBoxesBatched(boxes, confidences, class_ids, conf_threshold, nms_threshold, indices, 1f, topK);
        }

        Mat results = new Mat(indices.rows(), 6, CvType.CV_32FC1);

        for (int i = 0; i < indices.rows(); ++i)
        {
            int idx = (int)indices.get(i, 0)[0];

            pickup_blob_numx6.row(idx).copyTo(results.row(i));

            float[] bbox_arr = new float[4];
            pickup_box_delta.get(idx, 0, bbox_arr);
            float x = bbox_arr[0];
            float y = bbox_arr[1];
            float w = bbox_arr[2];
            float h = bbox_arr[3];
            results.put(i, 0, new float[] { x, y, x + w, y + h });
        }

        indices.Dispose();

        // [
        //   [xyxy, conf, cls]
        //   ...
        //   [xyxy, conf, cls]
        // ]
        return results;
    }

    // Pickups with optimized minMaxLoc times by recursive function.
    // 以下函数保留但不再使用，用于参考
    protected void searchAndPick(Mat scores, Mat box, Mat anchors, ref Mat dst, ref int index, int start_row, int end_row, int box_stride, float threshold = 0)
    {
        int stride = (end_row - start_row) / 2;
        for (int i = 0; i < 2; ++i)
        {
            int start = (i == 0) ? start_row : start_row + stride;
            int end = (i == 0) ? start_row + stride : end_row;
            if (check(scores, start, end, threshold))
            {
                if ((end - start) <= 50)
                {
                    pick(scores, box, anchors, ref dst, ref index, start, end, box_stride, threshold);
                }
                else
                {
                    searchAndPick(scores, box, anchors, ref dst, ref index, start, end, box_stride, threshold);
                }
            }
        }
    }

    protected bool check(Mat scores, int start_row, int end_row, float threshold = 0)
    {
        Mat cls_scores = scores.rowRange(start_row, end_row);
        Core.MinMaxLocResult minmax = Core.minMaxLoc(cls_scores);
        return ((float)minmax.maxVal > threshold);
    }

    protected void pick(Mat scores, Mat box, Mat anchors, ref Mat dst, ref int index, int start_row, int end_row, int box_stride, float threshold = 0)
    {
        for (int i = start_row; i < end_row; ++i)
        {
            Mat cls_scores = scores.row(i);
            Core.MinMaxLocResult minmax = Core.minMaxLoc(cls_scores);
            float conf = (float)minmax.maxVal;

            if (conf > threshold)
            {
                if (index > dst.rows())
                {
                    Mat _dst = new Mat(dst.rows() * 2, dst.cols(), dst.type(), new Scalar(0));
                    dst.copyTo(_dst.rowRange(0, pickup_blob_numx6.rows()));
                    dst = _dst;
                }

                Mat bbox_pred_row = box.row(i);
                float[] p_dot = new float[4];

                for (int p = 0; p < 4; p++)
                {
                    Mat bbox_pred_p = bbox_pred_row.colRange(p * 8, p * 8 + 8);
                    softmax(bbox_pred_p, bbox_pred_p);

                    p_dot[p] = (float)bbox_pred_p.dot(project);
                }

                p_dot[0] *= box_stride;
                p_dot[1] *= box_stride;
                p_dot[2] *= box_stride;
                p_dot[3] *= box_stride;

                // distance2bbox
                float[] anchor_arr = new float[2];
                anchors.get(i, 0, anchor_arr);
                float x1 = anchor_arr[0] - p_dot[0];
                float y1 = anchor_arr[1] - p_dot[1];
                float x2 = anchor_arr[0] + p_dot[2];
                float y2 = anchor_arr[1] + p_dot[3];
                if (input_size != null)
                {
                    x1 = Mathf.Clamp(x1, 0, (float)input_size.width);
                    y1 = Mathf.Clamp(y1, 0, (float)input_size.height);
                    x2 = Mathf.Clamp(x2, 0, (float)input_size.width);
                    y2 = Mathf.Clamp(y2, 0, (float)input_size.height);
                }

                dst.put(index, 0, new float[] { x1, y1, x2, y2, conf, (float)minmax.maxLoc.x });

                index++;
            }
        }
    }

    private void softmax(Mat src, Mat dst)
    {
        if (src == null)
            throw new ArgumentNullException("src");
        if (src != null)
            src.ThrowIfDisposed();

        if (dst == null)
            throw new ArgumentNullException("dst");
        if (dst != null)
            dst.ThrowIfDisposed();
        if (dst.rows() != src.rows() || dst.cols() != src.cols() || dst.type() != src.type())
            throw new ArgumentException("dst.rows() != src.rows() || dst.cols() != src.cols() || dst.type() != src.type()");

        // #x_exp = np.exp(x)
        // #x_sum = np.sum(x_exp, axis = axis, keepdims = True)
        // #s = x_exp / x_sum
        Core.exp(src, dst);
        Scalar sum = Core.sumElems(dst);
        Core.divide(dst, sum, dst);
    }

    
}
