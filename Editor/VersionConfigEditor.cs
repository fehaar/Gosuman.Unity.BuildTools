using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Gosuman.BuildTools
{
    // Single inspector for the VersionConfig asset: version, release notes, and the build
    // controls all live here (there is no separate Build window). The Build menu item just
    // selects this asset so the inspector is shown.
    [CustomEditor(typeof(VersionConfig))]
    public class VersionConfigEditor : Editor
    {
        const string NotesControlName = "ReleaseNotes";
        bool notesFocusedLastFrame;

        // Word-wrapping text-area style, built lazily (EditorStyles isn't ready at field init).
        static GUIStyle notesStyle;
        static GUIStyle NotesStyle =>
            notesStyle ??= new GUIStyle(EditorStyles.textArea) { wordWrap = true };

        // External release-notes buffer, loaded lazily and reloaded when major/minor changes.
        string notesBuffer = "";
        int loadedMajor = -1;
        int loadedMinor = -1;

        // Cached commit count. GetCommitCount() shells out to git, so it must NOT run every
        // repaint (the inspector repaints continuously while a text field is focused). It is
        // fetched on a background thread when the inspector is shown or the editor regains
        // focus, then cached; builds still read it fresh.
        int commitCount;
        bool commitCountKnown; // false until the first background fetch returns (shows "?")
        Task<int> commitCountTask;
        string projectDir; // captured on the main thread for the background git call

        // --- Azure upload ---

        const string AzureFoldoutPrefKey = "BuildTools.Azure.Foldout";
        bool azureFoldout;

        // --- Build configuration ---

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

        void OnEnable()
        {
            azureFoldout = EditorPrefs.GetBool(AzureFoldoutPrefKey, false);
            mode = (Mode)EditorPrefs.GetInt(ModePrefKey, (int)Mode.ActiveProfile);
            for (int i = 0; i < Platforms.Length; i++)
                selected[i] = EditorPrefs.GetBool(Platforms[i].PrefKey, false);

            projectDir = VersionReader.ProjectDir; // main thread
            RefreshCommitCountAsync();
            EditorApplication.focusChanged += OnEditorFocusChanged;
        }

        void OnDisable()
        {
            EditorApplication.focusChanged -= OnEditorFocusChanged;
            EditorApplication.update -= PollCommitCount;
        }

        // Refresh when the user returns to the Unity editor (e.g. after committing in a terminal).
        void OnEditorFocusChanged(bool focused)
        {
            if (focused) RefreshCommitCountAsync();
        }

        // Fetch the commit count on a background thread; PollCommitCount picks up the result
        // on the main thread and repaints. No-op if a fetch is already in flight.
        void RefreshCommitCountAsync()
        {
            if (commitCountTask != null && !commitCountTask.IsCompleted) return;
            string dir = projectDir;
            commitCountTask = Task.Run(() => VersionReader.GetCommitCount(dir));
            EditorApplication.update -= PollCommitCount;
            EditorApplication.update += PollCommitCount;
        }

        void PollCommitCount()
        {
            if (commitCountTask == null) { EditorApplication.update -= PollCommitCount; return; }
            if (!commitCountTask.IsCompleted) return;

            if (commitCountTask.Status == TaskStatus.RanToCompletion)
            {
                commitCount = commitCountTask.Result;
                commitCountKnown = true;
            }
            commitCountTask = null;
            EditorApplication.update -= PollCommitCount;
            Repaint();
        }

        public override void OnInspectorGUI()
        {
            var cfg = (VersionConfig)target;
            serializedObject.Update();
            GUI.changed = false;

            DrawVersion(cfg);
            EditorGUILayout.Space();
            DrawReleaseNotes(cfg);

            EditorGUILayout.Space();
            DrawSeparator();
            DrawAzure();

            EditorGUILayout.Space();
            DrawSeparator();
            EditorGUILayout.LabelField("Build", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);
            DrawBuild();

            serializedObject.ApplyModifiedProperties();
        }

        // --- Version ---

        void DrawVersion(VersionConfig cfg)
        {
            // Major / Minor (bumpable) + cached Commit count. Version is major.minor.commitCount.
            EditorGUILayout.BeginHorizontal();
            DrawBumpableField("Major", ref cfg.major, () =>
            {
                Undo.RecordObject(cfg, "Bump Major");
                cfg.major++;
                cfg.minor = 0;
                MarkDirty(cfg);
            });
            DrawBumpableField("Minor", ref cfg.minor, () =>
            {
                Undo.RecordObject(cfg, "Bump Minor");
                cfg.minor++;
                MarkDirty(cfg);
            });
            DrawComputedField("Commit", commitCountKnown ? commitCount.ToString() : "?", RefreshCommitCountAsync);
            EditorGUILayout.EndHorizontal();

            if (GUI.changed) MarkDirty(cfg);
        }

        // --- Release notes (external files) ---

        void DrawReleaseNotes(VersionConfig cfg)
        {
            string newFolder = EditorGUILayout.DelayedTextField("Release notes folder", cfg.releaseNotesFolder);
            if (newFolder != cfg.releaseNotesFolder)
            {
                cfg.releaseNotesFolder = newFolder;
                loadedMajor = loadedMinor = -1; // force reload from the new location
                MarkDirty(cfg);
            }

            string notesPath = VersionReader.GetReleaseNotesPath(cfg);
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.TextField("File", notesPath);

            // (Re)load the buffer when the targeted file changes
            if (loadedMajor != cfg.major || loadedMinor != cfg.minor)
            {
                notesBuffer = VersionReader.ReadReleaseNotes(cfg);
                loadedMajor = cfg.major;
                loadedMinor = cfg.minor;
                notesFocusedLastFrame = false;
            }

            EditorGUILayout.LabelField($"Release notes for {cfg.major}.{cfg.minor}", EditorStyles.boldLabel);

            if (!File.Exists(notesPath))
            {
                EditorGUILayout.HelpBox("No release-notes file yet. It will be created on save or first build.", MessageType.Info);
                if (GUILayout.Button("Create from template"))
                {
                    VersionReader.EnsureReleaseNotes(cfg);
                    notesBuffer = VersionReader.ReadReleaseNotes(cfg);
                }
            }

            // Word-wrapped text area sized to the available width, so long lines wrap
            // instead of widening the inspector and forcing a horizontal scrollbar.
            float width = EditorGUIUtility.currentViewWidth - 22f;
            float minHeight = NotesStyle.lineHeight * 3;
            float contentHeight = NotesStyle.CalcHeight(new GUIContent(notesBuffer), width);
            float notesHeight = Mathf.Max(minHeight, contentHeight);

            bool notesFocused = GUI.GetNameOfFocusedControl() == NotesControlName;

            GUI.SetNextControlName(NotesControlName);
            notesBuffer = EditorGUILayout.TextArea(notesBuffer, NotesStyle,
                GUILayout.Height(notesHeight), GUILayout.ExpandWidth(true));

            // Persist to the external file only when focus leaves the field
            if (notesFocusedLastFrame && !notesFocused)
                VersionReader.WriteReleaseNotes(cfg, notesBuffer);

            notesFocusedLastFrame = notesFocused;
        }

        // --- Azure upload ---

        void DrawAzure()
        {
            bool next = EditorGUILayout.Foldout(azureFoldout, "Azure Upload", true, EditorStyles.foldoutHeader);
            if (next != azureFoldout)
            {
                azureFoldout = next;
                EditorPrefs.SetBool(AzureFoldoutPrefKey, azureFoldout);
            }

            if (!azureFoldout) return;

            EditorGUI.indentLevel++;
            string currentSas = EditorPrefs.GetString(AzureUploader.PrefContainerSasUrl, "");
            string newSas = EditorGUILayout.PasswordField("Container SAS URL", currentSas);
            if (newSas != currentSas)
                EditorPrefs.SetString(AzureUploader.PrefContainerSasUrl, newSas);

            if (!AzureUploader.IsConfigured)
                EditorGUILayout.HelpBox("Enter SAS URL to enable automatic upload after build.", MessageType.None);
            EditorGUI.indentLevel--;
        }

        // --- Build ---

        void DrawBuild()
        {
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
        }

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
                // Defer out of OnInspectorGUI: BuildPipeline.BuildPlayer pumps its own progress
                // UI, and running it inside the IMGUI pass corrupts GUI state ("GUI Window tried
                // to begin rendering while something else had not finished rendering").
                EditorApplication.delayCall += () => BuildActiveProfile(profile);
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
                {
                    Debug.Log($"BuildTools: {profile.name} {version} succeeded → {output} ({report.summary.totalSize / 1024 / 1024} MB)");
                    BuildArtifacts.PrepareArtifact(output, target, outputDir, version, profile.name);
                }
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
                    EditorApplication.delayCall += RunBuilds; // defer out of the IMGUI pass (see DrawActiveProfile)
            }

            if (!anySelected)
                EditorGUILayout.HelpBox("Select at least one platform.", MessageType.Info);
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

                    string buildFolder = BuildArtifacts.BuildsIntoFolder(platform.Target) ? output : Path.GetDirectoryName(output)!;
                    string artifact = BuildArtifacts.PrepareArtifact(output, platform.Target, buildFolder, version, platform.Name);
                    if (AzureUploader.IsConfigured)
                        AzureUploader.Upload(artifact, Path.GetFileName(artifact));
                }
                else
                {
                    Debug.LogError($"BuildTools: {platform.Name} FAILED ({report.summary.totalErrors} error(s))");
                    failed++;
                }
            }

            Debug.Log($"BuildTools: done — {built} succeeded, {failed} failed. Version {version}");
        }

        // --- Small drawing helpers ---

        static void DrawSeparator()
        {
            var rect = EditorGUILayout.GetControlRect(false, 1);
            EditorGUI.DrawRect(rect, new Color(0f, 0f, 0f, 0.25f));
        }

        // Column with the label on top and an int field + bump button below.
        static void DrawBumpableField(string label, ref int value, System.Action onBump)
        {
            EditorGUILayout.BeginVertical();
            EditorGUILayout.LabelField(label, EditorStyles.miniLabel);
            EditorGUILayout.BeginHorizontal();
            value = EditorGUILayout.IntField(value);
            if (GUILayout.Button("↑", GUILayout.Width(22)))
                onBump();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        // Column with the label on top and a read-only field below, plus an optional refresh
        // button. Takes a string so it can show a placeholder (e.g. "?") while fetching.
        static void DrawComputedField(string label, string value, System.Action onRefresh = null)
        {
            EditorGUILayout.BeginVertical();
            EditorGUILayout.LabelField(label, EditorStyles.miniLabel);
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.TextField(value);
            if (onRefresh != null && GUILayout.Button("↻", GUILayout.Width(22)))
                onRefresh();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        static void MarkDirty(VersionConfig cfg)
        {
            EditorUtility.SetDirty(cfg);
            AssetDatabase.SaveAssetIfDirty(cfg);
        }
    }
}
