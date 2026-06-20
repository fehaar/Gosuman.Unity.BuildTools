using System.IO;
using System.IO.Compression;
using UnityEngine;

namespace Gosuman.BuildTools
{
    public static class BuildArtifacts
    {
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
