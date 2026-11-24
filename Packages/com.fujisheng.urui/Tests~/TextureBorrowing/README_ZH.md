# 借用纹理与合批检查（Texture Borrowing）

将本目录连同 `Tests~/Editor/Fixtures/BorrowedImageValidation.cs` 与 `Tests~/Editor/Fixtures/Editor/TextureSamplingGpuValidation.cs` 导入引用当前 Core 包的隔离 Unity 工程。可依次运行 `TextureSamplingGpuValidation.Run`（覆盖 Gamma 与 Linear 空间并自动恢复工程色彩空间设置）及 `BorrowedImageValidation.Run`（覆盖借用所有权与 GPU 退役检查）。`URUI.Tests.TextureBorrowing` 仅为测试友元程序集，不作为样例或运行时依赖。

传入 `--batching` 可分别验证关闭与开启批次合并时的三种当前借用图像布局。测试会记录原生纹理身份、稳定合并分组、已录制的 DrawMesh 提交次数及命令缓冲字节数（仅在有真实 GPU 提交的环境中读取指标，无头模式下的全局 GPU 计数器零样本不作为硬件绘制次数声明）。
