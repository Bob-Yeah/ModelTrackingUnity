using OpenCVForUnity.CoreModule;
using OpenCVForUnity.ImgprocModule;
using UnityEngine;
using System.Collections.Generic;
using Newtonsoft.Json;
using System.IO;
using OpenCVForUnity.Features2dModule;

namespace ModelTracker
{
    [System.Serializable]
    public class ViewIndex
    {
        private Mat _uvIndex;
        private Mat _knnNbrs;
        private double _du, _dv;

        // 将List<Vector3>转换为OpenCV的Mat
        private Mat ConvertVector3ListToMat(List<Vector3> vectors)
        {
            Mat mat = new Mat(vectors.Count, 3, CvType.CV_32F);
            float[] data = new float[vectors.Count * 3];

            for (int i = 0; i < vectors.Count; i++)
            {
                data[i * 3] = vectors[i].x;
                data[i * 3 + 1] = vectors[i].y;
                data[i * 3 + 2] = vectors[i].z;
            }

            mat.put(0, 0, data);
            return mat;
        }

        // 方向向量转UV坐标
        public static void Dir2UV(Vector3 dir, out double u, out double v)
        {
            v = Mathf.Acos(dir[2]);
            u = Mathf.Atan2(dir[1], dir[0]);
            if (u < 0)
                u += 2 * Mathf.PI;
        }

        // UV坐标转方向向量
        public static Vector3 UV2Dir(double u, double v)
        {
            return new Vector3(
                (float)(System.Math.Cos((float)u) * System.Math.Sin((float)v)),
                (float)(System.Math.Sin((float)u) * System.Math.Sin((float)v)),
                (float)System.Math.Cos(v)
            );
        }

        // 获取KNN邻居
        public int[] GetKnn(int vi)
        {
            int[] result = new int[_knnNbrs.cols() - 1];
            // 这里简化实现，实际需要从Mat中提取数据
            return result;
        }

        // 获取K值
        public int GetK()
        {
            return _knnNbrs.cols() - 1;
        }

        // 构建视图索引
        public void Build(List<DView> views, int nU = 200, int nV = 100, int k = 5)
        {
            Debug.Log("views count: " + views.Count);
            List<Vector3> viewDirs = new List<Vector3>();
            for (int i = 0; i < views.Count; i++)
            {
                viewDirs.Add(views[i].viewDir);
            }
            // 将List<Vector3>转换为Mat
            Mat viewDirsMat = ConvertVector3ListToMat(viewDirs);       
            
            // 创建并训练DescriptorMatcher
            DescriptorMatcher matcher = DescriptorMatcher.create(DescriptorMatcher.FLANNBASED);
            // 第一次knn搜索：对viewDirs自身进行k+1近邻搜索
            List<MatOfDMatch> matches= new List<MatOfDMatch>();
            matcher.knnMatch(viewDirsMat, viewDirsMat, matches, k + 1);
            // for (int i = 0; i < matches.Count; i++)
            // {
            //     List<DMatch> debugList = matches[i].toList();
            //     for (int j = 0; j < debugList.Count; j++)
            //     {
            //         Debug.Log($"matches[{i}][{j}]: {debugList[j]}");
            //     }
            // }

            // 处理匹配结果，构建_knnNbrs
            Debug.Log("views building index _knnNbrs ...");
            _knnNbrs = new Mat(viewDirs.Count, k + 1, CvType.CV_32S);
            int[] indicesData = new int[viewDirs.Count * (k + 1)];
            
            for (int i = 0; i < viewDirs.Count; i++)
            {
                DMatch[] matchArray = matches[i].toArray();
                for (int j = 0; j < k + 1; j++)
                {
                    indicesData[i * (k + 1) + j] = matchArray[j].trainIdx;
                }
                
                // 验证第一个匹配是自身
                Debug.Assert(indicesData[i * (k + 1)] == i, "First match should be self");
            }
            
            _knnNbrs.put(0, 0, indicesData);
            
            // 第二次knn搜索：生成uv方向向量网格
            Mat uvDirMat = new Mat(nU * nV, 3, CvType.CV_32F);
            _du = 2 * Mathf.PI / (nU - 1);
            _dv = Mathf.PI / (nV - 1);
            
            float[] uvDirData = new float[nU * nV * 3];
            
            for (int vi = 0; vi < nV; vi++)
            {
                for (int ui = 0; ui < nU; ui++)
                {
                    double u = ui * _du;
                    double v = vi * _dv;
                    
                    Vector3 dir = UV2Dir(u, v);

                    int index = vi * nU + ui;
                    //Debug.Log($"index: {index}, u: {Mathf.Rad2Deg * (float)u}, v: {Mathf.Rad2Deg * (float)v}");
                    uvDirData[index * 3] = dir.x;
                    uvDirData[index * 3 + 1] = dir.y;
                    uvDirData[index * 3 + 2] = dir.z;
                }
            }

            uvDirMat.put(0, 0, uvDirData);
            
            // 执行1近邻搜索
            Debug.Log("views building index uvMatches ...");
            List<MatOfDMatch> uvMatches = new List<MatOfDMatch>();
            matcher.knnMatch(uvDirMat, viewDirsMat, uvMatches, 1);

            // for (int i = 0; i < uvMatches.Count; i++)
            // {
            //     List<DMatch> debugList = uvMatches[i].toList();
                
            //     for (int j = 0; j < debugList.Count; j++)
            //     { 
            //         Debug.Log($"uvMatches[{i}][{j}]: {debugList[j]}");
            //     }
            // }

            // 处理匹配结果，构建_uvIndex并reshape
            Mat indices = new Mat(nU * nV, 1, CvType.CV_32S);
            int[] uvIndicesData = new int[nU * nV];
            
            for (int i = 0; i < nU * nV; i++)
            {
                DMatch[] matchArray = uvMatches[i].toArray();
                uvIndicesData[i] = matchArray[0].trainIdx;
            }
            
            indices.put(0, 0, uvIndicesData);
            
            // Reshape to (nV, nU)
            _uvIndex = indices.reshape(1, nV);

            // 释放资源
            viewDirsMat.Dispose();
            foreach (var match in matches)
                match.Dispose();
            foreach (var match in uvMatches)
                match.Dispose();
            uvDirMat.Dispose();
            indices.Dispose();
        }

        // 获取指定方向的视图
        public int GetViewInDir(Vector3 dir)
        {
            double u, v;
            Dir2UV(dir, out u, out v);
            int ui = Mathf.RoundToInt((float)(u / _du));
            int vi = Mathf.RoundToInt((float)(v / _dv));

            int[] data = new int[1];
            _uvIndex.get(vi, ui, data);
            return data[0];
        }
    }
    
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
        public void Build()
        {
            // 简化实现
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
                LoadFromJson(jsonData);

                Debug.Log($"成功加载模板文件: {path}");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"加载模板文件失败: {e.Message}");
                Debug.LogError($"堆栈跟踪: {e.StackTrace}");
            }
        }
        
        // 从JSON字符串加载数据的方法，便于异步加载使用
        public void LoadFromJson(string jsonData)
        {
            try
            {
                if (string.IsNullOrEmpty(jsonData))
                {
                    Debug.LogError("JSON数据为空");
                    return;
                }

                // 配置JSON序列化设置
                JsonSerializerSettings settings = new JsonSerializerSettings
                {
                    ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                    Converters = new List<JsonConverter> { new Vector3Converter() }
                };

                Templates loadedData = JsonConvert.DeserializeObject<Templates>(jsonData, settings);

                // 恢复数据
                this.modelCenter = loadedData.modelCenter;
                this.views = loadedData.views ?? new List<DView>();

                // 重建ViewIndex
                if (this.views.Count > 0)
                {
                    this.viewIndex.Build(this.views);
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"从JSON字符串加载数据失败: {e.Message}");
                Debug.LogError($"堆栈跟踪: {e.StackTrace}");
                throw; // 重新抛出异常，让调用者知道发生了错误
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

                // 配置JSON序列化设置，使用自定义的Vector3转换器
                JsonSerializerSettings settings = new JsonSerializerSettings
                {
                    Formatting = Formatting.Indented,
                    ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                    Converters = new List<JsonConverter> { new Vector3Converter() }
                };

                // 序列化数据
                string jsonData = JsonConvert.SerializeObject(this, settings);
                File.WriteAllText(path, jsonData);

                Debug.Log($"成功保存模板文件: {path}");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"保存模板文件失败: {e.Message}");
                Debug.LogError($"堆栈跟踪: {e.StackTrace}");
            }
        }
        
        // Vector3自定义JSON转换器，解决序列化问题
        public class Vector3Converter : JsonConverter<Vector3>
        {
            public override void WriteJson(JsonWriter writer, Vector3 value, JsonSerializer serializer)
            {
                writer.WriteStartObject();
                writer.WritePropertyName("x");
                writer.WriteValue(value.x);
                writer.WritePropertyName("y");
                writer.WriteValue(value.y);
                writer.WritePropertyName("z");
                writer.WriteValue(value.z);
                writer.WriteEndObject();
            }

            public override Vector3 ReadJson(JsonReader reader, System.Type objectType, Vector3 existingValue, bool hasExistingValue, JsonSerializer serializer)
            {
                Vector3 result = new Vector3();
                
                // 确保读取到对象开始
                if (reader.TokenType != JsonToken.StartObject)
                {
                    reader.Skip();
                    return result;
                }
                
                // 读取对象属性
                while (reader.Read())
                {
                    if (reader.TokenType == JsonToken.EndObject)
                        break;
                    
                    if (reader.TokenType == JsonToken.PropertyName)
                    {
                        string propertyName = reader.Value.ToString();
                        reader.Read(); // 移动到属性值
                        
                        // 使用更健壮的方式转换值
                        float value = 0f;
                        try
                        {
                            if (reader.Value != null)
                            {
                                value = float.Parse(reader.Value.ToString(), System.Globalization.CultureInfo.InvariantCulture);
                            }
                        }
                        catch (System.Exception ex)
                        {
                            Debug.LogWarning($"无法将值 '{reader.Value}' 转换为float: {ex.Message}");
                        }
                        
                        switch (propertyName)
                        {
                            case "x":
                                result.x = value;
                                break;
                            case "y":
                                result.y = value;
                                break;
                            case "z":
                                result.z = value;
                                break;
                        }
                    }
                }
                
                return result;
            }
        }
    }
}