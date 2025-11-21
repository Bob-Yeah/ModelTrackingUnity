using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using OpenCVForUnity.CoreModule;
using OpenCVForUnity.ImgprocModule;

namespace ModelTracker
{
    public class ColorHistogram
    {
        private const int CLR_RSB = 3;
        private const int TAB_WIDTH = (1 << (8 - CLR_RSB));
        private const int TAB_WIDTH_2 = TAB_WIDTH * TAB_WIDTH;
        private const int TAB_SIZE = TAB_WIDTH * TAB_WIDTH_2;

        // 直方图项，存储前景/背景计数
        private struct TabItem
        {
            public float[] nbf;  // [0]=背景, [1]=前景

            public TabItem(float bg = 0, float fg = 0)
            {
                nbf = new float[2];
                nbf[0] = bg;
                nbf[1] = fg;
            }
        }

        private int _consideredLength = 20;  // 考虑的采样长度
        private int _unconsiderLength = 1;   // 不考虑的初始长度
        private List<TabItem> _tab;   // 主直方图
        private List<TabItem> _dtab;  // 临时统计直方图

        public ColorHistogram()
        {
            _tab = new List<TabItem>(TAB_SIZE);
            _dtab = new List<TabItem>(TAB_SIZE);

            // 初始化直方图数组
            for (int i = 0; i < TAB_SIZE; i++)
            {
                _tab.Add(new TabItem());
                _dtab.Add(new TabItem());
            }
        }

        // 计算颜色索引
        private int _color_index(byte[] pix)
        {
            return ((pix[0] >> CLR_RSB) * TAB_WIDTH_2 +
                    (pix[1] >> CLR_RSB) * TAB_WIDTH +
                    (pix[2] >> CLR_RSB));
        }

        // 更新直方图数据（指数移动平均）
        private void _do_update(TabItem[] tab, TabItem[] dtab, float learningRate, float[] dtabSum)
        {
            float tscale = 1.0f - learningRate;
            float[] dscale = { learningRate / dtabSum[0], learningRate / dtabSum[1] };

            for (int i = 0; i < TAB_SIZE; ++i)
            {
                for (int j = 0; j < 2; ++j)
                {
                    tab[i].nbf[j] = tab[i].nbf[j] * tscale + dtab[i].nbf[j] * dscale[j];
                }
            }
        }

        // 获取每个像素的前景概率
        public Mat GetProb(Mat img)
        {
            Mat prob = new Mat(img.rows(), img.cols(), CvType.CV_32F);
            TabItem[] tab = _tab.ToArray();

            // 遍历图像计算概率
            for (int y = 0; y < img.rows(); y++)
            {
                for (int x = 0; x < img.cols(); x++)
                {
                    byte[] pixel = new byte[3];
                    img.get(y, x, pixel);

                    int ti = _color_index(pixel);
                    float[] nbf = tab[ti].nbf;
                    float p = (nbf[1] + 1e-6f) / (nbf[0] + nbf[1] + 2e-6f);

                    prob.put(y, x, p);
                }
            }

            return prob;
        }

        // 优化后的C#像素添加方法
        private bool AddPixel(Point p, int fgBgIndex, Mat img, ref float[] dtabSum)
        {
            // 四舍五入到最近的整数坐标
            int x = Mathf.RoundToInt((float)p.x);
            int y = Mathf.RoundToInt((float)p.y);

            // 边界检查
            if (x >= 0 && x < img.cols() && y >= 0 && y < img.rows())
            {
                // 获取像素颜色值
                byte[] pixel = new byte[3];
                img.get(y, x, pixel);

                // 计算颜色索引并更新统计
                int colorIndex = _color_index(pixel);
                if (colorIndex >= 0 && colorIndex < _dtab.Count)
                {
                    _dtab[colorIndex].nbf[fgBgIndex] += 1.0f;
                    dtabSum[fgBgIndex] += 1.0f;
                    return true;
                }
            }
            return false;
        }

        public void update(Templates templ, ref Mat img, ref Pose pose, Matx33f K, float learningRate)
        {
            //learningRate = 0.1f;
            Vector3 modelCenter = templ.modelCenter;
            Projector prj = new Projector(K, pose.R, pose.t);


            float[] dtabSum = { 0.0f, 0.0f };
            
            // 重置临时直方图
            for (int i = 0; i < _dtab.Count; i++)
            {
                _dtab[i].nbf[0] = 0.0f;
                _dtab[i].nbf[1] = 0.0f;
            }
            
            

            // // 获取最近的视图（需要确保Templates类有这个方法）
            // int curView = 0; // 假设这里需要调用Templates类的方法来获取最近视图
            
            // // 假设我们使用viewIndex.GetViewInDir方法来获取最近视图
            // // 这里需要根据实际的Templates类实现来调整
            // Vector3 viewDir = pose.t * -1; // 简化的视图方向计算
            // curView = templ.viewIndex.GetViewInDir(viewDir);
            
            // if (curView >= 0 && curView < templ.views.Count)
            // {
            //     Point objCenter = prj.Project(modelCenter);
                
            //     DView view = templ.views[curView];
            //     foreach (CPoint cp in view.contourPoints3d)
            //     {
            //         Point c = prj.Project(cp.center);
            //         Point n = new Point(objCenter.x - c.x, objCenter.y - c.y);
            //         float fgLength = (float)Mathf.Sqrt((float)(n.x * n.x + n.y * n.y));
                    
            //         if (fgLength > 0) // 避免除以零
            //         {
            //             n.x = n.x / fgLength;
            //             n.y = n.y / fgLength;
            //         }

            //         Point pt = new Point(c.x + _unconsiderLength * n.x, c.y + _unconsiderLength * n.y);
            //         int end = Mathf.Min(_consideredLength, (int)fgLength);
                    
            //         for (int i = _unconsiderLength; i < end; i++)
            //         {
            //             if (!AddPixel(pt, 1))
            //                 break;
                        
            //             pt.x += n.x;
            //             pt.y += n.y;
            //         }
                    
            //         end = _consideredLength * 4;
            //         pt = new Point(c.x - _unconsiderLength * n.x, c.y - _unconsiderLength * n.y);
                    
            //         for (int i = _unconsiderLength; i < end; i++)
            //         {
            //             if (!AddPixel(pt, 0))
            //                 break;
                        
            //             pt.x -= n.x;
            //             pt.y -= n.y;
            //         }
            //     }
                
            //     // 更新直方图
            //     if (dtabSum[0] > 0 && dtabSum[1] > 0)
            //     {
            //         _do_update(_tab.ToArray(), _dtab.ToArray(), learningRate, dtabSum);
            //     }
            // }
        }
    }
}