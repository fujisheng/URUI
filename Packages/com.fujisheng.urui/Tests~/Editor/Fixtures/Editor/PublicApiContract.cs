#if UNITY_EDITOR
using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace URUI.Tests.Editor
{
    /// <summary>
    /// 检查已编译运行时的公开边界、原生结构布局和默认诊断隔离。
    /// </summary>
    public static class PublicApiContract
    {
        /// <summary>
        /// 验证当前导入包的运行时契约，失败时抛出异常交给批处理入口。
        /// </summary>
        public static void Check()
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            var runtime = assemblies.Single(value => value.GetName().Name == "URUI.Runtime");
            var actual = runtime.GetExportedTypes().Select(value => value.FullName).OrderBy(value => value).ToArray();
            var expected = new[] {
                "URUI.ClickEvent",
                "URUI.Document",
                "URUI.Element",
                "URUI.ElementStyle",
                "URUI.ImageAsset"
            };
            if (!actual.SequenceEqual(expected))
            {
                throw new Exception("URUI public surface differs: " + string.Join(",", actual));
            }

            var borrowed = runtime.GetType("URUI.Internal.BorrowedFrame", true);
            var layout = borrowed.GetProperty("LayoutMatches", BindingFlags.NonPublic | BindingFlags.Static);
            if (!(bool)layout.GetValue(null))
            {
                throw new Exception("Native frame layout does not match target pointer width");
            }

            CheckDebugIsolation(runtime);
            ProductionSafetyContract.Check(runtime);
            Debug.Log("URUI_SINGLE_API_PASS " + string.Join(",", actual));
        }

        static void CheckDebugIsolation(Assembly runtime)
        {
#if !URUI_DEBUG
            // 用实际程序集确认默认关闭的诊断类型没有混入正式运行时，避免只检查源码条件。
            var diagnosticNames = new[]
            {
                "GeometryContentProbe",
                "PropertyDependencyCounter",
                "UruiProfileNative",
                "UruiApiProfileNative",
                "UruiTypedProbeNative",
                "UruiStyleProbeNative",
                "UruiLookupProfileNative"
            };
            var included = runtime.GetTypes()
                .Where(type => diagnosticNames.Contains(type.Name))
                .Select(type => type.FullName)
                .ToArray();
            if (included.Length > 0)
            {
                throw new Exception("URUI_DEBUG types included by default: " + string.Join(",", included));
            }

            Debug.Log("URUI_DEBUG_DEFAULT_OFF_PASS");
#endif
        }
    }
}
#endif
