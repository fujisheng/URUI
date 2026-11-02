using URUI.Controls;
using URUI.Elements;
using System.Collections.Generic;
using UnityEngine;

namespace URUI.Samples.GameFlow
{
    public sealed partial class GameFlowSample
    {
        sealed class ArtGroup
        {
            public Element owner;
            public Image[] pieces;
            public bool action;
            public Vector2 size = new Vector2(-1, -1);
        }
        readonly List<ArtGroup> artGroups = new List<ArtGroup>();
        // A bounded sample composition helper. Actual layout and all typography
        // remain in the authored RML/RCSS. The renderer needs no new ABI feature.
        void DecorateArtwork()
        {
            foreach (var panel in document.Root.Query(className: "nine-frame"))
                AddFrame(panel);
            foreach (var action in document.Root.Query(className: "art-action"))
            {
                artGroups.Add(new ArtGroup { owner = action, action = true, pieces = new[] {
                    AddArt(action, "action-left", "action-art action-left"),
                    AddArt(action, "action-middle", "action-art action-middle"),
                    AddArt(action, "action-right", "action-art action-right") } });
            }
        }

        void AddFrame(Element panel)
        {
            var parts = new Image[9];
            for (int y = 0; y < 3; y++)
                for (int x = 0; x < 3; x++)
                    parts[y * 3 + x] = AddArt(panel, "panel-" + y + x, "frame-part frame-" + y + x);
            artGroups.Add(new ArtGroup { owner = panel, pieces = parts });
        }

        Image AddArt(Element parent, string source, string classes)
        {
            var image = document.Create<Image>();
            image.Source = "gameflow/" + source + ".png";
            foreach (string value in classes.Split(' ')) image.AddToClassList(value);
            parent.Add(image);
            return image;
        }

        void UpdateArtwork()
        {
            for (int index = artGroups.Count - 1; index >= 0; index--)
            {
                var group = artGroups[index];
                if (!group.owner.IsValid) { artGroups.RemoveAt(index); continue; }
                if (!group.owner.HasLayout) continue;
                Vector2 size = group.owner.Layout.size;
                if (size == group.size || size.x <= 0 || size.y <= 0) continue;
                group.size = size;
                if (group.action)
                {
                    float left = size.y * 118 / 132, right = size.y * 104 / 132;
                    float scale = Mathf.Min(1, size.x / (left + right)); left *= scale; right *= scale;
                    Place(group.pieces[0], 0, 0, left, size.y);
                    Place(group.pieces[1], left, 0, size.x - left - right, size.y);
                    Place(group.pieces[2], size.x - right, 0, right, size.y);
                }
                else
                {
                    float border = Mathf.Min(12, Mathf.Min(size.x, size.y) * .25f);
                    float[] xs = { 0, border, size.x - border, size.x };
                    float[] ys = { 0, border, size.y - border, size.y };
                    for (int y = 0; y < 3; y++)
                        for (int x = 0; x < 3; x++)
                            Place(group.pieces[y * 3 + x], xs[x], ys[y], xs[x + 1] - xs[x], ys[y + 1] - ys[y]);
                }
            }
        }

        static void Place(Image image, float x, float y, float width, float height)
        {
            image.Style.Left = x; image.Style.Top = y;
            image.Style.Width = width; image.Style.Height = height;
        }

        void ButtonText(string id, string value)
        {
            var button = document.Q<Button>(id);
            if (button == null) return;
            var label = button.Q<Label>(className: "action-caption") ?? button.Q<Label>(className: "framed-caption");
            if (label != null) label.Text = value;
            else button.Text = value;
        }
    }
}
