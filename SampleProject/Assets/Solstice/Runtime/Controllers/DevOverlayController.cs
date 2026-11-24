using System;
using URUI.Controls;
using URUI.Documents;
using URUI.Elements;
using URUI.Hosting;

namespace Solstice
{
    /// <summary>
    /// Controls Dev Telemetry Drawer querying `Surface.GetRenderingStatistics()` live every frame
    /// to demonstrate zero-allocation steady-state native render plans.
    /// </summary>
    public sealed class DevOverlayController : IDisposable
    {
        readonly SolsticeApp app;
        readonly Surface surface;
        readonly Document doc;
        readonly CompositeDisposable subs = new();

        Element drawer;
        Element statDocs;
        Element statDraws;
        Element statCmds;
        Element statPlan;
        Element statAlloc;
        Element statGeom;
        bool open;

        public bool IsOpen => open;

        public DevOverlayController(SolsticeApp app, Surface surface, Document doc)
        {
            this.app = app;
            this.surface = surface;
            this.doc = doc;

            drawer = doc.Q("dev-drawer");
            statDocs = doc.Q("stat-docs");
            statDraws = doc.Q("stat-draws");
            statCmds = doc.Q("stat-cmds");
            statPlan = doc.Q("stat-plan");
            statAlloc = doc.Q("stat-alloc");
            statGeom = doc.Q("stat-geom");

            var closeBtn = doc.Q<Button>("btn-dev-close");
            if (closeBtn != null)
            {
                Action cb = () => app.SetDevOpen(false);
                closeBtn.Clicked += cb;
                subs.Add(new DisposableAction(() => closeBtn.Clicked -= cb));
            }
        }

        public void SetOpen(bool visible)
        {
            open = visible;
            if (drawer != null)
            {
                drawer.Style.Set("display", open ? "flex" : "none");
            }
            if (open) RefreshMetrics();
        }

        public void Tick()
        {
            if (open && doc != null && doc.IsLoaded)
            {
                RefreshMetrics();
            }
        }

        public void RefreshMetrics()
        {
            if (!surface || doc == null || !doc.IsLoaded) return;
            var stats = surface.GetRenderingStatistics();
            if (statDocs != null) statDocs.Text = stats.LoadedDocuments.ToString();
            if (statDraws != null) statDraws.Text = stats.RecordedGeometryDraws.ToString();
            if (statCmds != null) statCmds.Text = stats.NativePlanCommandCount.ToString();
            if (statPlan != null) statPlan.Text = $"{stats.NativePlanFrameBytes:N0} B";
            if (statAlloc != null) statAlloc.Text = $"{stats.NativePlanAllocations} / {stats.NativePlanFrees}";
            if (statGeom != null) statGeom.Text = $"{stats.GeometryUploadBytes:N0} B";
        }

        public void Dispose()
        {
            subs.Dispose();
        }
    }
}
