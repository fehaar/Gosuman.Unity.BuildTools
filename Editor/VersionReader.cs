using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Gosuman.BuildTools
{
    public static class VersionReader
    {
        const string AssetPath = "Assets/Editor/VersionConfig.asset";

        public static VersionConfig LoadOrCreate()
        {
            var cfg = AssetDatabase.LoadAssetAtPath<VersionConfig>(AssetPath);
            if (cfg != null) return cfg;

            Directory.CreateDirectory(Path.Combine(Application.dataPath, "Editor"));
            cfg = ScriptableObject.CreateInstance<VersionConfig>();
            AssetDatabase.CreateAsset(cfg, AssetPath);
            AssetDatabase.SaveAssets();
            UnityEngine.Debug.Log($"BuildTools: created {AssetPath}");
            return cfg;
        }

        public static string GetVersion()
        {
            var cfg = LoadOrCreate();
            string version = $"{cfg.major}.{cfg.minor}.{GetCommitCount()}";
            // Append the CI run number only when GitHub provides it; locally it is always 0.
            int run = GetRunNumber();
            return run > 0 ? $"{version}.{run}" : version;
        }

        // GitHub Actions run number, or 0 when not running in CI.
        public static int GetRunNumber()
        {
            string val = Environment.GetEnvironmentVariable("GITHUB_RUN_NUMBER");
            return int.TryParse(val, out int n) ? n : 0;
        }

        // --- Release notes (external <major.minor>.md files) ---

        // The Unity project directory. Reads Application.dataPath, so it must be accessed on
        // the main thread — capture it there before handing work to a background thread.
        public static string ProjectDir => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        public static string GetReleaseNotesFolder(VersionConfig cfg) =>
            Path.GetFullPath(Path.Combine(ProjectDir, cfg.releaseNotesFolder));

        public static string GetReleaseNotesPath(VersionConfig cfg) =>
            Path.Combine(GetReleaseNotesFolder(cfg), $"{cfg.major}.{cfg.minor}.md");

        public static string GetReleaseNotesTemplatePath(VersionConfig cfg) =>
            Path.Combine(GetReleaseNotesFolder(cfg), "Template.md");

        // Ensures a release-notes file exists for the current major.minor, seeding it from
        // Template.md when present. Returns the file path, or null if it could not be created.
        public static string EnsureReleaseNotes(VersionConfig cfg)
        {
            string path = GetReleaseNotesPath(cfg);
            if (File.Exists(path)) return path;

            string template = GetReleaseNotesTemplatePath(cfg);
            try
            {
                Directory.CreateDirectory(GetReleaseNotesFolder(cfg));
                if (File.Exists(template))
                {
                    File.Copy(template, path);
                    UnityEngine.Debug.Log($"BuildTools: created release notes {path} from template.");
                }
                else
                {
                    File.WriteAllText(path, $"# {cfg.major}.{cfg.minor}\n\n");
                    UnityEngine.Debug.LogWarning($"BuildTools: template not found at {template}; created empty {path}.");
                }
                return path;
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError($"BuildTools: could not create release notes at {path}: {e.Message}");
                return null;
            }
        }

        public static string ReadReleaseNotes(VersionConfig cfg)
        {
            string path = GetReleaseNotesPath(cfg);
            return File.Exists(path) ? File.ReadAllText(path) : "";
        }

        public static void WriteReleaseNotes(VersionConfig cfg, string text)
        {
            try
            {
                Directory.CreateDirectory(GetReleaseNotesFolder(cfg));
                File.WriteAllText(GetReleaseNotesPath(cfg), text);
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError($"BuildTools: could not write release notes: {e.Message}");
            }
        }

        public static int GetCommitCount() => GetCommitCount(ProjectDir);

        // Thread-safe overload: pass a working directory captured on the main thread so this
        // can run on a background thread without touching Application.dataPath.
        public static int GetCommitCount(string workingDir)
        {
            try
            {
                string output = RunGit("rev-list --count HEAD", workingDir);
                return int.TryParse(output.Trim(), out int n) ? n : 0;
            }
            catch { return 0; }
        }

        static string RunGit(string args, string workingDir)
        {
            var psi = new ProcessStartInfo("git", args)
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = workingDir
            };
            using var p = Process.Start(psi);
            string output = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            return output;
        }
    }
}
