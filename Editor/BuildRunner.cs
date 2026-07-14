using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Gosuman.BuildTools
{
    // Static entry points for headless/CI builds.
    //
    // Usage (Unity command line):
    //   -executeMethod Gosuman.BuildTools.BuildRunner.BuildActiveProfile
    //   -executeMethod Gosuman.BuildTools.BuildRunner.BuildPlatforms
    //
    // Environment variables:
    //   AZURE_CONTAINER_SAS_URL   Container SAS URL — overrides EditorPrefs when set.
    //                             Required for upload in headless mode (no EditorPrefs).
    //   BUILD_PLATFORMS           Comma-separated platform names for BuildPlatforms.
    //                             Accepted values: Windows, Linux, Android, WebGL, iOS
    //                             Example: BUILD_PLATFORMS=Windows,Linux
    public static class BuildRunner
    {
        // A property, not a static readonly field: ExecutableName reads Application.productName,
        // which isn't safe to evaluate at type-load time. WebGL/iOS build into a bare folder
        // (see BuildArtifacts.BuildsIntoFolder), so their RelPath has no filename to derive.
        static (string Name, BuildTarget Target, string RelPath)[] KnownPlatforms => new[]
        {
            ("Windows", BuildTarget.StandaloneWindows64, $"Builds/Windows/{ExecutableName(BuildTarget.StandaloneWindows64)}"),
            ("Linux",   BuildTarget.StandaloneLinux64,   $"Builds/Linux/{ExecutableName(BuildTarget.StandaloneLinux64)}"),
            ("Android", BuildTarget.Android,             $"Builds/Android/{ExecutableName(BuildTarget.Android)}"),
            ("WebGL",   BuildTarget.WebGL,               "Builds/WebGL"),
            ("iOS",     BuildTarget.iOS,                  "Builds/iOS"),
        };

        // -executeMethod Gosuman.BuildTools.BuildRunner.BuildActiveProfile
        public static void BuildActiveProfile()
        {
            ApplySasUrlFromEnv();

            var profile = BuildProfile.GetActiveBuildProfile();
            if (profile == null)
            {
                Debug.LogError("BuildRunner: no active build profile. Set one via File > Build Profiles.");
                EditorApplication.Exit(1);
                return;
            }

            string version = VersionReader.GetVersion();
            VersionReader.EnsureReleaseNotes(VersionReader.LoadOrCreate());

            var target = EditorUserBuildSettings.activeBuildTarget;
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string outputDir = Path.Combine(projectRoot, "Build", profile.name, version);
            Directory.CreateDirectory(outputDir);
            string output = Path.Combine(outputDir, ExecutableName(target));

            string[] scenes = profile.scenes
                .Where(s => s.enabled)
                .Select(s => s.path)
                .ToArray();

            string previousVersion = PlayerSettings.bundleVersion;
            PlayerSettings.bundleVersion = version;
            PlayerSettings.Android.bundleVersionCode = VersionReader.GetCommitCount();
            try
            {
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = output,
                    target = target,
                    options = BuildOptions.None,
                });

                if (report.summary.result == BuildResult.Succeeded)
                {
                    Debug.Log($"BuildRunner: {profile.name} {version} succeeded → {output} ({report.summary.totalSize / 1024 / 1024} MB)");
                    string artifact = BuildArtifacts.PrepareArtifact(output, target, outputDir, version, profile.name);
                    if (AzureUploader.IsConfigured)
                        AzureUploader.Upload(artifact, Path.GetFileName(artifact));
                    EditorApplication.Exit(0);
                }
                else
                {
                    Debug.LogError($"BuildRunner: FAILED — {report.summary.totalErrors} error(s)");
                    EditorApplication.Exit(1);
                }
            }
            finally
            {
                PlayerSettings.bundleVersion = previousVersion;
                AssetDatabase.SaveAssets();
            }
        }

        // -executeMethod Gosuman.BuildTools.BuildRunner.BuildPlatforms
        public static void BuildPlatforms()
        {
            ApplySasUrlFromEnv();

            string platformsEnv = Environment.GetEnvironmentVariable("BUILD_PLATFORMS") ?? "";
            var requested = platformsEnv
                .Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (requested.Count == 0)
            {
                Debug.LogError("BuildRunner: BUILD_PLATFORMS env var is empty. Example: BUILD_PLATFORMS=Windows,Linux");
                EditorApplication.Exit(1);
                return;
            }

            string version = VersionReader.GetVersion();
            VersionReader.EnsureReleaseNotes(VersionReader.LoadOrCreate());

            string[] scenes = EditorBuildSettings.scenes
                .Where(s => s.enabled)
                .Select(s => s.path)
                .ToArray();

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

            string previousVersion = PlayerSettings.bundleVersion;
            PlayerSettings.bundleVersion = version;
            PlayerSettings.Android.bundleVersionCode = VersionReader.GetCommitCount();

            int built = 0, failed = 0;
            try
            {
                foreach (var (name, target, relPath) in KnownPlatforms)
                {
                    if (!requested.Contains(name)) continue;

                    string output = Path.Combine(projectRoot, relPath);
                    Directory.CreateDirectory(Path.GetDirectoryName(output)!);

                    Debug.Log($"BuildRunner: building {name} → {output}");
                    var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                    {
                        scenes = scenes,
                        locationPathName = output,
                        target = target,
                        options = BuildOptions.None,
                    });

                    if (report.summary.result == BuildResult.Succeeded)
                    {
                        Debug.Log($"BuildRunner: {name} succeeded ({report.summary.totalSize / 1024 / 1024} MB)");
                        string buildFolder = BuildArtifacts.BuildsIntoFolder(target) ? output : Path.GetDirectoryName(output)!;
                        string artifact = BuildArtifacts.PrepareArtifact(output, target, buildFolder, version, name);
                        if (AzureUploader.IsConfigured)
                            AzureUploader.Upload(artifact, Path.GetFileName(artifact));
                        built++;
                    }
                    else
                    {
                        Debug.LogError($"BuildRunner: {name} FAILED ({report.summary.totalErrors} error(s))");
                        failed++;
                    }
                }
            }
            finally
            {
                PlayerSettings.bundleVersion = previousVersion;
                AssetDatabase.SaveAssets();
            }

            Debug.Log($"BuildRunner: done — {built} succeeded, {failed} failed. Version {version}");
            EditorApplication.Exit(failed > 0 ? 1 : 0);
        }

        // Allows CI to pass the SAS URL as an env var instead of requiring EditorPrefs
        // (which aren't available in headless mode).
        static void ApplySasUrlFromEnv()
        {
            string sasUrl = Environment.GetEnvironmentVariable("AZURE_CONTAINER_SAS_URL") ?? "";
            if (!string.IsNullOrEmpty(sasUrl))
                EditorPrefs.SetString(AzureUploader.PrefContainerSasUrl, sasUrl);
        }

        static string ExecutableName(BuildTarget target)
        {
            string product = Application.productName;
            return target switch
            {
                BuildTarget.StandaloneWindows or BuildTarget.StandaloneWindows64 => $"{product}.exe",
                BuildTarget.StandaloneOSX => $"{product}.app",
                BuildTarget.Android       => $"{product}.apk",
                BuildTarget.StandaloneLinux64 => product,
                _ => product,
            };
        }
    }
}
