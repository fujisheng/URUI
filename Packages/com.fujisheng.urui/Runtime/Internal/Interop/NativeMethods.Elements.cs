using System;
using System.Runtime.InteropServices;

namespace URUI.Internal.Interop
{
    [Flags]
    internal enum ElementStateFlags
    {
        None = 0,
        Valid = 1,
        Attached = 2,
        VisibleInHierarchy = 4,
        DisabledInHierarchy = 8
    }

    internal static class ElementReadField
    {
        internal const int Name = 0;
        internal const int Tag = 1;
        internal const int PlainText = 2;
        internal const int Attribute = 3;
        internal const int InnerHtml = 4;
    }

    internal static class ElementWriteField
    {
        internal const int Name = 0;
        internal const int PlainText = 1;
        internal const int Attribute = 2;
        internal const int AttachedInnerHtml = 3;
        internal const int InlineStyle = 4;
    }

    internal static class ElementEraseField
    {
        internal const int Attribute = 0;
        internal const int InlineStyle = 1;
    }

    internal static class ClassAction
    {
        internal const int Query = -1;
        internal const int Remove = 0;
        internal const int Add = 1;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct ElementSpatialSnapshot
    {
        internal fixed float Layout[4];
        internal fixed float Content[4];
        internal fixed float WorldBounds[4];
        internal fixed float Scroll[4];
        internal fixed float Viewport[4];
        internal fixed float LocalMatrix[16];
        internal fixed float RotationMatrix[16];
        internal int StateFlags;
        internal int SeparateRotation;
    }

    internal static partial class NativeMethods
    {
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong urui_element_ref_revision(ulong surface);

        [StructLayout(LayoutKind.Sequential)]
        internal struct ElementRefreshStamp
        {
            internal ulong Mutation;
            internal ulong References;
            internal bool Reusable => Mutation != 0 && Mutation != ulong.MaxValue && References != 0 && References != ulong.MaxValue;
            internal bool Matches(ElementRefreshStamp other) => Reusable && Mutation == other.Mutation && References == other.References;
        }

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_element_refresh_stamp(ulong surface, out ElementRefreshStamp stamp);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_element_state_flags(ulong surface, ulong owner, ulong token);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_element_spatial_snapshot(ulong surface, ulong owner, ulong token, out ElementSpatialSnapshot snapshot);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_element_event_interest(ulong surface, ulong owner, ulong token, uint mask, ulong version);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_element_refresh_state(ulong surface, ulong documentHandle, ulong token, out UnityEngine.Rect layout, int capacity);

        [StructLayout(LayoutKind.Sequential)]
        internal struct ElementRefreshState
        {
            internal int State;
            internal UnityEngine.Rect Layout;
        }

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern unsafe int urui_element_refresh_states(ulong surface, ulong owner, ulong* tokens, ElementRefreshState* states, uint count);

        [StructLayout(LayoutKind.Sequential)]
        internal struct RangeInfo
        {
            public float Min;
            public float Max;
            public float Step;
            public float StepBase;
            public int Flags;
        }

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_control_range_info(ulong surface, ulong owner, ulong token, out RangeInfo info);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong urui_element_create(ulong surface, ulong documentHandle,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string tag);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong urui_element_clone(ulong surface, ulong documentHandle, ulong token);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_element_clone_template_content(ulong surface, ulong documentHandle, ulong source, ulong destination);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_element_number(ulong surface, ulong documentHandle, ulong token,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string property, float value, int unit);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_element_color(ulong surface, ulong documentHandle, ulong token,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string property, int red, int green, int blue, int alpha);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_element_trs(ulong surface, ulong documentHandle, ulong token, [In] float[] values, int count);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern unsafe int urui_element_trs_ex(ulong surface, ulong documentHandle, ulong token, float* values, int count, int preserveOrigin);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong urui_element_parent(ulong surface, ulong documentHandle, ulong token);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_element_child_count(ulong surface, ulong documentHandle, ulong token);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong urui_element_child_at(ulong surface, ulong documentHandle, ulong token, int index);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong urui_element_find(ulong surface, ulong documentHandle, ulong token,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string className);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_element_query(ulong surface, ulong documentHandle, ulong token,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string className,
            [Out] ulong[] values, int capacity);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        static extern int urui_element_read(ulong surface, ulong documentHandle, ulong token,
            int field,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
            [Out] byte[] value, int capacity, out int required);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_element_write(ulong surface, ulong documentHandle, ulong token,
            int field,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string value);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_element_erase(ulong surface, ulong documentHandle, ulong token,
            int field,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string name);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_element_class(ulong surface, ulong documentHandle, ulong token,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int action);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_element_insert(ulong surface, ulong documentHandle, ulong parent, ulong child, int index);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_element_insert_before(ulong surface, ulong documentHandle, ulong parent, ulong child, ulong refChild);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_element_detach(ulong surface, ulong documentHandle, ulong token);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_element_clear(ulong surface, ulong documentHandle, ulong token);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_element_destroy(ulong surface, ulong documentHandle, ulong token);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_element_attached(ulong surface, ulong documentHandle, ulong token);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_element_pop_click(ulong surface, ulong documentHandle, out ulong target);

        internal static bool HasElementAttribute(ulong surface, ulong documentHandle, ulong token, string name)
        {
            int result = urui_element_read(surface, documentHandle, token, ElementReadField.Attribute, name, null, 0, out _);
            return result == NativeStatus.Ok || result == NativeStatus.BufferTooSmall;
        }

        static bool EqualsAscii(byte[] bytes, int length, string literal)
        {
            if (length != literal.Length)
            {
                return false;
            }
            for (var i = 0; i < length; i++)
            {
                if (bytes[i] != literal[i])
                {
                    return false;
                }
            }
            return true;
        }

        // Zero-allocation tag read for common HTML and URUI tags.
        internal static string ReadKnownTag(ulong surface, ulong documentHandle, ulong token)
        {
            var bytes = snapshotScratch ??= new byte[256];
            int result = urui_element_read(surface, documentHandle, token, ElementReadField.Tag, null, bytes, bytes.Length, out int required);
            if (result == NativeStatus.Absent)
            {
                return null;
            }
            int actual = required;
            if (result == NativeStatus.BufferTooSmall)
            {
                bytes = SnapshotBuffer(surface, result, required, int.MaxValue);
                result = urui_element_read(surface, documentHandle, token, ElementReadField.Tag, null, bytes, required, out actual);
            }
            ValidateSnapshot(result, actual, required, bytes);
            var len = actual - 1;
            if (len > 0)
            {
                switch (len)
                {
                    case 1:
                        if (bytes[0] == 'p') return "p";
                        if (bytes[0] == 'a') return "a";
                        break;
                    case 2:
                        if (EqualsAscii(bytes, 2, "li")) return "li";
                        if (EqualsAscii(bytes, 2, "ul")) return "ul";
                        if (EqualsAscii(bytes, 2, "ol")) return "ol";
                        if (EqualsAscii(bytes, 2, "br")) return "br";
                        break;
                    case 3:
                        if (EqualsAscii(bytes, 3, "div")) return "div";
                        if (EqualsAscii(bytes, 3, "img")) return "img";
                        if (EqualsAscii(bytes, 3, "nav")) return "nav";
                        break;
                    case 4:
                        if (EqualsAscii(bytes, 4, "span")) return "span";
                        if (EqualsAscii(bytes, 4, "body")) return "body";
                        if (EqualsAscii(bytes, 4, "html")) return "html";
                        if (EqualsAscii(bytes, 4, "head")) return "head";
                        break;
                    case 5:
                        if (EqualsAscii(bytes, 5, "#text")) return "#text";
                        if (EqualsAscii(bytes, 5, "label")) return "label";
                        if (EqualsAscii(bytes, 5, "input")) return "input";
                        if (EqualsAscii(bytes, 5, "style")) return "style";
                        break;
                    case 6:
                        if (EqualsAscii(bytes, 6, "button")) return "button";
                        if (EqualsAscii(bytes, 6, "select")) return "select";
                        if (EqualsAscii(bytes, 6, "header")) return "header";
                        if (EqualsAscii(bytes, 6, "footer")) return "footer";
                        break;
                    case 7:
                        if (EqualsAscii(bytes, 7, "section")) return "section";
                        break;
                    case 8:
                        if (EqualsAscii(bytes, 8, "template")) return "template";
                        if (EqualsAscii(bytes, 8, "textarea")) return "textarea";
                        if (EqualsAscii(bytes, 8, "#comment")) return "#comment";
                        break;
                    case 11:
                        if (EqualsAscii(bytes, 11, "urui-slider")) return "urui-slider";
                        if (EqualsAscii(bytes, 11, "urui-select")) return "urui-select";
                        if (EqualsAscii(bytes, 11, "urui-scroll")) return "urui-scroll";
                        break;
                    case 12:
                        if (EqualsAscii(bytes, 12, "urui-foldout")) return "urui-foldout";
                        break;
                    case 14:
                        if (EqualsAscii(bytes, 14, "urui-particles")) return "urui-particles";
                        if (EqualsAscii(bytes, 14, "urui-list-view")) return "urui-list-view";
                        break;
                    case 16:
                        if (EqualsAscii(bytes, 16, "urui-scroll-view")) return "urui-scroll-view";
                        break;
                    case 17:
                        if (EqualsAscii(bytes, 17, "urui-progress-bar")) return "urui-progress-bar";
                        break;
                }
            }
            return SnapshotText(bytes, len, null);
        }

        // Single-attempt optimistic read into thread-static scratch; only retries when buffer was too small.
        internal static string ReadElement(ulong surface, ulong documentHandle, ulong token, int field, string name = null, string previous = null)
        {
            var bytes = snapshotScratch ??= new byte[256];
            int result = urui_element_read(surface, documentHandle, token, field, name, bytes, bytes.Length, out int required);
            if (result == NativeStatus.Absent)
            {
                return null;
            }
            if (result == NativeStatus.Structured)
            {
                throw new InvalidOperationException("Text 仅支持普通文本子节点；结构内容请使用节点层级 API。");
            }

            int actual = required;
            if (result == NativeStatus.BufferTooSmall)
            {
                bytes = SnapshotBuffer(surface, result, required, int.MaxValue);
                result = urui_element_read(surface, documentHandle, token, field, name, bytes, required, out actual);
            }

            ValidateSnapshot(result, actual, required, bytes);
            return SnapshotText(bytes, actual - 1, previous);
        }
    }
}
