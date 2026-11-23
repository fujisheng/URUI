using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using URUI.Internal.Serialization;

namespace URUI.Animation
{
    /// <summary>
    /// 一个可读的序列轨道定义。CSS值属于命名关键帧；此对象只保存绑定、时间和资源引用。
    /// </summary>
    [Serializable]
    public sealed class SequenceTrack
    {
        public string Id = "track";
        public string Kind = "css";
        public string Target = "";
        public string Keyframes = "";
        public string Easing = "linear";
        public string Source = "";
        public double Start;
        public double Duration = .3;
        public double Speed = 1;
        public int Iterations = 1;
        public uint Seed = 1;
        public bool Alternate;
        public bool Muted;
        public bool Locked;
        public double End => Start + Duration * Iterations;

        /// <summary>
        /// 返回当前轨道的独立副本。
        /// </summary>
        public SequenceTrack Clone() => new SequenceTrack
        {
            Id = Id,
            Kind = Kind,
            Target = Target,
            Keyframes = Keyframes,
            Easing = Easing,
            Source = Source,
            Start = Start,
            Duration = Duration,
            Speed = Speed,
            Iterations = Iterations,
            Seed = Seed,
            Alternate = Alternate,
            Muted = Muted,
            Locked = Locked
        };
    }

    /// <summary>
    /// 格式v1序列。读取时严格校验，播放器复制定义；改动原对象不影响已有播放。
    /// </summary>
    [Serializable]
    public sealed class SequenceDefinition
    {
        public int Version = 1;
        public string Name = "新动画";
        public double Duration = 1;
        public bool Loop;
        public List<SequenceTrack> Tracks = new List<SequenceTrack>();

        /// <summary>
        /// 校验并返回当前序列定义的深拷贝，避免通过 JSON 序列化往返复制。
        /// </summary>
        public SequenceDefinition Clone()
        {
            Validate();
            var copy = new SequenceDefinition
            {
                Version = Version,
                Name = Name,
                Duration = Duration,
                Loop = Loop,
                Tracks = new List<SequenceTrack>(Tracks.Count)
            };
            for (int i = 0; i < Tracks.Count; i++)
            {
                copy.Tracks.Add(Tracks[i].Clone());
            }
            return copy;
        }

        public void Validate()
        {
            if (Version != 1)
            {
                throw new InvalidOperationException("unsupported_sequence_version: " + Version);
            }
            Text(Name, "name");
            Range(Duration, 0, 600, "duration");
            if (Loop && Duration == 0)
            {
                throw new InvalidOperationException("循环动画的时长必须大于0。");
            }
            if (Tracks == null || Tracks.Count > 256)
            {
                throw new InvalidOperationException("序列最多支持256条轨道。");
            }
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var track in Tracks)
            {
                if (track == null)
                {
                    throw new InvalidOperationException("轨道不能为null。");
                }
                Text(track.Id, "track.id");
                Text(track.Target, "track.target");
                Text(track.Easing, "track.easing");
                if (!ids.Add(track.Id))
                {
                    throw new InvalidOperationException("轨道id重复：" + track.Id);
                }
                if (track.Kind != "css" && track.Kind != "particles")
                {
                    throw new InvalidOperationException("不支持的轨道类型：" + track.Kind);
                }
                Range(track.Start, 0, 600, "start");
                Range(track.Duration, 0, 600, "track.duration");
                if (track.Iterations < 1 || track.Iterations > 100000 || !Finite(track.End) || track.End > Duration + 1e-7)
                {
                    throw new InvalidOperationException("轨道必须位于序列时长内，循环次数须为正整数。");
                }
                if (track.Kind == "css")
                {
                    Text(track.Keyframes, "keyframes");
                    ValidateEasing(track.Easing);
                }
                else
                {
                    if (track.Seed == 0 || !Finite(track.Speed) || track.Speed <= 0 || track.Speed > 4 || track.Duration * track.Speed > 600)
                    {
                        throw new InvalidOperationException("粒子需要非零种子、0..4播放速度，单轮模拟不超过600秒。");
                    }
                    if (track.Easing != "linear")
                    {
                        throw new InvalidOperationException("粒子轨道使用线性模拟时间。");
                    }
                    if (track.Source == null || track.Source.IndexOf('\0') >= 0)
                    {
                        throw new InvalidOperationException("无效粒子资源引用。");
                    }
                }
            }
        }

        static void ValidateEasing(string easing)
        {
            if (easing == "linear" || easing == "ease" || easing == "ease-in" || easing == "ease-out" || easing == "ease-in-out")
            {
                return;
            }
            if (easing.StartsWith("cubic-bezier(", StringComparison.Ordinal) && easing.EndsWith(")", StringComparison.Ordinal))
            {
                var parts = easing.Substring(13, easing.Length - 14).Split(',');
                if (parts.Length == 4)
                {
                    var values = new float[4];
                    bool valid = true;
                    for (int i = 0; i < 4; i++)
                    {
                        valid &= float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]) && !float.IsNaN(values[i]) && !float.IsInfinity(values[i]);
                    }
                    if (valid && values[0] >= 0 && values[0] <= 1 && values[2] >= 0 && values[2] <= 1)
                    {
                        return;
                    }
                }
            }
            if (easing.StartsWith("linear(", StringComparison.Ordinal) && easing.EndsWith(")", StringComparison.Ordinal))
            {
                var stops = easing.Substring(7, easing.Length - 8).Split(',');
                bool valid = stops.Length >= 2;
                foreach (var stop in stops)
                {
                    var parts = stop.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                    valid &= parts.Length >= 1 && parts.Length <= 3;
                    for (int i = 0; i < parts.Length; i++)
                    {
                        string value = parts[i];
                        if (i != 0)
                        {
                            if (!value.EndsWith("%", StringComparison.Ordinal))
                            {
                                valid = false;
                                continue;
                            }
                            value = value.Substring(0, value.Length - 1);
                        }
                        valid &= float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float number) && !float.IsNaN(number) && !float.IsInfinity(number);
                    }
                }
                if (valid)
                {
                    return;
                }
            }
            throw new InvalidOperationException("无效或不支持的缓动曲线：" + easing);
        }

        public static SequenceDefinition Parse(string text)
        {
            var root = Object(ValueJson.Parse(text));
            Known(root, "version", "name", "duration", "loop", "tracks");
            var result = new SequenceDefinition
            {
                Version = Integer(root, "version", 1),
                Name = String(root, "name", "新动画"),
                Duration = Number(root, "duration", 1),
                Loop = Boolean(root, "loop")
            };
            if (!root.TryGetValue("tracks", out var list) || !(list is List<object> tracks) || tracks.Count > 256)
            {
                throw new InvalidOperationException("tracks必须是最多256项的数组。");
            }
            foreach (var item in tracks)
            {
                var row = Object(item);
                Known(row, "id", "kind", "target", "keyframes", "easing", "source", "start", "duration", "speed", "iterations", "seed", "alternate", "muted", "locked");
                double seed = Number(row, "seed", 1);
                if (seed < 0 || seed > uint.MaxValue || seed != Math.Truncate(seed))
                {
                    throw new InvalidOperationException("无效粒子种子。");
                }
                result.Tracks.Add(new SequenceTrack
                {
                    Id = String(row, "id", ""),
                    Kind = String(row, "kind", "css"),
                    Target = String(row, "target", ""),
                    Keyframes = String(row, "keyframes", ""),
                    Easing = String(row, "easing", "linear"),
                    Source = String(row, "source", ""),
                    Start = Number(row, "start", 0),
                    Duration = Number(row, "duration", .3),
                    Iterations = Integer(row, "iterations", 1),
                    Speed = Number(row, "speed", 1),
                    Seed = (uint)seed,
                    Alternate = Boolean(row, "alternate"),
                    Muted = Boolean(row, "muted"),
                    Locked = Boolean(row, "locked")
                });
            }
            result.Validate();
            return result;
        }

        public string ToJson()
        {
            Validate();
            var tracks = new List<object>();
            foreach (var track in Tracks)
            {
                tracks.Add(new Dictionary<string, object>
                {
                    ["id"] = track.Id,
                    ["kind"] = track.Kind,
                    ["target"] = track.Target,
                    ["keyframes"] = track.Keyframes,
                    ["start"] = track.Start,
                    ["duration"] = track.Duration,
                    ["iterations"] = track.Iterations,
                    ["alternate"] = track.Alternate,
                    ["easing"] = track.Easing,
                    ["muted"] = track.Muted,
                    ["locked"] = track.Locked,
                    ["source"] = track.Source,
                    ["seed"] = track.Seed,
                    ["speed"] = track.Speed
                });
            }
            return Pretty(ValueJson.Stringify(new Dictionary<string, object>
            {
                ["version"] = Version,
                ["name"] = Name,
                ["duration"] = Duration,
                ["loop"] = Loop,
                ["tracks"] = tracks
            }));
        }

        static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        static void Range(double value, double minimum, double maximum, string name)
        {
            if (!Finite(value) || value < minimum || value > maximum)
            {
                throw new InvalidOperationException(name + "超出支持范围。");
            }
        }
        static void Text(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value) || value.IndexOf('\0') >= 0 || value.Length > 4096)
            {
                throw new InvalidOperationException("无效" + name);
            }
        }
        static Dictionary<string, object> Object(object value) => value as Dictionary<string, object> ?? throw new InvalidOperationException("序列需要JSON对象。");
        static void Known(Dictionary<string, object> row, params string[] names)
        {
            var allowed = new HashSet<string>(names);
            foreach (string name in row.Keys)
            {
                if (!allowed.Contains(name))
                {
                    throw new InvalidOperationException("不支持的序列字段：" + name);
                }
            }
        }
        static string String(Dictionary<string, object> row, string name, string fallback)
        {
            if (!row.TryGetValue(name, out var value))
            {
                return fallback;
            }
            return value as string ?? throw new InvalidOperationException(name + "需要字符串。");
        }
        static double Number(Dictionary<string, object> row, string name, double fallback)
        {
            if (!row.TryGetValue(name, out var value))
            {
                return fallback;
            }
            if (!(value is long) && !(value is double))
            {
                throw new InvalidOperationException(name + "需要数字。");
            }
            return Convert.ToDouble(value, CultureInfo.InvariantCulture);
        }
        static int Integer(Dictionary<string, object> row, string name, int fallback)
        {
            double value = Number(row, name, fallback);
            if (value != Math.Truncate(value) || value < int.MinValue || value > int.MaxValue)
            {
                throw new InvalidOperationException(name + "需要整数。");
            }
            return (int)value;
        }
        static bool Boolean(Dictionary<string, object> row, string name)
        {
            if (!row.TryGetValue(name, out var value))
            {
                return false;
            }
            return value is bool boolean ? boolean : throw new InvalidOperationException(name + "需要布尔值。");
        }
        static string Pretty(string json)
        {
            var result = new StringBuilder();
            int depth = 0;
            bool quoted = false;
            bool escaped = false;
            void NewLine()
            {
                result.Append('\n');
                result.Append(' ', depth * 2);
            }
            foreach (char c in json)
            {
                if (quoted)
                {
                    result.Append(c);
                    if (escaped)
                    {
                        escaped = false;
                    }
                    else if (c == '\\')
                    {
                        escaped = true;
                    }
                    else if (c == '"')
                    {
                        quoted = false;
                    }
                    continue;
                }
                if (c == '"')
                {
                    quoted = true;
                    result.Append(c);
                }
                else if (c == '{' || c == '[')
                {
                    result.Append(c);
                    depth++;
                    NewLine();
                }
                else if (c == '}' || c == ']')
                {
                    depth--;
                    NewLine();
                    result.Append(c);
                }
                else if (c == ',')
                {
                    result.Append(c);
                    NewLine();
                }
                else if (c == ':')
                {
                    result.Append(": ");
                }
                else
                {
                    result.Append(c);
                }
            }
            return result.Append('\n').ToString();
        }
    }
}
