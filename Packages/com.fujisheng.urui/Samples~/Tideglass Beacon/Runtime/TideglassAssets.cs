using System;
using System.Collections.Generic;
using UnityEngine;
using URUI.Animation;
using URUI.Documents;
using URUI.Elements;
using URUI.Hosting;
using URUI.Resources;
using URUI.Rendering.Effects;

namespace URUI.Samples.Tideglass
{
    internal sealed class TideglassAssets
    {
        internal const string Root = "URUI/Tideglass/";
        internal const string Url = "/tideglass/";
        internal const string ImageUrl = Url + "Images/";

        readonly Dictionary<string, Texture2D> textures = new(StringComparer.Ordinal);
        readonly Dictionary<string, SequenceDefinition> sequences = new(StringComparer.Ordinal);
        readonly List<TextResource> text = new();

        internal TideglassAssets()
        {
            Add("/urui/base.css", "URUI/base");
            Add("/urui/controls.css", "URUI/controls");
            Add(Url + "Tideglass.css", Root + "Tideglass");
            Add(Url + "Station.css", Root + "Station");
            Add(Url + "Bridge.css", Root + "Bridge");
            Add(Url + "Motion.css", Root + "Motion");
            Add(Url + "Fonts/TideglassSans.ttf.bytes", Root + "Fonts/TideglassSans.ttf");
            foreach (string family in new[] { "Sans", "Serif", "Mono" })
            {
                foreach (string face in new[] { "Regular", "Italic", "Bold", "BoldItalic" })
                {
                    string file = $"Liberation{family}-{face}.ttf";
                    Add("/urui/Fonts/" + file + ".bytes", "URUI/Fonts/" + file);
                }
            }
        }

        void Add(string url, string path) => text.Add(new TextResource(url, Get<TextAsset>(path)));

        internal static T Get<T>(string path) where T : UnityEngine.Object
        {
            var result = UnityEngine.Resources.Load<T>(path);
            if (!result)
                throw new InvalidOperationException($"Missing Tideglass resource: {path}");
            return result;
        }

        internal Document CreateDocument(Surface surface, Transform owner, string name, bool withEffects = false, bool opticalGlass = false)
        {
            var child = new GameObject(name);
            child.transform.SetParent(owner, false);
            var document = child.AddComponent<Document>();
            document.Surface = surface;
            if (opticalGlass)
                document.Effects.Register(new EffectDefinition("tideglass-lens", EffectKind.Element,
                    new TideglassRefraction(Get<Material>(Root + "OpticalGlass")), "tideglass-lens", "premultiplied"));
            document.ImageResolver = ResolveImage;
            var resources = new List<TextResource>(text);
            if (withEffects)
                resources.Add(new TextResource("/urui-effect/Effect.css", Get<TextAsset>("URUI.Effect/Effect")));
            document.TextResources = resources.ToArray();
            return document;
        }

        internal static void Load(Document document, string page)
        {
            document.Load(Get<TextAsset>(Root + "Pages/" + page), Url + "Pages/" + page + ".html");
        }

        internal static string ImageSource(string name) => ImageUrl + name + ".png";

        internal ImageLease ResolveImage(string url)
        {
            if (string.IsNullOrEmpty(url) || !url.StartsWith(ImageUrl, StringComparison.Ordinal) ||
                !url.EndsWith(".png", StringComparison.Ordinal))
                return null;

            string name = url.Substring(ImageUrl.Length, url.Length - ImageUrl.Length - 4);
            if (!textures.TryGetValue(name, out var texture))
            {
                texture = UnityEngine.Resources.Load<Texture2D>(Root + "Images/" + name);
                textures[name] = texture;
            }
            return texture ? new ImageLease(texture) : null;
        }

        internal bool HasTexture(string name) => textures.TryGetValue(name, out var texture) && texture;

        internal ResourceRequest LoadTextureAsync(string name) =>
            UnityEngine.Resources.LoadAsync<Texture2D>(Root + "Images/" + name);

        internal void KeepTexture(string name, ResourceRequest request)
        {
            var texture = (Texture2D)request.asset;
            if (!texture)
                throw new InvalidOperationException($"Missing Tideglass image: {name}");
            textures[name] = texture;
        }

        internal SequencePlayer Play(Document document, string sequence, IDictionary<string, Element> slots = null)
        {
            if (!sequences.TryGetValue(sequence, out var definition))
            {
                string path = Root + "Sequences/" + sequence + ".urui-sequence";
                definition = SequenceDefinition.Parse(Get<TextAsset>(path).text);
                sequences.Add(sequence, definition);
            }
            document.SynchronizeLayout();
            var player = new SequencePlayer(document.Body, definition, slots: slots);
            player.Play();
            return player;
        }
    }
}
