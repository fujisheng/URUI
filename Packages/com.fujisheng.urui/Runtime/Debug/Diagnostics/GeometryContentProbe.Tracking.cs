#if URUI_DEBUG
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEngine;

namespace URUI.Internal.Diagnostics
{
    // 诊断模式下跟踪资源引用和报告数据，释放通知与内容历史分别处理。
    internal sealed partial class GeometryContentProbe
    {
        Token Finish(Token t, long start)
        {
            t.observerTicks = Stopwatch.GetTimestamp() - start;
            total.probeTicks += t.observerTicks;
            return t;
        }

        internal void Commit(Token token, int n, int k, long snapshotTicks, long meshTicks, bool register)
        {
            Thread();
            total.snapshotTicks += snapshotTicks;
            total.meshTicks += meshTicks;
            if (token.active)
            {
                total.activePotentialMeshTicks += meshTicks;
            }

            if (token.history)
            {
                total.historyPotentialMeshTicks += meshTicks;
            }

            if (register && token.content != 0)
            {
                var key = (token.epoch, token.id);
                if (active.TryGetValue(key, out var old))
                {
                    if (old != token.content)
                    {
                        RemoveActive(old);
                        active[key] = token.content;
                        AddActive(token.content);
                    }
                }
                else if (active.Count < maxIds)
                {
                    active.Add(key, token.content);
                    AddActive(token.content);
                }
            }

            if (events.Count < maxEvents)
            {
                events.Add(new Event { t = token, vertices = n, indices = k, snapshot = snapshotTicks, mesh = meshTicks });
            }
            else
            {
                total.eventDrops++;
            }
        }

        void AddActive(ulong id)
        {
            activeCounts.TryGetValue(id, out int n);
            activeCounts[id] = n + 1;
        }

        void RemoveActive(ulong id)
        {
            if (!activeCounts.TryGetValue(id, out int n))
            {
                return;
            }

            if (n <= 1)
            {
                activeCounts.Remove(id);
            }
            else
            {
                activeCounts[id] = n - 1;
            }
        }

        internal void Released(ulong e, ulong id)
        {
            Thread();
            var key = (e, id);
            if (active.TryGetValue(key, out var value))
            {
                active.Remove(key);
                RemoveActive(value);
            }
        }

        internal string Save(string directory)
        {
            Thread();
            Directory.CreateDirectory(directory);
            total.payloadEntries = bySerial.Count;
            total.identityEntries = seen.Count;
            total.activeSources = active.Count;
            File.WriteAllText(Path.Combine(directory, "summary.json"), JsonUtility.ToJson(total, true));
            var b = new StringBuilder(events.Count * 100);
            b.AppendLine("frame,epoch,id,content,vertices,indices,new_handle,active_match,history_match,observer_ticks,snapshot_ticks,mesh_ticks");
            foreach (var x in events)
            {
                b.Append(x.t.frame).Append(',').Append(x.t.epoch).Append(',').Append(x.t.id).Append(',').Append(x.t.content).Append(',').Append(x.vertices).Append(',').Append(x.indices).Append(',').Append(x.t.newHandle ? 1 : 0).Append(',').Append(x.t.active ? 1 : 0).Append(',').Append(x.t.history ? 1 : 0).Append(',').Append(x.t.observerTicks).Append(',').Append(x.snapshot).Append(',').Append(x.mesh).AppendLine();
            }

            File.WriteAllText(Path.Combine(directory, "events.csv"), b.ToString());
            return directory;
        }

        internal void Clear()
        {
            Thread();
            buckets.Clear();
            bySerial.Clear();
            fifo.Clear();
            seen.Clear();
            active.Clear();
            activeCounts.Clear();
            events.Clear();
            total.payloadBytes = 0;
        }
    }
}
#endif
