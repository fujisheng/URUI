using System;
using System.Collections;
using System.IO;
using UnityEngine;
using URUI.Controls;
using URUI.Documents;
using URUI.Events;

namespace URUI.Tests
{
    /// <summary>使用真实相机绘制综合控件样例并保存 PNG。</summary>
    public sealed class ControlsPreview : MonoBehaviour
    {
        IEnumerator Start()
        {
            for (int i = 0; i < 8; i++)
            {
                yield return null;
            }

            try { CheckInteractions(GetComponent<Document>()); }
            catch (Exception error)
            {
                Debug.LogException(error);
#if UNITY_EDITOR
                UnityEditor.EditorApplication.Exit(1);
#endif
                yield break;
            }
            yield return null;
            yield return null;
            try
            {
                var document = GetComponent<Document>();
                if (!document.IsLoaded || document.Q<ListView>("items").RealizedItemCount == 0)
                {
                    throw new Exception("Controls sample did not create visible rows");
                }

                var camera = Camera.main;
                int width = Screen.width, height = Screen.height;
                var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
                var previous = RenderTexture.active;
                var previousTarget = camera.targetTexture;
                var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
                try
                {
                    camera.targetTexture = target;
                    camera.Render();
                    RenderTexture.active = target;
                    texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                    texture.Apply();
                    Directory.CreateDirectory("Evidence");
                    File.WriteAllBytes("Evidence/runtime-controls.png", texture.EncodeToPNG());
                    File.WriteAllText("Evidence/runtime-controls.json", "{\"passed\":true,\"sampleInteractions\":true,\"width\":" + width + ",\"height\":" + height + "}");
                }
                finally
                {
                    camera.targetTexture = previousTarget;
                    RenderTexture.active = previous;
                    target.Release();
                    Destroy(target);
                    Destroy(texture);
                }
#if UNITY_EDITOR
                UnityEditor.EditorApplication.Exit(0);
#endif
            }
            catch (Exception error)
            {
                Debug.LogException(error);
#if UNITY_EDITOR
                UnityEditor.EditorApplication.Exit(1);
#endif
            }
        }

        static void CheckInteractions(Document document)
        {
            document.Input.UseUnityInput = false;
            document.Q<Dropdown>("quality").Value = "High";
            document.Q<TextField>("player-name").Value = "Preview";
            document.Q<Button>("apply").Focus();
            document.Input.SendKey(KeyCode.Return, true);
            document.Input.SendKey(KeyCode.Return, false);
            if (document.Q<Label>("status").Text != "Saved Preview / High")
            {
                throw new Exception("Sample profile did not save through keyboard activation");
            }

            document.Q<Slider>("volume").Value = 75;
            if (document.Q<ProgressBar>("progress").Value != 75)
            {
                throw new Exception("Sample volume did not update progress");
            }

            var row = document.Q<ListView>("items").ContentContainer[0];
            var list = document.Q<ListView>("items");
            if (row.Layout.width < list.ViewportRect.width - 1)
                throw new Exception("Sample row does not fill viewport: " + row.Layout + " / " + list.ContentRect + " / content " + list.ContentContainer.Layout);
            var point = row.WorldBound.center;
            document.Input.SendPointer(PointerPhase.Down, point);
            document.Input.SendPointer(PointerPhase.Up, point);
            if (document.Q<ListView>("items").SelectedIndex != 0)
            {
                throw new Exception("Sample row selection did not receive pointer input");
            }
        }
    }
}
