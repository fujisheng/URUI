using System;
using System.IO;
using System.Xml;
using UnityEngine;
using URUI.Resources;
using UnityResources = UnityEngine.Resources;

namespace URUI.Samples.GameFlow
{
    /// <summary>Bounded sample adapter, not a general URUI virtual filesystem.</summary>
    public sealed class GameFlowResources : IDisposable
    {
        const string Root = "URUI/GameFlow/";
        string directory, stylesheet;
        public string FontPath { get; private set; }
        public Vector2 BackgroundSize { get; private set; }
        public ImageAsset[] Images { get; private set; }

        static T Required<T>(string path) where T : UnityEngine.Object =>
            UnityResources.Load<T>(Root + path) ?? throw new InvalidOperationException("Missing Game Flow resource: " + Root + path);

        public void Prepare()
        {
            var font = Required<TextAsset>("Fonts/LatoLatin-Regular.ttf");
            var css = Required<TextAsset>("Game.rcss");
            directory = Path.Combine(Application.temporaryCachePath, "URUI-GameFlow", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            FontPath = Path.Combine(directory, "LatoLatin-Regular.ttf");
            stylesheet = Path.Combine(directory, "Game.rcss");
            File.WriteAllBytes(FontPath, font.bytes);
            File.WriteAllText(stylesheet, css.text);
            var background = Required<Texture2D>("Images/world");
            if (!background.isReadable) throw new InvalidOperationException("Background image needs the included Read/Write setting.");
            BackgroundSize = new Vector2(background.width, background.height);
            var actor = Required<Texture2D>("Images/operator");
            if (!actor.isReadable) throw new InvalidOperationException("Operator image needs the included Read/Write setting.");
            var bootstrap = new System.Collections.Generic.List<ImageAsset> {
                new ImageAsset("gameflow/background.png", background), new ImageAsset("gameflow/operator.png", actor) };
            // Loading/retry is drawn before the remaining image catalog is ready.
            // Its frame must be part of the bootstrap mapping too.
            for (int y = 0; y < 3; y++)
                for (int x = 0; x < 3; x++)
                {
                    string name = "panel-" + y + x;
                    var image = Required<Texture2D>("Images/" + name);
                    if (!image.isReadable) throw new InvalidOperationException("Bootstrap frame needs the included Read/Write setting.");
                    bootstrap.Add(new ImageAsset("gameflow/" + name + ".png", image));
                }
            Images = bootstrap.ToArray();
        }

        public void LoadImages()
        {
            // Only this sample's imported Resources directory is scanned. Every file
            // is an independent production asset; design masters never ship here.
            var textures = UnityResources.LoadAll<Texture2D>(Root + "Images");
            Array.Sort(textures, (a, b) => string.CompareOrdinal(a.name, b.name));
            var images = new System.Collections.Generic.List<ImageAsset>();
            foreach (var image in textures)
            {
                if (!image.isReadable) throw new InvalidOperationException("Game Flow images require the included Read/Write importer settings.");
                string alias = image.name == "world" ? "background" : image.name;
                images.Add(new ImageAsset("gameflow/" + alias + ".png", image));
                if (image.name == "world") BackgroundSize = new Vector2(image.width, image.height);
            }
            foreach (var item in ReadCatalog().inventory) Required<Texture2D>("Images/" + Path.GetFileNameWithoutExtension(item.icon));
            // LoadAll cannot tell us which files are absent. Shared artwork must
            // also fail here, while the loading page can still offer recovery.
            foreach (string name in new[] { "avatar", "slot-normal", "slot-empty", "slot-selected",
                "action-left", "action-middle", "action-right", "dock-inventory", "dock-rankings",
                "dock-settings", "dock-profile", "currency-credit", "currency-core" })
                Required<Texture2D>("Images/" + name);
            for (int i = 1; i <= 6; i++) Required<Texture2D>("Images/rank-avatar-" + i);
            Images = images.ToArray();
        }

        public GameCatalog ReadCatalog()
        {
            foreach (GamePage page in Enum.GetValues(typeof(GamePage))) Required<TextAsset>("Rml/" + page + ".rml");
            return JsonUtility.FromJson<GameCatalog>(Required<TextAsset>("Catalog.json").text);
        }

        public string Page(GamePage page)
        {
            var xml = new XmlDocument { XmlResolver = null };
            xml.LoadXml(Required<TextAsset>("Rml/" + page + ".rml").text);
            var link = xml.SelectSingleNode("/rml/head/link") as XmlElement;
            if (link == null || link.GetAttribute("href") != "Game.rcss")
                throw new InvalidOperationException("Game Flow page must declare its external Game.rcss stylesheet.");
            // RmlUi's default JoinPath strips ONE resource-root slash. Preserve an OS
            // absolute POSIX path with the resource-root prefix; Windows drive paths
            // are already recognized. No developer-specific directory is embedded.
            string path = stylesheet.Replace('\\', '/');
            if (path.StartsWith("/", StringComparison.Ordinal)) path = "/" + path;
            link.SetAttribute("href", path);
            return xml.OuterXml;
        }

        public void Dispose()
        {
            if (directory == null) return;
            try { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
            catch (IOException error) { Debug.LogWarning("Could not remove Game Flow cache: " + error.Message); }
            directory = null;
        }
    }
}
