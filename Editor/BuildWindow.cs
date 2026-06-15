using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Gosuman.BuildTools
{
    public class BuildWindow : EditorWindow
    {
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

        public static void Open()
        {
            var w = GetWindow<BuildWindow>("Build");
            w.minSize = new Vector2(280, 240);
        }

        void OnEnable()
        {
            for (int i = 0; i < Platforms.Length; i++)
                selected[i] = EditorPrefs.GetBool(Platforms[i].PrefKey, false);
        }

        void OnGUI()
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField($"Version: {VersionReader.GetVersion()}", EditorStyles.boldLabel);
            EditorGUILayout.Space(8);

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
            {
                EditorGUILayout.HelpBox("Select at least one platform.", MessageType.Info);
            }
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
