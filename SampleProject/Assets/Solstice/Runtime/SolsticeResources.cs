using System;
using System.Collections.Generic;
using UnityEngine;
using URUI.Effect;
using URUI.Resources;
using URUI.Styles;

namespace Solstice
{
    /// <summary>
    /// Supplies TextResources, runtime ImageResolver delegates, AudioClips,
    /// and URUI.Effect resolved textures/prefabs from `Resources/URUI/Solstice/`.
    /// </summary>
    public sealed class SolsticeResources : IEffectResourceResolver
    {
        const string Root = "URUI/Solstice/";
        const string BuiltinFontsRoot = "URUI/Fonts/";
        const string BuiltinEffectRoot = "URUI.Effect/";

        readonly Dictionary<string, Texture2D> textureCache = new(StringComparer.Ordinal);
        readonly Dictionary<string, GameObject> prefabCache = new(StringComparer.Ordinal);
        TextResource[] sharedTextResources;

        public TextResource[] GetSharedTextResources()
        {
            if (sharedTextResources != null)
            {
                return sharedTextResources;
            }

            var list = new List<TextResource>(24);
            AddTextResource(list, "/urui/base.css", "URUI/base");
            AddTextResource(list, "/urui/controls.css", "URUI/controls");
            AddTextResource(list, "/urui/behaviors.css", "URUI/behaviors");
            AddTextResource(list, "/urui-effect/Effect.css", BuiltinEffectRoot + "Effect");
            AddTextResource(list, "/solstice/Css/tokens.css", Root + "Css/tokens");
            AddTextResource(list, "/solstice/Css/base.css", Root + "Css/base");
            AddTextResource(list, "/solstice/Css/solstice.css", Root + "Css/solstice");
            AddTextResource(list, "/solstice/Fonts/LatoLatin-Regular.ttf.bytes", Root + "Fonts/LatoLatin-Regular.ttf");
            AddTextResource(list, "/solstice/Fonts/RobotoMono-Regular.ttf.bytes", Root + "Fonts/RobotoMono-Regular.ttf");
            AddTextResource(list, "/solstice/Fonts/Solstice-SC.ttf.bytes", Root + "Fonts/Solstice-SC.ttf");

            string[] families = { "Serif", "Sans", "Mono" };
            string[] faces = { "Regular", "Italic", "Bold", "BoldItalic" };
            for (var f = 0; f < families.Length; f++)
            {
                for (var s = 0; s < faces.Length; s++)
                {
                    var name = $"Liberation{families[f]}-{faces[s]}.ttf";
                    AddTextResource(list, $"/urui/Fonts/{name}.bytes", BuiltinFontsRoot + name);
                }
            }

            sharedTextResources = list.ToArray();
            return sharedTextResources;
        }

        static void AddTextResource(List<TextResource> list, string virtualUrl, string resourcePath)
        {
            var asset = Resources.Load<TextAsset>(resourcePath);
            if (asset)
            {
                list.Add(new TextResource(virtualUrl, asset));
            }
        }

        public Func<string, ImageLease> CreateImageResolver(Func<string, ImageLease> customOverride = null)
        {
            return url =>
            {
                if (string.IsNullOrEmpty(url))
                {
                    return null;
                }
                if (customOverride != null)
                {
                    var over = customOverride(url);
                    if (over != null)
                    {
                        return over;
                    }
                }
                return TryLoadTexture(url, out var tex) ? new ImageLease(tex) : null;
            };
        }

        public TextAsset LoadHtmlAsset(string pageName)
            => Resources.Load<TextAsset>(Root + "Html/" + pageName);

        public CompiledDocumentAsset LoadCompiledAsset(string assetName)
            => Resources.Load<CompiledDocumentAsset>(Root + "Html/" + assetName);

        public AudioClip LoadAudio(string clipName)
            => Resources.Load<AudioClip>(Root + "Audio/" + clipName);

        public bool TryLoadTexture(string url, out Texture2D texture)
        {
            texture = null;
            var u = Normalize(url);
            if (!u.StartsWith("/solstice/Images/", StringComparison.Ordinal))
            {
                return false;
            }
            var resourcePath = Root + u.Substring("/solstice/".Length);
            var extIdx = resourcePath.LastIndexOf('.');
            if (extIdx > 0)
            {
                resourcePath = resourcePath.Substring(0, extIdx);
            }
            if (textureCache.TryGetValue(resourcePath, out texture) && texture)
            {
                return true;
            }
            texture = Resources.Load<Texture2D>(resourcePath);
            if (texture)
            {
                textureCache[resourcePath] = texture;
                return true;
            }
            return false;
        }

        public ImageLease AcquireImage(ComputedImageValue image)
        {
            if (image.Kind != ComputedImageKind.Url)
            {
                return null;
            }
            if (TryLoadTexture(image.Url, out var tex))
            {
                return new ImageLease(tex);
            }
            if (!string.IsNullOrEmpty(image.SourceUrl) && TryLoadTexture(image.SourceUrl, out tex))
            {
                return new ImageLease(tex);
            }
            return null;
        }

        public GameObject ResolveParticles(string source)
        {
            if (string.IsNullOrEmpty(source))
            {
                return null;
            }
            var u = Normalize(source);
            if (!u.StartsWith("/solstice/Prefabs/", StringComparison.Ordinal))
            {
                return null;
            }
            var resourcePath = Root + u.Substring("/solstice/".Length);
            if (resourcePath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                resourcePath = resourcePath.Substring(0, resourcePath.Length - ".prefab".Length);
            }
            if (prefabCache.TryGetValue(resourcePath, out var prefab) && prefab)
            {
                return prefab;
            }
            prefab = Resources.Load<GameObject>(resourcePath);
            if (prefab)
            {
                prefabCache[resourcePath] = prefab;
                return prefab;
            }
            return null;
        }

        static string Normalize(string url)
        {
            if (string.IsNullOrEmpty(url)) return string.Empty;
            var q = url.IndexOf('?');
            if (q >= 0) url = url.Substring(0, q);
            return url.Replace('\\', '/');
        }
    }
}
