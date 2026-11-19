using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace URUI.Internal.Serialization
{
    /// <summary>
    /// 协议使用的有界 JSON 值读取器；拒绝重复键、非有限数字与不完整 Unicode。
    /// </summary>
    internal static class ValueJson
    {
        public static object Parse(string text)
        {
            if (text == null || text.Length > 1048576)
            {
                throw new InvalidOperationException("bad_request: JSON size limit");
            }
            var reader = new Reader(text);
            object value = reader.Value(0);
            reader.Space();
            if (reader.Position != text.Length)
            {
                throw new InvalidOperationException("bad_request: trailing JSON content");
            }
            return value;
        }
        public static string Stringify(object value)
        {
            var text = new StringBuilder();
            Write(text, value, 0);
            return text.ToString();
        }
        static void Write(StringBuilder text, object value, int depth)
        {
            if (depth > 64)
            {
                throw new InvalidOperationException("JSON depth limit");
            }
            if (value == null)
            {
                text.Append("null");
                return;
            }
            if (value is string s)
            {
                String(text, s);
                return;
            }
            if (value is bool b)
            {
                text.Append(b ? "true" : "false");
                return;
            }
            if (value is Enum)
            {
                String(text, value.ToString());
                return;
            }
            if (value is byte || value is sbyte || value is short || value is ushort || value is int || value is uint || value is long || value is ulong || value is float || value is double || value is decimal)
            {
                double number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                if (double.IsNaN(number) || double.IsInfinity(number))
                {
                    throw new InvalidOperationException("JSON numbers must be finite.");
                }
                text.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
                return;
            }
            if (value is IDictionary dictionary)
            {
                text.Append('{');
                bool first = true;
                foreach (DictionaryEntry pair in dictionary)
                {
                    if (!(pair.Key is string key))
                    {
                        throw new InvalidOperationException("JSON object keys must be strings.");
                    }
                    if (!first)
                    {
                        text.Append(',');
                    }
                    first = false;
                    String(text, key);
                    text.Append(':');
                    Write(text, pair.Value, depth + 1);
                }
                text.Append('}');
                return;
            }
            if (value is IEnumerable list)
            {
                text.Append('[');
                bool first = true;
                foreach (object item in list)
                {
                    if (!first)
                    {
                        text.Append(',');
                    }
                    first = false;
                    Write(text, item, depth + 1);
                }
                text.Append(']');
                return;
            }
            var fields = value.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance);
            if (fields.Length == 0)
            {
                throw new InvalidOperationException("Protocol results require explicit fields or dictionaries.");
            }
            text.Append('{');
            for (int i = 0; i < fields.Length; i++)
            {
                if (i != 0)
                {
                    text.Append(',');
                }
                string name = fields[i].Name;
                String(text, char.ToLowerInvariant(name[0]) + name.Substring(1));
                text.Append(':');
                Write(text, fields[i].GetValue(value), depth + 1);
            }
            text.Append('}');
        }
        static void String(StringBuilder text, string value)
        {
            ValidateUnicode(value);
            text.Append('"');
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"':
                        text.Append("\\\"");
                        break;
                    case '\\':
                        text.Append("\\\\");
                        break;
                    case '\n':
                        text.Append("\\n");
                        break;
                    case '\r':
                        text.Append("\\r");
                        break;
                    case '\t':
                        text.Append("\\t");
                        break;
                    default:
                        if (c < 32)
                        {
                            text.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            text.Append(c);
                        }
                        break;
                }
            }
            text.Append('"');
        }
        static void ValidateUnicode(string value)
        {
            for (int i = 0; i < value.Length; i++)
            {
                if (char.IsHighSurrogate(value[i]))
                {
                    if (++i >= value.Length || !char.IsLowSurrogate(value[i]))
                    {
                        throw new InvalidOperationException("bad_request: incomplete Unicode scalar");
                    }
                }
                else if (char.IsLowSurrogate(value[i]))
                {
                    throw new InvalidOperationException("bad_request: incomplete Unicode scalar");
                }
            }
        }
        sealed class Reader
        {
            readonly string text;
            public int Position;
            internal Reader(string text)
            {
                this.text = text;
            }
            internal void Space()
            {
                while (Position < text.Length && (text[Position] == ' ' || text[Position] == '\t' || text[Position] == '\n' || text[Position] == '\r'))
                {
                    Position++;
                }
            }
            internal object Value(int depth)
            {
                if (depth > 64)
                {
                    Fail();
                }
                Space();
                if (Position >= text.Length)
                {
                    Fail();
                }
                char c = text[Position];
                if (c == '"')
                {
                    return ReadString();
                }
                if (c == '{')
                {
                    Position++;
                    var result = new Dictionary<string, object>(StringComparer.Ordinal);
                    Space();
                    if (Take('}'))
                    {
                        return result;
                    }
                    do
                    {
                        Space();
                        if (Position >= text.Length || text[Position] != '"')
                        {
                            Fail();
                        }
                        string key = ReadString();
                        Space();
                        Require(':');
                        if (result.ContainsKey(key))
                        {
                            Fail();
                        }
                        result.Add(key, Value(depth + 1));
                        Space();
                        if (Take('}'))
                        {
                            return result;
                        }
                        Require(',');
                    } while (true);
                }
                if (c == '[')
                {
                    Position++;
                    var result = new List<object>();
                    Space();
                    if (Take(']'))
                    {
                        return result;
                    }
                    do
                    {
                        result.Add(Value(depth + 1));
                        Space();
                        if (Take(']'))
                        {
                            return result;
                        }
                        Require(',');
                    } while (true);
                }
                if (c == 't')
                {
                    Literal("true");
                    return true;
                }
                if (c == 'f')
                {
                    Literal("false");
                    return false;
                }
                if (c == 'n')
                {
                    Literal("null");
                    return null;
                }
                int start = Position;
                Take('-');
                if (!Take('0'))
                {
                    Digits();
                }
                bool integer = true;
                if (Take('.'))
                {
                    integer = false;
                    Digits();
                }
                if (Take('e') || Take('E'))
                {
                    integer = false;
                    if (!Take('+'))
                    {
                        Take('-');
                    }
                    Digits();
                }
                string number = text.Substring(start, Position - start);
                if (integer && long.TryParse(number, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long whole))
                {
                    return whole;
                }
                if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out double floating) || double.IsNaN(floating) || double.IsInfinity(floating))
                {
                    Fail();
                }
                return floating;
            }
            string ReadString()
            {
                Require('"');
                var result = new StringBuilder();
                while (Position < text.Length)
                {
                    char c = text[Position++];
                    if (c == '"')
                    {
                        string value = result.ToString();
                        ValidateUnicode(value);
                        return value;
                    }
                    if (c < 32)
                    {
                        Fail();
                    }
                    if (c != '\\')
                    {
                        result.Append(c);
                        continue;
                    }
                    if (Position >= text.Length)
                    {
                        Fail();
                    }
                    c = text[Position++];
                    switch (c)
                    {
                        case '"':
                        case '\\':
                        case '/':
                            result.Append(c);
                            break;
                        case 'b':
                            result.Append('\b');
                            break;
                        case 'f':
                            result.Append('\f');
                            break;
                        case 'n':
                            result.Append('\n');
                            break;
                        case 'r':
                            result.Append('\r');
                            break;
                        case 't':
                            result.Append('\t');
                            break;
                        case 'u':
                            if (Position + 4 > text.Length || !ushort.TryParse(text.Substring(Position, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort code))
                            {
                                Fail();
                                return null;
                            }
                            result.Append((char)code);
                            Position += 4;
                            break;
                        default:
                            Fail();
                            break;
                    }
                }
                Fail();
                return null;
            }
            void Digits()
            {
                int start = Position;
                while (Position < text.Length && text[Position] >= '0' && text[Position] <= '9')
                {
                    Position++;
                }
                if (Position == start)
                {
                    Fail();
                }
            }
            void Literal(string value)
            {
                if (Position + value.Length > text.Length || string.CompareOrdinal(text, Position, value, 0, value.Length) != 0)
                {
                    Fail();
                }
                Position += value.Length;
            }
            bool Take(char c)
            {
                if (Position >= text.Length || text[Position] != c)
                {
                    return false;
                }
                Position++;
                return true;
            }
            void Require(char c)
            {
                if (!Take(c))
                {
                    Fail();
                }
            }
            void Fail() => throw new InvalidOperationException("bad_request: invalid JSON at offset " + Position);
        }
    }
}
