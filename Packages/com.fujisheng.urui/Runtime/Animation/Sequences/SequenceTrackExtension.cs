using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using URUI.Documents;
using URUI.Elements;

namespace URUI.Animation
{
    /// <summary>
    /// 同一元素/类型的轨道共用一个驱动。Create应验证资源，Dispose恢复自有覆盖；均为Unity主线程。
    /// </summary>
    public interface ISequenceTrackFactory
    {
        string Kind
        {
            get;
        }
        ISequenceTrackDriver Create(Element target, IReadOnlyList<SequenceTrack> tracks);
    }
    /// <summary>
    /// track=null或active=false表示尚未开始；否则localTime是该轮绝对模拟时间。不得写入共享资产。
    /// </summary>
    public interface ISequenceTrackDriver : IDisposable
    {
        void Sample(SequenceTrack track, double localTime, bool active);
    }
    /// <summary>
    /// 可选运行模块注册文档范围的轨道工厂；租约由模块拥有，释放后不影响其他模块。
    /// </summary>
    public static class SequenceTrackRegistry
    {
        sealed class Factories
        {
            internal readonly Dictionary<string, ISequenceTrackFactory> Values = new Dictionary<string, ISequenceTrackFactory>(StringComparer.Ordinal);
        }
        sealed class Registration : IDisposable
        {
            internal Action Release;
            public void Dispose()
            {
                var action = Release;
                Release = null;
                action?.Invoke();
            }
        }
        static readonly ConditionalWeakTable<Document, Factories> documents = new ConditionalWeakTable<Document, Factories>();
        public static IDisposable Register(Document document, ISequenceTrackFactory factory)
        {
            if (!document || factory == null || string.IsNullOrWhiteSpace(factory.Kind) || factory.Kind == "css")
            {
                throw new ArgumentException("需要文档与有效扩展类型。");
            }
            document.CheckThread();
            var entry = documents.GetValue(document, _ => new Factories());
            if (entry.Values.ContainsKey(factory.Kind))
            {
                throw new InvalidOperationException("轨道类型已注册：" + factory.Kind);
            }
            entry.Values.Add(factory.Kind, factory);
            return new Registration
            {
                Release = () =>
                {
                    if (document)
                    {
                        document.CheckThread();
                    }
                    if (entry.Values.TryGetValue(factory.Kind, out var current) && ReferenceEquals(current, factory))
                    {
                        entry.Values.Remove(factory.Kind);
                    }
                }
            };
        }
        internal static ISequenceTrackFactory Find(Document document, string kind)
            => documents.TryGetValue(document, out var entry) && entry.Values.TryGetValue(kind, out var factory) ? factory : null;
    }
}
