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
            return $"{cfg.major}.{cfg.minor}.{GetCommitCount()}.{GetRunNumber()}";
        }

        public static int GetCommitCount()
        {
            try
            {
                string output = RunGit("rev-list --count HEAD");
                return int.TryParse(output.Trim(), out int n) ? n : 0;
            }
            catch { return 0; }
        }

        public static int GetRunNumber()
        {
            string val = Environment.GetEnvironmentVariable("GITHUB_RUN_NUMBER");
            return int.TryParse(val, out int n) ? n : 0;
        }

        static string RunGit(string args)
        {
            var psi = new ProcessStartInfo("git", args)
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, ".."))
            };
            using var p = Process.Start(psi);
            string output = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            return output;
        }
    }
}
