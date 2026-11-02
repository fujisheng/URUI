using System;
using System.Runtime.InteropServices;
using System.Text;

namespace URUI.Internal.Interop
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct ElementEventNative
    {
        internal ulong target, current;
        internal int type, phase, pointer, button, buttons, modifiers, key, kind;
        internal float x, y, dx, dy;
        internal IntPtr text;
    }

    internal static partial class NativeMethods
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate int ElementEventCallback(ref ElementEventNative value);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_element_api_version();

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_set_event_callback(ElementEventCallback callback);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_document_listen();

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_element_listen(ulong token, int type, int capture, int enabled);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_element_box(ulong token, [Out] float[] values, int capacity);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_element_viewport(ulong token, [Out] float[] values, int capacity);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_element_point(ulong token, int toWorld, float x, float y, [Out] float[] values);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong urui_element_pick(float x, float y);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_element_focus(ulong token, int focus);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong urui_focused_element();

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_element_scroll(ulong token, float x, float y);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_control_value_write(ulong token, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_control_value_read(ulong token, [Out] byte[] value, int capacity, out int required);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_element_style_read(ulong token, int local, [MarshalAs(UnmanagedType.LPUTF8Str)] string property, [Out] byte[] value, int capacity, out int required);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_pointer_event(int pointer, int kind, int phase, float x, float y, int button, int buttons, float dx, float dy, int modifiers);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_pointer_capture(int pointer, ulong token);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong urui_pointer_captured(int pointer);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_input_reset();

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_cancel_pointers(ulong owner);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_key_event(int key, int pressed, int modifiers);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_text_input([MarshalAs(UnmanagedType.LPUTF8Str)] string text);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_text_composition([MarshalAs(UnmanagedType.LPUTF8Str)] string text, int operation);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_text_selection(ref int start, ref int end, int write);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_clipboard_write([MarshalAs(UnmanagedType.LPUTF8Str)] string text);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_clipboard_read([Out] byte[] text, int capacity, out int required);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_process_deferred_operations();

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_select_option_count(ulong token);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_select_option_read(ulong token, int index, [Out] byte[] value, int capacity, out int required);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_select_option_add(ulong token, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_select_options_clear(ulong token);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_select_selection(ulong token, int index);

        delegate int StringReader(byte[] value, int capacity, out int required);

        static string ReadUtf8Snapshot(StringReader read)
        {
            int result = read(null, 0, out int required);
            if (result == 0)
            {
                return null;
            }

            if (result != -3 || required <= 0 || required > 16 * 1024 * 1024)
            {
                throw new InvalidOperationException("URUI 字符串读取失败：" + LastError());
            }

            var bytes = new byte[required];
            if (read(bytes, required, out int actual) != 1 || actual != required || bytes[actual - 1] != 0)
            {
                throw new InvalidOperationException("URUI 字符串快照失效。");
            }

            return Encoding.UTF8.GetString(bytes, 0, actual - 1);
        }

        internal static string ReadControl(ulong token) => ReadUtf8Snapshot(
            (byte[] bytes, int capacity, out int required) => urui_control_value_read(token, bytes, capacity, out required));

        internal static string ReadStyle(ulong token, string property, bool local) => ReadUtf8Snapshot(
            (byte[] bytes, int capacity, out int required) => urui_element_style_read(token, local ? 1 : 0, property, bytes, capacity, out required));

        internal static string ReadOption(ulong token, int index) => ReadUtf8Snapshot(
            (byte[] bytes, int capacity, out int required) => urui_select_option_read(token, index, bytes, capacity, out required));

        internal static string ReadClipboard() => ReadUtf8Snapshot(urui_clipboard_read);
    }
}
