using UnityEngine;
using System.Collections.Generic;
using OpenCVForUnity.CoreModule;
using Debug = UnityEngine.Debug;

public class TrackerUnityTest : MonoBehaviour
{
    void Start()
    {
        // 创建测试用的viewDirs数据
        List<Vector3> viewDirs = GenerateTestViewDirections();
        
        // 设置参数
        int k = 5; // 近邻数量
        int nU = 32; // u方向分辨率
        int nV = 16; // v方向分辨率
        
        // 创建并初始化TrackerUnity
        TrackerUnity tracker = new TrackerUnity();
        
        try
        {
            tracker.Initialize(viewDirs, k, nU, nV);
            Debug.Log("TrackerUnity initialization successful!");
            Debug.Log($"Test completed with {viewDirs.Count} view directions, k={k}, resolution={nU}x{nV}");
        }
        catch (System.Exception e)
        {
            Debug.LogError("Error during initialization: " + e.Message);
            Debug.LogError(e.StackTrace);
        }
    }
    
    // 生成测试用的视图方向数据
    private List<Vector3> GenerateTestViewDirections()
    {
        List<Vector3> directions = new List<Vector3>();
        
        // 生成一些基本方向
        directions.Add(Vector3.forward);
        directions.Add(Vector3.back);
        directions.Add(Vector3.left);
        directions.Add(Vector3.right);
        directions.Add(Vector3.up);
        directions.Add(Vector3.down);
        
        // 生成一些对角线方向
        directions.Add(new Vector3(1, 1, 1).normalized);
        directions.Add(new Vector3(1, 1, -1).normalized);
        directions.Add(new Vector3(1, -1, 1).normalized);
        directions.Add(new Vector3(1, -1, -1).normalized);
        directions.Add(new Vector3(-1, 1, 1).normalized);
        directions.Add(new Vector3(-1, 1, -1).normalized);
        directions.Add(new Vector3(-1, -1, 1).normalized);
        directions.Add(new Vector3(-1, -1, -1).normalized);
        
        return directions;
    }
}