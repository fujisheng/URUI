#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine.Rendering;

public static class TextureSamplingGpuValidation
{
    [Serializable] public sealed class Report { public bool passed; public string colorSpace,graphics,error; public int comparedPixels, maxRgbError,maxAlphaError,variants; }
    const string Output="Artifacts/ImageMemoryAudit/Implementation";
    public static void Run()
    {
        Directory.CreateDirectory(Output);
        bool requestedLinear=Environment.GetCommandLineArgs().Contains("--urui-gpu-linear");
        ColorSpace requested=requestedLinear?ColorSpace.Linear:ColorSpace.Gamma;
        var restoreArg=Environment.GetCommandLineArgs().FirstOrDefault(x=>x.StartsWith("--urui-gpu-original="));
        ColorSpace original=restoreArg==null?PlayerSettings.colorSpace:(ColorSpace)Enum.Parse(typeof(ColorSpace),restoreArg.Substring("--urui-gpu-original=".Length));
        if(QualitySettings.activeColorSpace!=requested)
        {
            if(restoreArg==null)throw new ArgumentException("Use Tools/verify-borrowed-images.py so original project settings are restored.");
            PlayerSettings.colorSpace=requested;AssetDatabase.SaveAssets();EditorApplication.Exit(3);return;
        }
        var report=new Report{colorSpace=QualitySettings.activeColorSpace.ToString(),graphics=SystemInfo.graphicsDeviceType.ToString()};
        try
        {
            var shader=Shader.Find("URUI/Premultiplied"); if(!shader||!shader.isSupported)throw new Exception("Borrowed shader unavailable");
            foreach(var path in new[]{"Packages/com.fujisheng.urui/Runtime/Resources/URUI/Premultiplied.shader","Packages/com.fujisheng.urui/Runtime/Resources/URUI/ParameterTable.shader"})
            {
                var s=AssetDatabase.LoadAssetAtPath<Shader>(path); var errors=ShaderUtil.GetShaderMessages(s).Where(x=>x.severity==ShaderCompilerMessageSeverity.Error).ToArray(); if(errors.Length>0)throw new Exception(path+": "+string.Join(";",errors.Select(x=>x.message)));
            }
            foreach(bool sourceLinear in new[]{false,true}) foreach(int width in new[]{3,31,65}) Compare(shader,sourceLinear,width,17,report);
            report.passed=true;
        }
        catch(Exception e){report.error=e.ToString();Debug.LogException(e);}
        finally
        {
            File.WriteAllText(Output+"/gpu-"+report.colorSpace.ToLowerInvariant()+".json",JsonUtility.ToJson(report,true));
            PlayerSettings.colorSpace=original;AssetDatabase.SaveAssets();
        }
        Debug.Log("BORROWED_GPU_"+(report.passed?"PASS":"FAIL")+" "+report.colorSpace+" rgbError="+report.maxRgbError);EditorApplication.Exit(report.passed?0:1);
    }
    static void Compare(Shader shader,bool sourceLinear,int w,int h,Report report)
    {
        var source=new Texture2D(4,4,TextureFormat.RGBA32,true,sourceLinear){filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Repeat,anisoLevel=4};
        var reference=new Texture2D(4,4,TextureFormat.RGBA32,false,sourceLinear){filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};
        var raw=new Color32[16]; var premul=new Color32[16];
        for(int y=0;y<4;y++)for(int x=0;x<4;x++)
        {
            byte alpha=(byte)(new[]{0,64,128,255}[(x+y)%4]);
            var color=new Color32((byte)(13+x*70),(byte)(11+y*63),(byte)(239-x*40),alpha);raw[y*4+x]=color;
            premul[(3-y)*4+x]=new Color32((byte)((color.r*alpha+127)/255),(byte)((color.g*alpha+127)/255),(byte)((color.b*alpha+127)/255),alpha);
        }
        source.SetPixels32(raw);source.Apply(true,true);reference.SetPixels32(premul);reference.Apply(false,true);
        var a=new Material(shader);var b=new Material(shader);
        a.SetTexture("_MainTex",source);a.SetFloat("_UruiBorrowedTexture",1);a.SetFloat("_UruiSourceSrgb",source.isDataSRGB?1:0);a.SetVector("_UruiSourceSize",new Vector4(4,4,.25f,.25f));
        b.SetTexture("_MainTex",reference);b.SetFloat("_UruiBorrowedTexture",0);
        var actual=Draw(source,a,w,h);var expected=Draw(reference,b,w,h);
        try
        {
            var p=actual.GetPixels32();var q=expected.GetPixels32();int rgb=0,alpha=0;
            for(int i=0;i<p.Length;i++){rgb=Math.Max(rgb,Math.Max(Math.Abs(p[i].r-q[i].r),Math.Max(Math.Abs(p[i].g-q[i].g),Math.Abs(p[i].b-q[i].b))));alpha=Math.Max(alpha,Math.Abs(p[i].a-q[i].a));}
            report.maxRgbError=Math.Max(report.maxRgbError,rgb);report.maxAlphaError=Math.Max(report.maxAlphaError,alpha);report.comparedPixels+=p.Length;report.variants++;
            if(w==65){File.WriteAllBytes(Output+"/gpu-"+report.colorSpace+"-"+(sourceLinear?"linear-source":"srgb-source")+".png",actual.EncodeToPNG());}
            if(rgb>(report.colorSpace=="Gamma"?2:3)||alpha>1)throw new Exception("GPU premultiplied edge/UV comparison differs: sourceLinear="+sourceLinear+" size="+w+" rgb="+rgb+" alpha="+alpha);
            if(source.filterMode!=FilterMode.Point||source.wrapMode!=TextureWrapMode.Repeat||source.anisoLevel!=4||source.isReadable)throw new Exception("Source sampler/readability was mutated");
        }
        finally{UnityEngine.Object.DestroyImmediate(a);UnityEngine.Object.DestroyImmediate(b);UnityEngine.Object.DestroyImmediate(source);UnityEngine.Object.DestroyImmediate(reference);UnityEngine.Object.DestroyImmediate(actual);UnityEngine.Object.DestroyImmediate(expected);}
    }
    static Texture2D Draw(Texture2D source,Material material,int w,int h)
    {
        var target=RenderTexture.GetTemporary(w,h,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Default);var previous=RenderTexture.active;
        try{RenderTexture.active=target;GL.Clear(true,true,new Color(.07f,.13f,.19f,.5f));Graphics.Blit(source,target,material);RenderTexture.active=target;var result=new Texture2D(w,h,TextureFormat.RGBA32,false,true);result.ReadPixels(new Rect(0,0,w,h),0,0,false);result.Apply(false,false);return result;}
        finally{RenderTexture.active=previous;RenderTexture.ReleaseTemporary(target);}
    }
}
#endif
