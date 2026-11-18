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
    public static class AgentValidation
    {
        static int count;
        static void Check(bool condition)
        {
            count++;
            if (!condition)
                throw new Exception("Agent assertion " + count);
        }
        static void Reject(Action action)
        {
            count++;
            try
            {
                action();
            }
            catch (InvalidOperationException) { return; }
            throw new Exception("Expected Agent rejection");
        }
        public static int RunSync()
        {
            var api = new AgentDispatcher("test-epoch");
            var caps = api.Execute(new Dictionary<string, object> { ["op"] = "capabilities" });
            Check((bool)caps["ok"]);
            var author = AuthoringEnvironment.Author;
            string key = null;
            foreach (var node in author.Model.Nodes)
                if (node.Tag == "div")
                {
                    key = node.Key;
                    break;
                }
            var request = new Dictionary<string, object>
            {
                ["op"] = "properties.set",
                ["mode"] = "authoring",
                ["session"] = author.Id,
                ["expectedRevision"] = author.Revision,
                ["target"] = key,
                ["property"] = "style.width",
                ["value"] = "66px"
            };
            api.Execute(request);
            Check(author.File.Text.Contains("66px"));
            Reject(() => api.Execute(request));
            request["expectedRevision"] = author.Revision;
            request["session"] = "old-session";
            Reject(() => api.Execute(request));
            request["session"] = author.Id;
            request["property"] = "arbitrary.privateField";
            Reject(() => api.Execute(request));
            var capture = api.Execute(new Dictionary<string, object> { ["op"] = "preview.capture", ["mode"] = "authoring" });
            var data = (Dictionary<string, object>)capture["data"];
            Check(File.Exists((string)data["path"]));
            Check((long)capture["renderedRevision"] == author.Revision);
            Check((long)capture["frameId"] > 0);
            int clicks = 0;
            using (var subscription = AuthoringEnvironment.Preview.Document.Q("direct").RegisterCallback<URUI.Events.ClickEvent>(_ => clicks++))
            {
                api.Execute(new Dictionary<string, object> { ["op"] = "preview.input", ["mode"] = "authoring", ["session"] = author.Id, ["expectedRevision"] = author.Revision, ["kind"] = "pointer", ["phase"] = "Down", ["x"] = 20L, ["y"] = 30L, ["pressedButtons"] = 1L });
                api.Execute(new Dictionary<string, object> { ["op"] = "preview.input", ["mode"] = "authoring", ["session"] = author.Id, ["expectedRevision"] = author.Revision, ["kind"] = "pointer", ["phase"] = "Up", ["x"] = 20L, ["y"] = 30L });
                Check(clicks == 1);
            }
            Reject(() => api.Execute(new Dictionary<string, object> { ["op"] = "document.read", ["mode"] = "authoring", ["path"] = "../outside.html" }));
            return count;
        }
        public static void RunHttp()
        {
            Directory.CreateDirectory("Assets/AuthoringChecks");
            File.WriteAllText("Assets/AuthoringChecks/agent.html", "<!doctype html><html><head></head><body><div id='agent'>original</div></body></html>");
            AgentServer.Start();
            var endpoint = (Dictionary<string, object>)AgentJson.Parse(File.ReadAllText(AgentServer.EndpointPath));
            var work = Task.Run(async () =>
            {
                using (var client = new HttpClient(new HttpClientHandler { UseProxy = false }) { BaseAddress = new Uri((string)endpoint["url"]), Timeout = TimeSpan.FromSeconds(15) })
                {
                    var denied = await client.PostAsync("v1/commands", new StringContent("{}", Encoding.UTF8, "application/json"));
                    Check((int)denied.StatusCode == 401);
                    client.DefaultRequestHeaders.Add("Authorization", "Bearer " + (string)endpoint["token"]);
                    client.DefaultRequestHeaders.Add("X-URUI-Session", (string)endpoint["editorSession"]);
                    client.DefaultRequestHeaders.Add("Origin", "https://untrusted.example");
                    denied = await client.PostAsync("v1/commands", new StringContent("{}", Encoding.UTF8, "application/json"));
                    Check((int)denied.StatusCode == 403);
                    client.DefaultRequestHeaders.Remove("Origin");
                    var caps = await Command(client, new Dictionary<string, object> { ["op"] = "capabilities" });
                    string session = (string)caps["session"];
                    long revision = (long)caps["sourceRevision"];
                    var opened = await Command(client, new Dictionary<string, object> { ["op"] = "document.open", ["mode"] = "authoring", ["session"] = session, ["expectedRevision"] = revision, ["path"] = "Assets/AuthoringChecks/agent.html" });
                    var found = await Command(client, new Dictionary<string, object> { ["op"] = "hierarchy.find", ["mode"] = "authoring", ["name"] = "agent" });
                    var list = (List<object>)((Dictionary<string, object>)found["data"])["nodes"];
                    string target = (string)((Dictionary<string, object>)list[0])["id"];
                    var changed = await Command(client, new Dictionary<string, object> { ["op"] = "properties.set", ["mode"] = "authoring", ["session"] = session, ["expectedRevision"] = opened["sourceRevision"], ["target"] = target, ["property"] = "text", ["value"] = "edited through HTTP 😀" });
                    Check(!File.ReadAllText("Assets/AuthoringChecks/agent.html").Contains("edited through HTTP"));
                    var capture = await Command(client, new Dictionary<string, object> { ["op"] = "preview.capture", ["mode"] = "authoring" });
                    Check((long)capture["renderedRevision"] == (long)changed["sourceRevision"]);
                    Check(File.Exists((string)((Dictionary<string, object>)capture["data"])["path"]));
                    await Command(client, new Dictionary<string, object> { ["op"] = "document.save", ["mode"] = "authoring", ["session"] = session, ["expectedRevision"] = changed["sourceRevision"] });
                    Check(File.ReadAllText("Assets/AuthoringChecks/agent.html").Contains("edited through HTTP 😀"));
                }
            });
            EditorApplication.CallbackFunction finish = null;
            finish = () =>
            {
                if (!work.IsCompleted)
                    return;
                EditorApplication.update -= finish;
                AgentServer.Stop();
                Directory.CreateDirectory("Evidence");
                if (work.IsFaulted)
                {
                    File.WriteAllText("Evidence/agent-http-failure.txt", work.Exception.ToString());
                    UnityEngine.Debug.LogException(work.Exception);
                    EditorApplication.Exit(1);
                }
                else
                {
                    File.WriteAllText("Evidence/agent-http.json", "{\"passed\":" + count + ",\"failed\":0}");
                    UnityEngine.Debug.Log("URUI HTTP PASS " + count);
                    EditorApplication.Exit(0);
                }
            };
            EditorApplication.update += finish;
        }
        internal static async Task<Dictionary<string, object>> Command(HttpClient client, Dictionary<string, object> request)
        {
            var response = await client.PostAsync("v1/commands", new StringContent(AgentJson.Stringify(request), Encoding.UTF8, "application/json"));
            string json = await response.Content.ReadAsStringAsync();
            Check((int)response.StatusCode == 202);
            var queued = (Dictionary<string, object>)AgentJson.Parse(json);
            string id = (string)queued["job"];
            for (int attempt = 0; attempt < 400; attempt++)
            {
                var job = (Dictionary<string, object>)AgentJson.Parse(await client.GetStringAsync("v1/jobs/" + id));
                string status = (string)job["status"];
                if (status == "done")
                    return (Dictionary<string, object>)job["result"];
                if (status == "failed" || status == "cancelled")
                    throw new Exception(AgentJson.Stringify(job));
                await Task.Delay(25);
            }
            throw new Exception("Agent job timeout: " + id);
        }
    }
}
