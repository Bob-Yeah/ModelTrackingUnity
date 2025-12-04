using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Sentis;
using OpenCVForUnity.UnityUtils;
using OpenCVForUnity.UnityUtils.Helper;

public class SentisInfer : MonoBehaviour
{
    public ModelAsset modelAsset;
    public Texture2D inputTexture;
    public TMPro.TextMeshProUGUI resultText;
    public RenderTexture resultRT;
    Model runtimeModel;
    Worker worker;
    public float[] results;
    WebCamTextureToMatHelper webCamTextureToMatHelper;
    Tensor<float> inputTensor;
    TextureTransform resizeTransform;
    // Start is called before the first frame update
    void Start()
    {
        Model sourceModel = Unity.Sentis.ModelLoader.Load(modelAsset);
        // Create a functional graph that runs the input model and then applies softmax to the output.
        FunctionalGraph graph = new FunctionalGraph();
        FunctionalTensor[] inputs = graph.AddInputs(sourceModel);
        FunctionalTensor[] outputs = Functional.Forward(sourceModel, inputs);
        FunctionalTensor softmax = Functional.Softmax(outputs[0]);
        // Create a model with softmax by compiling the functional graph.
        runtimeModel = graph.Compile(softmax);

        // Create input data as a tensor
        using Tensor<float> _inputTensor = TextureConverter.ToTensor(inputTexture, width: 28, height: 28, channels: 1);
        TextureConverter.RenderToTexture(_inputTensor, resultRT, new TextureTransform());

        // Create an engine
        worker = new Worker(runtimeModel, BackendType.GPUCompute);

        // Run the model with the input data
        worker.Schedule(_inputTensor);

        // Get the result
        Tensor<float> outputTensor = worker.PeekOutput() as Tensor<float>;

        // outputTensor is still pending
        // Either read back the results asynchronously or do a blocking download call
        results = outputTensor.DownloadToArray();

        inputTensor = new Tensor<float>(new TensorShape(1, 1, 28, 28));
        resizeTransform = new TextureTransform().SetDimensions(28, 28, 1);
        webCamTextureToMatHelper = gameObject.GetComponent<WebCamTextureToMatHelper>();
    }

    // Update is called once per frame
    void Update()
    {
        if (webCamTextureToMatHelper.IsPlaying() && webCamTextureToMatHelper.DidUpdateThisFrame())
        {
            // Create input data as a tensor

            //using Tensor inputTensor = TextureConverter.ToTensor(webCamTextureToMatHelper.GetWebCamTexture(), width: 28, height: 28, channels: 1);

            TextureConverter.ToTensor(webCamTextureToMatHelper.GetWebCamTexture(), inputTensor, resizeTransform);
            

            worker.Schedule(inputTensor);

            // Get the result
            Tensor<float> outputTensor = worker.PeekOutput() as Tensor<float>;

            // outputTensor is still pending
            // Either read back the results asynchronously or do a blocking download call
            results = outputTensor.DownloadToArray();

            

            int maxIdx = -1;
            float maxProb = -1;
            for (int i = 0; i < results.Length; i++)
            {
                if (results[i] > maxProb)
                {
                    maxIdx = i;
                    maxProb = results[i];
                }
               
            }

            //Debug.Log($"result: {maxIdx}");
            resultText.text = $"R:{maxIdx}";
        }

        

            
    }

    void OnDisable()
    {
        // Tell the GPU we're finished with the memory the engine used
        worker.Dispose();
    }
}
