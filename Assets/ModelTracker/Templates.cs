using OpenCVForUnity.CoreModule;
using OpenCVForUnity.ImgprocModule;
using UnityEngine;
using System.Collections.Generic;
using Newtonsoft.Json;
using System.IO;

namespace ModelTracker
{
    // 模板类，包含3D模型的多视图表示
    [System.Serializable]
    public class Templates
    {
        public Vector3 modelCenter;
        public List<DView> views = new List<DView>();
        [JsonIgnore] // ViewIndex包含Mat对象，无法直接序列化，需要特殊处理
        public ViewIndex viewIndex = new ViewIndex();

        // Build函数，创作不同视角的模板
        // 可以利用Unity的其他功能进行离线的制作
        // 这里不做实现，需要进行额外的加载，这里只做模板文件所需内容的分析
        public void Build(List<DView> views, Vector3 modelCenter)
        {
            // 简化实现
            this.views = new List<DView>(views);
            viewIndex.Build(this.views);
        }

        // load函数，加载模板文件
        public void Load(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    Debug.LogError($"模板文件不存在: {path}");
                    return;
                }

                string jsonData = File.ReadAllText(path);
                Templates loadedData = JsonConvert.DeserializeObject<Templates>(jsonData);

                // 恢复数据
                this.modelCenter = loadedData.modelCenter;
                this.views = loadedData.views ?? new List<DView>();

                // 重建ViewIndex
                if (this.views.Count > 0)
                {
                    this.viewIndex.Build(this.views);
                }

                Debug.Log($"成功加载模板文件: {path}");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"加载模板文件失败: {e.Message}");
            }
        }

        // save函数，保存模板文件
        public void Save(string path)
        {
            try
            {
                // 确保目录存在
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // 序列化数据
                string jsonData = JsonConvert.SerializeObject(this, Formatting.Indented);
                File.WriteAllText(path, jsonData);

                Debug.Log($"成功保存模板文件: {path}");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"保存模板文件失败: {e.Message}");
            }
        }
    }
}