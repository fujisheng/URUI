using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using Unity.Profiling;
using URUI.Elements;

namespace URUI.Tests
{
    public sealed partial class HtmlCssPlayerValidation
    {
        [Serializable] sealed class CostReport
        {
            public int schemaVersion = 1;
            public string unity, graphics, cpu, os, nativeSha256, inputDigest;
            public string scope = "Warm after functional validation. SynchronizeLayout includes shared native layout and CPU recording; excludes GPU execution. Frame allocations include the isolated scene and harness. Load RSS is sampled, not an attributed peak.";
            public bool allocationCounterAvailable = true;
            public bool profilerFrameAllocationAvailable, systemMemoryCounterAvailable;
            public string frameAllocationMethod = "Unity Memory/GC Allocated In Frame, whole isolated scene including harness, all threads";
            public List<CostCase> cases = new List<CostCase>();
        }
        [Serializable] sealed class CostCase
        {
            public string name;
            public int documents;
            public bool updated;
            public double loadMilliseconds;
            public long loadManagedBytes, residentBeforeLoad, residentAfterLoad;
            public long systemMemoryBeforeLoad, systemMemoryAfterLoad;
            public List<double> synchronizeMilliseconds = new List<double>();
            public List<long> synchronizeManagedBytes = new List<long>();
            public List<long> wholeFrameManagedBytes = new List<long>();
            public List<double> frameMilliseconds = new List<double>();
            public List<long> profilerFrameManagedBytes = new List<long>();
            public List<long> systemMemoryBytes = new List<long>();
        }
        readonly CostReport costs = new CostReport();

        long AllocationBytes()
        {
            if (!costs.allocationCounterAvailable) return -1;
            try { return GC.GetAllocatedBytesForCurrentThread(); }
            catch (NotSupportedException) { costs.allocationCounterAvailable = false; return -1; }
        }
        static long ResidentBytes()
        {
            try { using var process = Process.GetCurrentProcess(); return process.WorkingSet64 > 0 ? process.WorkingSet64 : -1; }
            catch (Exception) { return -1; }
        }
        static double Milliseconds(long start) => (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
        static ProfilerRecorder Counter(string name)
        {
            try { return ProfilerRecorder.StartNew(ProfilerCategory.Memory, name, 1); }
            catch (Exception) { return default; }
        }

        IEnumerator CapturePerformance()
        {
            using var frameAllocations = Counter("GC Allocated In Frame");
            using var systemMemory = Counter("System Used Memory");
            Case fixture = null;
            if (!Run(() => {
                fixture = Array.Find(manifest.cases, value => value.name == "typography-1280x720");
                Need(fixture != null, "Performance typography fixture");
                costs.unity = Application.unityVersion; costs.graphics = SystemInfo.graphicsDeviceType.ToString();
                costs.cpu = SystemInfo.processorType; costs.os = SystemInfo.operatingSystem; costs.nativeSha256 = manifest.nativeSha256;
                costs.inputDigest = Hash(Encoding.UTF8.GetBytes(string.Join("\n", fixture.resources.Select(value => value.uri + " " + value.sha256))));
                var before = AllocationBytes();
                var allocationProbe = new byte[4096];
                var after = AllocationBytes();
                GC.KeepAlive(allocationProbe);
                if (before < 0 || after - before < allocationProbe.Length) costs.allocationCounterAvailable = false;
                deadline = Time.realtimeSinceStartupAsDouble + 180;
            })) yield break;
            yield return null;
            if (finished) yield break;
            costs.profilerFrameAllocationAvailable = frameAllocations.Valid && frameAllocations.Count > 0 && frameAllocations.LastValue >= 4096;
            costs.systemMemoryCounterAvailable = systemMemory.Valid && systemMemory.Count > 0 && systemMemory.LastValue > 0;
            foreach (var documents in new[] { 1, 2 })
            foreach (var updated in new[] { false, true })
            {
                var sample = new CostCase { name = (documents == 1 ? "single" : "multiple") + (updated ? "-updated" : "-static"), documents = documents, updated = updated };
                Element first = null, second = null;
                if (!Run(() => {
                    a.Unload(); b.Unload(); surface.Viewport = new Vector2Int(fixture.width, fixture.height); SetTarget(fixture.width, fixture.height);
                    sample.residentBeforeLoad = ResidentBytes();
                    sample.systemMemoryBeforeLoad = costs.systemMemoryCounterAvailable ? systemMemory.LastValue : -1;
                    var bytes = AllocationBytes(); var started = Stopwatch.GetTimestamp();
                    LoadCase(a, fixture); if (documents == 2) LoadCase(b, fixture);
                    sample.loadMilliseconds = Milliseconds(started);
                    sample.loadManagedBytes = bytes < 0 ? -1 : AllocationBytes() - bytes;
                    sample.residentAfterLoad = ResidentBytes();
                    first = a.Q("regular"); second = documents == 2 ? b.Q("regular") : null;
                    Need(first != null && (documents == 1 || second != null), "Performance text targets");
                })) yield break;
                yield return null;
                if (finished) yield break;
                sample.systemMemoryAfterLoad = costs.systemMemoryCounterAvailable ? systemMemory.LastValue : -1;
                // 预热不混入样本；不把绑定字符串分配伪装成框架分配。
                for (var index = 0; index < 150; ++index)
                {
                    if (!Run(() => {
                        var bytes = AllocationBytes(); var started = Stopwatch.GetTimestamp();
                        if (updated)
                        {
                            var text = index % 2 == 0 ? "Hello URUI 123" : "Hello URUI 456";
                            first.Text = text; if (second != null) second.Text = text;
                        }
                        a.SynchronizeLayout();
                        var elapsed = Milliseconds(started); var allocated = bytes < 0 ? -1 : AllocationBytes() - bytes;
                        if (index >= 30)
                        {
                            sample.synchronizeMilliseconds.Add(elapsed);
                            if (allocated >= 0) sample.synchronizeManagedBytes.Add(allocated);
                        }
                    })) yield break;
                    var frameStart = AllocationBytes();
                    yield return null;
                    if (finished) yield break;
                    if (index >= 30)
                    {
                        var frameEnd = AllocationBytes();
                        if (frameStart >= 0 && frameEnd >= frameStart) sample.wholeFrameManagedBytes.Add(frameEnd - frameStart);
                        sample.frameMilliseconds.Add(Time.unscaledDeltaTime * 1000.0);
                        if (costs.profilerFrameAllocationAvailable && frameAllocations.Count > 0)
                            sample.profilerFrameManagedBytes.Add(frameAllocations.LastValue);
                        if (costs.systemMemoryCounterAvailable && systemMemory.Count > 0)
                            sample.systemMemoryBytes.Add(systemMemory.LastValue);
                    }
                }
                costs.cases.Add(sample);
            }
            Run(() => File.WriteAllText(Path.Combine(output, "html-css-player-costs.json"), JsonUtility.ToJson(costs, true)));
        }
    }
}
