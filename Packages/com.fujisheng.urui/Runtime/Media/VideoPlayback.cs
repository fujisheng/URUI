using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Video;
using URUI.Resources;

namespace URUI.Media
{
    /// <summary>与视图格式无关的视频解码器。逐帧呈现到持久 GPU 纹理，租约可延迟资源释放。</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(VideoPlayer))]
    public sealed class VideoPlayback : MonoBehaviour
    {
        sealed class OutputOwner
        {
            internal readonly RenderTexture texture;
            readonly ImageLeaseSource source;
            internal OutputOwner(int width, int height, string name)
            {
                texture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32)
                { name = name, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
                if (!texture.Create()) { Destroy(texture); throw new InvalidOperationException("无法创建视频输出纹理。"); }
                var previous = RenderTexture.active;
                try { RenderTexture.active = texture; GL.Clear(true, true, Color.clear); }
                finally { RenderTexture.active = previous; }
                source = new ImageLeaseSource(texture, () => { if (texture) { texture.Release(); GpuRetirement.DestroyOwnedObject(texture); } });
            }
            internal ImageLease Acquire() => source.Acquire();
            internal void Release() => source.Dispose();
        }

        VideoPlayer player;
        OutputOwner output;
        Coroutine recovery;
        long readyVersion;
        long lastReadyFrame = -1;
        double lastProgressTime;
        bool requested;
        bool paused;
        bool presented;
        public int PresentedFrame { get; private set; } = -1;
        public ulong PresentedVersion {get;private set;}
        public RenderTexture Output => output?.texture;
        public bool HasPresentedFrame => presented;
        public event Action<VideoPlayback> FramePresented;
        public event Action<string> Failed;

        void Awake() => Configure();
        void OnEnable() { if (requested) player.Prepare(); }
        void Configure()
        {
            player ??= GetComponent<VideoPlayer>();
            player.playOnAwake = false;
            player.waitForFirstFrame = true;
            player.renderMode = VideoRenderMode.APIOnly;
            player.targetTexture = null;
            player.audioOutputMode = VideoAudioOutputMode.None;
            player.prepareCompleted -= OnPrepared; player.prepareCompleted += OnPrepared;
            player.frameReady -= OnFrame; player.frameReady += OnFrame;
            player.errorReceived -= OnError; player.errorReceived += OnError;
        }

        /// <summary>播放源资产；调用方持有 VideoClip。更换源会使旧输出退役，已有租约仍有效。</summary>
        public void Play(VideoClip clip, bool loop = true)
        {
            if (!clip) throw new ArgumentNullException(nameof(clip));
            Stop(); Configure();
            player.source = VideoSource.VideoClip; player.clip = clip; player.isLooping = loop;
            EnsureOutput((int)clip.width, (int)clip.height);
            Begin();
        }

        /// <summary>播放 URL；在 Prepare 完成后获得输出尺寸。</summary>
        public void PlayUrl(string url, bool loop = true)
        {
            if (string.IsNullOrWhiteSpace(url)) throw new ArgumentException("视频 URL 不能为空。", nameof(url));
            Stop(); Configure();
            player.source = VideoSource.Url; player.clip = null; player.url = url; player.isLooping = loop;
            Begin();
        }
        void Begin()
        {
            requested = true;
            lastProgressTime = Time.realtimeSinceStartupAsDouble;
            // 父视图激活时，兄弟组件的 OnEnable 可能先于本组件执行。
            // 保存播放请求，由本组件激活后准备，避免依赖 Unity 的组件回调顺序。
            if (isActiveAndEnabled) player.Prepare();
        }
        void EnsureOutput(int width, int height)
        {
            if (width <= 0 || height <= 0) throw new InvalidOperationException("视频源没有有效尺寸。");
            if (output != null && output.texture.width == width && output.texture.height == height) return;
            var replacement = new OutputOwner(width, height, name + " Video Output");
            output?.Release(); output = replacement;
        }
        void OnPrepared(VideoPlayer source)
        {
            if (!requested || !isActiveAndEnabled) return;
            EnsureOutput((int)source.width, (int)source.height);
            lastProgressTime = Time.realtimeSinceStartupAsDouble;
            source.sendFrameReadyEvents = true;
            if (!paused) source.Play();
        }
        void OnFrame(VideoPlayer source, long frame)
        {
            readyVersion++;
            if (frame != lastReadyFrame)
            {
                lastReadyFrame = frame;
                lastProgressTime = Time.realtimeSinceStartupAsDouble;
            }
            // 保留原 UGUI 的循环首帧门控，挂点只读取实际写入输出的帧。
            if (!requested || (frame == 0 && presented) || !source.texture || output == null) return;
            Graphics.Blit(source.texture, output.texture);
            presented = true; PresentedFrame = (int)frame; PresentedVersion++;
            FramePresented?.Invoke(this);
        }
        void OnError(VideoPlayer source, string message)
        {
            presented = false; PresentedFrame = -1;
            Failed?.Invoke(message);
            Debug.LogError($"VideoPlayback: {name}: {message}", this);
        }
        /// <summary>输出可在首帧前取得，此时内容透明；租约交由渲染器归还。</summary>
        public ImageLease AcquireOutput() => output?.Acquire() ?? throw new InvalidOperationException("视频输出尚未准备。");
        public void Pause()
        {
            paused = true;
            if (player) player.Pause();
        }
        public void Resume()
        {
            paused = false;
            lastProgressTime = Time.realtimeSinceStartupAsDouble;
            if (requested && player && player.isPrepared) player.Play();
        }
        /// <summary>从开头准备当前源，保留已被渲染器持有的输出纹理身份。</summary>
        public void Replay()
        {
            if (!player) throw new InvalidOperationException("视频源尚未配置。");
            if (recovery != null) { StopCoroutine(recovery); recovery = null; }
            player.sendFrameReadyEvents = false; player.Stop();
            presented = false; PresentedFrame = -1; readyVersion = 0; lastReadyFrame = -1; paused = false;
            Begin();
        }
        public void Stop()
        {
            requested = false; paused = false;
            if (recovery != null) { StopCoroutine(recovery); recovery = null; }
            if (player) { player.sendFrameReadyEvents = false; player.Stop(); }
            presented = false; PresentedFrame = -1; readyVersion = 0; lastReadyFrame = -1;
            output?.Release(); output = null;
        }
        void OnApplicationFocus(bool focused)
        {
            if (recovery != null) { StopCoroutine(recovery); recovery = null; }
            if (focused && requested && !paused && readyVersion > 0 && isActiveAndEnabled) recovery = StartCoroutine(Recover());
        }
        IEnumerator Recover()
        {
            yield return new WaitForSecondsRealtime(1);
            recovery = null;
            if (!requested || paused || !isActiveAndEnabled) yield break;
            if (player.isPaused) Replay();
            else CheckPlaybackProgress();
        }
        void Update() => CheckPlaybackProgress();
        void CheckPlaybackProgress()
        {
            if (!requested || paused || recovery != null) return;
            var elapsed = Time.realtimeSinceStartupAsDouble - lastProgressTime;
            if (elapsed < 3 || !player || !player.isPrepared || !player.isPlaying || player.playbackSpeed <= 0) return;
            var count = player.frameCount;
            if (count == 1 || (!player.isLooping && count > 0 && player.frame >= 0 && (ulong)player.frame >= count - 1)) return;
            var rate = (double)player.frameRate * player.playbackSpeed;
            var timeout = double.IsFinite(rate) && rate > 0 ? Math.Max(3, 3 / rate) : 3;
            // 平台解码器可能仍报告 playing，却不再交付新帧；重复首帧也不算进展。
            // 只重新准备当前源，Replay 保留输出纹理和已经借出的租约。
            if (elapsed >= timeout) Replay();
        }
        void OnDisable() => Stop();
        void OnDestroy()
        {
            Stop();
            if (player)
            {
                player.prepareCompleted -= OnPrepared; player.frameReady -= OnFrame; player.errorReceived -= OnError;
            }
            FramePresented = null; Failed = null;
        }
    }
}
