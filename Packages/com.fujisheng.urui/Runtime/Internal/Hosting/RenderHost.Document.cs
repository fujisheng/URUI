using System;
using URUI.Internal.Interop;

namespace URUI.Internal.Hosting
{
    // 宿主内部的文档读写入口；影响渲染内容的修改同时使已有记录失效。
    internal sealed partial class RenderHost
    {
        public void SetText(ulong documentHandle, string id, string value)
        {
            if (!active)
            {
                return;
            }
#if URUI_DEBUG
            long t = phaseDiagnostics ? Now() : 0;
#endif
            NativeMethods.urui_set_text(documentHandle, id, value);
#if URUI_DEBUG
            if (phaseDiagnostics)
            {
                double ms = Millis(Now() - t);
                textMilliseconds += ms;
                frameTextMilliseconds += ms;
                textCalls++;
                apiFrame.textMs += ms;
                apiFrame.texts++;
            }
#endif
        }

        public float GetScrollTop(ulong documentHandle, string id) => active ? NativeMethods.urui_get_scroll_top(documentHandle, id) : -1f;
        public bool SetPixelStyle(ulong documentHandle, string id, string property, float pixels) => active && NativeMethods.urui_set_pixel_style(documentHandle, id, property, pixels) != 0;
        public bool ElementExists(ulong documentHandle, string id) => active && NativeMethods.urui_element_exists(documentHandle, id) == 1;

        public bool SetAttribute(ulong documentHandle, string id, string name, string value)
        {
            if (!active)
            {
                return false;
            }
#if URUI_DEBUG
            long t = phaseDiagnostics ? Now() : 0;
#endif
            bool ok = NativeMethods.urui_set_attribute(documentHandle, id, name, value) == 1;
#if URUI_DEBUG
            if (phaseDiagnostics)
            {
                apiFrame.attributeMs += Millis(Now() - t);
                apiFrame.attributes++;
            }
#endif
            return ok;
        }

        public bool SetStyle(ulong documentHandle, string id, string property, string value)
        {
            if (!active)
            {
                return false;
            }
#if URUI_DEBUG
            using var marker = propertyApiMarker.Auto();
            long t = phaseDiagnostics ? Now() : 0;
#endif
            bool ok = NativeMethods.urui_set_style(documentHandle, id, property, value) == 1;
#if URUI_DEBUG
            if (phaseDiagnostics)
            {
                apiFrame.styleMs += Millis(Now() - t);
                apiFrame.styles++;
            }
#endif
            return ok;
        }

        public bool SetTranslateScale2D(ulong documentHandle, string id, float x, float y, float sx, float sy)
        {
            if (!active)
            {
                return false;
            }
#if URUI_DEBUG
            using var marker = propertyApiMarker.Auto();
            long t = phaseDiagnostics ? Now() : 0;
#endif
            bool ok = NativeMethods.urui_set_translate_scale_2d(documentHandle, id, x, y, sx, sy) == 1;
#if URUI_DEBUG
            if (phaseDiagnostics)
            {
                apiFrame.styleMs += Millis(Now() - t);
                apiFrame.styles++;
            }
#endif
            return ok;
        }

        public bool SetInnerHtml(ulong documentHandle, string id, string markup) => active && NativeMethods.urui_set_inner_html(documentHandle, id, markup) == 1;
        public bool AppendElement(ulong documentHandle, string parentId, string tag, string id) => active && NativeMethods.urui_append_element(documentHandle, parentId, tag, id) == 1;
        public bool RemoveElement(ulong documentHandle, string id) => active && NativeMethods.urui_remove_element(documentHandle, id) == 1;

        public string LastError => active ? NativeMethods.LastError() : "URUI host is not active";
    }
}
