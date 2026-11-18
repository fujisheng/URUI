using System;
using System.Runtime.InteropServices;
using System.Text;

namespace URUI.Internal.Interop
{
    internal static partial class NativeMethods
    {
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong urui_element_ref_revision();

        [StructLayout(LayoutKind.Sequential)]
        internal struct ElementRefreshStamp
        {
            internal ulong Mutation, References;
            internal bool Reusable => Mutation != 0 && Mutation != ulong.MaxValue && References != 0 && References != ulong.MaxValue;
            internal bool Matches(ElementRefreshStamp other) => Reusable && Mutation == other.Mutation && References == other.References;
        }
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_element_refresh_stamp(out ElementRefreshStamp stamp);

        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]
        internal static extern int urui_element_event_interest(ulong owner,ulong token,uint mask,ulong version);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_element_refresh_state(ulong documentHandle, ulong token, out UnityEngine.Rect layout, int capacity);
        [StructLayout(LayoutKind.Sequential)]
        internal struct ElementRefreshState { internal int State; internal UnityEngine.Rect Layout; }
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern unsafe int urui_element_refresh_states(ulong owner, ulong* tokens, ElementRefreshState* states, uint count);
        [StructLayout(LayoutKind.Sequential)]
        internal struct RangeInfo { public float Min, Max, Step, StepBase; public int Flags; }
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_control_range_info(ulong owner, ulong token, out RangeInfo info);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong urui_element_create(ulong documentHandle, [MarshalAs(UnmanagedType.LPUTF8Str)] string tag);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong urui_element_clone(ulong documentHandle, ulong token);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_element_number(ulong documentHandle, ulong token, [MarshalAs(UnmanagedType.LPUTF8Str)] string property, float value, int unit);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_element_color(ulong documentHandle, ulong token, [MarshalAs(UnmanagedType.LPUTF8Str)] string property, int red, int green, int blue, int alpha);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_element_trs(ulong documentHandle, ulong token, [In] float[] values, int count);


        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong urui_element_parent(ulong documentHandle, ulong token);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_element_child_count(ulong documentHandle, ulong token);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong urui_element_child_at(ulong documentHandle, ulong token, int index);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong urui_element_find(ulong documentHandle, ulong token,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string className);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_element_query(ulong documentHandle, ulong token,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string className,
            [Out] ulong[] values,
            int capacity);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        static extern int urui_element_read(ulong documentHandle, ulong token,
            int field,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
            [Out] byte[] value,
            int capacity,
            out int required);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_element_write(ulong documentHandle, ulong token,
            int field,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string value);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_element_erase(ulong documentHandle, ulong token, int field, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_element_class(ulong documentHandle, ulong token, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int action);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_element_insert(ulong documentHandle, ulong parent, ulong child, int index);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_element_detach(ulong documentHandle, ulong token);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_element_clear(ulong documentHandle, ulong token);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_element_destroy(ulong documentHandle, ulong token);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_element_attached(ulong documentHandle, ulong token);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_element_pop_click(ulong documentHandle, out ulong target);

        // Two synchronous copies: no borrowed native string survives the call.
        internal static string ReadElement(ulong documentHandle, ulong token, int field, string name = null, string previous = null)
        {
            int result = urui_element_read(documentHandle, token, field, name, null, 0, out var required);
            if (result == 0)
            {
                return null;
            }

            if (result == -2)
            {
                throw new InvalidOperationException("Text 仅支持普通文本子节点；结构内容请使用节点层级 API。");
            }

            var bytes = SnapshotBuffer(result, required, int.MaxValue);
            ValidateSnapshot(urui_element_read(documentHandle, token, field, name, bytes, required, out var actual), actual, required, bytes);
            return SnapshotText(bytes, required - 1, previous);
        }
    }
}
