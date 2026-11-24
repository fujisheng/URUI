using System;
using System.IO;
using System.Text;
using URUI.Authoring.Source;

static partial class Program
{
    static partial void WorkspaceTests()
    {
        string root = Path.Combine(Path.GetTempPath(), "urui-authoring-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Assets"));
        try
        {
            string a = Path.Combine(root, "Assets/a.html"), b = Path.Combine(root, "Assets/b.css");
            var utf8 = new UTF8Encoding(true);
            File.WriteAllText(a, "<body>中文😀</body>\r\n", utf8);
            File.WriteAllText(b, "body { color:red; }", new UTF8Encoding(false));
            byte[] original = File.ReadAllBytes(a);
            var work = new SourceWorkspace(root);
            var file = work.Open(a);
            Equal(false, file.Dirty);
            work.Save(a);
            Equal(Convert.ToBase64String(original), Convert.ToBase64String(File.ReadAllBytes(a)));
            long revision = file.Revision;
            work.Edit(a, "<body>edited</body>", revision);
            Equal("<body>中文😀</body>\r\n", File.ReadAllText(a));
            Throws(() => work.Edit(a, "<body>stale</body>", revision));
            work.Undo(); Equal("<body>中文😀</body>\r\n", file.Text);
            work.Redo(); Equal("<body>edited</body>", file.Text);
            work.Save(a); Equal("<body>edited</body>", File.ReadAllText(a));
            Equal((byte)0xef, File.ReadAllBytes(a)[0]);
            work.Edit(a, "<body>local</body>", file.Revision);
            File.WriteAllText(a, "<body>external</body>");
            Throws(() => work.Save(a));
            Equal("<body>local</body>", file.Text);
            Equal("<body>external</body>", File.ReadAllText(a));
            Throws(() => work.Reload(a, false));
            work.Reload(a, true); Equal("<body>external</body>", file.Text);
            work.Open(b);
            long before = file.Revision;
            Throws(() => work.EditBatch(new[] { new SourceChange(a, "<body>batch</body>", file.Revision), new SourceChange(b, "invalid", -1) }));
            Equal(before, file.Revision);
            Equal("<body>external</body>", file.Text);
            int replaceAttempt = 0;
            var transactional = new SourceWorkspace(root, null, (stage, target) =>
            {
                if (++replaceAttempt == 2) throw new IOException("Injected second-file replacement failure");
                File.Replace(stage, target, null);
            });
            var first = transactional.Open(a); var second = transactional.Open(b);
            string firstBefore = File.ReadAllText(a), secondBefore = File.ReadAllText(b);
            transactional.Edit(a, "<body>transaction</body>", first.Revision);
            transactional.Edit(b, "body { color:purple; }", second.Revision);
            Throws(() => transactional.Save(a, b));
            Equal(firstBefore, File.ReadAllText(a)); Equal(secondBefore, File.ReadAllText(b));
            Equal(true, first.Dirty); Equal(true, second.Dirty);
            transactional.Save(a, b);
            Equal("<body>transaction</body>", File.ReadAllText(a)); Equal("body { color:purple; }", File.ReadAllText(b));
            Equal(false, first.Dirty); Equal(false, second.Dirty);
            // Continue the independent conflict checks with their original known disk state.
            File.WriteAllText(a, firstBefore); File.WriteAllText(b, secondBefore);
            work.Edit(a, "<body>waiting</body>", file.Revision);
            work.Edit(b, "body { color:blue; }", work.Open(b).Revision);
            File.WriteAllText(b, "body { color:green; }");
            Throws(() => work.Save(a, b));
            Equal("<body>external</body>", File.ReadAllText(a));
            Throws(() => work.Open(Path.Combine(root, "../outside.html")));
            Throws(() => work.Open(Path.Combine(root, "Library/private.html")));
            var validate = new SourceWorkspace(root, (path, text) => text.Contains("broken") ? "bad candidate" : null);
            var checkedFile = validate.Open(a);
            Throws(() => validate.Edit(a, "broken", checkedFile.Revision));
            Equal("<body>external</body>", checkedFile.Text);
            File.WriteAllText(a, "broken external");
            Throws(() => validate.Reload(a, false));
            Equal("<body>external</body>", checkedFile.Text);
            // Negative leak regression: opening many clean files must be prunable without dropping dirty or active files.
            var pruneWork = new SourceWorkspace(root);
            string dirtyPath = Path.Combine(root, "Assets/dirty.html");
            File.WriteAllText(dirtyPath, "<div>dirty</div>");
            var dirtyFile = pruneWork.Open(dirtyPath);
            pruneWork.Edit(dirtyPath, "<div>edited</div>", dirtyFile.Revision);
            string keepPath = null;
            for (int i = 0; i < 40; i++)
            {
                string p = Path.Combine(root, $"Assets/clean_{i}.html");
                File.WriteAllText(p, $"<div>{i}</div>");
                pruneWork.Open(p);
                keepPath = p;
            }
            pruneWork.PruneCleanUnused(keepPath);
            int remaining = 0;
            foreach (var _ in pruneWork.Files) remaining++;
            Equal(2, remaining);
        }
        finally { Directory.Delete(root, true); }
    }
}
