using UnityEditor;
using UnityEngine;

namespace Gosuman.BuildTools
{
    public static class BuildMenu
    {
        [MenuItem("Build/Build... %#b")]
        static void OpenBuildWindow() => BuildWindow.Open();

        [MenuItem("Build/Version Info #&v")]
        static void SelectVersionInfo()
        {
            var cfg = VersionReader.LoadOrCreate();
            Selection.activeObject = cfg;
            EditorGUIUtility.PingObject(cfg);
        }
    }
}
