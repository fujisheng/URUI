using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using URUI.Resources;
using UnityResources = UnityEngine.Resources;

namespace URUI.Samples.GameFlow
{
    /// <summary>样例的显式 HTML/CSS/字体和图片依赖，不改写作者文档或生成临时文件。</summary>
    public sealed class GameFlowResources
    {
        const string Root = "URUI/GameFlow/";
        const string UrlRoot = "samples/gameflow/";
        public Vector2 BackgroundSize { get; private set; }
        public ImageAsset[] Images { get; private set; }
        public TextResource[] TextResources { get; private set; }

        static T Required<T>(string path) where T : UnityEngine.Object =>
            UnityResources.Load<T>(Root + path) ?? throw new InvalidOperationException("Missing Game Flow resource: " + Root + path);

        public void Prepare()
        {
            var files = new List<TextResource> {
                new TextResource(UrlRoot + "Game.css", Required<TextAsset>("Game")),
                new TextResource(UrlRoot + "Fonts/LatoLatin-Regular.ttf.bytes", Required<TextAsset>("Fonts/LatoLatin-Regular.ttf"))
            };
            AddFile("/urui/base.css", "URUI/base");
            AddFile("/urui/controls.css", "URUI/controls");
            foreach (var family in new[] { "Serif", "Sans", "Mono" })
                foreach (var face in new[] { "Regular", "Italic", "Bold", "BoldItalic" })
                {
                    var name = "Liberation" + family + "-" + face + ".ttf";
                    AddFile("/urui/Fonts/" + name + ".bytes", "URUI/Fonts/" + name);
                }
            TextResources = files.ToArray();
            var background = Required<Texture2D>("Images/world");
            BackgroundSize = new Vector2(background.width, background.height);
            var bootstrap = new List<ImageAsset> {
                new ImageAsset("../Images/world.png", background),
                new ImageAsset("../Images/operator.png", Required<Texture2D>("Images/operator"))
            };
            for (var y = 0; y < 3; y++)
                for (var x = 0; x < 3; x++)
                {
                    var name = "panel-" + y + x;
                    bootstrap.Add(new ImageAsset("../Images/" + name + ".png", Required<Texture2D>("Images/" + name)));
                }
            Images = bootstrap.ToArray();

            void AddFile(string uri, string resource)
            {
                files.Add(new TextResource(uri, UnityResources.Load<TextAsset>(resource)
                    ?? throw new InvalidOperationException("Missing URUI author resource: " + resource)));
            }
        }

        public void LoadImages()
        {
            var textures = UnityResources.LoadAll<Texture2D>(Root + "Images");
            Array.Sort(textures, (a, b) => string.CompareOrdinal(a.name, b.name));
            var images = new List<ImageAsset>();
            foreach (var image in textures)
            {
                images.Add(new ImageAsset("../Images/" + image.name + ".png", image));
                if (image.name == "world") BackgroundSize = new Vector2(image.width, image.height);
            }
            foreach (var item in ReadCatalog().inventory) Required<Texture2D>("Images/" + Path.GetFileNameWithoutExtension(item.icon));
            foreach (var name in new[] { "avatar", "slot-normal", "slot-empty", "slot-selected",
                "action-left", "action-middle", "action-right", "dock-inventory", "dock-rankings",
                "dock-settings", "dock-profile", "currency-credit", "currency-core" })
                Required<Texture2D>("Images/" + name);
            for (var i = 1; i <= 6; i++) Required<Texture2D>("Images/rank-avatar-" + i);
            Images = images.ToArray();
        }

        public GameCatalog ReadCatalog()
        {
            foreach (GamePage page in Enum.GetValues(typeof(GamePage))) Page(page);
            return JsonUtility.FromJson<GameCatalog>(Required<TextAsset>("Catalog.json").text);
        }

        public TextAsset Page(GamePage page) => Required<TextAsset>("Html/" + page);
        public string PageUrl(GamePage page) => UrlRoot + "Html/" + page + ".html";
    }
}
