# Game Flow 资源与开发流程

Game Flow 的权威源码在 Packages/com.fujisheng.urui/Samples~/Game Flow，Unity 导入后的 Assets/Samples 目录是副本。导入、场景、数据与运行要求见[样例说明](../Packages/com.fujisheng.urui/Samples~/Game%20Flow/README.md)。

修改结构、样式、脚本和资源时先更新包内样例，再显式重新导入到独立测试工程。不要把导入副本当作发布事实源，也不要要求使用者访问开发机 artifacts。字体、图片、租约和 URL 映射见[生命周期与资源](../Packages/com.fujisheng.urui/Documentation~/LIFECYCLE_ZH.md)。

录制或截图说明应区分 Editor、Player、输入数据和 mock 服务。当前主入口共用包内已有动图；运行验收入口见[Editor 检查](../Packages/com.fujisheng.urui/Tests~/Editor/README.md)。
