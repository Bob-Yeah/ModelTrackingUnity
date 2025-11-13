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


    }
}