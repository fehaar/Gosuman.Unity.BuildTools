using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Gosuman.BuildTools
{
    // Reads x.y from a gitignored version.txt in the project root.
    // z = total git commit count (monotonically increasing, pins the code point).
    // w = GITHUB_RUN_NUMBER env var, falls back to 0 for local builds.
    public static class VersionConfig
    {
        const string VersionFile = "version.txt";  // relative to project root, gitignored

        public static string GetVersion()
        {
            string xy = ReadVersionFile();
            int z = GetCommitCount();
            int w = GetRunNumber();
            return $"{xy}.{z}.{w}";
        }

        static string ReadVersionFile()
        {
            string path = Path.Combine(GetProjectRoot(), VersionFile);
            if (!File.Exists(path))
            {
                UnityEngine.Debug.LogWarning($"BuildTools: {VersionFile} not found at {path}. Using 0.1. Create it with content like \"0.1\".");
                return "0.1";
            }
            return File.ReadAllText(path).Trim();
        }

        public static int GetCommitCount()
        {
            try
            {
                var result = RunGit("rev-list --count HEAD");
                return int.TryParse(result.Trim(), out int count) ? count : 0;
            }
            catch { return 0; }
        }

        static int GetRunNumber()
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
                WorkingDirectory = GetProjectRoot()
            };
            using var p = Process.Start(psi);
            string output = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            return output;
        }

        static string GetProjectRoot()
        {
            // Application.dataPath is <project>/Assets — go up one level
            return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        }
    }
}
