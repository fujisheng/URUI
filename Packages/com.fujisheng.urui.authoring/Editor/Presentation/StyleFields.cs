using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace URUI.Authoring.Presentation
{
    /// <summary>
    /// Lossless helpers for the small subset represented by numeric fields.
    /// </summary>
    public static class StyleFields
    {
        static readonly Regex number = new Regex(@"^(\s*)([+-]?(?:\d+(?:\.\d*)?|\.\d+))(px|%|em|rem|vw|vh|deg)?(\s*)$", RegexOptions.CultureInvariant);
        static readonly Regex function = new Regex(@"\G\s*(translate|rotate|scale)\(([^()]*)\)", RegexOptions.CultureInvariant);

        public static bool TryNumber(string value, out double result, out string unit)
        {
            var match = number.Match(value ?? "");
            unit = match.Groups[3].Value;
            result = 0;
            return match.Success && double.TryParse(match.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out result) && !double.IsInfinity(result) && !double.IsNaN(result);
        }

        public static bool RequiresExpressionEdit(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }
            return value.IndexOf('!') >= 0 || (value.IndexOf('(') >= 0 && !Regex.IsMatch(value, @"^\s*rgba?\([^()]*\)\s*$", RegexOptions.CultureInvariant));
        }

        public static string WithNumber(string original, double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || !TryNumber(original, out _, out _))
            {
                throw new InvalidOperationException("Use the expression field for this value.");
            }
            var match = number.Match(original);
            return match.Groups[1].Value + value.ToString("0.########", CultureInfo.InvariantCulture) + match.Groups[3].Value + match.Groups[4].Value;
        }

        public static bool TryTransform(string value, out Dictionary<string, string[]> parts)
        {
            parts = new Dictionary<string, string[]>(StringComparer.Ordinal);
            if (string.IsNullOrWhiteSpace(value) || value.Trim() == "none")
            {
                return true;
            }
            int position = 0;
            while (position < value.Length && value.Substring(position).Trim().Length != 0)
            {
                var match = function.Match(value, position);
                if (!match.Success)
                {
                    return false;
                }
                string name = match.Groups[1].Value;
                string[] values = match.Groups[2].Value.Split(',');
                if (parts.ContainsKey(name) || values.Length != (name == "rotate" ? 1 : 2))
                {
                    return false;
                }
                for (int i = 0; i < values.Length; i++)
                {
                    values[i] = values[i].Trim();
                    if (!TryNumber(values[i], out double parsed, out string unit))
                    {
                        return false;
                    }
                    if (name == "rotate" ? unit != "deg" : name == "scale" ? unit.Length != 0 : unit == "deg" || (unit.Length == 0 && parsed != 0))
                    {
                        return false;
                    }
                }
                parts.Add(name, values);
                position = match.Index + match.Length;
            }
            return true;
        }

        public static string SetTransform(string original, string part, string[] values)
        {
            string replacement = part + "(" + string.Join(",", values) + ")";
            if (!TryTransform(original, out _) || !TryTransform(replacement, out _) || (part != "translate" && part != "rotate" && part != "scale"))
            {
                throw new InvalidOperationException("Composite transforms remain editable as expressions.");
            }
            if (string.IsNullOrWhiteSpace(original) || original.Trim() == "none")
            {
                return replacement;
            }
            foreach (Match match in function.Matches(original))
            {
                if (match.Groups[1].Value == part)
                {
                    int start = match.Groups[1].Index;
                    return original.Substring(0, start) + replacement + original.Substring(match.Index + match.Length);
                }
            }
            return original.TrimEnd() + " " + replacement;
        }
    }
}
