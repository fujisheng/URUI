using UnityEngine;

namespace URUI.Effect
{
    /// <summary>
    /// 可序列化的共享参数资产；调用者取得独立快照后再应用节点覆盖。
    /// </summary>
    [CreateAssetMenu(menuName = "URUI/Effect Preset")]
    public sealed class EffectPreset : ScriptableObject
    {
        [SerializeField]
        EffectParameters parameters = new();
        public EffectParameters CreateParameters()
        {
            return (parameters ?? new EffectParameters()).Clone();
        }
        public void SetParameters(EffectParameters value)
        {
            parameters = (value ?? throw new System.ArgumentNullException(nameof(value))).Clone();
        }
        public void Append(EffectParameters value)
        {
            parameters = EffectParameterMerge.Merge(parameters ?? new EffectParameters(), value ?? throw new System.ArgumentNullException(nameof(value)));
        }
    }
}
