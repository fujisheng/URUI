using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using URUI.Authoring.Agent;
using URUI.Authoring.Sessions;
namespace URUI.Authoring.Tests
{
    public static class AnimationAgentValidation
    {
        static int count;
        static void Check(bool value, string message)
        {
            count++;
            if (!value)
            {
                throw new Exception(message);
            }
        }
        public static void Run()
        {
            Directory.CreateDirectory("Assets/AnimationAgentChecks");
            const string path = "Assets/AnimationAgentChecks/agent.html";
            File.Delete(Path.ChangeExtension(path, ".animations.css"));
            File.Delete(Path.ChangeExtension(path, ".urui-sequence.json"));
            File.WriteAllText(path, "<!doctype html><html><head><style>#agent{position:absolute;left:10px;top:20px;width:100px;height:60px;background-color:red;}</style></head><body><div id='agent'>Agent</div></body></html>");
            AgentServer.Start();
            var endpoint = (Dictionary<string, object>)AgentJson.Parse(File.ReadAllText(AgentServer.EndpointPath));
            var task = Task.Run(async () =>
            {
                using (var client = new HttpClient(new HttpClientHandler { UseProxy = false })
                {
                    BaseAddress = new Uri((string)endpoint["url"]),
                    Timeout = TimeSpan.FromSeconds(20)
                })
                {
                    client.DefaultRequestHeaders.Add("Authorization", "Bearer " + (string)endpoint["token"]);
                    client.DefaultRequestHeaders.Add("X-URUI-Session", (string)endpoint["editorSession"]);
                    var caps = await AgentValidation.Command(client, new Dictionary<string, object> { { "op", "capabilities" } });
                    string session = (string)caps["session"];
                    long revision = (long)caps["sourceRevision"];
                    async Task<Dictionary<string, object>> Write(string op, params object[] pairs)
                    {
                        var request = AgentDispatcher.Obj("op", op, "mode", "authoring", "session", session, "expectedRevision", revision);
                        for (int i = 0; i < pairs.Length; i += 2)
                        {
                            request[(string)pairs[i]] = pairs[i + 1];
                        }
                        var result = await AgentValidation.Command(client, request);
                        revision = (long)result["sourceRevision"];
                        return result;
                    }
                    await Write("document.open", "path", path);
                    var found = await AgentValidation.Command(client, AgentDispatcher.Obj("op", "hierarchy.find", "mode", "authoring", "name", "agent"));
                    var nodes = (List<object>)((Dictionary<string, object>)found["data"])["nodes"];
                    string target = (string)((Dictionary<string, object>)nodes[0])["id"];
                    var preset = await Write("animation.preset", "target", target, "preset", "popup", "duration", .3);
                    var data = (Dictionary<string, object>)preset["data"];
                    string track = (string)data["selectedTrack"];
                    string sequence = (string)data["path"];
                    Check(!File.Exists(sequence), "Preset wrote the sequence before Save");
                    await Write("animation.key", "track", track, "position", .5, "property", "opacity", "value", ".6");
                    await Write("animation.sample", "time", .15);
                    var capture = await AgentValidation.Command(client, AgentDispatcher.Obj("op", "preview.capture", "mode", "authoring"));
                    Check((long)capture["renderedRevision"] == revision && File.Exists((string)((Dictionary<string, object>)capture["data"])["path"]), "Sample/capture revisions differ");
                    var read = await AgentValidation.Command(client, AgentDispatcher.Obj("op", "animation.read", "mode", "authoring"));
                    Check(Math.Abs(Convert.ToDouble(((Dictionary<string, object>)read["data"])["time"]) - .15) < .001, "Agent sample ignored requested time");
                    await Write("animation.stop");
                    await Write("document.save");
                    Check(File.Exists(sequence) && File.ReadAllText(Path.ChangeExtension(path, ".animations.css")).Contains(".6"), "Save missed animation source");
                    await Write("animation.open", "path", sequence);
                    long before = revision;
                    var failed = await Failed(client, AgentDispatcher.Obj("op", "animation.key", "mode", "authoring", "session", session, "expectedRevision", revision, "keys", new List<object> { AgentDispatcher.Obj("track", track, "position", .4, "property", "opacity", "value", ".2"), AgentDispatcher.Obj("track", track, "position", double.PositiveInfinity, "property", "opacity", "value", "1") }));
                    Check(failed, "Invalid batch succeeded");
                    read = await AgentValidation.Command(client, AgentDispatcher.Obj("op", "animation.read", "mode", "authoring"));
                    Check((long)read["sourceRevision"] == before, "Failed batch published partial source");
                    failed = await Failed(client, AgentDispatcher.Obj("op", "animation.track", "mode", "authoring", "session", session, "expectedRevision", revision - 1, "track", track, "changes", AgentDispatcher.Obj("start", .1)));
                    Check(failed, "Stale revision accepted");
                    failed = await Failed(client, AgentDispatcher.Obj("op", "animation.track", "mode", "authoring", "session", session, "expectedRevision", revision, "track", track, "changes", AgentDispatcher.Obj("easing", "ease-out paused")));
                    Check(failed, "Invalid easing accepted");
                    await Write("animation.lock", "track", track, "locked", true);
                    failed = await Failed(client, AgentDispatcher.Obj("op", "animation.key", "mode", "authoring", "session", session, "expectedRevision", revision, "track", track, "position", .4, "property", "opacity", "value", ".2"));
                    Check(failed, "Locked track allowed edits");
                }
            });
            EditorApplication.CallbackFunction finish = null;
            finish = () =>
            {
                if (!task.IsCompleted)
                {
                    return;
                }
                EditorApplication.update -= finish;
                AgentServer.Stop();
                Directory.CreateDirectory("Evidence");
                if (task.IsFaulted)
                {
                    File.WriteAllText("Evidence/animation-agent-failure.txt", task.Exception.ToString());
                    UnityEngine.Debug.LogException(task.Exception);
                    EditorApplication.Exit(1);
                }
                else
                {
                    File.WriteAllText("Evidence/animation-agent.json", "{\"passed\":" + count + ",\"failed\":0}");
                    UnityEngine.Debug.Log("URUI Animation HTTP PASS " + count);
                    EditorApplication.Exit(0);
                }
            };
            EditorApplication.update += finish;
        }
        static async Task<bool> Failed(HttpClient client, Dictionary<string, object> request)
        {
            // JSON itself rejects infinity. A finite out-of-range position exercises transactional source validation.
            if (request.TryGetValue("keys", out var raw) && raw is List<object> list)
            {
                ((Dictionary<string, object>)list[1])["position"] = 1.5;
            }
            var response = await client.PostAsync("v1/commands", new StringContent(AgentJson.Stringify(request), Encoding.UTF8, "application/json"));
            var queued = (Dictionary<string, object>)AgentJson.Parse(await response.Content.ReadAsStringAsync());
            if ((int)response.StatusCode != 202)
            {
                return false;
            }
            string id = (string)queued["job"];
            for (int i = 0; i < 400; i++)
            {
                var job = (Dictionary<string, object>)AgentJson.Parse(await client.GetStringAsync("v1/jobs/" + id));
                string status = (string)job["status"];
                if (status == "failed" || status == "cancelled")
                {
                    return true;
                }
                if (status == "done")
                {
                    return false;
                }
                await Task.Delay(25);
            }
            throw new Exception("Animation rejection timeout");
        }
    }
}
