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
                Debug.LogError("BuildTools: No active Build Profile set. Open Build Profiles (File > Build Profiles) and set one as active.");
                return;
            }

            string version = VersionConfig.GetVersion();
            PlayerSettings.bundleVersion = version;
            PlayerSettings.Android.bundleVersionCode = VersionConfig.GetCommitCount();
            Debug.Log($"BuildTools: building version {version} using profile '{profile.name}'");

            BuildPipeline.BuildPlayer(new BuildPlayerWithProfileOptions
            {
                buildProfile = profile
            });
        }

        [MenuItem("Build/Log Version")]
        static void LogVersion()
        {
            Debug.Log($"BuildTools: version would be {VersionConfig.GetVersion()}");
        }
    }
}
