using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Build.Reporting;
using UnityEngine;
using URUI.Effect.Examples;
namespace URUI.Effect.PlayerValidation.Editor
{
    public static class EffectPlayerBuild
    {
        public static void Build()
        {
            Build(false);
        }
        public static void BuildScreenSurface()
        {
            Build(true);
        }
        static void Build(bool screenSurface)
        {
            try
            {
                var output=Environment.GetEnvironmentVariable("URUI_EFFECT_BUILD_OUTPUT")??throw new InvalidOperationException("Build output missing");
                var linear=Environment.GetEnvironmentVariable("URUI_EFFECT_BUILD_LINEAR")=="1";
                PlayerSettings.colorSpace=linear?ColorSpace.Linear:ColorSpace.Gamma;
                PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Standalone,ScriptingImplementation.IL2CPP);
                PlayerSettings.SetIl2CppCompilerConfiguration(UnityEditor.Build.NamedBuildTarget.Standalone,Il2CppCompilerConfiguration.Release);
                UnityEditor.OSXStandalone.UserBuildSettings.architecture=UnityEditor.Build.OSArchitecture.ARM64;
                PlayerSettings.defaultScreenWidth=1280;PlayerSettings.defaultScreenHeight=720;PlayerSettings.fullScreenMode=FullScreenMode.Windowed;PlayerSettings.runInBackground=true;PlayerSettings.enableFrameTimingStats=true;
                var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                if(screenSurface)
                {
                    new GameObject("Actual screen surface validation").AddComponent<ScreenSurfaceValidation>();
                }
                else
                {
                var cameraObject=new GameObject("Gallery Camera",typeof(Camera));cameraObject.tag="MainCamera";var camera=cameraObject.GetComponent<Camera>();camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.clear;
                var host=new GameObject("Independent URUI Effect Gallery");var gallery=host.AddComponent<EffectGallery>();
                gallery.html=UnityEngine.Resources.Load<TextAsset>("URUI.Effect.Gallery/GalleryDocument");gallery.style=UnityEngine.Resources.Load<TextAsset>("URUI.Effect.Gallery/GalleryStyle");gallery.white=UnityEngine.Resources.Load<Texture2D>("URUI.Effect.Gallery/white");gallery.source=UnityEngine.Resources.Load<Texture2D>("URUI.Effect.Gallery/input");gallery.pattern=UnityEngine.Resources.Load<Texture2D>("URUI.Effect.Gallery/pattern");gallery.gradient=UnityEngine.Resources.Load<Texture2D>("URUI.Effect.Gallery/gradient-lut");gallery.shader=UnityEngine.Resources.Load<Shader>("URUI.Effect.Gallery/GalleryProgram");
                var validation=host.AddComponent<EffectPlayerValidation>();validation.gallery=gallery;validation.captureCamera=camera;validation.manifest=UnityEngine.Resources.Load<TextAsset>("URUI.Effect.Gallery/GalleryCoverage");
                }
                Directory.CreateDirectory("Assets/Player");var path=screenSurface?"Assets/Player/ScreenSurface.unity":"Assets/Player/EffectGallery.unity";EditorSceneManager.SaveScene(scene,path);
                var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{path},locationPathName=output,target=BuildTarget.StandaloneOSX,options=BuildOptions.Development});
                File.WriteAllText(output+".build.json","{\"result\":\""+report.summary.result+"\",\"errors\":"+report.summary.totalErrors+",\"bytes\":"+report.summary.totalSize+"}");
                EditorApplication.Exit(report.summary.result==BuildResult.Succeeded?0:2);
            }
            catch(Exception exception){Debug.LogException(exception);EditorApplication.Exit(2);}
        }
    }
}
