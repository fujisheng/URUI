using System;
using System.Runtime.InteropServices;
using System.Text;

namespace URUI.Internal
{
    internal static partial class NativeMethods
    {
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong urui_element_root();

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong urui_element_create([MarshalAs(UnmanagedType.LPUTF8Str)] string tag);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong urui_element_parent(ulong token);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_element_child_count(ulong token);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong urui_element_child_at(ulong token, int index);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong urui_element_find(
            ulong token,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string className);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_element_query(
            ulong token,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string className,
            [Out] ulong[] values,
            int capacity);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        static extern int urui_element_read(
            ulong token,
            int field,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
            [Out] byte[] value,
            int capacity,
            out int required);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_element_write(
            ulong token,
            int field,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string value);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_element_erase(ulong token, int field, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_element_class(ulong token, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int action);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_element_insert(ulong parent, ulong child, int index);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_element_detach(ulong token);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_element_clear(ulong token);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_element_destroy(ulong token);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_element_attached(ulong token);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_element_pop_click(out ulong target);

        // Two synchronous copies: no borrowed native string survives the call.
        internal static string ReadElement(ulong token, int field, string name = null)
        {
            int result = urui_element_read(token, field, name, null, 0, out var required);
            if (result == 0)
            {
                return null;
            }

            if (result == -2)
            {
                throw new InvalidOperationException("Text 仅支持普通文本子节点；结构内容请使用节点层级 API。");
            }

            if (result != -3 || required <= 0)
            {
                throw new InvalidOperationException("URUI 无法读取元素：" + LastError());
            }

            var bytes = new byte[required];
            if (urui_element_read(token, field, name, bytes, bytes.Length, out var actual) != 1
                || actual != required
                || bytes[required - 1] != 0)
            {
                throw new InvalidOperationException("URUI 元素字符串快照无效。");
            }

            return Encoding.UTF8.GetString(bytes, 0, required - 1);
        }
    }
}
