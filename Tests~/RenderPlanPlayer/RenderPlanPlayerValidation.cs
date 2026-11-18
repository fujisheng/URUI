using System;
using System.Collections;
using System.IO;
using System.Runtime.InteropServices;
using Unity.Collections;
using UnityEngine;

namespace URUI.Validation
{
    /// <summary>在实际 Player 图形后端验证共同参数纹理契约，结果由宿主保存。</summary>
    public sealed class RenderPlanPlayerValidation : MonoBehaviour
    {
        [Serializable] sealed class Result
        {
            public bool passed;
            public int cases,geometryCases;
            public string graphics, device, space, error, copyTextureSupport, parameterTransfer,corePhase;
            public bool graphicsFence;
        }
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void UruiReportRenderPlanContract(string json);
#endif
        IEnumerator Start()
        {
            yield return null;
            yield return null;
            var result = new Result { graphics = SystemInfo.graphicsDeviceType.ToString(), device = SystemInfo.graphicsDeviceName, space = QualitySettings.activeColorSpace.ToString(), copyTextureSupport = SystemInfo.copyTextureSupport.ToString(), graphicsFence = SystemInfo.supportsGraphicsFence,
                parameterTransfer = (SystemInfo.copyTextureSupport & UnityEngine.Rendering.CopyTextureSupport.Basic) != 0 ? "row upload plus GPU copy" : "full Texture2D upload" };
            try
            {
                foreach (var index in new[] { 65535, 32768, 4095 }) { Check(index, false); result.cases++; }
                Check(65535, true); result.cases++;
                CheckOrigin(16777216);result.cases++;CheckOrigin(-16777216);result.cases++;
                CheckRendering(result);result.cases += result.geometryCases;
                result.passed = true;
            }
            catch (Exception exception) { result.error = exception.ToString(); }
            var json = JsonUtility.ToJson(result, true);
            Debug.Log("URUI_RENDER_PARAMETER_CONTRACT " + json);
#if UNITY_WEBGL && !UNITY_EDITOR
            UruiReportRenderPlanContract(json);
#else
            var output = Environment.GetEnvironmentVariable("URUI_PLAN_PLAYER_RESULT");
            if (!string.IsNullOrEmpty(output)) { File.WriteAllText(output, json); }
            Application.Quit(result.passed ? 0 : 2);
#endif
        }
        static void CheckRendering(Result result)
        {
            var cameraObject=new GameObject("WebGL draw camera",typeof(Camera));cameraObject.tag="MainCamera";
            var camera=cameraObject.GetComponent<Camera>();camera.enabled=true;camera.allowHDR=false;camera.allowMSAA=false;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;
            var root=new GameObject("WebGL Core Surface");var surface=root.AddComponent<URUI.Hosting.Surface>();surface.Viewport=new Vector2Int(128,128);surface.Input.UseUnityInput=false;
            var document=root.AddComponent<URUI.Documents.Document>();document.Surface=surface;
            var target=new RenderTexture(128,128,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);target.Create();camera.targetTexture=target;
            var textures=new Texture2D[11];var images=new URUI.Resources.ImageAsset[11];
            var read=new Texture2D(128,128,TextureFormat.RGBA32,false,true);
            try
            {
                for(var n=0;n<9;n++){
                    var texture=new Texture2D(1,1,TextureFormat.RGBA32,false,true);texture.filterMode=FilterMode.Point;texture.SetPixel(0,0,n%3==0?Color.red:n%3==1?Color.green:Color.blue);texture.Apply();textures[n]=texture;images[n]=new URUI.Resources.ImageAsset("t"+n+".png",texture);
                }
                var atlas=new Texture2D(4,1,TextureFormat.RGBA32,false,true);atlas.filterMode=FilterMode.Point;atlas.SetPixels(new[]{Color.red,Color.red,Color.blue,Color.blue});atlas.Apply();textures[9]=atlas;images[9]=new URUI.Resources.ImageAsset("atlas.png",atlas);
                var mask=new Texture2D(64,1,TextureFormat.RGBA32,false,true);mask.filterMode=FilterMode.Point;var maskColors=new Color[64];for(var n=0;n<32;n++)maskColors[n]=Color.white;mask.SetPixels(maskColors);mask.Apply();textures[10]=mask;images[10]=new URUI.Resources.ImageAsset("mask.png",mask);document.Images=images;
                Color Pixel(int x,int y){return read.GetPixel(x,127-y);}
                void Render(){document.SynchronizeLayout();camera.Render();var previous=RenderTexture.active;try{RenderTexture.active=target;read.ReadPixels(new Rect(0,0,128,128),0,0);read.Apply();}finally{RenderTexture.active=previous;}}
                void Expect(Color actual,Color expected){if(Mathf.Abs(actual.r-expected.r)>3f/255||Mathf.Abs(actual.g-expected.g)>3f/255||Mathf.Abs(actual.b-expected.b)>3f/255)throw new InvalidOperationException("WebGL production draw pixel "+actual+" != "+expected);}
                result.corePhase="1000-images-nine-textures";
                var html=new System.Text.StringBuilder("<!doctype html><html><head><style>body{margin:0;}img{position:absolute;width:3px;height:3px;}</style></head><body>");
                for(var n=0;n<1000;n++)html.Append("<img src='t").Append(n%9).Append(".png' style='left:").Append(n%32*4).Append("px;top:").Append(n/32*4).Append("px;'>");
                document.LoadHtml(html.Append("</body></html>").ToString(),"/webgl-batches.html");Render();
                for(var n=0;n<1000;n++)Expect(Pixel(n%32*4+1,n/32*4+1),n%9%3==0?Color.red:n%9%3==1?Color.green:Color.blue);
                result.geometryCases++;result.corePhase="dynamic-source-rectangle";
                document.LoadHtml("<!doctype html><html><head><style>body{margin:0;}</style></head><body><img id='atlas' src='atlas.png' data-urui-rect='0 0 2 1' style='position:absolute;left:8px;top:8px;width:8px;height:8px;'></body></html>","/webgl-uv.html");Render();Expect(Pixel(10,10),Color.red);
                document.Q("atlas").SetAttribute("data-urui-rect","2 0 2 1");Render();Expect(Pixel(10,10),Color.blue);
                result.geometryCases++;result.corePhase="alpha-mask";
                document.LoadHtml("<!doctype html><html><head><style>body{margin:0;}#mask{position:absolute;left:8px;top:8px;width:16px;height:16px;mask-image:url(mask.png);mask-repeat:no-repeat;mask-size:100% 100%;}</style></head><body><div id='mask'><img src='t0.png' style='width:16px;height:16px;'></div></body></html>","/webgl-mask.html");Render();Expect(Pixel(10,12),Color.red);Expect(Pixel(20,12),Color.black);
                result.geometryCases++;result.corePhase="rectangular-clip";
                document.LoadHtml("<!doctype html><html><head><style>body{margin:0;}#clip{position:absolute;left:8px;top:8px;width:8px;height:8px;overflow:hidden;}img{width:32px;height:32px;}</style></head><body><div id='clip'><img src='t0.png'></div></body></html>","/webgl-clip.html");Render();Expect(Pixel(10,10),Color.red);Expect(Pixel(20,10),Color.black);
                result.geometryCases++;result.corePhase="overlapping-group-mask";
                document.LoadHtml("<!doctype html><html><head><style>body{margin:0;}#group{position:absolute;left:8px;top:8px;width:32px;height:24px;mask-image:url(mask.png);mask-repeat:no-repeat;mask-size:100% 100%;}img{position:absolute;width:24px;height:24px;}</style></head><body><div id='group'><img src='t0.png'><img src='t1.png' style='left:8px;'></div></body></html>","/webgl-overlapping-mask.html");Render();Expect(Pixel(12,16),Color.red);Expect(Pixel(20,16),Color.green);Expect(Pixel(28,16),Color.black);
                result.geometryCases++;result.corePhase="overlapping-group-neighborhood-filter";
                document.LoadHtml("<!doctype html><html><head><style>body{margin:0;}#group{position:absolute;left:8px;top:8px;width:48px;height:24px;filter:blur(1px);}img{position:absolute;width:24px;height:24px;}</style></head><body><div id='group'><img src='t0.png'><img src='t1.png' style='left:24px;'></div></body></html>","/webgl-group-filter.html");Render();Expect(Pixel(16,16),Color.red);Expect(Pixel(48,16),Color.green);Expect(Pixel(72,16),Color.black);
                result.geometryCases++;result.corePhase="complete";
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);camera.targetTexture=null;UnityEngine.Object.DestroyImmediate(cameraObject);target.Release();UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(read);
                foreach(var texture in textures)if(texture)UnityEngine.Object.DestroyImmediate(texture);
            }
        }
        static void CheckOrigin(float origin)
        {
            var parameters=new Texture2D(256,1,TextureFormat.RGBAFloat,false,true){filterMode=FilterMode.Point};
            var material=new Material(UnityEngine.Resources.Load<Shader>("URUI.RenderPlanPlayer/RenderParameterContract"));
            var target=new RenderTexture(1,1,0,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear);
            var read=new Texture2D(1,1,TextureFormat.RGBAFloat,false,true);var previous=RenderTexture.active;
            var values=new NativeArray<Vector4>(256,Allocator.Temp);
            try
            {
                values[0]=new Vector4(1.25f,0,.25f,.125f);values[1]=new Vector4(0,3,-.5f,.25f);
                values[2]=new Vector4(origin,-65536,1,0);values[3]=new Vector4(.125f,-.25f,.5f,1);values[7]=new Vector4(512,0,0,0);
                parameters.SetPixelData(values,0);parameters.Apply(false);material.SetTexture("_UruiParameters",parameters);
                material.SetVector("_Probe",new Vector4(origin+8,-65535.5f,0,1));material.SetFloat("_Mode",3);
                target.Create();Graphics.Blit(Texture2D.whiteTexture,target,material);RenderTexture.active=target;
                read.ReadPixels(new Rect(0,0,1,1),0,0);read.Apply(false);var actual=read.GetPixel(0,0);
                var expected=new Color(10.125f,1.25f,2.25f,2.125f);
                for(int component=0;component<4;component++)if(!float.IsFinite(actual[component])||Mathf.Abs(actual[component]-expected[component])>.001f)throw new InvalidOperationException("Origin "+origin+" component="+component+": "+actual[component]+" expected "+expected[component]);
            }
            finally
            {
                values.Dispose();
                RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(parameters);UnityEngine.Object.DestroyImmediate(material);
                target.Release();UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(read);
            }
        }

        static void Check(int index, bool matrix)
        {
            if (!SystemInfo.SupportsTextureFormat(TextureFormat.RGBAFloat)) { throw new InvalidOperationException("RGBAFloat texture unsupported"); }
            var parameters = new Texture2D(256, 256, TextureFormat.RGBAFloat, false, true) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var shader = UnityEngine.Resources.Load<Shader>("URUI.RenderPlanPlayer/RenderParameterContract");
            if (!shader || !shader.isSupported) { throw new InvalidOperationException("Parameter contract shader unsupported"); }
            var material = new Material(shader);
            var color = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
            var mask = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
            var target = new RenderTexture(1, 1, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            var read = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
            var update = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
            var previous = RenderTexture.active;
            var values = new NativeArray<Vector4>(65536, Allocator.Temp);
            try
            {
                values[0] = new Vector4(2, 0, 0, 65536); values[1] = new Vector4(0, 3, 0, -4096); values[2] = new Vector4(0, 0, 1, .125f);
                values[index] = Vector4.zero;
                update.SetPixel(0, 0, new Color(65536.125f, -4096.125f, .001953125f, 1)); update.Apply(false);
                if ((SystemInfo.copyTextureSupport & UnityEngine.Rendering.CopyTextureSupport.Basic) != 0)
                {
                    parameters.SetPixelData(values, 0); parameters.Apply(false);
                    Graphics.CopyTexture(update, 0, 0, 0, 0, 1, 1, parameters, 0, 0, index % 256, index / 256);
                }
                else
                {
                    values[index] = new Vector4(65536.125f, -4096.125f, .001953125f, 1);
                    parameters.SetPixelData(values, 0); parameters.Apply(false);
                }
                color.SetPixel(0, 0, new Color(1, .5f, .25f, 1)); color.Apply(false);
                mask.SetPixel(0, 0, new Color(.5f, .5f, .5f, .5f)); mask.Apply(false);
                material.SetTexture("_Parameters", parameters); material.SetTexture("_Mask", mask);
                for (var slot = 0; slot < 8; slot++) { material.SetTexture("_Color" + slot, color); }
                material.SetFloat("_ParameterIndex", index); material.SetFloat("_Mode", matrix ? 1 : 0);
                target.Create(); Graphics.Blit(Texture2D.whiteTexture, target, material);
                RenderTexture.active = target; read.ReadPixels(new Rect(0, 0, 1, 1), 0, 0); read.Apply(false);
                var actual = read.GetPixel(0, 0); var expected = matrix ? new Color(.5f, .25f, .125f, .5f) : new Color(1, -.125f, .015625f, .5f);
                for (var component = 0; component < 4; component++)
                {
                    if (!float.IsFinite(actual[component]) || Mathf.Abs(actual[component] - expected[component]) > .001f)
                    { throw new InvalidOperationException("Parameter " + index + " matrix=" + matrix + " component=" + component + ": " + actual[component] + " expected " + expected[component]); }
                }
            }
            finally
            {
                values.Dispose(); RenderTexture.active = previous;
                Destroy(parameters); Destroy(material); Destroy(color); Destroy(mask); target.Release(); Destroy(target); Destroy(read); Destroy(update);
            }
        }
    }
}
