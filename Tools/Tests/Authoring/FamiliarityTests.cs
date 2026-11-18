using System;
using System.Reflection;
using URUI.Authoring.Source;

static partial class Program
{
    static partial void FamiliarityTests()
    {
        var type = typeof(Program).Assembly.GetType("URUI.Authoring.Presentation.NodePresentation");
        Equal(true, type != null);
        string Name(string tag, string id) => (string)type.GetMethod("Name").Invoke(null, new object[] { tag, id });
        bool Show(string tag, string id, bool full) => (bool)type.GetMethod("Visible").Invoke(null, new object[] { tag, id, full });
        Equal("ClaimButton", Name("button", "ClaimButton"));
        Equal("奖励_领取", Name("div", "奖励_领取"));
        Equal("div（未命名）", Name("div", null));
        Equal(false, Show("head", null, false));
        Equal(false, Show("style", null, false));
        Equal(false, Show("html", null, false));
        Equal(true, Show("div", null, false));
        Equal(true, Show("style", "boundStyle", false));
        Equal(true, Show("custom-widget", null, false));
        Equal(true, Show("head", null, true));
        Equal(false, Show("#text", null, false));
        var inspector = typeof(Program).Assembly.GetType("URUI.Authoring.Presentation.InspectorPresentation");
        Equal(true, inspector != null);
        string[] Sections(string tag) => (string[])inspector.GetMethod("SectionNames").Invoke(null, new object[] { tag });
        Equal(true, Array.IndexOf(Sections("img"), "图像 Image") >= 0);
        Equal(false, Array.IndexOf(Sections("img"), "文字 Text") >= 0);
        Equal(true, Array.IndexOf(Sections("button"), "交互 Interaction") >= 0);
        Equal(false, Array.IndexOf(Sections("div"), "图像 Image") >= 0);
        bool HasProperty(string tag, string property)
        {
            var sections = (System.Collections.IEnumerable)inspector.GetMethod("Sections").Invoke(null, new object[] { tag });
            foreach (var section in sections)
                if (Array.IndexOf((string[])section.GetType().GetProperty("Properties").GetValue(section), property) >= 0)
                    return true;
            return false;
        }
        Equal(false, HasProperty("input", "attr.min"));
        Equal(true, HasProperty("input", "attr.placeholder"));
        Equal(false, HasProperty("progress", "attr.placeholder"));
        bool HasControlProperty(string tag, string inputType, bool textContent, string property)
        {
            var sections = (System.Collections.IEnumerable)inspector.GetMethod("SectionsForControl").Invoke(null, new object[] { tag, inputType, textContent });
            foreach (var section in sections)
                if (Array.IndexOf((string[])section.GetType().GetProperty("Properties").GetValue(section), property) >= 0)
                    return true;
            return false;
        }
        Equal(true, HasControlProperty("div", null, true, "text"));
        Equal(false, HasControlProperty("div", null, false, "text"));
        Equal(false, HasControlProperty("input", "checkbox", false, "style.font-size"));
        Equal(false, HasControlProperty("input", "range", false, "attr.placeholder"));
        Equal(true, HasControlProperty("input", "range", false, "attr.min"));
        Equal(false, HasControlProperty("textarea", null, true, "text"));
        var fields = typeof(Program).Assembly.GetType("URUI.Authoring.Presentation.StyleFields");
        Equal(true, fields != null);
        string Number(string value, double number) => (string)fields.GetMethod("WithNumber").Invoke(null, new object[] { value, number });
        bool Complex(string value) => (bool)fields.GetMethod("RequiresExpressionEdit").Invoke(null, new object[] { value });
        Equal(true, Complex("20px !important"));
        Equal(true, Complex("calc(50% - 8px)"));
        Equal(true, Complex("var(--x)"));
        Equal(false, Complex("rgba(1,2,3,.4)"));
        Equal(" 42.5% ", Number(" 10% ", 42.5));
        Equal("12px", Number("10px", 12));
        Equal("0.3", Number(".5", .3));
        string Transform(string value, string part, params string[] components) => (string)fields.GetMethod("SetTransform").Invoke(null, new object[] { value, part, components });
        Equal("translate(10%,20px) rotate(40deg) scale(2,3)", Transform("translate(10%,20px) rotate(15deg) scale(2,3)", "rotate", "40deg"));
        Equal("rotate(40deg)", Transform("none", "rotate", "40deg"));
        foreach (string complex in new[] { "auto", "var(--size)", "calc(100% - 2px)", "10px !important" })
        {
            bool rejected = false;
            try
            {
                Number(complex, 20);
            }
            catch (TargetInvocationException error) when (error.InnerException is InvalidOperationException) { rejected = true; }
            Equal(true, rejected);
        }
        foreach (string complex in new[] { "matrix(1,0,0,1,10,20)", "translateX(20%) rotate(2deg)", "rotate(1rad)", "rotate(2deg) rotate(3deg)", "var(--transform)" })
        {
            bool rejected = false;
            try
            {
                Transform(complex, "rotate", "40deg");
            }
            catch (TargetInvocationException error) when (error.InnerException is InvalidOperationException) { rejected = true; }
            Equal(true, rejected);
        }
        foreach (string kind in new[] { "panel", "image", "text", "button", "input", "toggle", "slider", "progress", "particles" })
        {
            string markup = (string)type.GetMethod("Template").Invoke(null, new object[] { kind });
            var parsed = SourceDocument.Parse(markup);
            Equal(true, parsed.Roots.Count > 0);
            Equal(markup, parsed.Text);
        }
    }
}
