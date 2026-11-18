using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace URUI.Authoring.Source
{
    /// <summary>保真关键帧编辑。位置归一化到 0..1；只改命名动画内的目标规则，保留无关源码。</summary>
    public static class CssKeyframes
    {
        public sealed class Key
        {
            public double Position;
            public string Declarations;
            internal CssSource.Rule Rule;
        }
        public static IReadOnlyList<Key> Read(string text, string name)
        {
            var rules = CssSource.Rules(text);
            var parent = Find(rules, name);
            var result = new List<Key>();
            if (parent == null)
                return result;
            foreach (var rule in rules)
            {
                if (rule.Start <= parent.Start || rule.BodyEnd >= parent.BodyEnd)
                    continue;
                foreach (string part in rule.Prelude.Split(','))
                {
                    double position = ParsePosition(part.Trim());
                    result.Add(new Key { Position = position, Declarations = text.Substring(rule.BodyStart, rule.BodyEnd - rule.BodyStart), Rule = rule });
                    if (result.Count > 4096)
                        throw new InvalidOperationException("关键帧数量不能超过 4096。");
                }
            }
            return result;
        }
        public static string SetKey(string text, string name, double position, string property, string value)
        {
            RequirePosition(position);
            var keys = Read(text, name);
            var target = keys.LastOrDefault(k => Same(k.Position, position));
            if (target == null)
            {
                if (value == null)
                    return text;
                var parent = Find(CssSource.Rules(text), name);
                if (parent == null)
                    throw new InvalidOperationException("animation_not_found: " + name);
                if (keys.Count >= 4096)
                    throw new InvalidOperationException("关键帧数量不能超过 4096。");
                string newline = text.Contains("\r\n") ? "\r\n" : "\n";
                string key = "    " + Label(position) + " { " + CssSource.SetDeclaration("", property, value) + " }" + newline;
                return text.Insert(parent.BodyEnd, key);
            }
            if (value == null)
            {
                foreach (var key in keys.Where(k => Same(k.Position, position)).GroupBy(k => k.Rule.Start).Select(g => g.First()).OrderByDescending(k => k.Rule.Start))
                {
                    text = Separate(text, key, position, out var targetStart);
                    text = CssSource.SetRuleDeclaration(text, targetStart, property, null);
                }
                return text;
            }
            text = Separate(text, target, position, out var start);
            return CssSource.SetRuleDeclaration(text, start, property, value);
        }
        public static string RemoveKey(string text, string name, double position)
        {
            RequirePosition(position);
            foreach (var key in Read(text, name).Where(k => Same(k.Position, position)).GroupBy(k => k.Rule.Start).Select(g => g.First()).OrderByDescending(k => k.Rule.Start))
            {
                var remaining = key.Rule.Prelude.Split(',').Where(p => !Same(ParsePosition(p.Trim()), position)).ToArray();
                text = remaining.Length == 0 ? text.Remove(key.Rule.Start, key.Rule.BodyEnd + 1 - key.Rule.Start)
                    : ReplacePrelude(text, key.Rule, string.Join(", ", remaining.Select(p => p.Trim())));
            }
            return text;
        }
        public static string MoveKey(string text, string name, double from, double to)
        {
            RequirePosition(from);
            RequirePosition(to);
            if (Same(from, to))
                return text;
            var keys = Read(text, name);
            if (keys.Any(k => Same(k.Position, to)))
                throw new InvalidOperationException("目标位置已有关键帧，请先合并或删除。");
            var moving = keys.Where(k => Same(k.Position, from)).GroupBy(k => k.Rule.Start).Select(g => g.First()).OrderByDescending(k => k.Rule.Start).ToArray();
            if (moving.Length == 0)
                throw new InvalidOperationException("关键帧已失效。");
            foreach (var key in moving)
            {
                text = Separate(text, key, from, out int start);
                var rule = CssSource.Rules(text).First(r => r.Start == start);
                text = ReplacePrelude(text, rule, Label(to));
            }
            return text;
        }
        public static string CreatePreset(string text, string name, string preset)
        {
            SourceDocument.RequireName(name);
            if (Find(CssSource.Rules(text), name) != null)
                throw new InvalidOperationException("动画名称已存在。");
            string body;
            switch (preset)
            {
                case "popup":
                    body = "from { opacity: 0; transform: scale(.85); }\nto { opacity: 1; transform: scale(1); }";
                    break;
                case "fade-in":
                    body = "from { opacity: 0; }\nto { opacity: 1; }";
                    break;
                case "fade-out":
                    body = "from { opacity: 1; }\nto { opacity: 0; }";
                    break;
                case "slide-in":
                    body = "from { opacity: 0; transform: translateY(30px); }\nto { opacity: 1; transform: translateY(0px); }";
                    break;
                case "pulse":
                    body = "from { transform: scale(1); }\n50% { transform: scale(1.1); }\nto { transform: scale(1); }";
                    break;
                case "empty":
                    body = "from { opacity: 1; }\nto { opacity: 1; }";
                    break;
                default:
                    throw new InvalidOperationException("未知动画预设：" + preset);
            }
            string newline = text.Contains("\r\n") ? "\r\n" : "\n";
            return text + newline + "@keyframes " + name + " {" + newline + "    " + body.Replace("\n", newline + "    ") + newline + "}" + newline;
        }
        static string Separate(string text, Key key, double position, out int start)
        {
            start = key.Rule.Start;
            var parts = key.Rule.Prelude.Split(',');
            if (parts.Length == 1)
                return text;
            string remaining = string.Join(", ", parts.Where(p => !Same(ParsePosition(p.Trim()), position)).Select(p => p.Trim()));
            if (remaining.Length == 0)
                return text;
            string prefix = remaining + " {" + key.Declarations + "} ";
            string selected = Label(position) + " {" + key.Declarations + "}";
            text = text.Substring(0, key.Rule.Start) + prefix + selected + text.Substring(key.Rule.BodyEnd + 1);
            start += prefix.Length;
            return text;
        }
        static string ReplacePrelude(string text, CssSource.Rule rule, string value)
            => text.Substring(0, rule.Start) + value + " " + text.Substring(rule.BodyStart - 1);
        static CssSource.Rule Find(IReadOnlyList<CssSource.Rule> rules, string name)
        {
            CssSource.Rule result = null;
            foreach (var rule in rules)
                if (rule.Prelude == "@keyframes " + name)
                {
                    if (result != null)
                        throw new InvalidOperationException("同名关键帧定义不唯一：" + name);
                    result = rule;
                }
            return result;
        }
        static double ParsePosition(string text)
        {
            if (text == "from")
                return 0;
            if (text == "to")
                return 1;
            if (!text.EndsWith("%", StringComparison.Ordinal) || !double.TryParse(text.Substring(0, text.Length - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                throw new InvalidOperationException("无法可视化此关键帧位置：" + text);
            value /= 100;
            RequirePosition(value);
            return value;
        }
        static void RequirePosition(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0 || value > 1)
                throw new InvalidOperationException("关键帧位置必须在 0..1。");
        }
        static string Label(double value) => (value * 100).ToString("0.########", CultureInfo.InvariantCulture) + "%";
        static bool Same(double a, double b) => Math.Abs(a - b) < .000000001;
    }
}
