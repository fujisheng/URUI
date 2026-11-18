using System;
using System.IO;
using System.Linq;
using URUI.Authoring.Source;
static partial class Program
{
    static partial void AnimationSourceTests()
    {
        string css = "/* 中文😀 */\r\n@keyframes popup { from, 20% { opacity:0; /* keep */ } 80% { opacity: .8; } to { opacity:1; } }\r\n.x{color:red;}";
        var keys = CssKeyframes.Read(css, "popup");
        Equal(4, keys.Count);
        var changed = CssKeyframes.SetKey(css, "popup", .2, "opacity", ".3");
        Equal(true, changed.Contains("from { opacity:0; /* keep */ }"));
        Equal(true, changed.Contains("20% { opacity:.3; /* keep */ }"));
        Equal(true, changed.EndsWith("\r\n.x{color:red;}"));
        Equal(true, CssKeyframes.SetKey(css, "popup", .5, "transform", "translateX(10px)").Contains("50% { transform: translateX(10px); }"));
        Equal(3, CssKeyframes.Read(CssKeyframes.RemoveKey(css, "popup", .2), "popup").Count);
        Equal(4, CssKeyframes.Read(CssKeyframes.MoveKey(css, "popup", .2, .4), "popup").Count);
        Throws(() => CssKeyframes.MoveKey(css, "popup", .2, .8));
        Throws(() => CssKeyframes.SetKey(css, "popup", double.NaN, "opacity", "1"));
        Equal(2, CssKeyframes.Read("@keyframes a{50%{opacity:.2;}50%{transform:none;}}", "a").Count);
        string aliases = "@keyframes alias{from,0%{opacity:0;}to{opacity:1;}}.keep{color:red;}";
        Equal(true, CssKeyframes.RemoveKey(aliases, "alias", 0).EndsWith(".keep{color:red;}"));
        Equal(1, CssKeyframes.Read(CssKeyframes.RemoveKey(aliases, "alias", 0), "alias").Count);
        Equal(true, CssKeyframes.SetKey(aliases, "alias", 0, "opacity", ".3").Contains("from,0%{opacity:.3;}"));
        Equal(2, CssKeyframes.Read(CssKeyframes.MoveKey(aliases, "alias", 0, .2), "alias").Count);
        var siblings = SourceDocument.Parse("<body><div></div><div></div><section></section></body>");
        var identities = new NodeIdentities(siblings);
        string first = identities.Identity("n1"), second = identities.Identity("n2");
        var reordered = SourceDocument.Parse(siblings.Move("n1", "n3", -1));
        var mapped = identities.Transform(siblings, reordered, m => m.Move("n1", "n3", -1));
        Equal("n3", mapped.Key(first));
        Equal("n1", mapped.Key(second));
        Equal("n1", identities.Key(first));
        var root = Path.Combine(Path.GetTempPath(), "urui-animation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Assets"));
        try
        {
            var workspace = new SourceWorkspace(root);
            string path = Path.Combine(root, "Assets/new.urui-sequence.json");
            workspace.Create(path, "{}", workspace.Revision);
            Equal(false, File.Exists(path));
            Equal("{}", workspace.Open(path).Text);
            workspace.Undo();
            Equal(null, workspace.Open(path).Text);
            workspace.Redo();
            Equal("{}", workspace.Open(path).Text);
            File.WriteAllText(path, "external");
            Throws(() => workspace.Save(path));
            File.Delete(path);
            workspace.Save(path);
            Equal("{}", File.ReadAllText(path));
            Equal(false, workspace.Open(path).Dirty);
            string a = Path.Combine(root, "Assets/a.css"), b = Path.Combine(root, "Assets/b.css");
            File.WriteAllText(a, ".a{}");
            int moves = 0;
            var failure = new SourceWorkspace(root, null, (stage, target) => { if (++moves == 1) throw new IOException("injected"); if (File.Exists(target)) File.Replace(stage, target, null); else File.Move(stage, target); });
            var file = failure.Open(a);
            failure.EditBatch(new[] { new SourceChange(b, ".b{}", 0, true), new SourceChange(a, ".a{color:red;}", file.Revision) });
            Throws(() => failure.Save(b, a));
            Equal(".a{}", File.ReadAllText(a));
            Equal(false, File.Exists(b));
            Throws(() => workspace.Create("Assets/unsupported.json", "{}", workspace.Revision));
        }
        finally { Directory.Delete(root, true); }
    }
}
