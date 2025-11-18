using OpenCVForUnity.CoreModule;
using OpenCVForUnity.Calib3dModule;
using OpenCVForUnity.ImgprocModule;
using OpenCVForUnity.UtilsModule;
using UnityEngine;
using System.Collections.Generic;
using System;


namespace ModelTracker
{


    [System.Serializable]
    public class DView
    {
        public Vector3 viewDir;
        public Matx33f R;
        public Vector3 t;
        public List<CPoint> contourPoints3d = new List<CPoint>();
    }

    [System.Serializable]
    public class ViewIndex
    {
        private Mat _uvIndex;
        private Mat _knnNbrs;
        private double _du, _dv;

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
                (float)(Math.Cos(u) * Math.Sin(v)),
                (float)(Math.Sin(u) * Math.Sin(v)),
                (float)Math.Cos(v)
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
            // 简化实现，实际需要构建FLANN索引
            _du = 2 * Mathf.PI / (nU - 1);
            _dv = Mathf.PI / (nV - 1);
            _uvIndex = Mat.zeros(nV, nU, CvType.CV_32S);
            _knnNbrs = Mat.zeros(views.Count, k + 1, CvType.CV_32S);
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
    
    // 3x3矩阵类，使用float类型
    [System.Serializable]
    public class Matx33f
    {
        public float[] val;

        // 默认构造函数，初始化为单位矩阵
        public Matx33f()
        {
            val = new float[9];
            // 初始化为单位矩阵
            val[0] = 1; val[1] = 0; val[2] = 0;
            val[3] = 0; val[4] = 1; val[5] = 0;
            val[6] = 0; val[7] = 0; val[8] = 1;
        }

        // 使用指定值初始化
        public Matx33f(float m00, float m01, float m02,
                     float m10, float m11, float m12,
                     float m20, float m21, float m22)
        {
            val = new float[9] {
                m00, m01, m02,
                m10, m11, m12,
                m20, m21, m22
            };
        }

        // 从OpenCV的Mat创建
        public Matx33f(Mat mat)
        {
            val = new float[9];
            if (mat.rows() == 3 && mat.cols() == 3)
            {
                for (int i = 0; i < 3; i++)
                {
                    for (int j = 0; j < 3; j++)
                    {
                        val[i * 3 + j] = (float)mat.get(i, j)[0];
                    }
                }
            }
            else
            {
                // 如果尺寸不匹配，初始化为单位矩阵
                val[0] = 1; val[1] = 0; val[2] = 0;
                val[3] = 0; val[4] = 1; val[5] = 0;
                val[6] = 0; val[7] = 0; val[8] = 1;
            }
        }

        // 从旋转向量创建（Rodrigues变换）
        public Matx33f(Vector3 rotVec)
        {
            val = new float[9];
            // 实现Rodrigues变换
            float angle = rotVec.magnitude;
            if (angle > 0)
            {
                Vector3 axis = rotVec.normalized;
                float c = Mathf.Cos(angle);
                float s = Mathf.Sin(angle);
                float t = 1 - c;
                
                val[0] = t * axis[0] * axis[0] + c;
                val[1] = t * axis[0] * axis[1] - s * axis[2];
                val[2] = t * axis[0] * axis[2] + s * axis[1];
                
                val[3] = t * axis[1] * axis[0] + s * axis[2];
                val[4] = t * axis[1] * axis[1] + c;
                val[5] = t * axis[1] * axis[2] - s * axis[0];
                
                val[6] = t * axis[2] * axis[0] - s * axis[1];
                val[7] = t * axis[2] * axis[1] + s * axis[0];
                val[8] = t * axis[2] * axis[2] + c;
            }
            else
            {
                // 如果角度为0，初始化为单位矩阵
                val[0] = 1; val[1] = 0; val[2] = 0;
                val[3] = 0; val[4] = 1; val[5] = 0;
                val[6] = 0; val[7] = 0; val[8] = 1;
            }
        }

        // 转换为OpenCV的Mat
        public Mat ToMat()
        {
            Mat mat = new Mat(3, 3, CvType.CV_32FC1);
            for (int i = 0; i < 3; i++)
            {
                for (int j = 0; j < 3; j++)
                {
                    mat.put(i, j, val[i * 3 + j]);
                }
            }
            return mat;
        }

        // 克隆方法
        public Matx33f clone()
        {
            return new Matx33f(
                val[0], val[1], val[2],
                val[3], val[4], val[5],
                val[6], val[7], val[8]
            );
        }

        // 获取元素访问器
        public float this[int row, int col]
        {
            get
            {
                if (row >= 0 && row < 3 && col >= 0 && col < 3)
                    return val[row * 3 + col];
                throw new System.IndexOutOfRangeException();
            }
            set
            {
                if (row >= 0 && row < 3 && col >= 0 && col < 3)
                    val[row * 3 + col] = value;
                else
                    throw new System.IndexOutOfRangeException();
            }
        }

        // 矩阵乘法
        public static Matx33f operator *(Matx33f a, Matx33f b)
        {
            Matx33f result = new Matx33f();
            
            for (int i = 0; i < 3; i++)
            {
                for (int j = 0; j < 3; j++)
                {
                    result.val[i * 3 + j] = 0;
                    for (int k = 0; k < 3; k++)
                    {
                        result.val[i * 3 + j] += a.val[i * 3 + k] * b.val[k * 3 + j];
                    }
                }
            }
            
            return result;
        }

        // 矩阵与向量乘法
        public static Vector3 operator *(Matx33f m, Vector3 v)
        {
            Vector3 result = new Vector3();
            
            result[0] = m.val[0] * v[0] + m.val[1] * v[1] + m.val[2] * v[2];
            result[1] = m.val[3] * v[0] + m.val[4] * v[1] + m.val[5] * v[2];
            result[2] = m.val[6] * v[0] + m.val[7] * v[1] + m.val[8] * v[2];
            
            return result;
        }

        // 矩阵数乘
        public static Matx33f operator *(Matx33f m, float scalar)
        {
            Matx33f result = new Matx33f();
            for (int i = 0; i < 9; i++)
            {
                result.val[i] = m.val[i] * scalar;
            }
            return result;
        }

        // 转置矩阵
        public Matx33f t()
        {
            return new Matx33f(
                val[0], val[3], val[6],
                val[1], val[4], val[7],
                val[2], val[5], val[8]
            );
        }

        // 获取行列式
        public float det()
        {
            // 按第一行展开法计算行列式
            float a11 = val[0], a12 = val[1], a13 = val[2];
            float a21 = val[3], a22 = val[4], a23 = val[5];
            float a31 = val[6], a32 = val[7], a33 = val[8];

            // 计算各余子式
            float M11 = a22 * a33 - a23 * a32;  // 去掉第一行第一列后的2x2行列式
            float M12 = a21 * a33 - a23 * a31;  // 去掉第一行第二列后的2x2行列式
            float M13 = a21 * a32 - a22 * a31;  // 去掉第一行第三列后的2x2行列式

            // 按第一行展开计算行列式（符号：+M11 -M12 +M13）
            return a11 * M11 - a12 * M12 + a13 * M13;
        }
        
        // 计算矩阵的逆
        public Matx33f inv()
        {
            float determinant = det();
            //Debug.Log("行列式：" + determinant);
            
            // 检查矩阵是否可逆（行列式不为零）
            if (Mathf.Abs(determinant) < 1e-6f)
            {
                // 如果矩阵不可逆，返回单位矩阵作为默认值
                Debug.LogWarning("Matrix is singular and cannot be inverted, returning identity matrix");
                return new Matx33f(); // 单位矩阵
            }
            
            // 计算伴随矩阵
            Matx33f adjugate = new Matx33f();

            // 计算代数余子式
            adjugate.val[0] = (+1) * (val[4] * val[8] - val[5] * val[7]);
            adjugate.val[1] = (-1) * (val[3] * val[8] - val[5] * val[6]);
            adjugate.val[2] = (+1) * (val[3] * val[7] - val[4] * val[6]);

            adjugate.val[3] = (-1) * (val[1] * val[8] - val[2] * val[7]);
            adjugate.val[4] = (+1) * (val[0] * val[8] - val[2] * val[6]);
            adjugate.val[5] = (-1) * (val[0] * val[7] - val[1] * val[6]);

            adjugate.val[6] = (+1) * (val[1] * val[5] - val[2] * val[4]);
            adjugate.val[7] = (-1) * (val[0] * val[5] - val[2] * val[3]);
            adjugate.val[8] = (+1) * (val[0] * val[4] - val[1] * val[3]);

            // 转置伴随矩阵
            adjugate = adjugate.t();
            
            // 将伴随矩阵除以行列式得到逆矩阵
            return adjugate * (1.0f / determinant);
        }

        // 字符串表示
        public override string ToString()
        {
            return string.Format(
                "[{0}, {1}, {2}]",
                string.Format("[{0}, {1}, {2}]", val[0], val[1], val[2]),
                string.Format("[{0}, {1}, {2}]", val[3], val[4], val[5]),
                string.Format("[{0}, {1}, {2}]", val[6], val[7], val[8])
            );
        }
    }

    // 2x3矩阵类，使用float类型
    [System.Serializable]
    public class Matx23f
    {
        public float[] val;

        // 默认构造函数，初始化为单位矩阵形式
        public Matx23f()
        {
            val = new float[6];
            // 初始化为单位矩阵形式（前两列是单位矩阵，最后一列是平移）
            val[0] = 1; val[1] = 0; val[2] = 0;
            val[3] = 0; val[4] = 1; val[5] = 0;
        }

        // 使用指定值初始化
        public Matx23f(float m00, float m01, float m02,
                     float m10, float m11, float m12)
        {
            val = new float[6] {
                m00, m01, m02,
                m10, m11, m12
            };
        }

        // 从OpenCV的Mat创建
        public Matx23f(Mat mat)
        {
            val = new float[6];
            if (mat.rows() == 2 && mat.cols() == 3)
            {
                for (int i = 0; i < 2; i++)
                {
                    for (int j = 0; j < 3; j++)
                    {
                        val[i * 3 + j] = (float)mat.get(i, j)[0];
                    }
                }
            }
            else
            {
                // 如果尺寸不匹配，初始化为单位矩阵形式
                val[0] = 1; val[1] = 0; val[2] = 0;
                val[3] = 0; val[4] = 1; val[5] = 0;
            }
        }

        // 转换为OpenCV的Mat
        public Mat ToMat()
        {
            Mat mat = new Mat(2, 3, CvType.CV_32FC1);
            for (int i = 0; i < 2; i++)
            {
                for (int j = 0; j < 3; j++)
                {
                    mat.put(i, j, val[i * 3 + j]);
                }
            }
            return mat;
        }

        // 获取元素访问器
        public float get(int row, int col)
        {
            if (row >= 0 && row < 2 && col >= 0 && col < 3)
                return val[row * 3 + col];
            throw new System.IndexOutOfRangeException();
        }

        // 设置元素值
        public void set(int row, int col, float value)
        {
            if (row >= 0 && row < 2 && col >= 0 && col < 3)
                val[row * 3 + col] = value;
            else
                throw new System.IndexOutOfRangeException();
        }

        // 矩阵乘法（2x3矩阵乘以2x3矩阵，用于仿射变换的组合）
        public static Matx23f operator *(Matx23f a, Matx23f b)
        {
            Matx23f result = new Matx23f();
            
            // 计算前两列（线性变换部分）
            for (int i = 0; i < 2; i++)
            {
                for (int j = 0; j < 2; j++)
                {
                    result.val[i * 3 + j] = 0;
                    for (int k = 0; k < 2; k++)
                    {
                        result.val[i * 3 + j] += a.val[i * 3 + k] * b.val[k * 3 + j];
                    }
                }
                
                // 计算第三列（平移部分）
                result.val[i * 3 + 2] = a.val[i * 3 + 0] * b.val[0 * 3 + 2] + 
                                      a.val[i * 3 + 1] * b.val[1 * 3 + 2] + 
                                      a.val[i * 3 + 2];
            }
            
            return result;
        }

        // 矩阵与点乘法（应用仿射变换）
        public static Vector2 operator *(Matx23f m, Vector2 v)
        {
            Vector2 result = new Vector2();
            
            result.x = m.val[0] * v.x + m.val[1] * v.y + m.val[2];
            result.y = m.val[3] * v.x + m.val[4] * v.y + m.val[5];
            
            return result;
        }

        // 矩阵数乘
        public static Matx23f operator *(Matx23f m, float scalar)
        {
            Matx23f result = new Matx23f();
            for (int i = 0; i < 6; i++)
            {
                result.val[i] = m.val[i] * scalar;
            }
            return result;
        }

        // 字符串表示
        public override string ToString()
        {
            return string.Format(
                "[{0}, {1}, {2}]",
                string.Format("[{0}, {1}, {2}]", val[0], val[1], val[2]),
                string.Format("[{0}, {1}, {2}]", val[3], val[4], val[5])
            );
        }
    }

    // 基础数据结构定义
    public struct Pose
    {
        public Matx33f R;  // 旋转矩阵
        public Vector3 t;    // 平移向量

        // 从C++的赋值运算符转换而来
        public static Pose FromPose(Pose pose)
        {
            return new Pose { R = pose.R.clone(), t = pose.t };
        }
    }
    [System.Serializable]
    public struct CPoint
    {
        public Vector3 center;
        public Vector3 normal_offset;
    }

}

