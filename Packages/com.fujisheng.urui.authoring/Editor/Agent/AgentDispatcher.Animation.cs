using System;
using System.Collections.Generic;
using System.Linq;
using URUI.Authoring.Animation;
using URUI.Authoring.Sessions;
using URUI.Authoring.Source;
using URUI.Authoring.Windows;
namespace URUI.Authoring.Agent
{
    public sealed partial class AgentDispatcher
    {
        object AnimationCommand(string operation, Dictionary<string, object> request, bool live)
        {
            if (operation != "animation.read" && (operation == "animation.sample" || operation == "animation.play" || operation == "animation.pause" || operation == "animation.stop" || operation == "animation.record") && live != UnityEditor.EditorApplication.isPlaying)
                throw new InvalidOperationException("preview_unavailable: 动画预览 mode 与当前 Edit/Play 状态不一致。");
            AnimationEnvironment.EnsureWorkspace();
            var workspace = AnimationEnvironment.Workspace;
            void Edit(Action action)
            {
                RequireAuthor(live);
                SessionUndoBridge.Record(AuthoringEnvironment.Author, action);
            }
            switch (operation)
            {
                case "animation.read":
                    break;
                case "animation.open":
                    RequireAuthor(live);
                    AnimationEnvironment.Stop();
                    workspace.Open(Text(request, "path"));
                    break;
                case "animation.create":
                    Edit(() => workspace.Create(workspace.Revision));
                    break;
                case "animation.preset":
                    string target = Target(request, live).SourceKey;
                    Edit(() => { string id = workspace.AddPreset(target, Text(request, "preset"), OptionalNumber(request, "start", 0), OptionalNumber(request, "duration", .3), workspace.Revision); AnimationEnvironment.SelectTrack(id); });
                    break;
                case "animation.particles":
                    string particleTarget = Target(request, live).SourceKey;
                    Edit(() => { string id = workspace.AddParticles(particleTarget, Text(request, "source", false) ?? "", OptionalNumber(request, "start", 0), OptionalNumber(request, "duration", 1), workspace.Revision); AnimationEnvironment.SelectTrack(id); });
                    break;
                case "animation.track":
                    if (!(request.TryGetValue("changes", out var raw) && raw is Dictionary<string, object> changes))
                        throw new InvalidOperationException("bad_request: changes must be an object");
                    Edit(() => workspace.SetTrack(Text(request, "track"), track =>
                    {
                        foreach (var pair in changes)
                        {
                            var row = new Dictionary<string, object> { { pair.Key, pair.Value } };
                            switch (pair.Key)
                            {
                                case "start":
                                    track.Start = Number(row, pair.Key);
                                    break;
                                case "duration":
                                    track.Duration = Number(row, pair.Key);
                                    break;
                                case "iterations":
                                    track.Iterations = checked((int)Integer(row, pair.Key));
                                    break;
                                case "alternate":
                                    track.Alternate = Boolean(row, pair.Key);
                                    break;
                                case "muted":
                                    track.Muted = Boolean(row, pair.Key);
                                    break;
                                case "easing":
                                    track.Easing = Text(row, pair.Key);
                                    break;
                                case "target":
                                    track.Target = Text(row, pair.Key);
                                    break;
                                case "keyframes":
                                    track.Keyframes = Text(row, pair.Key);
                                    break;
                                case "source":
                                    track.Source = Text(row, pair.Key);
                                    break;
                                case "seed":
                                    track.Seed = checked((uint)Integer(row, pair.Key));
                                    break;
                                case "speed":
                                    track.Speed = Number(row, pair.Key);
                                    break;
                                default:
                                    throw new InvalidOperationException("bad_request: unknown track field " + pair.Key);
                            }
                        }
                    }, workspace.Revision));
                    break;
                case "animation.lock":
                    Edit(() => workspace.SetLocked(Text(request, "track"), Boolean(request, "locked"), workspace.Revision));
                    break;
                case "animation.remove":
                    Edit(() => workspace.RemoveTrack(Text(request, "track"), workspace.Revision));
                    break;
                case "animation.key":
                    Edit(() =>
                    {
                        var rows = new List<Dictionary<string, object>>();
                        if (request.TryGetValue("keys", out var rawKeys) && rawKeys is List<object> list)
                            foreach (var row in list)
                                rows.Add(row as Dictionary<string, object> ?? throw new InvalidOperationException("bad_request: keys must contain objects"));
                        else
                            rows.Add(request);
                        if (rows.Count == 0 || rows.Count > 4096)
                            throw new InvalidOperationException("bad_request: key limit");
                        var candidates = new Dictionary<SourceFile, string>();
                        var definition = workspace.Definition ?? throw new InvalidOperationException("请先创建序列。");
                        foreach (var row in rows)
                        {
                            var track = definition.Tracks.FirstOrDefault(t => t.Id == Text(row, "track")) ?? throw new InvalidOperationException("stale_track");
                            if (track.Locked || track.Kind != "css")
                                throw new InvalidOperationException("轨道已锁定或不支持 CSS 关键帧。");
                            var file = workspace.KeyframeFile(track);
                            string text = candidates.TryGetValue(file, out var previous) ? previous : file.Text;
                            candidates[file] = CssKeyframes.SetKey(text, track.Keyframes, Number(row, "position"), Text(row, "property"), Boolean(row, "unset") ? null : Text(row, "value"));
                        }
                        workspace.PublishKeys(candidates.Select(p => new SourceChange(p.Key.Path, p.Value, p.Key.Revision)), workspace.Revision);
                    });
                    break;
                case "animation.keys.move":
                case "animation.keys.remove":
                    Edit(() =>
                    {
                        if (!(request.TryGetValue("keys", out var rawKeys) && rawKeys is List<object> list) || list.Count == 0 || list.Count > 4096)
                            throw new InvalidOperationException("bad_request: keys are required");
                        var keys = new List<AnimationWorkspace.KeySelection>();
                        foreach (var item in list)
                        {
                            var row = item as Dictionary<string, object> ?? throw new InvalidOperationException("bad_request: keys must contain objects");
                            keys.Add(new AnimationWorkspace.KeySelection(Text(row, "track"), Number(row, "position")));
                        }
                        workspace.TransformKeys(keys, OptionalNumber(request, "seconds", 0), Boolean(request, "copy"), operation == "animation.keys.remove", workspace.Revision);
                    });
                    break;
                case "animation.sample":
                    AnimationEnvironment.Seek(Number(request, "time"));
                    break;
                case "animation.play":
                    AnimationEnvironment.Play();
                    break;
                case "animation.pause":
                    AnimationEnvironment.Pause();
                    break;
                case "animation.stop":
                    AnimationEnvironment.Stop();
                    break;
                case "animation.record":
                    RequireAuthor(live);
                    if (Boolean(request, "enabled"))
                    {
                        AnimationEnvironment.SelectTrack(Text(request, "track"));
                        AnimationEnvironment.BeginRecording(Target(request, false));
                    }
                    else
                        AnimationEnvironment.EndRecording();
                    break;
                default:
                    throw new InvalidOperationException("unsupported_operation: " + operation);
            }
            return Obj("path", workspace.Path, "definition", workspace.Definition, "time", AnimationEnvironment.Time, "playing", AnimationEnvironment.Playing, "recording", AnimationEnvironment.Recording, "selectedTrack", AnimationEnvironment.SelectedTrack);
        }
        static double OptionalNumber(Dictionary<string, object> request, string key, double fallback) => request.ContainsKey(key) ? Number(request, key) : fallback;
    }
}
