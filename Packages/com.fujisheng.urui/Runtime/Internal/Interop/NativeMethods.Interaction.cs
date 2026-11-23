using System;
using System.Runtime.InteropServices;
using System.Text;

namespace URUI.Internal.Interop
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct ElementEventNative
    {
        internal ulong surface;
        internal ulong document;
        internal ulong target;
        internal ulong current;
        internal int type;
        internal int phase;
        internal int pointer;
        internal int button;
        internal int buttons;
        internal int modifiers;
        internal int key;
        internal int kind;
        internal float x;
        internal float y;
        internal float dx;
        internal float dy;
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
        internal static extern int urui_element_local_transform(ulong surface, ulong documentHandle, ulong token, [Out] float[] values, int capacity);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_set_event_callback(ulong surface, ElementEventCallback callback);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_document_listen(ulong surface, ulong documentHandle);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_element_listen(ulong surface, ulong documentHandle, ulong token, int type, int capture, int enabled);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_element_box(ulong surface, ulong documentHandle, ulong token, [Out] float[] values, int capacity);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_element_viewport(ulong surface, ulong documentHandle, ulong token, [Out] float[] values, int capacity);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_element_point(ulong surface, ulong documentHandle, ulong token, int toWorld, float x, float y, [Out] float[] values);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong urui_element_pick(ulong surface, ulong documentHandle, float x, float y);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_element_focus(ulong surface, ulong documentHandle, ulong token, int focus);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong urui_focused_element(ulong surface, ulong documentHandle);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_element_scroll(ulong surface, ulong documentHandle, ulong token, float x, float y);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_control_value_write(ulong surface, ulong documentHandle, ulong token,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string value);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_control_value_read(ulong surface, ulong documentHandle, ulong token, [Out] byte[] value, int capacity, out int required);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_element_style_read(ulong surface, ulong documentHandle, ulong token, int local,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string property, [Out] byte[] value, int capacity, out int required);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_pointer_poll(ulong surface, int pointer, int kind, float x, float y, int buttons, int modifiers);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_pointer_event(ulong surface, int pointer, int kind, int phase, float x, float y, int button, int buttons, float dx, float dy, int modifiers);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_pointer_capture(ulong surface, ulong documentHandle, int pointer, ulong token);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong urui_pointer_captured(ulong surface, ulong documentHandle, int pointer);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_input_reset(ulong surface);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_cancel_pointers(ulong surface, ulong documentHandle, ulong owner);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_key_event(ulong surface, int key, int pressed, int modifiers);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_text_input(ulong surface, [MarshalAs(UnmanagedType.LPUTF8Str)] string text);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_text_composition(ulong surface, [MarshalAs(UnmanagedType.LPUTF8Str)] string text, int operation);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_text_selection(ulong surface, ref int start, ref int end, int write);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong urui_clipboard_version(ulong surface);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_clipboard_write(ulong surface, [MarshalAs(UnmanagedType.LPUTF8Str)] string text);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_clipboard_read(ulong surface, [Out] byte[] text, int capacity, out int required);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_process_deferred_operations(ulong surface);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_select_option_count(ulong surface, ulong documentHandle, ulong token);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_select_option_read(ulong surface, ulong documentHandle, ulong token, int index, [Out] byte[] value, int capacity, out int required);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_select_option_add(ulong surface, ulong documentHandle, ulong token,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string value);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_select_options_clear(ulong surface, ulong documentHandle, ulong token);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_select_selection(ulong surface, ulong documentHandle, ulong token, int index);

        // Zero-closure single-attempt optimistic UTF-8 reads into thread-static scratch.
        internal static string ReadControl(ulong surface, ulong documentHandle, ulong token)
        {
            var bytes = snapshotScratch ??= new byte[256];
            int result = urui_control_value_read(surface, documentHandle, token, bytes, bytes.Length, out int required);
            if (result == NativeStatus.Absent)
            {
                return null;
            }
            int actual = required;
            if (result == NativeStatus.BufferTooSmall)
            {
                bytes = SnapshotBuffer(surface, result, required);
                result = urui_control_value_read(surface, documentHandle, token, bytes, required, out actual);
            }
            ValidateSnapshot(result, actual, required, bytes);
            return Encoding.UTF8.GetString(bytes, 0, actual - 1);
        }

        [ThreadStatic]
        static byte[] styleNumberScratch;
        internal static ReadOnlySpan<byte> ReadStyleNumberText(ulong surface, ulong documentHandle, ulong token, string property)
        {
            // 原生有限 float 的规范文本和单位可放入固定缓冲；复杂 CSS 值不是长度。
            var bytes = styleNumberScratch ??= new byte[64];
            var result = urui_element_style_read(surface, documentHandle, token, 0, property, bytes, bytes.Length, out int required);
            if (result == NativeStatus.Absent || (result == NativeStatus.BufferTooSmall && required > bytes.Length))
            {
                return default;
            }
            ValidateSnapshot(result, required, required, bytes);
            return new ReadOnlySpan<byte>(bytes, 0, required - 1);
        }

        internal static string ReadStyle(ulong surface, ulong documentHandle, ulong token, string property, bool local, string previous = null, string alternate = null)
        {
            var bytes = snapshotScratch ??= new byte[256];
            int localFlag = local ? 1 : 0;
            int result = urui_element_style_read(surface, documentHandle, token, localFlag, property, bytes, bytes.Length, out int required);
            if (result == NativeStatus.Absent)
            {
                return null;
            }
            int actual = required;
            if (result == NativeStatus.BufferTooSmall)
            {
                bytes = SnapshotBuffer(surface, result, required);
                result = urui_element_style_read(surface, documentHandle, token, localFlag, property, bytes, required, out actual);
            }
            ValidateSnapshot(result, actual, required, bytes);
            return SnapshotText(bytes, actual - 1, previous, alternate);
        }

        internal static string ReadOption(ulong surface, ulong documentHandle, ulong token, int index)
        {
            var bytes = snapshotScratch ??= new byte[256];
            int result = urui_select_option_read(surface, documentHandle, token, index, bytes, bytes.Length, out int required);
            if (result == NativeStatus.Absent)
            {
                return null;
            }
            int actual = required;
            if (result == NativeStatus.BufferTooSmall)
            {
                bytes = SnapshotBuffer(surface, result, required);
                result = urui_select_option_read(surface, documentHandle, token, index, bytes, required, out actual);
            }
            ValidateSnapshot(result, actual, required, bytes);
            return Encoding.UTF8.GetString(bytes, 0, actual - 1);
        }

        [ThreadStatic]
        static string clipboardSnapshot;
        internal static string ReadClipboard(ulong surface)
        {
            var bytes = snapshotScratch ??= new byte[256];
            int result = urui_clipboard_read(surface, bytes, bytes.Length, out int required);
            if (result == NativeStatus.Absent)
            {
                clipboardSnapshot = null;
                return null;
            }
            int actual = required;
            if (result == NativeStatus.BufferTooSmall)
            {
                bytes = SnapshotBuffer(surface, result, required);
                result = urui_clipboard_read(surface, bytes, required, out actual);
            }
            ValidateSnapshot(result, actual, required, bytes);
            return clipboardSnapshot = SnapshotText(bytes, actual - 1, clipboardSnapshot);
        }

        [ThreadStatic]
        static byte[] snapshotScratch;
        [ThreadStatic]
        static byte[] snapshotComparison;

        static byte[] SnapshotBuffer(ulong surface, int result, int required, int maximum = 16 * 1024 * 1024)
        {
            if (result != NativeStatus.BufferTooSmall || required <= 0 || required > maximum)
            {
                throw new InvalidOperationException("URUI 字符串读取失败：" + LastError(surface));
            }
            return GrowScratch(ref snapshotScratch, required, 256 * 1024);
        }

        static void ValidateSnapshot(int result, int actual, int expected, byte[] bytes)
        {
            if (result != NativeStatus.Ok || actual != expected || actual <= 0 || actual > bytes.Length || bytes[actual - 1] != 0)
            {
                throw new InvalidOperationException("URUI 字符串快照失效。");
            }
        }

        static string SnapshotText(byte[] bytes, int count, string previous, string alternate = null)
        {
            // 每次仍读取原生的真实值；只复用相同文本，不依赖可能尚未推进的布局版本。
            if (MatchesSnapshot(bytes, count, previous))
            {
                return previous;
            }
            if (MatchesSnapshot(bytes, count, alternate))
            {
                return alternate;
            }
            return Encoding.UTF8.GetString(bytes, 0, count);
        }

        static bool MatchesSnapshot(byte[] bytes, int count, string value)
        {
            if (value == null || Encoding.UTF8.GetByteCount(value) != count)
            {
                return false;
            }
            if (snapshotComparison == null || snapshotComparison.Length < count)
            {
                GrowScratch(ref snapshotComparison, count, 256 * 1024);
            }
            Encoding.UTF8.GetBytes(value, 0, value.Length, snapshotComparison, 0);
            return new ReadOnlySpan<byte>(bytes, 0, count).SequenceEqual(new ReadOnlySpan<byte>(snapshotComparison, 0, count));
        }
    }
}
