using System.IO;
using System.IO.Compression;
using UnityEditor;
using UnityEngine;

namespace Gosuman.BuildTools
{
    public static class BuildArtifacts
    {
        // WebGL and iOS builds are handed a bare folder path as locationPathName and Unity
        // populates that folder directly — output already IS the build folder. Every other
        // target builds a single file (.exe/.apk/binary) inside a folder, so the build folder
        // is output's parent. Callers must branch on this before deriving buildFolder from
        // output, or WebGL/iOS zips end up one directory level too high.
        public static bool BuildsIntoFolder(BuildTarget target) =>
            target == BuildTarget.WebGL || target == BuildTarget.iOS;

        // Returns the path of the artifact to upload for a build. Android already produces a
        // single .apk file, so zipping it would just add an unnecessary wrapper — upload it
        // as-is. Every other platform builds a folder, which still needs zipping.
        public static string PrepareArtifact(string outputPath, BuildTarget target, string buildFolder, string version, string label) =>
            target == BuildTarget.Android ? outputPath : ZipBuild(buildFolder, version, label);

        // Zips a build's output folder into a sibling archive named
        // <product>-<version>-<label>.zip and returns its path. Overwrites an existing zip of
        // the same name. <label> is the platform name (Platforms mode) or the build profile
        // name (Active Profile mode).
        public static string ZipBuild(string buildFolder, string version, string label)
        {
            string product = Sanitize(Application.productName);
            string zipName = $"{product}-{version}-{Sanitize(label)}.zip";
            string zipPath = Path.Combine(Directory.GetParent(buildFolder)!.FullName, zipName);

            if (File.Exists(zipPath)) File.Delete(zipPath);
            ZipFile.CreateFromDirectory(buildFolder, zipPath, System.IO.Compression.CompressionLevel.Optimal, includeBaseDirectory: false);

            long mb = new FileInfo(zipPath).Length / 1024 / 1024;
            Debug.Log($"BuildTools: zipped build → {zipPath} ({mb} MB)");
            return zipPath;
        }

        static string Sanitize(string s)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                s = s.Replace(c, '-');
            return s.Replace(' ', '-');
        }
    }
}
