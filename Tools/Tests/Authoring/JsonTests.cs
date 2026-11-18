using System;
using System.Collections.Generic;
using URUI.Authoring.Agent;

static partial class Program
{
    static partial void JsonTests()
    {
        var value = (Dictionary<string, object>)AgentJson.Parse("{\"text\":\"中文\\ud83d\\ude00\\n\",\"count\":42,\"items\":[true,null,-1.25e2]}");
        Equal("中文😀\n", value["text"]);
        Equal(42L, value["count"]);
        Equal(-125d, ((List<object>)value["items"])[2]);
        Equal("{\"a\":\"<b>\\\"\\n\",\"b\":null}", AgentJson.Stringify(new Dictionary<string, object> { ["a"] = "<b>\"\n", ["b"] = null }));
        Throws(() => AgentJson.Parse("{\"a\":1,\"a\":2}"));
        Throws(() => AgentJson.Parse("[01]"));
        Throws(() => AgentJson.Parse("{\"a\":NaN}"));
        Throws(() => AgentJson.Parse("true false"));
        Throws(() => AgentJson.Parse("\"\\ud83d\""));
        Throws(() => AgentJson.Parse(new string('[', 65) + "0" + new string(']', 65)));
        Throws(() => AgentJson.Stringify(double.PositiveInfinity));
        var jobs = new AgentJobQueue("epoch");
        var job = jobs.Submit(new Dictionary<string, object> { ["op"] = "edit" });
        Equal("queued", jobs.Read(job.Id).Status);
        Equal(true, jobs.Cancel(job.Id));
        int executions = 0;
        Equal(false, jobs.RunNext("epoch", request => { executions++; return "{}"; }));
        Equal(0, executions);
        var stale = jobs.Submit(new Dictionary<string, object>());
        Equal(true, jobs.RunNext("different-epoch", request => { executions++; return "{}"; }));
        Equal("cancelled", jobs.Read(stale.Id).Status);
        Equal(0, executions);
        var completed = jobs.Submit(new Dictionary<string, object>());
        jobs.RunNext("epoch", request => { Equal(false, jobs.Cancel(completed.Id)); return "{\"ok\":true}"; });
        Equal("done", jobs.Read(completed.Id).Status);
        Equal("{\"ok\":true}", jobs.Read(completed.Id).ResultJson);
        var failed = jobs.Submit(new Dictionary<string, object>());
        jobs.RunNext("epoch", request => throw new InvalidOperationException("stale_node: expired"));
        Equal("failed", jobs.Read(failed.Id).Status);
        jobs.Invalidate(); Throws(() => jobs.Submit(new Dictionary<string, object>()));
    }
}
