using System;
using System.IO;
using URUI.Authoring.Source;
static partial class Program
{
    static void ReviewRegression(string name)
    {
        string root = Path.Combine(Path.GetTempPath(), "urui-review-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Assets"));
        string a = Path.Combine(root, "Assets/a.css"), b = Path.Combine(root, "Assets/b.css");
        File.WriteAllText(a, ".a{}");
        try
        {
            if (name == "save-race")
            {
                var workspace = new SourceWorkspace(root, null, (stage, target) => { File.Replace(stage, target, null); File.WriteAllText(b, "external"); });
                var file = workspace.Open(a);
                workspace.EditBatch(new[] { new SourceChange(a, ".a{opacity:.5;}", file.Revision), new SourceChange(b, ".b{}", 0, true) });
                Throws(() => workspace.Save(a, b));
                Equal("external", File.ReadAllText(b));
                Equal(".a{}", File.ReadAllText(a));
            }
            else if (name == "rollback-external")
            {
                File.WriteAllText(b, ".b{}");
                int index = 0;
                var workspace = new SourceWorkspace(root, null, (stage, target) => { if (++index == 2) { File.WriteAllText(a, "external"); throw new IOException("injected"); } File.Replace(stage, target, null); });
                var first = workspace.Open(a);
                var second = workspace.Open(b);
                workspace.EditBatch(new[] { new SourceChange(a, ".a{opacity:.5;}", first.Revision), new SourceChange(b, ".b{opacity:.5;}", second.Revision) });
                Throws(() => workspace.Save(a, b));
                Equal("external", File.ReadAllText(a));
                Equal(true, Directory.GetFiles(Path.Combine(root, "Library/URUI/Authoring/Recovery"), "*.journal").Length > 0);
            }
            else
                throw new Exception("Unknown review case");
            Console.WriteLine("PASS review " + name);
        }
        finally { Directory.Delete(root, true); }
    }
}
