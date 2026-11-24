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
    /// <summary>
    /// 按项目启用的 Editor-only loopback HTTP 服务；Unity API 仅在主线程 Pump 调用。
    /// </summary>
    [InitializeOnLoad]
    public static class AgentServer
    {
        const int MaxBodyBytes = 1048576;
        const int RequestReadTimeoutMs = 5000;

        sealed class ServerState
        {
            internal readonly HttpListener Listener;
            internal readonly AgentJobQueue Jobs;
            internal readonly AgentDispatcher Dispatcher;
            internal readonly string Token;
            internal readonly string ExpectedAuth;
            internal readonly string Epoch;
            internal readonly string Url;
            internal readonly string ExpectedHost;
            internal readonly string ExpectedOrigin;
            internal readonly CancellationTokenSource Shutdown = new CancellationTokenSource();

            internal ServerState(HttpListener listener, AgentJobQueue jobs, AgentDispatcher dispatcher, string token, string epoch, string url)
            {
                Listener = listener;
                Jobs = jobs;
                Dispatcher = dispatcher;
                Token = token;
                ExpectedAuth = "Bearer " + token;
                Epoch = epoch;
                Url = url;
                ExpectedHost = new Uri(url).Authority;
                ExpectedOrigin = url.TrimEnd('/');
            }
        }

        static ServerState activeState;
        static Thread thread;
        static string Preference => "URUI.Authoring.Agent." + Application.dataPath;
        public static string EndpointPath => Path.Combine(AuthoringEnvironment.ProjectRoot, "Library/URUI/Authoring/endpoint.json");
        public static bool IsRunning => Volatile.Read(ref activeState) != null;
        [DllImport("libc", SetLastError = true)]
        static extern int chmod(string path, uint mode);
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
            EditorApplication.delayCall += () =>
            {
                if (EditorPrefs.GetBool(Preference, false))
                {
                    AuthoringEnvironment.Try(Start);
                }
            };
        }
        [MenuItem("Window/URUI/Agent/Enable Local API")]
        static void Enable()
        {
            Start();
            EditorPrefs.SetBool(Preference, true);
        }
        [MenuItem("Window/URUI/Agent/Enable Local API", true)]
        static bool CanEnable() => !IsRunning;
        [MenuItem("Window/URUI/Agent/Disable Local API")]
        static void Disable()
        {
            EditorPrefs.SetBool(Preference, false);
            Stop();
        }
        [MenuItem("Window/URUI/Agent/Disable Local API", true)]
        static bool CanDisable() => IsRunning;
        [MenuItem("Window/URUI/Agent/Show Connection File")]
        static void Reveal()
        {
            if (File.Exists(EndpointPath))
            {
                EditorUtility.RevealInFinder(EndpointPath);
            }
            else
            {
                AuthoringEnvironment.Report("Enable Local API first.");
            }
        }

        public static void Start()
        {
            if (Volatile.Read(ref activeState) != null)
            {
                return;
            }
            var probe = new TcpListener(IPAddress.Loopback, 0);
            int port;
            try
            {
                probe.Start();
                port = ((IPEndPoint)probe.LocalEndpoint).Port;
            }
            finally
            {
                probe.Stop();
            }
            var random = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(random);
            }
            string token = BitConverter.ToString(random).Replace("-", "").ToLowerInvariant();
            string epoch = Guid.NewGuid().ToString("N");
            string url = "http://127.0.0.1:" + port + "/";
            var listener = new HttpListener();
            listener.Prefixes.Add(url);
            ServerState state = null;
            try
            {
                listener.Start();
                var jobs = new AgentJobQueue(epoch);
                var dispatcher = new AgentDispatcher(epoch);
                state = new ServerState(listener, jobs, dispatcher, token, epoch, url);
                Directory.CreateDirectory(Path.GetDirectoryName(EndpointPath));
                string temporary = EndpointPath + ".tmp";
                File.WriteAllText(temporary, AgentJson.Stringify(AgentDispatcher.Obj("apiVersion", 1, "url", url, "token", token, "editorSession", epoch, "project", AuthoringEnvironment.ProjectRoot)), new UTF8Encoding(false));
                RestrictEndpointPermissions(temporary);
                if (File.Exists(EndpointPath))
                {
                    File.Replace(temporary, EndpointPath, null);
                }
                else
                {
                    File.Move(temporary, EndpointPath);
                }
                Volatile.Write(ref activeState, state);
                AuthoringEnvironment.Observe(true);
                thread = new Thread(Listen)
                {
                    IsBackground = true,
                    Name = "URUI Authoring HTTP"
                };
                thread.Start(state);
                AuthoringEnvironment.Report("Local Agent API enabled · " + EndpointPath);
            }
            catch
            {
                Volatile.Write(ref activeState, null);
                try
                {
                    state?.Shutdown.Cancel();
                    state?.Shutdown.Dispose();
                }
                catch { }
                state?.Jobs.Invalidate();
                try
                {
                    listener.Close();
                }
                catch { }
                if (File.Exists(EndpointPath + ".tmp"))
                {
                    File.Delete(EndpointPath + ".tmp");
                }
                throw;
            }
        }
        static void RestrictEndpointPermissions(string path)
        {
            if (Application.platform == RuntimePlatform.OSXEditor || Application.platform == RuntimePlatform.LinuxEditor)
            {
                if (chmod(path, 384) != 0)
                {
                    throw new IOException("Cannot restrict endpoint file permissions.");
                }
                return;
            }
            if (Application.platform == RuntimePlatform.WindowsEditor)
            {
                string user = Environment.UserName;
                if (string.IsNullOrEmpty(user))
                {
                    return;
                }
                string domain = Environment.UserDomainName;
                string account = string.IsNullOrEmpty(domain) ? user : domain + "\\" + user;
                try
                {
                    var psi = new ProcessStartInfo("icacls", $"\"{path}\" /inheritance:r /grant:r \"{account}:(R,W)\"")
                    {
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };
                    using (var process = Process.Start(psi))
                    {
                        process?.WaitForExit(3000);
                    }
                }
                catch
                {
                    // Best-effort ACL restriction on Windows environments where icacls is unavailable.
                }
            }
        }
        public static void Stop()
        {
            var state = Interlocked.Exchange(ref activeState, null);
            if (state != null)
            {
                AuthoringEnvironment.Observe(false);
                try
                {
                    state.Shutdown.Cancel();
                }
                catch { }
                state.Jobs.Invalidate();
                try
                {
                    state.Listener.Close();
                }
                catch { }
            }
            var worker = thread;
            thread = null;
            if (worker != null && worker != Thread.CurrentThread)
            {
                worker.Join(1000);
            }
            if (state != null)
            {
                try
                {
                    state.Shutdown.Dispose();
                }
                catch { }
            }
            if (File.Exists(EndpointPath))
            {
                File.Delete(EndpointPath);
            }
        }
        static void Listen(object arg)
        {
            var state = (ServerState)arg;
            while (!state.Shutdown.IsCancellationRequested)
            {
                HttpListenerContext context;
                try
                {
                    context = state.Listener.GetContext();
                }
                catch (Exception error) when (error is HttpListenerException || error is ObjectDisposedException)
                {
                    break;
                }
                try
                {
                    Handle(state, context);
                }
                catch (OperationCanceledException)
                {
                    try
                    {
                        context.Response.Abort();
                    }
                    catch { }
                }
                catch (Exception error)
                {
                    try
                    {
                        Send(context, 400, AgentJson.Stringify(AgentDispatcher.Obj("ok", false, "error", error.Message)));
                    }
                    catch (Exception) { }
                }
                finally
                {
                    try
                    {
                        context.Response.Close();
                    }
                    catch (Exception) { }
                }
            }
        }
        static void Handle(ServerState state, HttpListenerContext context)
        {
            var request = context.Request;
            if (request.RemoteEndPoint == null || !IPAddress.IsLoopback(request.RemoteEndPoint.Address))
            {
                Send(context, 403, "{\"error\":\"loopback_required\"}");
                return;
            }
            if (!ConstantEquals(request.Headers["Authorization"], state.ExpectedAuth))
            {
                Send(context, 401, "{\"error\":\"authentication_required\"}");
                return;
            }
            if (request.Headers["X-URUI-Session"] != state.Epoch)
            {
                Send(context, 409, "{\"error\":\"editor_session_changed\"}");
                return;
            }
            if (!string.Equals(request.Headers["Host"], state.ExpectedHost, StringComparison.OrdinalIgnoreCase))
            {
                Send(context, 403, "{\"error\":\"invalid_host\"}");
                return;
            }
            string origin = request.Headers["Origin"];
            if (!string.IsNullOrEmpty(origin) && origin != state.ExpectedOrigin)
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
                using (var readCts = CancellationTokenSource.CreateLinkedTokenSource(state.Shutdown.Token))
                {
                    readCts.CancelAfter(RequestReadTimeoutMs);
                    using (readCts.Token.Register(() =>
                    {
                        try
                        {
                            context.Response.Abort();
                        }
                        catch { }
                    }))
                    using (var body = new MemoryStream())
                    {
                        var buffer = new byte[8192];
                        int count;
                        while ((count = request.InputStream.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            readCts.Token.ThrowIfCancellationRequested();
                            if (body.Length + count > MaxBodyBytes)
                            {
                                Send(context, 413, "{\"error\":\"request_too_large\"}");
                                return;
                            }
                            body.Write(buffer, 0, count);
                        }
                        var json = new UTF8Encoding(false, true).GetString(body.GetBuffer(), 0, (int)body.Length);
                        if (!(AgentJson.Parse(json) is Dictionary<string, object> command))
                        {
                            throw new InvalidOperationException("bad_request: expected JSON object");
                        }
                        var job = state.Jobs.Submit(command);
                        Send(context, 202, AgentJson.Stringify(AgentDispatcher.Obj("job", job.Id, "status", job.Status, "editorSession", state.Epoch)));
                    }
                }
            }
            else if (path.StartsWith("/v1/jobs/", StringComparison.Ordinal))
            {
                string id = path.Substring(9);
                if (request.HttpMethod == "DELETE")
                {
                    bool cancelled = state.Jobs.TryCancel(id, out var snapshot);
                    if (snapshot == null)
                    {
                        Send(context, 404, "{\"error\":\"job_not_found\"}");
                        return;
                    }
                    Send(context, cancelled ? 200 : 409, AgentJson.Stringify(AgentDispatcher.Obj("job", snapshot.Id, "status", snapshot.Status)));
                    return;
                }
                if (request.HttpMethod != "GET")
                {
                    if (state.Jobs.Read(id) == null)
                    {
                        Send(context, 404, "{\"error\":\"job_not_found\"}");
                        return;
                    }
                    Send(context, 405, "{\"error\":\"method_not_allowed\"}");
                    return;
                }
                var job = state.Jobs.Read(id);
                if (job == null)
                {
                    Send(context, 404, "{\"error\":\"job_not_found\"}");
                    return;
                }
                string header = AgentJson.Stringify(AgentDispatcher.Obj("job", job.Id, "status", job.Status));
                Send(context, 200, header.Substring(0, header.Length - 1) + ",\"result\":" + (job.ResultJson ?? "null") + "}");
            }
            else
            {
                Send(context, 404, "{\"error\":\"not_found\"}");
            }
        }
        static bool ConstantEquals(string a, string b)
        {
            if (a == null || a.Length != b.Length)
            {
                return false;
            }
            int difference = 0;
            for (int i = 0; i < a.Length; i++)
            {
                difference |= a[i] ^ b[i];
            }
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
        static readonly long PumpBudgetTicks = Stopwatch.Frequency * 16 / 1000;

        static void Pump()
        {
            var state = Volatile.Read(ref activeState);
            if (state == null)
            {
                return;
            }
            long start = Stopwatch.GetTimestamp();
            int count = 0;
            while (count++ < 4 && (Stopwatch.GetTimestamp() - start) < PumpBudgetTicks && state.Jobs.RunNext(state.Epoch, request => AgentJson.Stringify(state.Dispatcher.Execute(request))))
            {
            }
        }
    }
}
