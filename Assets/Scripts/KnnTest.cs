using UnityEngine;
using System.Collections.Generic;
using OpenCVForUnity.CoreModule;
using OpenCVForUnity.Features2dModule;
using OpenCVForUnity.ImgprocModule;
using OpenCVForUnity.UnityUtils;
using Debug = UnityEngine.Debug;

public class TrackerUnity
{
    // 成员变量
    private Mat _knnNbrs;
    private Mat _uvIndex;
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
    
    // uv坐标转换为方向向量
    private Vector3 uv2dir(double u, double v)
    {
        double x = Mathf.Sin((float)v) * Mathf.Cos((float)u);
        double y = Mathf.Sin((float)v) * Mathf.Sin((float)u);
        double z = Mathf.Cos((float)v);
        return new Vector3((float)x, (float)y, (float)z);
    }
    
    // 主要功能方法，对应原始C++代码
    public void Initialize(List<Vector3> viewDirs, int k, int nU, int nV)
    {
        Debug.Log("viewDirs count: " + viewDirs.Count);
        for (int i = 0; i < viewDirs.Count; i++)
        {
            Debug.Log($"viewDirs[{i}]: {viewDirs[i]}");
        }
        // 将List<Vector3>转换为Mat
        Mat viewDirsMat = ConvertVector3ListToMat(viewDirs);
        
        // 创建并训练DescriptorMatcher
        DescriptorMatcher matcher = DescriptorMatcher.create(DescriptorMatcher.FLANNBASED);
        //matcher.add(viewDirsMat);
        //matcher.train();
        
        // 第一次knn搜索：对viewDirs自身进行k+1近邻搜索
        List<MatOfDMatch> matches= new List<MatOfDMatch>();
        matcher.knnMatch(viewDirsMat, viewDirsMat, matches, k + 1);
        for (int i = 0; i < matches.Count; i++)
        {
            List<DMatch> debugList = matches[i].toList();
            for (int j = 0; j < debugList.Count; j++)
            {
                Debug.Log($"matches[{i}][{j}]: {debugList[j]}");
            }
        }

        // 处理匹配结果，构建_knnNbrs
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
                Vector3 dir = uv2dir(u, v);
                
                int index = vi * nU + ui;
                uvDirData[index * 3] = dir.x;
                uvDirData[index * 3 + 1] = dir.y;
                uvDirData[index * 3 + 2] = dir.z;
            }
        }
        
        uvDirMat.put(0, 0, uvDirData);
        
        // 执行1近邻搜索
        List<MatOfDMatch> uvMatches = new List<MatOfDMatch>();
        matcher.knnMatch(uvDirMat, viewDirsMat, uvMatches, 1);

        for (int i = 0; i < uvMatches.Count; i++)
        {
            List<DMatch> debugList = uvMatches[i].toList();
            
            for (int j = 0; j < debugList.Count; j++)
            { 
                Debug.Log($"uvMatches[{i}][{j}]: {debugList[j]}");
            }
        }

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
}