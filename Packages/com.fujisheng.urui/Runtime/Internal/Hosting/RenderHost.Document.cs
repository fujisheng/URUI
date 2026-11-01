using System;
using URUI.Internal.Interop;

namespace URUI.Internal.Hosting
{
    // 宿主内部的文档读写入口；影响渲染内容的修改同时使已有记录失效。
    internal sealed partial class RenderHost
    {
        public void LoadMarkup(string markup)
        {
            if (active)
            {
                InvalidateRecording(true);
                ClearBatches();
                ClearFrameScratch();
                foreach (var mesh in meshes.Values)
                {
                    if (mesh.mesh)
                    {
                        Destroy(mesh.mesh);
                    }
                }

                meshes.Clear();
                foreach (var texture in textures.Values)
                {
                    if (texture)
                    {
                        Destroy(texture);
                    }
                }

                textures.Clear();
                buffer.Clear();
                if (NativeMethods.urui_load_document(markup.Replace("__URUI_FONT__", "Roboto")) == 0)
                {
                    throw new Exception("URUI document load: " + NativeMethods.LastError());
                }
            }
            else
            {
                InitialMarkup = markup;
            }
        }

        public void SetText(string id, string value)
        {
            if (!active)
            {
                return;
            }
#if URUI_DEBUG
            long t = phaseDiagnostics ? Now() : 0;
#endif
            NativeMethods.urui_set_text(id, value);
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

        public float GetScrollTop(string id) => active ? NativeMethods.urui_get_scroll_top(id) : -1f;
        public bool SetPixelStyle(string id, string property, float pixels) => active && NativeMethods.urui_set_pixel_style(id, property, pixels) != 0;
        public bool ElementExists(string id) => active && NativeMethods.urui_element_exists(id) == 1;

        public bool SetAttribute(string id, string name, string value)
        {
            if (!active)
            {
                return false;
            }
#if URUI_DEBUG
            long t = phaseDiagnostics ? Now() : 0;
#endif
            bool ok = NativeMethods.urui_set_attribute(id, name, value) == 1;
#if URUI_DEBUG
            if (phaseDiagnostics)
            {
                apiFrame.attributeMs += Millis(Now() - t);
                apiFrame.attributes++;
            }
#endif
            return ok;
        }

        public bool SetStyle(string id, string property, string value)
        {
            if (!active)
            {
                return false;
            }
#if URUI_DEBUG
            using var marker = propertyApiMarker.Auto();
            long t = phaseDiagnostics ? Now() : 0;
#endif
            bool ok = NativeMethods.urui_set_style(id, property, value) == 1;
#if URUI_DEBUG
            if (phaseDiagnostics)
            {
                apiFrame.styleMs += Millis(Now() - t);
                apiFrame.styles++;
            }
#endif
            return ok;
        }

        public bool SetTranslateScale2D(string id, float x, float y, float sx, float sy)
        {
            if (!active)
            {
                return false;
            }
#if URUI_DEBUG
            using var marker = propertyApiMarker.Auto();
            long t = phaseDiagnostics ? Now() : 0;
#endif
            bool ok = NativeMethods.urui_set_translate_scale_2d(id, x, y, sx, sy) == 1;
#if URUI_DEBUG
            if (phaseDiagnostics)
            {
                apiFrame.styleMs += Millis(Now() - t);
                apiFrame.styles++;
            }
#endif
            return ok;
        }

        public bool SetInnerRml(string id, string markup) => active && NativeMethods.urui_set_inner_rml(id, markup) == 1;
        public bool AppendElement(string parentId, string tag, string id) => active && NativeMethods.urui_append_element(parentId, tag, id) == 1;
        public bool RemoveElement(string id) => active && NativeMethods.urui_remove_element(id) == 1;

        public string LastError => active ? NativeMethods.LastError() : "URUI host is not active";
    }
}
