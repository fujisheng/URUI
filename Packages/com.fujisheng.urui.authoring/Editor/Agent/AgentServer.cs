using L = URUI.Editor.Localization.EditorText;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEngine;
using URUI.Authoring.Sessions;

namespace URUI.Authoring.Agent
{
    /// <summary>按项目启用的 Editor-only loopback HTTP 服务；Unity API 仅在主线程 Pump 调用。</summary>
    [InitializeOnLoad]
    public static class AgentServer
    {
        const int MaxBodyBytes = 1048576;
        static HttpListener listener;
        static Thread thread;
        static AgentJobQueue jobs;
        static AgentDispatcher dispatcher;
        static string token, epoch, url;
        static volatile bool running;
        static string Preference => "URUI.Authoring.Agent." + Application.dataPath;
        public static string EndpointPath => Path.Combine(AuthoringEnvironment.ProjectRoot, "Library/URUI/Authoring/endpoint.json");
        public static bool IsRunning => running;
        [DllImport("libc", SetLastError = true)] static extern int chmod(string path, uint mode);
        static AgentServer()
        {
            EditorApplication.delayCall += () =>
            {
                URUI.Editor.Localization.EditorMenus.Register("Window/URUI/Agent/Enable Local API", 100, Enable, CanEnable);
                URUI.Editor.Localization.EditorMenus.Register("Window/URUI/Agent/Disable Local API", 100, Disable, CanDisable);
                URUI.Editor.Localization.EditorMenus.Register("Window/URUI/Agent/Show Connection File", 100, Reveal);
            };
            EditorApplication.update += Pump;
            AssemblyReloadEvents.beforeAssemblyReload += Stop;
            EditorApplication.quitting += Stop;
            EditorApplication.delayCall += () => { if (EditorPrefs.GetBool(Preference, false)) AuthoringEnvironment.Try(Start); };
        }
        [MenuItem("Window/URUI/Agent/Enable Local API")]
        static void Enable()
        {
            Start();
            EditorPrefs.SetBool(Preference, true);
        }
        [MenuItem("Window/URUI/Agent/Enable Local API", true)] static bool CanEnable() => !IsRunning;
        [MenuItem("Window/URUI/Agent/Disable Local API")]
        static void Disable()
        {
            EditorPrefs.SetBool(Preference, false);
            Stop();
        }
        [MenuItem("Window/URUI/Agent/Disable Local API", true)] static bool CanDisable() => IsRunning;
        [MenuItem("Window/URUI/Agent/Show Connection File")]
        static void Reveal()
        {
            if (File.Exists(EndpointPath))
                EditorUtility.RevealInFinder(EndpointPath);
            else
                AuthoringEnvironment.Report("Enable Local API first.");
        }

        public static void Start()
        {
            if (running)
                return;
            var probe = new TcpListener(IPAddress.Loopback, 0);
            int port;
            try
            {
                probe.Start();
                port = ((IPEndPoint)probe.LocalEndpoint).Port;
            }
            finally { probe.Stop(); }
            var random = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
                rng.GetBytes(random);
            token = BitConverter.ToString(random).Replace("-", "").ToLowerInvariant();
            epoch = Guid.NewGuid().ToString("N");
            url = "http://127.0.0.1:" + port + "/";
            listener = new HttpListener();
            listener.Prefixes.Add(url);
            try
            {
                listener.Start();
                jobs = new AgentJobQueue(epoch);
                dispatcher = new AgentDispatcher(epoch);
                Directory.CreateDirectory(Path.GetDirectoryName(EndpointPath));
                string temporary = EndpointPath + ".tmp";
                File.WriteAllText(temporary, AgentJson.Stringify(AgentDispatcher.Obj("apiVersion", 1, "url", url, "token", token, "editorSession", epoch, "project", AuthoringEnvironment.ProjectRoot)), new UTF8Encoding(false));
                if ((Application.platform == RuntimePlatform.OSXEditor || Application.platform == RuntimePlatform.LinuxEditor) && chmod(temporary, 384) != 0)
                    throw new IOException("Cannot restrict endpoint file permissions.");
                if (File.Exists(EndpointPath))
                    File.Replace(temporary, EndpointPath, null);
                else
                    File.Move(temporary, EndpointPath);
                running = true;
                AuthoringEnvironment.Observe(true);
                thread = new Thread(Listen) { IsBackground = true, Name = "URUI Authoring HTTP" };
                thread.Start();
                AuthoringEnvironment.Report("Local Agent API enabled · " + EndpointPath);
            }
            catch { listener?.Close(); listener = null; jobs?.Invalidate(); if (File.Exists(EndpointPath + ".tmp")) File.Delete(EndpointPath + ".tmp"); throw; }
        }
        public static void Stop()
        {
            if (running)
                AuthoringEnvironment.Observe(false);
            running = false;
            jobs?.Invalidate();
            listener?.Close();
            listener = null;
            if (thread != null && thread != Thread.CurrentThread)
                thread.Join(1000);
            thread = null;
            jobs = null;
            dispatcher = null;
            if (File.Exists(EndpointPath))
                File.Delete(EndpointPath);
        }
        static void Listen()
        {
            var current = listener;
            while (running)
            {
                HttpListenerContext context;
                try
                {
                    context = current.GetContext();
                }
                catch (Exception error) when (error is HttpListenerException || error is ObjectDisposedException) { break; }
                try
                {
                    Handle(context);
                }
                catch (Exception error)
                {
                    try
                    {
                        Send(context, 400, AgentJson.Stringify(AgentDispatcher.Obj("ok", false, "error", error.Message)));
                    }
                    catch (Exception) { }
                }
                finally { try { context.Response.Close(); } catch (Exception) { } }
            }
        }
        static void Handle(HttpListenerContext context)
        {
            var request = context.Request;
            if (request.RemoteEndPoint == null || !IPAddress.IsLoopback(request.RemoteEndPoint.Address))
            {
                Send(context, 403, "{\"error\":\"loopback_required\"}");
                return;
            }
            if (!ConstantEquals(request.Headers["Authorization"], "Bearer " + token))
            {
                Send(context, 401, "{\"error\":\"authentication_required\"}");
                return;
            }
            if (request.Headers["X-URUI-Session"] != epoch)
            {
                Send(context, 409, "{\"error\":\"editor_session_changed\"}");
                return;
            }
            var expectedHost = new Uri(url).Authority;
            if (!string.Equals(request.Headers["Host"], expectedHost, StringComparison.OrdinalIgnoreCase))
            {
                Send(context, 403, "{\"error\":\"invalid_host\"}");
                return;
            }
            string origin = request.Headers["Origin"];
            if (!string.IsNullOrEmpty(origin) && origin != url.TrimEnd('/'))
            {
                Send(context, 403, "{\"error\":\"invalid_origin\"}");
                return;
            }
            string path = request.Url.AbsolutePath;
            if (request.HttpMethod == "POST" && path == "/v1/commands")
            {
                if (request.ContentLength64 > MaxBodyBytes)
                {
                    Send(context, 413, "{\"error\":\"request_too_large\"}");
                    return;
                }
                if (request.ContentType == null || !request.ContentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase))
                {
                    Send(context, 415, "{\"error\":\"json_required\"}");
                    return;
                }
                using (var body = new MemoryStream())
                {
                    var buffer = new byte[8192];
                    int count;
                    while ((count = request.InputStream.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        if (body.Length + count > MaxBodyBytes)
                        {
                            Send(context, 413, "{\"error\":\"request_too_large\"}");
                            return;
                        }
                        body.Write(buffer, 0, count);
                    }
                    var json = new UTF8Encoding(false, true).GetString(body.ToArray());
                    if (!(AgentJson.Parse(json) is Dictionary<string, object> command))
                        throw new InvalidOperationException("bad_request: expected JSON object");
                    var job = jobs.Submit(command);
                    Send(context, 202, AgentJson.Stringify(AgentDispatcher.Obj("job", job.Id, "status", job.Status, "editorSession", epoch)));
                }
            }
            else if (path.StartsWith("/v1/jobs/", StringComparison.Ordinal))
            {
                string id = path.Substring(9);
                var job = jobs.Read(id);
                if (job == null)
                {
                    Send(context, 404, "{\"error\":\"job_not_found\"}");
                    return;
                }
                if (request.HttpMethod == "DELETE")
                {
                    Send(context, jobs.Cancel(id) ? 200 : 409, "{\"job\":\"" + id + "\",\"status\":\"" + jobs.Read(id).Status + "\"}");
                    return;
                }
                if (request.HttpMethod != "GET")
                {
                    Send(context, 405, "{\"error\":\"method_not_allowed\"}");
                    return;
                }
                string header = AgentJson.Stringify(AgentDispatcher.Obj("job", job.Id, "status", job.Status));
                Send(context, 200, header.Substring(0, header.Length - 1) + ",\"result\":" + (job.ResultJson ?? "null") + "}");
            }
            else
                Send(context, 404, "{\"error\":\"not_found\"}");
        }
        static bool ConstantEquals(string a, string b)
        {
            if (a == null || a.Length != b.Length)
                return false;
            int difference = 0;
            for (int i = 0; i < a.Length; i++)
                difference |= a[i] ^ b[i];
            return difference == 0;
        }
        static void Send(HttpListenerContext context, int status, string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            context.Response.StatusCode = status;
            context.Response.ContentType = "application/json; charset=utf-8";
            context.Response.Headers["Cache-Control"] = "no-store";
            context.Response.ContentLength64 = bytes.Length;
            context.Response.OutputStream.Write(bytes, 0, bytes.Length);
        }
        static void Pump()
        {
            if (!running || jobs == null)
                return;
            var timer = Stopwatch.StartNew();
            int count = 0;
            while (count++ < 4 && timer.ElapsedMilliseconds < 16 && jobs.RunNext(epoch, request => AgentJson.Stringify(dispatcher.Execute(request))))
            {
            }
        }
    }
}
