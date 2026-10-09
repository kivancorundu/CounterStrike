using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Vexa.EditorTools
{
    /// <summary>
    /// Command-line builds (CI: .github/workflows/android.yml). The project keeps no hand-made scenes or settings
    /// assets, so <see cref="Prepare"/> creates what a player build needs before building:
    ///   * an empty boot scene (VexaApp starts itself from any scene),
    ///   * a URP pipeline asset, assigned to Graphics and every Quality level,
    ///   * materials in Resources that reference the shaders the game finds by name at runtime
    ///     (Shader.Find only sees shaders some build asset uses), with the keyword variants it enables.
    /// </summary>
    public static class VexaBuild
    {
        const string SceneDir = "Assets/Vexa/Scenes";
        const string ScenePath = SceneDir + "/Boot.unity";
        const string SettingsDir = "Assets/Vexa/Settings";
        const string ShaderRefDir = "Assets/Vexa/Resources/ShaderRefs";

        [MenuItem("VEXA/Build/Prepare Project")]
        public static void Prepare()
        {
            EnsureScene();
            EnsurePipeline();
            EnsureShaderRefs();
            AssetDatabase.SaveAssets();
        }

        [MenuItem("VEXA/Build/Android APK")]
        public static void BuildAndroid()
        {
            Prepare();
            PlayerSettings.companyName = "VEXA";
            PlayerSettings.productName = "VEXA";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.vexa.game");
            PlayerSettings.bundleVersion = "0.1." + (Environment.GetEnvironmentVariable("GITHUB_RUN_NUMBER") ?? "0");
            PlayerSettings.Android.bundleVersionCode = int.TryParse(Environment.GetEnvironmentVariable("GITHUB_RUN_NUMBER"), out var n) ? n : 1;
            // 64-bit phones need ARM64, which needs IL2CPP
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.Android, ManagedStrippingLevel.Minimal);
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.Android.forceInternetPermission = true; // LAN / online matches
            EditorUserBuildSettings.buildAppBundle = false;         // a plain APK to sideload
            EditorUserBuildSettings.androidBuildSystem = AndroidBuildSystem.Gradle;
            Build(BuildTarget.Android, Arg("-customBuildPath") ?? "Builds/Android/VEXA.apk");
        }

        [MenuItem("VEXA/Build/Windows")]
        public static void BuildWindows()
        {
            Prepare();
            PlayerSettings.companyName = "VEXA";
            PlayerSettings.productName = "VEXA";
            Build(BuildTarget.StandaloneWindows64, Arg("-customBuildPath") ?? "Builds/Windows/VEXA.exe");
        }

        static void Build(BuildTarget target, string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = path,
                target = target,
                options = BuildOptions.None,
            });
            var s = report.summary;
            Debug.Log($"VEXA build {s.result}: {path}, {s.totalSize / (1024 * 1024)} MB, {s.totalErrors} errors, {s.totalTime}");
            if (s.result != BuildResult.Succeeded && Application.isBatchMode) EditorApplication.Exit(1);
        }

        static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        static void EnsureScene()
        {
            if (File.Exists(ScenePath)) return;
            Directory.CreateDirectory(SceneDir);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }

        static void EnsurePipeline()
        {
            Directory.CreateDirectory(SettingsDir);
            string assetPath = SettingsDir + "/VexaURP.asset";
            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(assetPath);
            if (asset == null)
            {
                var renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(renderer, SettingsDir + "/VexaURP_Renderer.asset");
                asset = UniversalRenderPipelineAsset.Create(renderer);
                asset.shadowDistance = 60f;
                asset.supportsHDR = true;
                AssetDatabase.CreateAsset(asset, assetPath);
            }
            GraphicsSettings.defaultRenderPipeline = asset;
            int current = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = asset;
            }
            QualitySettings.SetQualityLevel(current, false);
        }

        static void EnsureShaderRefs()
        {
            Directory.CreateDirectory(ShaderRefDir);
            // ModelLibrary: URP Lit with the mask (metallic / smoothness) and normal maps in every combination
            string[][] lit = { new string[0], new[] { "_NORMALMAP" }, new[] { "_METALLICSPECGLOSSMAP" }, new[] { "_METALLICSPECGLOSSMAP", "_NORMALMAP" } };
            for (int i = 0; i < lit.Length; i++) MakeRef($"Lit{i}", "Universal Render Pipeline/Lit", lit[i], false);
            // MapBuilder, WorldVisuals, ShotEffects
            MakeRef("Unlit", "Universal Render Pipeline/Unlit", new string[0], false);
            MakeRef("UnlitTransparent", "Universal Render Pipeline/Unlit", new[] { "_SURFACE_TYPE_TRANSPARENT" }, true);
            MakeRef("Sprites", "Sprites/Default", new string[0], false);
        }

        static void MakeRef(string name, string shaderName, string[] keywords, bool transparent)
        {
            string path = $"{ShaderRefDir}/{name}.mat";
            if (File.Exists(path)) return;
            var shader = Shader.Find(shaderName);
            if (shader == null) { Debug.LogWarning("VEXA build: shader not found: " + shaderName); return; }
            var m = new Material(shader) { name = name };
            foreach (var k in keywords) m.EnableKeyword(k);
            if (transparent)
            {
                m.SetFloat("_Surface", 1f);
                m.renderQueue = (int)RenderQueue.Transparent;
            }
            AssetDatabase.CreateAsset(m, path);
        }
    }
}
