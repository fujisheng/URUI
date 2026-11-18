using System;
using System.Runtime.InteropServices;
using System.Text;

namespace URUI.Internal.Interop
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct ElementEventNative
    {
        internal ulong document, target, current;
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
        internal static extern int urui_transform_api_version();

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_element_local_transform(ulong documentHandle, ulong token, [Out] float[] values, int capacity);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_set_event_callback(ElementEventCallback callback);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_document_listen(ulong documentHandle);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_element_listen(ulong documentHandle, ulong token, int type, int capture, int enabled);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_element_box(ulong documentHandle, ulong token, [Out] float[] values, int capacity);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_element_viewport(ulong documentHandle, ulong token, [Out] float[] values, int capacity);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_element_point(ulong documentHandle, ulong token, int toWorld, float x, float y, [Out] float[] values);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong urui_element_pick(ulong documentHandle, float x, float y);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_element_focus(ulong documentHandle, ulong token, int focus);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong urui_focused_element(ulong documentHandle);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_element_scroll(ulong documentHandle, ulong token, float x, float y);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_control_value_write(ulong documentHandle, ulong token, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_control_value_read(ulong documentHandle, ulong token, [Out] byte[] value, int capacity, out int required);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_element_style_read(ulong documentHandle, ulong token, int local, [MarshalAs(UnmanagedType.LPUTF8Str)] string property, [Out] byte[] value, int capacity, out int required);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_pointer_poll(int pointer,int kind,float x,float y,int buttons,int modifiers);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_pointer_event(int pointer, int kind, int phase, float x, float y, int button, int buttons, float dx, float dy, int modifiers);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_pointer_capture(ulong documentHandle, int pointer, ulong token);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong urui_pointer_captured(ulong documentHandle, int pointer);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_input_reset();

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_cancel_pointers(ulong documentHandle, ulong owner);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_key_event(int key, int pressed, int modifiers);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_text_input([MarshalAs(UnmanagedType.LPUTF8Str)] string text);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_text_composition([MarshalAs(UnmanagedType.LPUTF8Str)] string text, int operation);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_text_selection(ref int start, ref int end, int write);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong urui_clipboard_version();

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_clipboard_write([MarshalAs(UnmanagedType.LPUTF8Str)] string text);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_clipboard_read([Out] byte[] text, int capacity, out int required);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_process_deferred_operations();

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_select_option_count(ulong documentHandle, ulong token);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_select_option_read(ulong documentHandle, ulong token, int index, [Out] byte[] value, int capacity, out int required);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_select_option_add(ulong documentHandle, ulong token, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_select_options_clear(ulong documentHandle, ulong token);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_select_selection(ulong documentHandle, ulong token, int index);

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

        internal static string ReadControl(ulong documentHandle, ulong token) => ReadUtf8Snapshot(
            (byte[] bytes, int capacity, out int required) => urui_control_value_read(documentHandle, token, bytes, capacity, out required));

        [ThreadStatic] static byte[] styleNumberScratch;
        internal static ReadOnlySpan<byte> ReadStyleNumberText(ulong documentHandle, ulong token, string property)
        {
            // 原生有限 float 的规范文本和单位可放入固定缓冲；复杂 CSS 值不是长度。
            var bytes = styleNumberScratch ??= new byte[64];
            var result = urui_element_style_read(documentHandle, token, 0, property, bytes, bytes.Length, out var required);
            if (result == 0 || (result == -3 && required > bytes.Length)) { return default; }
            ValidateSnapshot(result, required, required, bytes);
            return new ReadOnlySpan<byte>(bytes, 0, required - 1);
        }

        internal static string ReadStyle(ulong documentHandle, ulong token, string property, bool local, string previous = null, string alternate = null)
        {
            var result = urui_element_style_read(documentHandle, token, local ? 1 : 0, property, null, 0, out var required);
            if (result == 0) { return null; }
            var bytes = SnapshotBuffer(result, required);
            ValidateSnapshot(urui_element_style_read(documentHandle, token, local ? 1 : 0, property, bytes, required, out var actual), actual, required, bytes);
            return SnapshotText(bytes, actual - 1, previous, alternate);
        }

        internal static string ReadOption(ulong documentHandle, ulong token, int index) => ReadUtf8Snapshot(
            (byte[] bytes, int capacity, out int required) => urui_select_option_read(documentHandle, token, index, bytes, capacity, out required));

        [ThreadStatic] static string clipboardSnapshot;
        internal static string ReadClipboard()
        {
            var result = urui_clipboard_read(null, 0, out var required);
            if (result == 0) { clipboardSnapshot = null; return null; }
            var bytes = SnapshotBuffer(result, required);
            ValidateSnapshot(urui_clipboard_read(bytes, required, out var actual), actual, required, bytes);
            return clipboardSnapshot = SnapshotText(bytes, actual - 1, clipboardSnapshot);
        }

        [ThreadStatic] static byte[] snapshotScratch, snapshotComparison;
        static byte[] SnapshotBuffer(int result, int required, int maximum = 16 * 1024 * 1024)
        {
            if (result != -3 || required <= 0 || required > maximum)
            { throw new InvalidOperationException("URUI 字符串读取失败：" + LastError()); }
            if (required > 256 * 1024) { return new byte[required]; }
            if (snapshotScratch == null || snapshotScratch.Length < required) { snapshotScratch = new byte[required]; }
            return snapshotScratch;
        }
        static void ValidateSnapshot(int result, int actual, int expected, byte[] bytes)
        {
            if (result != 1 || actual != expected || bytes[actual - 1] != 0) { throw new InvalidOperationException("URUI 字符串快照失效。"); }
        }
        static string SnapshotText(byte[] bytes, int count, string previous, string alternate = null)
        {
            // 每次仍读取原生的真实值；只复用相同文本，不依赖可能尚未推进的布局版本。
            if (MatchesSnapshot(bytes, count, previous)) { return previous; }
            if (MatchesSnapshot(bytes, count, alternate)) { return alternate; }
            return Encoding.UTF8.GetString(bytes, 0, count);
        }
        static bool MatchesSnapshot(byte[] bytes, int count, string value)
        {
            if (value == null || Encoding.UTF8.GetByteCount(value) != count) { return false; }
            if (snapshotComparison == null || snapshotComparison.Length < count) { snapshotComparison = new byte[count]; }
            Encoding.UTF8.GetBytes(value, 0, value.Length, snapshotComparison, 0);
            return new ReadOnlySpan<byte>(bytes, 0, count).SequenceEqual(new ReadOnlySpan<byte>(snapshotComparison, 0, count));
        }
    }
}
