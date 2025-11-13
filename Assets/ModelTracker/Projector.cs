using OpenCVForUnity.CoreModule;
using OpenCVForUnity.ImgprocModule;
using UnityEngine;
using System.Collections.Generic;

namespace ModelTracker
{
    public class Projector
    {
        private Matx33f _KR;
        private Vector3 _Kt;
        private Matx33f _KR_inv; // KR矩阵的逆矩阵，用于反投影
        
        // 构造函数
        public Projector(Matx33f K, Matx33f R, Vector3 t)
        {
            // 计算KR = K * R 和 Kt = K * t
            _KR = K * R;
            _Kt = K * t;
            
            // 计算KR的逆矩阵，用于反投影
            _KR_inv = _KR.inv();
        }
        
        // 将2D点和深度反投影到3D点的方法
        public Vector3 Unproject(float x, float y, float z)
        {
            // 首先将2D点转换为相机坐标系中的点
            // 1. 计算归一化设备坐标 (考虑深度)
            float x_camera = (x * z - _Kt[0]) / _KR[0, 0];
            float y_camera = (y * z - _Kt[1]) / _KR[1, 1];
            
            // 相机坐标系中的3D点
            Vector3 p_camera = new Vector3(x_camera, y_camera, z);
            
            // 2. 使用KR的逆矩阵将相机坐标系中的点转换为世界坐标系
            Vector3 P_world = _KR_inv * p_camera;
            
            return P_world;
        }
        
        // 将3D点投影到2D点的方法（对应C++的operator()重载）
        public Vector2 Project(Vector3 P)
        {
            // 计算 p = KR * P + Kt
            Vector3 p = _KR * P + _Kt;
            // 透视除法，返回2D点
            return new Vector2((float)(p[0] / p[2]), (float)(p[1] / p[2]));
        }
        
        // 将3D点列表投影到2D点列表的泛型方法（对应C++的模板方法）
        public List<Vector2> Project<_ValT>(List<_ValT> vP, System.Func<_ValT, Vector3> getPoint = null)
        {
            // 如果没有提供getPoint函数，使用默认的恒等转换
            if (getPoint == null)
            {
                getPoint = (v) => {
                    // 尝试直接转换，如果类型不匹配可能会抛出异常
                    return (Vector3)(object)v;
                };
            }
            
            List<Vector2> vp = new List<Vector2>(vP.Count);
            foreach (var item in vP)
            {
                vp.Add(Project(getPoint(item)));
            }
            return vp;
        }
        
        // 重载方法，支持数组输入
        public List<Vector2> Project<_ValT>(_ValT[] vP, System.Func<_ValT, Vector3> getPoint = null)
        {
            return Project(new List<_ValT>(vP), getPoint);
        }
    }
}
