using System;
using System.Collections.Generic;

namespace URUI.Authoring.Agent
{
    public sealed class AgentJobSnapshot
    {
        public string Id { get; internal set; }
        public string Status { get; internal set; }
        public string ResultJson { get; internal set; }
    }
    /// <summary>网络线程只排队；调用者在主线程执行，取消不能中断已经开始的原子命令。</summary>
    public sealed class AgentJobQueue
    {
        sealed class Job
        {
            internal string Id, Status = "queued", ResultJson;
            internal Dictionary<string, object> Request;
        }
        readonly object gate = new object();
        readonly string epoch;
        readonly Dictionary<string, Job> jobs = new Dictionary<string, Job>();
        readonly List<Job> pending = new List<Job>(), ordered = new List<Job>();
        bool active = true;
        public AgentJobQueue(string epoch) { this.epoch = epoch ?? throw new ArgumentNullException(nameof(epoch)); }
        public AgentJobSnapshot Submit(Dictionary<string, object> request)
        {
            lock (gate)
            {
                if (!active) throw new InvalidOperationException("operation_cancelled: Editor session ended");
                if (pending.Count >= 128) throw new InvalidOperationException("busy: pending job limit");
                if (jobs.Count >= 128)
                {
                    var old = ordered.Find(value => value.Status != "queued" && value.Status != "running");
                    if (old == null) throw new InvalidOperationException("busy: job limit");
                    jobs.Remove(old.Id); ordered.Remove(old);
                }
                var job = new Job { Id = Guid.NewGuid().ToString("N"), Request = request };
                jobs.Add(job.Id, job); pending.Add(job); ordered.Add(job); return Snapshot(job);
            }
        }
        public AgentJobSnapshot Read(string id)
        { lock (gate) return jobs.TryGetValue(id, out var job) ? Snapshot(job) : null; }
        public bool Cancel(string id)
        {
            lock (gate)
            {
                if (!jobs.TryGetValue(id, out var job) || job.Status != "queued") return false;
                job.Status = "cancelled"; pending.Remove(job); return true;
            }
        }
        public bool RunNext(string currentEpoch, Func<Dictionary<string, object>, string> execute)
        {
            Job job;
            lock (gate)
            {
                if (pending.Count == 0) return false;
                job = pending[0]; pending.RemoveAt(0);
                if (!active || currentEpoch != epoch) { job.Status = "cancelled"; return true; }
                job.Status = "running";
            }
            try
            {
                string result = execute(job.Request);
                lock (gate) { job.ResultJson = result; job.Request = null; job.Status = "done"; }
            }
            catch (Exception error)
            {
                string message = error.Message, code = message.IndexOf(':') > 0 ? message.Substring(0, message.IndexOf(':')) : "operation_failed";
                string result = AgentJson.Stringify(new Dictionary<string, object> { ["ok"] = false, ["error"] = new Dictionary<string, object> { ["code"] = code, ["message"] = message } });
                lock (gate) { job.ResultJson = result; job.Request = null; job.Status = "failed"; }
            }
            return true;
        }
        public void Invalidate()
        {
            lock (gate) { active = false; foreach (var job in pending) { job.Status = "cancelled"; job.Request = null; } pending.Clear(); }
        }
        static AgentJobSnapshot Snapshot(Job job) => new AgentJobSnapshot { Id = job.Id, Status = job.Status, ResultJson = job.ResultJson };
    }
}
