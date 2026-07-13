using UnityEditor;

namespace Gosuman.BuildTools
{
    public static class BuildMenu
    {
        // Selects (creating if needed) the VersionConfig asset. Its inspector holds the
        // version, release notes, and build controls — there is no separate Build window.
        [MenuItem("Build/Build Config #&b")]
        static void SelectBuildConfig()
        {
            var cfg = VersionReader.LoadOrCreate();
            Selection.activeObject = cfg;
            EditorGUIUtility.PingObject(cfg);
        }

        [MenuItem("Build/Release Notes")]
        static void OpenReleaseNotes()
        {
            var cfg = VersionReader.LoadOrCreate();
            string path = VersionReader.EnsureReleaseNotes(cfg);
            if (!string.IsNullOrEmpty(path))
                EditorUtility.RevealInFinder(path);
        }
    }
}
