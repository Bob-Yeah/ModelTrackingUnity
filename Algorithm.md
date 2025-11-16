1. 创建Tracker，加载模型

2. Tracker.reset() 加载开始时的GT pose，K内参矩阵，和start时的图像

    a. _cur Frame 初始化，update ColorHistogram， 即利用模型，当前帧的图像，当前帧的gt pose，内参矩阵，learningrate为1. 运行时可以用参考图像进行手动对准进行初始化，初始化的质量可能影响跟踪的质量。

3. 1000帧循环：

    a. startUpdate()， 当前帧的img

        把_cur赋值到_prev中，_cur的img更新，获得新的概率图
    
    b. 利用老的pose，tracker.update() 跟踪当前帧

        主要的优化过程，优化得到pose结果

    c. tracker.endUpdate() 

        利用优化后的pose，更新color histogram，learningrate为0.2f。

现在在应用使用的时候，只需要在循环中调用startUpdate()，update()，endUpdate()即可。


---

优化过程：

template的pro方法：

获得当前帧的最近视图，投影对应的三维轮廓点，计算bounding box和ROI

计算重叠的ROI，计算扫描线：dfr的computeScanLine方法。输入的时概率图和roi，

outiter
    dfr update
    

inneriter
    non-local的搜索

outiter
    dfr update
