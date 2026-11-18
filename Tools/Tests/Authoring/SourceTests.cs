using System;
using System.Linq;
using URUI.Authoring.Source;

static partial class Program
{
    static int assertions;
    static void Equal<T>(T expected, T actual)
    {
        assertions++;
        if (!Equals(expected, actual))
            throw new Exception($"Expected [{expected}], got [{actual}]");
    }
    static void Throws(Action action)
    {
        assertions++;
        try
        {
            action();
        }
        catch (InvalidOperationException) { return; }
        throw new Exception("Expected rejection");
    }
    static void Main()
    {
        string localizationReview = Environment.GetEnvironmentVariable("URUI_LOCALIZATION_REVIEW_CASE");
        if (!string.IsNullOrEmpty(localizationReview))
        {
            LocalizationReviewTests(localizationReview);
            return;
        }
        string review = Environment.GetEnvironmentVariable("URUI_REVIEW_CASE");
        if (!string.IsNullOrEmpty(review))
        {
            ReviewRegression(review);
            return;
        }
        var html = "<!doctype html>\r\n<!-- 中文😀 > -->\r\n<html><head><style>div::before { content: '>'; }</style></head><body><div id='a' title='x > y' style='width: 10%; color: red'>原文😀</div><img src='a.png' /></body></html>";
        var doc = SourceDocument.Parse(html);
        Equal(html, doc.Text);
        Equal(6, doc.Nodes.Count);
        var div = doc.Nodes.Single(x => x.Tag == "div");
        Equal("x > y", div.Attribute("title"));
        Equal(html.Replace("title='x > y'", "title='&lt;新&gt; &amp; &#39;字&#39;'"), doc.SetAttribute(div.Key, "title", "<新> & '字'"));
        Equal(html.Replace("原文😀", "&lt;b&gt;😀&amp;"), doc.SetText(div.Key, "<b>😀&"));
        Equal(html.Replace("width: 10%;", "width: 40px;"), doc.SetStyle(div.Key, "width", "40px"));
        Equal(" /* preserve */ color: red; width: 4px;", CssSource.SetDeclaration(" /* preserve */ color: red; width: 1px;", "width", "4px"));
        Equal("color: red;", CssSource.SetDeclaration("color: red; width: 10%; width: 20px;", "width", null).Trim());
        Equal("background: url('a;b:c'); color: blue;", CssSource.SetDeclaration("background: url('a;b:c'); color: red;", "color", "blue"));
        Equal("color: blue; /* tail */", CssSource.SetDeclaration("color: red; /* tail */", "color", "blue"));
        var nested = SourceDocument.Parse("<body><div id='p'><span id='c'>hi</span></div><div id='q'></div></body>");
        var parent = nested.Nodes.Single(x => x.Attribute("id") == "p");
        var child = nested.Nodes.Single(x => x.Attribute("id") == "c");
        var other = nested.Nodes.Single(x => x.Attribute("id") == "q");
        Throws(() => nested.SetText(parent.Key, "lost children"));
        Throws(() => nested.Move(parent.Key, child.Key, -1));
        Equal("<body><div id='p'></div><div id='q'><span id='c'>hi</span></div></body>", nested.Move(child.Key, other.Key, -1));
        Equal("<body><div id='p'></div><div id='q'></div></body>", nested.Remove(child.Key));
        Equal("<body><div id='p'><span id='c'>hi</span><b>new</b></div><div id='q'></div></body>", nested.Insert(parent.Key, "<b>new</b>", -1));
        Throws(() => SourceDocument.Parse("<div><span></div>"));
        Throws(() => SourceDocument.Parse("<div title='broken>"));
        Throws(() => CssSource.SetDeclaration("width: 10px;", "width", "10px; color:red"));
        var boolean = SourceDocument.Parse("<input disabled class='field' />");
        Equal("<input disabled=\"false\" class='field' />", boolean.SetAttribute(boolean.Nodes[0].Key, "disabled", "false"));
        Equal("<input  class='field' />", boolean.SetAttribute(boolean.Nodes[0].Key, "disabled", null));
        var entities = SourceDocument.Parse("<div style='color:&#114;ed; width:10px; /* untouched */'></div>");
        Equal("<div style='color:&#114;ed; width:25%; /* untouched */'></div>", entities.SetStyle(entities.Nodes[0].Key, "width", "25%"));
        Equal("color: blue /* keep */;", CssSource.SetDeclaration("color: red /* keep */;", "color", "blue"));
        string sheet = "/* header */\r\n@media (min-width: 1px) { .card { color: red; } }\r\n.card { width:10px; }";
        var rules = CssSource.Rules(sheet);
        var widthRule = rules.Single(r => r.Prelude == ".card" && sheet.Substring(r.BodyStart, r.BodyEnd - r.BodyStart).Contains("width"));
        Equal(sheet.Replace("width:10px", "width:25%"), CssSource.SetRuleDeclaration(sheet, widthRule.Start, "width", "25%"));
        LocalizationTests();
        FamiliarityTests();
        WorkspaceTests();
        JsonTests();
        SequenceTests();
        AnimationSourceTests();
        Console.WriteLine($"PASS: {assertions} authoring assertions");
    }
    static partial void LocalizationReviewTests(string name);
    static partial void LocalizationTests();
    static partial void FamiliarityTests();
    static partial void WorkspaceTests();
    static partial void JsonTests();
    static partial void SequenceTests();
    static partial void AnimationSourceTests();
}
