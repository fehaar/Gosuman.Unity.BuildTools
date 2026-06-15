using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEngine;

namespace Gosuman.BuildTools
{
    public static class BuildMenu
    {
        [MenuItem("Build/Build Active Profile")]
        static void BuildActiveProfile()
        {
            var profile = BuildProfile.GetActiveBuildProfile();
            if (profile == null)
            {
                Debug.LogError("BuildTools: No active Build Profile set. Open File > Build Profiles and set one as active.");
                return;
            }

            string version = VersionReader.GetVersion();
            PlayerSettings.bundleVersion = version;
            PlayerSettings.Android.bundleVersionCode = VersionReader.GetCommitCount();
            Debug.Log($"BuildTools: building version {version} using profile '{profile.name}'");

            BuildPipeline.BuildPlayer(new BuildPlayerWithProfileOptions
            {
                buildProfile = profile
            });
        }

        [MenuItem("Build/Log Version")]
        static void LogVersion()
        {
            Debug.Log($"BuildTools: version = {VersionReader.GetVersion()}");
        }

        [MenuItem("Gosuman/Version Info #&v")]
        static void SelectVersionInfo()
        {
            var cfg = VersionReader.LoadOrCreate();
            Selection.activeObject = cfg;
            EditorGUIUtility.PingObject(cfg);
        }
    }
}
