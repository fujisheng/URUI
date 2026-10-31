#if UNITY_EDITOR
using System;
using System.Collections;
using System.Reflection;
using System.Runtime.Serialization;
using UnityEngine;

namespace URUI.Tests.Editor
{
    /// <summary>
    /// 检查正式配置仍能拒绝无法证明 GPU 已完成的提交；不分配或伪造实际 GPU 资源。
    /// </summary>
    public static class ProductionSafetyContract
    {
        public static void Check(Assembly runtime)
        {
            var rendererType = runtime.GetType("URUI.Internal.ParameterTableRenderer", true);
            var packetType = rendererType.GetNestedType("Packet", BindingFlags.NonPublic);
            var stateType = rendererType.GetNestedType("State", BindingFlags.NonPublic);
            var packetsField = rendererType.GetField("packets", BindingFlags.Instance | BindingFlags.NonPublic);
            var stateField = packetType.GetField("state", BindingFlags.Instance | BindingFlags.NonPublic);
            var faulted = rendererType.GetProperty("Faulted", BindingFlags.Instance | BindingFlags.NonPublic);
            // 不运行构造器，避免创建材质或 CommandBuffer；这里只测试故障状态的放行边界。
            var renderer = FormatterServices.GetUninitializedObject(rendererType);
            var packet = FormatterServices.GetUninitializedObject(packetType);
            var packets = (IList)Activator.CreateInstance(packetsField.FieldType);
            packetsField.SetValue(renderer, packets);
            packets.Add(packet);
            stateField.SetValue(packet, Enum.Parse(stateType, "Uncertain"));
            if (!(bool)faulted.GetValue(renderer))
            {
                throw new Exception("Uncertain GPU packet was accepted with diagnostics disabled");
            }

            stateField.SetValue(packet, Enum.Parse(stateType, "Free"));
            if ((bool)faulted.GetValue(renderer))
            {
                throw new Exception("Free packet incorrectly marked as faulted");
            }

            Debug.Log("URUI_PRODUCTION_FENCE_GUARD_PASS");
        }
    }
}
#endif
