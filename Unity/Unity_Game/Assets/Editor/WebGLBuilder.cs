#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Command-line WebGL build for the therapy web app. Invoke with:
///   Unity.exe -quit -batchmode -nographics -projectPath &lt;proj&gt; -executeMethod WebGLBuilder.Build
///             -logFile &lt;log&gt; -buildOut &lt;outDir&gt;
/// Builds SampleScene with product name "therapy" and compression disabled so the Next.js
/// UnityPlayer can load therapy.loader.js / .data / .framework.js / .wasm directly.
/// </summary>
public static class WebGLBuilder
{
    public static void Build()
    {
        string outDir = GetArg("-buildOut") ?? Path.Combine(Directory.GetCurrentDirectory(), "WebGLBuild");

        // Name the output files "therapy.*" to match components/mode-runner.tsx (<UnityPlayer name="therapy" />).
        PlayerSettings.productName = "therapy";
        // Uncompressed build = simplest static hosting (no server Content-Encoding config needed).
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
        PlayerSettings.WebGL.dataCaching = true;
        PlayerSettings.WebGL.template = "APPLICATION:Default";
        // Smaller/faster to build; brotli/threads not needed here.
        PlayerSettings.WebGL.linkerTarget = WebGLLinkerTarget.Wasm;

        var scenes = new[] { "Assets/Scenes/SampleScene.unity" };

        var opts = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = outDir,
            target = BuildTarget.WebGL,
            options = BuildOptions.None,
        };

        Debug.Log($"[WebGLBuilder] building {scenes[0]} -> {outDir}");
        BuildReport report = BuildPipeline.BuildPlayer(opts);
        BuildSummary summary = report.summary;

        if (summary.result == BuildResult.Succeeded)
        {
            Debug.Log($"[WebGLBuilder] SUCCESS {summary.totalSize} bytes -> {outDir}");
            EditorApplication.Exit(0);
        }
        else
        {
            Debug.LogError($"[WebGLBuilder] FAILED: {summary.result} ({summary.totalErrors} errors)");
            EditorApplication.Exit(1);
        }
    }

    private static string GetArg(string name)
    {
        var args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == name) return args[i + 1];
        return null;
    }
}
#endif
