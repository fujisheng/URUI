using System.Linq;
using NUnit.Framework;
using URUI.Editor.Authoring;
using UnityEngine;
using System.Collections.Generic;
using System.Text;
namespace URUI.Tests.Editor
{
    public sealed class HtmlCssValidatorTests
    {
        [Test]
        public void HtmlAuthoringExposesInlineAndEmbeddedStyleSources()
        {
            var result = HtmlCssValidator.Validate("<!doctype html><html><head><style>.a{background-image:url(a.png);}</style></head><body><div style=\"background-image:url(b.png)\"></div></body></html>", "ui/page.html", false);
            Assert.IsTrue(result.IsValid);
            Assert.AreEqual("background-image:url(b.png)", result.Nodes.Single(node => node.Tag == "div").InlineStyle);
            Assert.AreEqual(".a{background-image:url(a.png);}", result.Nodes.Single(node => node.Tag == "style").StyleText);
        }

        [Test]
        public void CssAuthoringExposesDecodedUrlsWithoutReparsingValues()
        {
            var result = HtmlCssValidator.ValidateCss(".x{background-image:url(icons/a\\ b.png),url(\"other.png\");}", "ui/page.css");
            Assert.IsFalse(result.Diagnostics.Any(issue => issue.IsError));
            CollectionAssert.AreEqual(new[] { "icons/a b.png", "other.png" }, result.Rules.Single().Declarations.Single().Urls);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CssDiagnosticsAndDeclarationsKeepOriginalUtf8Locations(bool descriptor)
        {
            var prefix = descriptor ? "/*中文*/\n@font-face {\n  " : "/*中文*/\n.x {\n  ";
            var result = HtmlCssValidator.ValidateCss(prefix + "font-weight:0; " +
                (descriptor ? "font-family:LocalFace;src:url(font.ttf);}" : "color:red;}"), "ui/author.css");
            var issue = result.Diagnostics.Single();
            Assert.AreEqual("ui/author.css", issue.Uri); Assert.AreEqual(3, issue.Line); Assert.AreEqual(3, issue.Column);
            Assert.AreEqual(Encoding.UTF8.GetByteCount(prefix), issue.ByteOffset);
            var declaration = result.Rules.Single().Declarations.First();
            Assert.AreEqual("font-weight", declaration.Property); Assert.AreEqual("0", declaration.Value);
            Assert.AreEqual(issue.ByteOffset, declaration.ByteOffset);
        }

        [Test]
        public void SpriteSheetAuthorRuleHasNoRuntimeCompatibilityDispatch()
        {
            var result=HtmlCssValidator.ValidateCss("@spritesheet atlas {src:atlas.png;icon:0px 0px 2px 2px;}","ui/old-atlas.css");
            Assert.IsTrue(result.Diagnostics.Any(issue=>issue.Code=="CSS_AT_RULE"));
        }

        [Test]
        [TestCase("0 274 0 27 fill",1)]
        [TestCase("0 273 0 27 fill",0)]
        [TestCase("50% fill",1)]
        [TestCase("50%",0)]
        public void BorderImageFillChecksSourceDimensionsWithoutRejectingValidCss(string slice,int expected)
        {
            var sheet=new TextAsset(".panel{border-image:url('panel).png') "+slice+" / 0 274px 0 27px stretch;}");
            try
            {
                var calls=0;
                var issues=StyleDependencyValidator.Validate(new[]{new KeyValuePair<string,TextAsset>("/ui/page.css",sheet)},null,uri=>
                {
                    Assert.AreEqual("/ui/panel).png",uri);calls++;return new Vector2Int(301,207);
                });
                Assert.AreEqual(expected,issues.Count);
                Assert.IsFalse(issues.Any(value=>value.IsError),"退化中心属于合法CSS，应给作者提示而非改变解析语义。");
                if(expected>0){Assert.AreEqual("empty-border-image-center",issues[0].Code);}
                if(slice.EndsWith("fill")){Assert.AreEqual(1,calls);}
            }
            finally{Object.DestroyImmediate(sheet);}
        }

        [Test]
        public void DependencyChecksUseDeclarationsAndIgnoreQuotedLookalikes()
        {
            var sheet = new TextAsset(".x{--label:'--urui-image-effect:fake;';--urui-image-effect:missing;}" +
                ".y{--urui-image-effect:inherit;}.z{--urui-image-effect:var(--chosen-effect);}");
            try
            {
                var issues = StyleDependencyValidator.Validate(new[] { new KeyValuePair<string,TextAsset>("ui/effects.css", sheet) }, new[] { "available" });
                Assert.AreEqual(1, issues.Count); StringAssert.Contains("missing", issues[0].Message);
                Assert.Greater(issues[0].Column, 1);
            }
            finally { Object.DestroyImmediate(sheet); }
        }

        [Test]
        public void StandardDocumentSelectorsAndKeyframeOverridesAreValid()
        {
            var first = new TextAsset("body{color:red;}@keyframes pulse{to{opacity:1;}}");
            var second = new TextAsset("@keyframes pulse{to{opacity:.5;}}");
            try
            {
                Assert.IsEmpty(StyleDependencyValidator.Validate(new[] {
                    new KeyValuePair<string,TextAsset>("ui/first.css", first),
                    new KeyValuePair<string,TextAsset>("ui/second.css", second) }));
            }
            finally { Object.DestroyImmediate(first); Object.DestroyImmediate(second); }
        }

        [Test]
        public void CompleteDocumentsAndFragmentsUseDifferentStrictEntryPoints()
        {
            const string fragment = "<div id='node' disabled></div>";
            Assert.IsTrue(HtmlCssValidator.Validate(fragment, "ui/fragment.html", true).IsValid);
            Assert.IsFalse(HtmlCssValidator.Validate(fragment, "ui/page.html", false).IsValid);
            var invalid = HtmlCssValidator.Validate("<div>\n<span>missing", "ui/missing.html", true);
            Assert.IsFalse(invalid.IsValid);
            Assert.IsTrue(invalid.Diagnostics.All(value => value.Uri == "ui/missing.html" && value.Line > 0));
        }
        [Test]
        public void InventoryExcludesRawTextMarkupAndMarksInertTemplateContent()
        {
            const string html = "<!doctype html><html><head><style>.a{content:'<span id=\"fake\">';}</style></head><body><div id='real'></div><template><button id='inert'>Text</button></template></body></html>";
            var result = HtmlCssValidator.Validate(html, "ui/page.html", false);
            Assert.IsTrue(result.IsValid);
            Assert.IsTrue(result.Nodes.Any(node => node.Id == "real" && !node.IsInert));
            Assert.IsTrue(result.Nodes.Any(node => node.Id == "inert" && node.IsInert));
            Assert.IsFalse(result.Nodes.Any(node => node.Id == "fake"));
        }
    }
}
