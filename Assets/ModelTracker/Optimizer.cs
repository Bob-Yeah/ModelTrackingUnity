using OpenCVForUnity.CoreModule;
using OpenCVForUnity.Calib3dModule;
using OpenCVForUnity.ImgprocModule;
using OpenCVForUnity.UtilsModule;
using UnityEngine;
using System.Collections.Generic;
using System;
using System.Threading.Tasks;

namespace ModelTracker
{
    public class Optimizer
    {
        public struct ContourPoint
        {
            public float w; //weight
            public float x; //position on the scan-line
        };

        const int MAX_POINTS_PER_LINE = 3;

        // 扫描线横线单线，y是y方向上的坐标
        public struct ScanLine
        {
            public float y;
            public Vector2 xdir;
            public Vector2 xstart;
            public List<ContourPoint> vPoints;
            public int nPoints;
            public List<short> cpIndex;

            public void setCoordinates(Vector2 start, Vector2 end, float y)
            {
                xstart = start;
                xdir = (end - start).normalized;
                this.y = y;
            }

            public int GetClosestContourPoint(Vector2 pt, int xsize)
            {
                int x = (int)(Vector2.Dot(pt - xstart, xdir) + 0.5f);
                if ((uint)x < (uint)(xsize))
                {
                    return cpIndex[x];
                }
                return -1;
            }
        }

        // 方向数据，多条横扫描线组成的
        public struct DirData
        {
            public Vector2 dir; // 方向向量
            public Vector2 ystart; // 起始点
            public Vector2 ydir; // 方向增量
            public List<ScanLine> lines; // 扫描线列表
            public Mat _cpIndexBuf; // 索引缓冲区

            public void setCoordinates(Vector2 ystart, Vector2 ypt)
            {
                this.ystart = ystart;
                ydir = (ypt - ystart).normalized;
            }

            public void resize(int rows, int cols)
            {
                lines.Clear();
                _cpIndexBuf = new Mat(rows, cols, CvType.CV_16SC1);
                for (int y = 0; y < rows; y++)
                {
                    lines.Add(new ScanLine());
                    // lines[y].cpIndex =
                }

            }
        }

        public List<DirData> _dirs;
        OpenCVForUnity.CoreModule.Rect _roi;

        // x位置值的加权平均
        static void _gaussianFitting(float[] data, int size, ref ContourPoint cp)
        {
            float w = 0.0f;
            float wsum = 0.0f;
            for (int i = 0; i < size; i++)
            {
                wsum += data[i] * (float)(i);
                w += data[i];
            }
            cp.x = wsum / w;
        }

        public struct LocalMaxima
        {
            public int x;
            public float val;
        }

        public class _LineBuilder
        {
            public List<LocalMaxima> _lmBuf;
            public int _bufSize;
            public _LineBuilder(int bufSize)
            {
                _bufSize = bufSize;
                _lmBuf = new List<LocalMaxima>();
            }

            // 由于C#不直接支持operator()重载，使用Execute方法模拟括号操作符
            public void Execute(ref ScanLine line, float[] data, int size, int gaussWindowSizeHalf)
            {
                _lmBuf.Clear();
                int nlm = 0;

                // 查找局部最大值
                for (int i = 1; i < size - 1; ++i)
                {
                    if (data[i] > data[i - 1] && data[i] > data[i + 1])
                    {
                        _lmBuf.Add(new LocalMaxima { x = i, val = data[i] });
                        nlm++;
                    }
                }

                // 如果找到的局部最大值超过限制，按值排序并取前MAX_POINTS_PER_LINE个
                if (nlm > MAX_POINTS_PER_LINE)
                {
                    // 按值降序排序
                    _lmBuf.Sort((a, b) => b.val.CompareTo(a.val));
                    // 移除多余的元素
                    while (_lmBuf.Count > MAX_POINTS_PER_LINE)
                    {
                        _lmBuf.RemoveAt(_lmBuf.Count - 1);
                    }
                    nlm = MAX_POINTS_PER_LINE;

                    // 再按x坐标升序排序
                    _lmBuf.Sort((a, b) => a.x.CompareTo(b.x));
                }

                // 确保vPoints有足够的容量
                if (line.vPoints == null)
                {
                    line.vPoints = new List<ContourPoint>();
                }

                while (line.vPoints.Count < nlm)
                {
                    line.vPoints.Add(new ContourPoint());
                }

                // 对每个局部最大值进行高斯拟合
                for (int i = 0; i < nlm; ++i)
                {
                    LocalMaxima lm = _lmBuf[i];
                    ContourPoint cp = line.vPoints[i];

                    int start = Math.Max(0, lm.x - gaussWindowSizeHalf);
                    int end = Math.Min(size, lm.x + gaussWindowSizeHalf);

                    // 为_gaussianFitting创建子数组
                    float[] subData = new float[end - start];
                    Array.Copy(data, start, subData, 0, subData.Length);

                    _gaussianFitting(subData, subData.Length, ref cp);

                    cp.x += start;
                    cp.w = lm.val;

                    line.vPoints[i] = cp;
                }

                line.nPoints = nlm;

                // 初始化cpIndex数组
                if (line.cpIndex == null)
                {
                    line.cpIndex = new List<short>(size);
                    for (int i = 0; i < size; i++)
                    {
                        line.cpIndex.Add(0);
                    }
                }
                else
                {
                    while (line.cpIndex.Count < size)
                    {
                        line.cpIndex.Add(0);
                    }
                }

                // 设置cpIndex映射
                if (nlm <= 1)
                {
                    short fillValue = (nlm == 0) ? (short)0xFF : (short)0;
                    for (int i = 0; i < size; i++)
                    {
                        line.cpIndex[i] = fillValue;
                    }
                }
                else
                {
                    int startIndex = 0;
                    for (int pi = 0; pi < nlm - 1; ++pi)
                    {
                        // 计算两个点之间的中间位置
                        int endIndex = (int)((line.vPoints[pi].x + line.vPoints[pi + 1].x) / 2 + 0.5f) + 1;
                        endIndex = Math.Min(endIndex, size);

                        // 填充当前区间
                        for (int i = startIndex; i < endIndex; ++i)
                        {
                            line.cpIndex[i] = (short)pi;
                        }

                        startIndex = endIndex;
                    }

                    // 填充最后一个区间
                    for (int i = startIndex; i < size; ++i)
                    {
                        line.cpIndex[i] = (short)(nlm - 1);
                    }
                }
            }

        }

        public List<int> _dirIndex = new List<int>();

        public void computeScanLines(Mat prob_, OpenCVForUnity.CoreModule.Rect roi)
        {
            const int N = 8;

            // 定义中心点
            Vector2 center = new Vector2((float)(roi.x + roi.width / 2), (float)(roi.y + roi.height / 2));
            
            // 定义矩形和角落点
            List<Vector2> corners = new List<Vector2> {
                new Vector2((float)roi.x, (float)roi.y),
                new Vector2((float)(roi.x + roi.width), (float)roi.y),
                new Vector2((float)(roi.x + roi.width), (float)(roi.y + roi.height)),
                new Vector2((float)roi.x, (float)(roi.y + roi.height))
            };

            // 重置并调整_dirs列表大小
            if (_dirs == null)
                _dirs = new List<DirData>();
            _dirs.Clear();
            for (int i = 0; i < N * 2; i++)
            {
                _dirs.Add(new DirData());
            }

            // 使用C#的Parallel.For进行并行处理
            System.Threading.Tasks.Parallel.For(0, N, i =>
            {
                double theta = 180.0 / N * i;
                
                // 获取旋转矩阵
                Vector2 opencvCenter = new Vector2(center.x, center.y);
                Mat rotationMatrix = Imgproc.getRotationMatrix2D(new Point(opencvCenter.x, opencvCenter.y), theta, 1.0);
                Matx23f A = new Matx23f(
                    (float)rotationMatrix.get(0, 0)[0], (float)rotationMatrix.get(0, 1)[0], (float)rotationMatrix.get(0, 2)[0],
                    (float)rotationMatrix.get(1, 0)[0], (float)rotationMatrix.get(1, 1)[0], (float)rotationMatrix.get(1, 2)[0]
                );
                
                // 转换角点
                // 将Vector2列表转换为Point2f数组
                Point[] cornersArray = new Point[corners.Count];
                for (int j = 0; j < corners.Count; j++)
                {
                    cornersArray[j] = new Point(corners[j].x, corners[j].y);
                }
                
                // 创建输出数组
                Point[] AcornersArray = new Point[corners.Count];
                Core.transform(new MatOfPoint2f(cornersArray), new MatOfPoint2f(AcornersArray), rotationMatrix);
                
                // 将Point2f数组转换回Vector2列表
                List<Vector2> Acorners = new List<Vector2>();
                foreach (var p in AcornersArray)
                {
                    Acorners.Add(new Vector2((float)p.x, (float)p.y));
                }
                
                // 获取边界框
                OpenCVForUnity.CoreModule.Rect droi = getBoundingBox2D(Acorners);
                
                // 更新变换矩阵
                Matx23f translationMatrix = new Matx23f(1, 0, -droi.x, 0, 1, -droi.y);
                A = translationMatrix * A;
                
                // 应用仿射变换
                Mat dirProb = new Mat();
                Imgproc.warpAffine(prob_, dirProb, A.ToMat(), droi.size(), Imgproc.INTER_LINEAR, Core.BORDER_CONSTANT, new Scalar(0));
                
                // 计算方向向量
                theta = theta / 180.0 * Math.PI;
                Vector2 dir = new Vector2((float)Math.Cos(theta), (float)Math.Sin(theta));
                
                // 处理方向数据（注意线程安全问题）
                lock (_dirs) // 使用锁确保线程安全
                {
                    DirData positiveDir = _dirs[i];
                    DirData negativeDir = _dirs[i + N];
                    
                    // 这里需要实现invertAffine和_calcScanLinesForRows方法
                    // 假设这些方法已经存在于类中
                    Matx23f invA = invertAffine(A);
                    _calcScanLinesForRows(dirProb, ref positiveDir, ref negativeDir, invA);
                    
                    // 设置方向向量
                    positiveDir.dir = dir;
                    negativeDir.dir = -dir;
                    
                    // 更新_dirs中的数据
                    _dirs[i] = positiveDir;
                    _dirs[i + N] = negativeDir;
                }
            });

            // 归一化轮廓点权重
            float wMax = 0;
            foreach (var dir in _dirs)
            {
                if (dir.lines != null)
                {
                    foreach (var dirLine in dir.lines)
                    {
                        if (dirLine.vPoints != null)
                        {
                            for (int i = 0; i < dirLine.nPoints; ++i)
                            {
                                if (i < dirLine.vPoints.Count && dirLine.vPoints[i].w > wMax)
                                {
                                    wMax = dirLine.vPoints[i].w;
                                }
                            }
                        }
                    }
                }
            }

            if (wMax > 0) // 避免除以零
            {
                foreach (var dir in _dirs)
                {
                    if (dir.lines != null)
                    {
                        foreach (var dirLine in dir.lines)
                        {
                            if (dirLine.vPoints != null)
                            {
                                for (int i = 0; i < dirLine.nPoints; ++i)
                                {
                                    if (i < dirLine.vPoints.Count)
                                    {
                                        dirLine.vPoints[i].w /= wMax;
                                    }
                                }
                            }
                        }
                    }
                }
            }

            // 构建方向索引
            if (_dirIndex.Count == 0)
            {
                _dirIndex = new List<int>(361);
                for (int i = 0; i < 361; ++i)
                {
                    _dirIndex.Add(0);
                }
                
                for (int i = 0; i < _dirIndex.Count; ++i)
                {
                    float theta = i * (float)Math.PI / 180f - (float)Math.PI;
                    Vector2 dirVector = new Vector2((float)Math.Cos(theta), (float)Math.Sin(theta));

                    float cosMax = -1;
                    int jm = -1;
                    for (int j = 0; j < _dirs.Count; ++j)
                    {
                        // 计算向量点积
                        float vcos = Vector2.Dot(_dirs[j].dir, dirVector);
                        if (vcos > cosMax)
                        {
                            cosMax = vcos;
                            jm = j;
                        }
                    }
                    _dirIndex[i] = jm;
                }
            }

            _roi = roi;
        }

        // 计算仿射变换的逆变换
        private Matx23f invertAffine(Matx23f affineMatrix)
        {
            // 获取仿射矩阵元素
            float a = affineMatrix.get(0, 0);
            float b = affineMatrix.get(0, 1);
            float c = affineMatrix.get(0, 2);
            float d = affineMatrix.get(1, 0);
            float e = affineMatrix.get(1, 1);
            float f = affineMatrix.get(1, 2);

            // 计算2x2旋转矩阵的逆矩阵
            float det = a * e - b * d;
            if (Mathf.Abs(det) < 1e-6f)
            {
                throw new System.Exception("Singular matrix, cannot invert");
            }

            float invDet = 1.0f / det;
            // 计算逆矩阵的四个元素
            float m00 = e * invDet;
            float m01 = -b * invDet;
            float m10 = -d * invDet;
            float m11 = a * invDet;

            // 计算向量部分：应用逆矩阵到[-c, -f]向量
            float v0 = m00 * (-c) + m01 * (-f);
            float v1 = m10 * (-c) + m11 * (-f);

            // 构造并返回逆仿射矩阵
            return new Matx23f(m00, m01, v0, m10, m11, v1);
        }

        // 获取点集的边界框
        static public OpenCVForUnity.CoreModule.Rect getBoundingBox2D(List<Vector2> points)
        {
            if (points == null || points.Count == 0)
            {
                return new OpenCVForUnity.CoreModule.Rect(0, 0, 0, 0);
            }

            float minX = float.MaxValue;
            float minY = float.MaxValue;
            float maxX = float.MinValue;
            float maxY = float.MinValue;

            foreach (var point in points)
            {
                minX = Mathf.Min(minX, point.x);
                minY = Mathf.Min(minY, point.y);
                maxX = Mathf.Max(maxX, point.x);
                maxY = Mathf.Max(maxY, point.y);
            }

            int x = Mathf.FloorToInt(minX);
            int y = Mathf.FloorToInt(minY);
            int width = Mathf.CeilToInt(maxX - minX);
            int height = Mathf.CeilToInt(maxY - minY);

            return new OpenCVForUnity.CoreModule.Rect(x, y, width, height);
        }

        // 计算扫描线（需要根据实际实现补充）
        private void _calcScanLinesForRows(Mat dirProb, ref DirData positiveDir, ref DirData negativeDir, Matx23f invA)
        {
            const int gaussWindowSizeHalf = 3;

            // 执行Sobel边缘检测
            Mat edgeProb = new Mat();
            Imgproc.Sobel(dirProb, edgeProb, CvType.CV_32F, 1, 0, 7);

            // 初始化方向数据的坐标
            Vector2 O = transA(new Vector2(0f, 0f), invA.val);
            Vector2 P = transA(new Vector2(0f, (float)(dirProb.rows() - 1)), invA.val);
            positiveDir.setCoordinates(O, P);
            negativeDir.setCoordinates(P, O);

            // 调整大小
            positiveDir.resize(dirProb.rows(), dirProb.cols());
            negativeDir.resize(dirProb.rows(), dirProb.cols());

            // 使用数组替代C++的unique_ptr和指针
            float[] _rdata = new float[dirProb.cols() * 2];
            float[] posData = _rdata; // 前半部分
            float[] negData = new float[dirProb.cols()]; // 后半部分
            _LineBuilder buildLine = new _LineBuilder(dirProb.cols());

            const int xend = dirProb.cols() - 1;
            for (int y = 0; y < dirProb.rows(); ++y)
            {
                // 获取当前行的扫描线引用
                ScanLine positiveLine = positiveDir.lines[y];
                ScanLine negativeLine = negativeDir.lines[dirProb.rows() - 1 - y];

                // 设置扫描线坐标
                O = transA(new Vector2(0f, (float)y), invA.val);
                P = transA(new Vector2((float)(dirProb.cols() - 1), (float)y), invA.val);
                positiveLine.setCoordinates(O, P, (float)y);
                negativeLine.setCoordinates(P, O, (float)(dirProb.rows() - 1 - y));

                // 获取边缘概率数据
                Mat rowMat = edgeProb.row(y);
                byte[] rowData = new byte[rowMat.cols() * rowMat.elemSize()];
                rowMat.get(0, 0, rowData);
                float[] ep = new float[rowMat.cols()];
                // 将byte数组转换为float数组（假设是32F类型）
                for (int i = 0; i < rowMat.cols(); i++)
                {
                    int index = i * 4; // 每个float占4个字节
                    ep[i] = BitConverter.ToSingle(rowData, index);
                }

                // 填充正负方向数据
                for (int x = 0; x < dirProb.cols(); ++x)
                {
                    if (ep[x] > 0)
                    {
                        posData[x] = ep[x];
                        negData[xend - x] = 0f;
                    }
                    else
                    {
                        posData[x] = 0f;
                        negData[xend - x] = -ep[x];
                    }
                }

                // 使用Execute方法替代buildLine()调用
                buildLine.Execute(ref positiveLine, posData, dirProb.cols(), gaussWindowSizeHalf);
                buildLine.Execute(ref negativeLine, negData, dirProb.cols(), gaussWindowSizeHalf);

                // 更新引用
                positiveDir.lines[y] = positiveLine;
                negativeDir.lines[dirProb.rows() - 1 - y] = negativeLine;
            }
        }
        
        public void visualizeScanLines(Mat mat)
        {
            // 可视化扫描线
            
        }

        // 实现transA函数，应用仿射变换矩阵到点
        public Vector2 transA(Vector2 pt, float[] A)
        {
            // 应用仿射变换: [x', y'] = [A[0] A[1] A[2]; A[3] A[4] A[5]] * [x; y; 1]
            float x = A[0] * pt.x + A[1] * pt.y + A[2];
            float y = A[3] * pt.x + A[4] * pt.y + A[5];
            return new Vector2(x, y);
        }
        // public bool update(PoseData& pose, const Matx33f& K, const std::vector<CPoint>& cpoints, int maxItrs, float alpha, float eps)
	    // {
		//     for (int itr = 0; itr<maxItrs; ++itr)
		// 	    if (this->_update(pose, K, cpoints, alpha, eps) <= 0)
		// 		    return false;
		//     return true;
        // }

    }
}