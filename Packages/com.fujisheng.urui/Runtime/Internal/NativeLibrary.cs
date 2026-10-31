namespace URUI.Internal
{
    // Static iOS/WebGL plugins link into the application; desktop and Android
    // resolve their platform-native URUIBridge library through Unity.
    internal static class NativeLibrary
    {
#if (UNITY_IOS || UNITY_WEBGL) && !UNITY_EDITOR
        internal const string Name = "__Internal";
#else
        internal const string Name = "URUIBridge";
#endif
    }
}
