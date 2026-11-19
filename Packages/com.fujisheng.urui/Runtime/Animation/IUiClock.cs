namespace URUI.Animation
{
    /// <summary>
    /// 界面动画和效果显式选择的时间源。
    /// </summary>
    public interface IUIClock
    {
        double Now
        {
            get;
        }
        float Delta
        {
            get;
        }
    }
}
