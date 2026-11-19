using System;
using System.Collections.Generic;

namespace URUI.Authoring.Source
{
    /// <summary>
    /// 在声明列表中修改一个值，保留不相关注释、顺序和单位。
    /// </summary>
    public static class CssSource
    {
        public sealed class Rule
        {
            public int Start;
            public int BodyStart;
            public int BodyEnd;
            public string Prelude;
        }
        public static IReadOnlyList<Rule> Rules(string text)
        {
            var result = new List<Rule>();
            var stack = new Stack<Rule>();
            int boundary = 0;
            char quote = '\0';
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (quote != '\0')
                {
                    if (c == '\\')
                    {
                        i++;
                    }
                    else if (c == quote)
                    {
                        quote = '\0';
                    }
                    continue;
                }
                if (c == '\'' || c == '"')
                {
                    quote = c;
                    continue;
                }
                if (c == '/' && i + 1 < text.Length && text[i + 1] == '*')
                {
                    int end = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    if (end < 0)
                    {
                        throw new InvalidOperationException("Unclosed CSS comment.");
                    }
                    i = end + 1;
                    continue;
                }
                if (c == '{')
                {
                    int start = SkipTrivia(text, boundary, i);
                    stack.Push(new Rule
                    {
                        Start = start,
                        BodyStart = i + 1,
                        Prelude = text.Substring(start, i - start).Trim()
                    });
                    boundary = i + 1;
                }
                else if (c == '}')
                {
                    if (stack.Count == 0)
                    {
                        throw new InvalidOperationException("Unbalanced CSS rule.");
                    }
                    var rule = stack.Pop();
                    rule.BodyEnd = i;
                    result.Add(rule);
                    boundary = i + 1;
                }
                else if (c == ';')
                {
                    boundary = i + 1;
                }
            }
            if (stack.Count != 0 || quote != '\0')
            {
                throw new InvalidOperationException("Unclosed CSS rule.");
            }
            result.Sort((a, b) => a.Start.CompareTo(b.Start));
            return result;
        }
        public static string SetRuleDeclaration(string text, int ruleStart, string property, string value)
        {
            var rules = Rules(text);
            Rule target = null;
            foreach (var rule in rules)
            {
                if (rule.Start == ruleStart)
                {
                    target = rule;
                }
            }
            if (target == null)
            {
                throw new InvalidOperationException("source_conflict: CSS rule no longer exists");
            }
            foreach (var rule in rules)
            {
                if (rule.Start > target.Start && rule.BodyEnd < target.BodyEnd)
                {
                    throw new InvalidOperationException("Select an individual style rule, not its containing at-rule.");
                }
            }
            return text.Substring(0, target.BodyStart) + SetDeclaration(text.Substring(target.BodyStart, target.BodyEnd - target.BodyStart), property, value) + text.Substring(target.BodyEnd);
        }
        public sealed class Declaration
        {
            public string Name;
            public int Start;
            public int End;
            public int ValueStart;
            public int ValueEnd;
        }

        public static IReadOnlyList<Declaration> Declarations(string text)
        {
            var result = new List<Declaration>();
            int start = 0;
            foreach (int end in Separators(text, ';'))
            {
                Add(text, start, end, end < text.Length ? end + 1 : end, result);
                start = end + 1;
            }
            return result;
        }

        public static string GetDeclaration(string text, string property)
        {
            string result = null;
            foreach (var d in Declarations(text))
            {
                if (d.Name == property)
                {
                    result = text.Substring(d.ValueStart, d.ValueEnd - d.ValueStart);
                }
            }
            return result;
        }

        public static string SetDeclaration(string text, string property, string value)
        {
            SourceDocument.RequireName(property);
            if (value != null)
            {
                foreach (int separator in Separators(value, ';'))
                {
                    if (separator < value.Length)
                    {
                        throw new InvalidOperationException("A CSS value cannot contain another declaration.");
                    }
                }
                if (value.IndexOf('{') >= 0 || value.IndexOf('}') >= 0)
                {
                    throw new InvalidOperationException("Invalid CSS value.");
                }
            }
            var declarations = Declarations(text);
            if (value == null)
            {
                for (int i = declarations.Count - 1; i >= 0; i--)
                {
                    if (declarations[i].Name == property)
                    {
                        text = text.Remove(declarations[i].Start, declarations[i].End - declarations[i].Start);
                    }
                }
                return text;
            }
            for (int i = declarations.Count - 1; i >= 0; i--)
            {
                var d = declarations[i];
                if (d.Name == property)
                {
                    return text.Substring(0, d.ValueStart) + value + text.Substring(d.ValueEnd);
                }
            }
            string prefix = text.Length == 0 ? "" : text.TrimEnd().EndsWith(";", StringComparison.Ordinal) ? " " : "; ";
            return text + prefix + property + ": " + value + ";";
        }

        static void Add(string text, int start, int end, int fullEnd, List<Declaration> result)
        {
            int nameStart = SkipTrivia(text, start, end);
            int colon = -1;
            foreach (int at in Separators(text.Substring(nameStart, end - nameStart), ':'))
            {
                if (at < end - nameStart)
                {
                    colon = nameStart + at;
                    break;
                }
            }
            if (colon < 0)
            {
                return;
            }
            string name = text.Substring(nameStart, colon - nameStart).Trim();
            int valueStart = colon + 1;
            int valueEnd = end;
            while (valueStart < end && char.IsWhiteSpace(text[valueStart]))
            {
                valueStart++;
            }
            while (valueEnd > valueStart && char.IsWhiteSpace(text[valueEnd - 1]))
            {
                valueEnd--;
            }
            while (valueEnd >= valueStart + 2 && text[valueEnd - 2] == '*' && text[valueEnd - 1] == '/')
            {
                int comment = text.LastIndexOf("/*", valueEnd - 2, StringComparison.Ordinal);
                if (comment < valueStart)
                {
                    break;
                }
                valueEnd = comment;
                while (valueEnd > valueStart && char.IsWhiteSpace(text[valueEnd - 1]))
                {
                    valueEnd--;
                }
            }
            result.Add(new Declaration
            {
                Name = name,
                Start = nameStart,
                End = fullEnd,
                ValueStart = valueStart,
                ValueEnd = valueEnd
            });
        }
        static int SkipTrivia(string text, int start, int end)
        {
            while (start < end)
            {
                if (char.IsWhiteSpace(text[start]))
                {
                    start++;
                    continue;
                }
                if (start + 1 < end && text[start] == '/' && text[start + 1] == '*')
                {
                    int close = text.IndexOf("*/", start + 2, StringComparison.Ordinal);
                    if (close < 0 || close >= end)
                    {
                        return end;
                    }
                    start = close + 2;
                    continue;
                }
                break;
            }
            return start;
        }
        static IEnumerable<int> Separators(string text, char separator)
        {
            char quote = '\0';
            int depth = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (quote != '\0')
                {
                    if (c == '\\')
                    {
                        i++;
                    }
                    else if (c == quote)
                    {
                        quote = '\0';
                    }
                    continue;
                }
                if (c == '\'' || c == '"')
                {
                    quote = c;
                    continue;
                }
                if (c == '/' && i + 1 < text.Length && text[i + 1] == '*')
                {
                    int end = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    if (end < 0)
                    {
                        throw new InvalidOperationException("Unclosed CSS comment.");
                    }
                    i = end + 1;
                    continue;
                }
                if (c == '(' || c == '[')
                {
                    depth++;
                }
                else if (c == ')' || c == ']')
                {
                    if (--depth < 0)
                    {
                        throw new InvalidOperationException("Unbalanced CSS value.");
                    }
                }
                else if (c == separator && depth == 0)
                {
                    yield return i;
                }
            }
            if (quote != '\0' || depth != 0)
            {
                throw new InvalidOperationException("Unclosed CSS value.");
            }
            yield return text.Length;
        }
    }
}
