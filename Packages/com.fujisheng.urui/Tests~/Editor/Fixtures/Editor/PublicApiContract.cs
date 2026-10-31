#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEngine;

namespace URUI.Tests.Editor
{
public static class PublicApiContract
{
    public static void Check()
    {
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        var runtime = assemblies.Single(value => value.GetName().Name == "URUI.Runtime");
        var actual = runtime.GetExportedTypes().Select(value => value.FullName).OrderBy(value => value).ToArray();
        var expected = new[] { "URUI.ClickEvent", "URUI.Document", "URUI.Element", "URUI.ElementStyle", "URUI.ImageAsset" };
        if (!actual.SequenceEqual(expected)) throw new Exception("URUI public surface differs: " + string.Join(",", actual));
        var borrowed = runtime.GetType("URUI.Internal.BorrowedFrame", true);
        var layout = borrowed.GetProperty("LayoutMatches", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        if (!(bool)layout.GetValue(null)) throw new Exception("Native frame layout does not match target pointer width");
        Debug.Log("URUI_SINGLE_API_PASS " + string.Join(",", actual));
    }
}
}
#endif
