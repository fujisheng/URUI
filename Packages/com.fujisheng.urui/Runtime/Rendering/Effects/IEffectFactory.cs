namespace URUI.Rendering.Effects
{
    /// <summary>
    /// 每个作者节点创建一个实例；失败前创建的资源交给 context.Own。
    /// </summary>
    public interface IEffectFactory
    {
        IElementEffect Create(EffectContext context);
    }
}
