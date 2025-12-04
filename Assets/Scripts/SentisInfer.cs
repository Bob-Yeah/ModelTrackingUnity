using OpenCVForUnity.UnityUtils;
using OpenCVForUnity.UnityUtils.Helper;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.Sentis;
using UnityEngine;

public class SentisInfer : MonoBehaviour
{
    public ModelAsset modelAsset;
    //public Texture2D inputTexture;
    public TMPro.TextMeshProUGUI resultText;
    public RenderTexture resultRT;
    Model runtimeModel;
    Worker worker;
    public float[] results;
    WebCamTextureToMatHelper webCamTextureToMatHelper;
    Tensor<float> inputTensor;
    TextureTransform resizeTransform;
    TextureTransform getTransform;
    // Start is called before the first frame update
    void Start()
    {
        Model sourceModel = Unity.Sentis.ModelLoader.Load(modelAsset);
        // Create a functional graph that runs the input model and then applies softmax to the output.
        FunctionalGraph graph = new FunctionalGraph();
        FunctionalTensor[] inputs = graph.AddInputs(sourceModel);
        FunctionalTensor[] outputs = Functional.Forward(sourceModel, inputs);
        //FunctionalTensor softmax = Functional.Softmax(outputs[0]);
        // Create a model with softmax by compiling the functional graph.
        runtimeModel = graph.Compile(outputs);

        // Create input data as a tensor
        //using Tensor<float> _inputTensor = TextureConverter.ToTensor(inputTexture, width: 28, height: 28, channels: 1);
        //TextureConverter.RenderToTexture(_inputTensor, resultRT, new TextureTransform());

        // Create an engine
        worker = new Worker(runtimeModel, BackendType.GPUCompute);

        // Run the model with the input data
        //worker.Schedule(_inputTensor);

        // Get the result
        //Tensor<float> outputTensor = worker.PeekOutput() as Tensor<float>;

        // outputTensor is still pending
        // Either read back the results asynchronously or do a blocking download call
        //results = outputTensor.DownloadToArray();

        inputTensor = new Tensor<float>(new TensorShape(1, 3, 320, 320));
        resizeTransform = new TextureTransform().SetDimensions(320, 320, 3);
        getTransform = new TextureTransform().SetBroadcastChannels(false).SetDimensions(320,320,3);
        webCamTextureToMatHelper = gameObject.GetComponent<WebCamTextureToMatHelper>();
    }

    // Update is called once per frame
    void Update()
    {
        if (webCamTextureToMatHelper.IsPlaying() && webCamTextureToMatHelper.DidUpdateThisFrame())
        {
            // Create input data as a tensor

            //using Tensor inputTensor = TextureConverter.ToTensor(webCamTextureToMatHelper.GetWebCamTexture(), width: 28, height: 28, channels: 1);
            inputTensor = TextureConverter.ToTensor(webCamTextureToMatHelper.GetWebCamTexture());
            //TextureConverter.ToTensor(webCamTextureToMatHelper.GetWebCamTexture(), inputTensor, resizeTransform);
            //resultRT = TextureConverter.RenderToTexture(inputTensor);
            TextureConverter.RenderToTexture(inputTensor, resultRT, getTransform);
            Stopwatch sw = Stopwatch.StartNew();
            worker.Schedule(inputTensor);
            // Get the result
            Tensor<float> outputTensor = worker.PeekOutput() as Tensor<float>;
            sw.Stop();
            UnityEngine.Debug.Log($"推理时间: {sw.ElapsedMilliseconds:F2} ms");

            // outputTensor is still pending
            // Either read back the results asynchronously or do a blocking download call
            results = outputTensor.DownloadToArray();

            UnityEngine.Debug.Log("results.lenght:" + results.Length);
            //for (int i = 0; i < results.Length; i++)
            //{
            //    Debug.Log($"result[{i}]: {results[i]}");
            //}


            //int maxIdx = -1;
            //float maxProb = -1;
            //for (int i = 0; i < results.Length; i++)
            //{
            //    if (results[i] > maxProb)
            //    {
            //        maxIdx = i;
            //        maxProb = results[i];
            //    }

            //}

            ////Debug.Log($"result: {maxIdx}");
            //resultText.text = $"R:{maxIdx}";
        }

        

            
    }

    void OnDisable()
    {
        // Tell the GPU we're finished with the memory the engine used
        worker.Dispose();
    }
}
