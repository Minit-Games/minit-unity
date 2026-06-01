using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEditor;
using UnityEditor.Build;           // NamedBuildTarget
using UnityEditor.Build.Reporting; // BuildReport, BuildResult
using UnityEngine;

namespace MinitGames.Editor
{
    /// <summary>
    /// One-click WebGL build tool for the Minit platform.
    /// Accessible via <b>Minit → Build for Minit</b> in the Unity menu bar.
    /// </summary>
    public static class MinitBuild
    {
        private const string MenuPath = "Minit/Build for Minit";
        private const long SizeCapBytes = 52_428_800L; // 50 MiB

        [MenuItem(MenuPath)]
        public static void BuildForMinit()
        {
            try
            {
                RunBuild();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Minit] Build for Minit failed with an unexpected exception:\n{ex}");
            }
        }

        private static void RunBuild()
        {
            // ── 1. Guard: WebGL template present ────────────────────────────────────
            const string templateRelPath = "Assets/WebGLTemplates/Minit/index.html";
            string templateFullPath = Path.Combine(
                Path.GetDirectoryName(Application.dataPath) ?? "",
                templateRelPath.Replace('/', Path.DirectorySeparatorChar));

            if (!File.Exists(templateFullPath))
            {
                Debug.LogError(
                    "[Minit] Minit WebGL template missing — see README.\n" +
                    $"Expected: {templateFullPath}\n" +
                    "The template ships with the project's Assets/WebGLTemplates/Minit/ folder, " +
                    "not inside the UPM package. Copy it into your project if you're using the " +
                    "package via UPM Git URL (follow-up: DROP-2022).");
                return;
            }

            // ── 2. Guard: at least one enabled scene ────────────────────────────────
            EditorBuildSettingsScene[] allScenes = EditorBuildSettings.scenes;
            var enabledScenePaths = new System.Collections.Generic.List<string>();
            foreach (var s in allScenes)
            {
                if (s.enabled && !string.IsNullOrEmpty(s.path))
                    enabledScenePaths.Add(s.path);
            }

            if (enabledScenePaths.Count == 0)
            {
                Debug.LogError(
                    "[Minit] No enabled scenes found in Build Settings (File → Build Settings). " +
                    "Add and enable at least one scene, then try again.");
                return;
            }

            // ── 3. Apply compliant Player Settings ──────────────────────────────────

            // Select the Minit WebGL template.
            PlayerSettings.WebGL.template = "PROJECT:Minit";

            // Brotli: smallest transfer size; Unity 6 supports it natively in WebGL.
            // VERIFY: WebGLCompressionFormat enum — confirmed in Unity 6000.0 docs.
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Brotli;

            // Decompression fallback ON: the Minit platform serves bundle files from S3/CloudFront
            // WITHOUT a Content-Encoding header, so the browser cannot decompress *.br files
            // natively. Unity's JS decompressor must be bundled to decompress Brotli files
            // client-side. Brotli compression is kept (files stay small on the wire); only the
            // small JS decompressor shim is added to the build output.
            PlayerSettings.WebGL.decompressionFallback = true;

            // No C# exceptions: reduces code size substantially; games should not throw.
            // VERIFY: WebGLExceptionSupport.None — confirmed in Unity 6000.0 docs.
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.None;

            // No IndexedDB data caching: the Minit platform forbids persistent client-side
            // storage (localStorage, sessionStorage, IndexedDB). Disable the Unity asset cache.
            PlayerSettings.WebGL.dataCaching = false;

            // Wasm linker target: single-threaded Wasm is the only output that works inside
            // WKWebView without COOP/COEP headers. Do NOT enable WebGL multithreading —
            // threaded builds require SharedArrayBuffer which needs those headers; Minit does
            // not serve them and threaded builds will crash in the in-app WebView.
            // VERIFY: WebGLLinkerTarget.Wasm — confirmed in Unity 6000.0 docs.
            PlayerSettings.WebGL.linkerTarget = WebGLLinkerTarget.Wasm;

            // IL2CPP: better runtime performance and smaller binary than Mono for WebGL.
            // NamedBuildTarget overloads replace the deprecated BuildTargetGroup overloads in Unity 6.
            // VERIFY: NamedBuildTarget.WebGL — confirmed in Unity 6000.0 docs.
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.WebGL, ScriptingImplementation.IL2CPP);

            // High managed stripping: removes unused managed code, reducing output size.
            // VERIFY: ManagedStrippingLevel.High — confirmed in Unity 6000.0 docs.
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.WebGL, ManagedStrippingLevel.High);

            // Strip unused engine code: removes unused Unity engine modules.
            PlayerSettings.stripEngineCode = true;

            // Run in background: keeps the game loop alive when the browser tab loses focus
            // (essential for score-reporting not to stall when the WebView is backgrounded).
            PlayerSettings.runInBackground = true;

            // ── 4. PlayerPrefs warning (non-blocking) ───────────────────────────────
            // PlayerPrefs maps to localStorage, which is forbidden on the Minit platform.
            WarnIfPlayerPrefsUsed();

            // ── 5. Build ─────────────────────────────────────────────────────────────
            string projectRoot = Path.GetDirectoryName(Application.dataPath) ?? ".";
            string outputDir   = Path.Combine(projectRoot, "Build", "MinitWebGL");

            // Clean previous output so Unity doesn't merge stale files.
            if (Directory.Exists(outputDir))
                Directory.Delete(outputDir, recursive: true);
            Directory.CreateDirectory(outputDir);

            var buildOptions = new BuildPlayerOptions
            {
                scenes             = enabledScenePaths.ToArray(),
                locationPathName   = outputDir,
                target             = BuildTarget.WebGL,
                options            = BuildOptions.None,
            };

            Debug.Log($"[Minit] Starting WebGL build → {outputDir}");
            BuildReport report = BuildPipeline.BuildPlayer(buildOptions);

            if (report.summary.result != BuildResult.Succeeded)
            {
                Debug.LogError(
                    $"[Minit] Build failed (result: {report.summary.result}). " +
                    "Check the Console for compiler errors and try again. No ZIP was created.");
                return;
            }

            Debug.Log($"[Minit] Build succeeded in {report.summary.totalTime.TotalSeconds:F1}s.");

            // ── 6. Zip ───────────────────────────────────────────────────────────────
            string safeName = SanitizeFileName(Application.productName);
            string zipPath  = Path.Combine(projectRoot, "Build", $"{safeName}_minit.zip");

            if (File.Exists(zipPath))
                File.Delete(zipPath);

            // includeBaseDirectory: false → index.html is at the archive root.
            // Wrap in try/catch so a partial/corrupt ZIP is removed before rethrowing —
            // otherwise a corrupt archive at zipPath could confuse a subsequent build run.
            try
            {
                ZipFile.CreateFromDirectory(outputDir, zipPath, System.IO.Compression.CompressionLevel.Optimal, includeBaseDirectory: false);
            }
            catch
            {
                if (File.Exists(zipPath))
                    File.Delete(zipPath);
                throw;
            }

            // ── 7. Report size ───────────────────────────────────────────────────────
            long   zipBytes = new FileInfo(zipPath).Length;
            double zipMb    = zipBytes / (1024.0 * 1024.0);

            Debug.Log($"[Minit] ZIP created: {zipPath} ({zipMb:F2} MB)");

            if (zipBytes > SizeCapBytes)
            {
                Debug.LogWarning(
                    $"[Minit] ZIP is {zipMb:F2} MB — this exceeds the Minit 50 MB hard cap. " +
                    "The file was still written, but the upload to the creator console will be rejected. " +
                    "Enable High managed stripping, strip engine code, and reduce asset sizes.");
            }

            EditorUtility.RevealInFinder(zipPath);
        }

        // ── Helpers ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Scans <c>Assets/</c> for <c>.cs</c> files that reference <c>PlayerPrefs</c>.
        /// PlayerPrefs persists via localStorage, which is forbidden on the Minit platform.
        /// This is a warning only — the build continues.
        /// </summary>
        private static void WarnIfPlayerPrefsUsed()
        {
            string dataPath = Application.dataPath;
            string[] csFiles;
            try
            {
                csFiles = Directory.GetFiles(dataPath, "*.cs", SearchOption.AllDirectories);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Minit] Could not scan Assets/ for PlayerPrefs usage: {ex.Message}");
                return;
            }

            var hits = new System.Collections.Generic.List<string>();
            foreach (string file in csFiles)
            {
                try
                {
                    if (File.ReadAllText(file).Contains("PlayerPrefs"))
                        hits.Add(file);
                }
                catch
                {
                    // Skip unreadable files silently.
                }
            }

            if (hits.Count > 0)
            {
                var sb = new StringBuilder();
                sb.AppendLine(
                    "[Minit] WARNING: PlayerPrefs usage detected. " +
                    "PlayerPrefs maps to localStorage, which is forbidden on the Minit platform " +
                    "(persistent storage APIs are blocked). Replace with in-memory state or " +
                    "Minit config values. Affected files:");
                foreach (string h in hits)
                    sb.AppendLine($"  {h}");
                Debug.LogWarning(sb.ToString());
            }
        }

        /// <summary>
        /// Returns <paramref name="name"/> with characters that are illegal in filenames
        /// replaced by underscores and leading/trailing whitespace trimmed.
        /// Falls back to <c>"Game"</c> if the result would be empty.
        /// </summary>
        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "Game";

            char[] invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(name.Trim());
            for (int i = 0; i < sb.Length; i++)
            {
                if (Array.IndexOf(invalid, sb[i]) >= 0 || sb[i] == ' ')
                    sb[i] = '_';
            }

            string result = sb.ToString().Trim('_');
            return string.IsNullOrEmpty(result) ? "Game" : result;
        }
    }
}
