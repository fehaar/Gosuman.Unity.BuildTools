using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Gosuman.BuildTools
{
    public class BuildWindow : EditorWindow
    {
        enum Mode { ActiveProfile, Platforms }

        const string ModePrefKey = "BuildTools.Build.Mode";

        struct Platform
        {
            public string Name;
            public BuildTarget Target;
            public string OutputPath;
            public string PrefKey => $"BuildTools.Build.{Name}";
        }

        static readonly Platform[] Platforms =
        {
            new() { Name = "Android", Target = BuildTarget.Android,           OutputPath = "Builds/Android/app.apk"       },
            new() { Name = "iOS",     Target = BuildTarget.iOS,                OutputPath = "Builds/iOS"                   },
            new() { Name = "Windows", Target = BuildTarget.StandaloneWindows64,OutputPath = "Builds/Windows/app.exe"       },
            new() { Name = "Linux",   Target = BuildTarget.StandaloneLinux64,  OutputPath = "Builds/Linux/app.x86_64"      },
            new() { Name = "Web",     Target = BuildTarget.WebGL,              OutputPath = "Builds/WebGL"                 },
        };

        bool[] selected = new bool[5];
        Mode mode;

        Editor versionEditor;
        Vector2 scroll;

        public static void Open()
        {
            var w = GetWindow<BuildWindow>("Build");
            w.minSize = new Vector2(300, 320);
        }

        void OnEnable()
        {
            mode = (Mode)EditorPrefs.GetInt(ModePrefKey, (int)Mode.ActiveProfile);
            for (int i = 0; i < Platforms.Length; i++)
                selected[i] = EditorPrefs.GetBool(Platforms[i].PrefKey, false);
        }

        void OnDisable()
        {
            if (versionEditor != null) DestroyImmediate(versionEditor);
        }

        void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);

            // Version config (bump / computed version / release notes) embedded at the top,
            // reusing the asset's own inspector so there is a single source of truth.
            EditorGUILayout.Space(4);
            DrawVersionConfig();

            EditorGUILayout.Space(8);
            DrawSeparator();
            EditorGUILayout.LabelField("Build", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            var newMode = (Mode)GUILayout.Toolbar((int)mode, new[] { "Active Profile", "Platforms" });
            if (newMode != mode)
            {
                mode = newMode;
                EditorPrefs.SetInt(ModePrefKey, (int)mode);
            }
            EditorGUILayout.Space(8);

            if (mode == Mode.ActiveProfile)
                DrawActiveProfile();
            else
                DrawPlatforms();

            EditorGUILayout.EndScrollView();
        }

        void DrawVersionConfig()
        {
            var cfg = VersionReader.LoadOrCreate();
            if (versionEditor == null || versionEditor.target != cfg)
            {
                if (versionEditor != null) DestroyImmediate(versionEditor);
                versionEditor = Editor.CreateEditor(cfg);
            }
            versionEditor.OnInspectorGUI();
        }

        static void DrawSeparator()
        {
            var rect = EditorGUILayout.GetControlRect(false, 1);
            EditorGUI.DrawRect(rect, new Color(0f, 0f, 0f, 0.25f));
        }

        // --- Active Build Profile mode ---

        void DrawActiveProfile()
        {
            var profile = BuildProfile.GetActiveBuildProfile();
            if (profile == null)
            {
                EditorGUILayout.HelpBox("No active build profile. Select one via File > Build Profiles.", MessageType.Warning);
                return;
            }

            EditorGUILayout.LabelField("Active profile", profile.name);
            EditorGUILayout.LabelField("Target", EditorUserBuildSettings.activeBuildTarget.ToString());
            EditorGUILayout.Space(8);

            if (GUILayout.Button("Build Active Profile", GUILayout.Height(32)))
                BuildActiveProfile(profile);
        }

        void BuildActiveProfile(BuildProfile profile)
        {
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
                    Debug.Log($"BuildTools: {profile.name} {version} succeeded → {output} ({report.summary.totalSize / 1024 / 1024} MB)");
                else
                    Debug.LogError($"BuildTools: {profile.name} {version} FAILED ({report.summary.totalErrors} error(s))");
            }
            finally
            {
                PlayerSettings.bundleVersion = previousVersion;
                AssetDatabase.SaveAssets();
            }
        }

        static string ExecutableName(BuildTarget target)
        {
            string product = Application.productName;
            return target switch
            {
                BuildTarget.StandaloneWindows or BuildTarget.StandaloneWindows64 => $"{product}.exe",
                BuildTarget.StandaloneOSX => $"{product}.app",
                BuildTarget.Android => $"{product}.apk",
                BuildTarget.StandaloneLinux64 => product,
                _ => product, // WebGL / iOS build into a folder named after the product
            };
        }

        // --- Platforms + Azure mode ---

        void DrawPlatforms()
        {
            EditorGUILayout.LabelField("Platforms", EditorStyles.boldLabel);
            for (int i = 0; i < Platforms.Length; i++)
            {
                bool next = EditorGUILayout.ToggleLeft(Platforms[i].Name, selected[i]);
                if (next != selected[i])
                {
                    selected[i] = next;
                    EditorPrefs.SetBool(Platforms[i].PrefKey, next);
                }
            }

            EditorGUILayout.Space(8);
            bool anySelected = selected.Any(s => s);
            using (new EditorGUI.DisabledScope(!anySelected))
            {
                if (GUILayout.Button("Build", GUILayout.Height(32)))
                    RunBuilds();
            }

            if (!anySelected)
                EditorGUILayout.HelpBox("Select at least one platform.", MessageType.Info);

            EditorGUILayout.Space(12);
            EditorGUILayout.LabelField("Azure Upload", EditorStyles.boldLabel);

            string currentSas = EditorPrefs.GetString(AzureUploader.PrefContainerSasUrl, "");
            string newSas = EditorGUILayout.PasswordField("Container SAS URL", currentSas);
            if (newSas != currentSas)
                EditorPrefs.SetString(AzureUploader.PrefContainerSasUrl, newSas);

            if (!AzureUploader.IsConfigured)
                EditorGUILayout.HelpBox("Enter SAS URL to enable automatic upload after build.", MessageType.None);
        }

        void RunBuilds()
        {
            string version = VersionReader.GetVersion();
            PlayerSettings.bundleVersion = version;
            PlayerSettings.Android.bundleVersionCode = VersionReader.GetCommitCount();

            string[] scenes = EditorBuildSettings.scenes
                .Where(s => s.enabled)
                .Select(s => s.path)
                .ToArray();

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

            int built = 0, failed = 0;
            for (int i = 0; i < Platforms.Length; i++)
            {
                if (!selected[i]) continue;

                var platform = Platforms[i];
                string output = Path.Combine(projectRoot, platform.OutputPath);
                Directory.CreateDirectory(Path.GetDirectoryName(output)!);

                Debug.Log($"BuildTools: building {platform.Name} → {output}");

                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = output,
                    target = platform.Target,
                    options = BuildOptions.None,
                });

                if (report.summary.result == BuildResult.Succeeded)
                {
                    Debug.Log($"BuildTools: {platform.Name} succeeded ({report.summary.totalSize / 1024 / 1024} MB)");
                    built++;

                    if (File.Exists(output) && AzureUploader.IsConfigured)
                    {
                        string ext = Path.GetExtension(output);
                        string blobName = $"{Application.productName}-{version}-{platform.Name.ToLower()}{ext}";
                        AzureUploader.Upload(output, blobName);
                    }
                }
                else
                {
                    Debug.LogError($"BuildTools: {platform.Name} FAILED ({report.summary.totalErrors} error(s))");
                    failed++;
                }
            }

            Debug.Log($"BuildTools: done — {built} succeeded, {failed} failed. Version {version}");
        }
    }
}
