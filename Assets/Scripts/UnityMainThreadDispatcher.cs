using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 主线程调度器，用于在后台线程和主线程之间进行安全的代码调度
/// </summary>
public class UnityMainThreadDispatcher : MonoBehaviour
{
    private static UnityMainThreadDispatcher _instance;
    private Queue<Action> _executionQueue = new Queue<Action>();

    /// <summary>
    /// 获取调度器实例
    /// </summary>
    /// <returns>主线程调度器实例</returns>
    public static UnityMainThreadDispatcher Instance()
    {
        if (_instance == null)
        {
            // 尝试查找已存在的实例
            _instance = FindObjectOfType<UnityMainThreadDispatcher>();
            
            // 如果没有找到，创建一个新的GameObject并添加此组件
            if (_instance == null)
            {
                GameObject go = new GameObject("MainThreadDispatcher");
                _instance = go.AddComponent<UnityMainThreadDispatcher>();
                DontDestroyOnLoad(go);
            }
        }
        return _instance;
    }

    private void Update()
    {
        // 每帧处理队列中的所有操作
        lock (_executionQueue)
        {
            while (_executionQueue.Count > 0)
            {
                _executionQueue.Dequeue().Invoke();
            }
        }
    }

    /// <summary>
    /// 将操作加入主线程执行队列
    /// </summary>
    /// <param name="action">要在主线程中执行的操作</param>
    public void Enqueue(Action action)
    {
        lock (_executionQueue)
        {
            _executionQueue.Enqueue(action);
        }
    }
}