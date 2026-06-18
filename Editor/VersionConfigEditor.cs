using System.IO;
using UnityEditor;
using UnityEngine;

namespace Gosuman.BuildTools
{
    [CustomEditor(typeof(VersionConfig))]
    public class VersionConfigEditor : Editor
    {
        const string NotesControlName = "ReleaseNotes";
        bool notesFocusedLastFrame;

        // External release-notes buffer, loaded lazily and reloaded when major/minor changes.
        string notesBuffer = "";
        int loadedMajor = -1;
        int loadedMinor = -1;

        public override void OnInspectorGUI()
        {
            var cfg = (VersionConfig)target;
            serializedObject.Update();
            GUI.changed = false;

            // Major / minor with inline bump buttons
            EditorGUILayout.LabelField("Version", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            cfg.major = EditorGUILayout.IntField("Major", cfg.major);
            if (GUILayout.Button("↑", GUILayout.Width(28)))
            {
                Undo.RecordObject(cfg, "Bump Major");
                cfg.major++;
                cfg.minor = 0;
                MarkDirty(cfg);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            cfg.minor = EditorGUILayout.IntField("Minor", cfg.minor);
            if (GUILayout.Button("↑", GUILayout.Width(28)))
            {
                Undo.RecordObject(cfg, "Bump Minor");
                cfg.minor++;
                MarkDirty(cfg);
            }
            EditorGUILayout.EndHorizontal();

            if (GUI.changed) MarkDirty(cfg);

            EditorGUILayout.Space();

            // Computed fields (read-only)
            EditorGUILayout.LabelField("Computed at build time", EditorStyles.boldLabel);
            int z = VersionReader.GetCommitCount();
            int w = VersionReader.GetRunNumber();
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.IntField("Commit count (z)", z);
                EditorGUILayout.IntField("Run number (w)", w);
                EditorGUILayout.TextField("Full version", $"{cfg.major}.{cfg.minor}.{z}.{w}");
            }

            EditorGUILayout.Space();

            // Release notes folder (external files)
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

            float minHeight = EditorStyles.textArea.lineHeight * 3;
            float contentHeight = EditorStyles.textArea.CalcHeight(
                new GUIContent(notesBuffer),
                EditorGUIUtility.currentViewWidth - 22f);
            float notesHeight = Mathf.Max(minHeight, contentHeight);

            bool notesFocused = GUI.GetNameOfFocusedControl() == NotesControlName;

            GUI.SetNextControlName(NotesControlName);
            notesBuffer = EditorGUILayout.TextArea(notesBuffer, GUILayout.Height(notesHeight));

            // Persist to the external file only when focus leaves the field
            if (notesFocusedLastFrame && !notesFocused)
                VersionReader.WriteReleaseNotes(cfg, notesBuffer);

            notesFocusedLastFrame = notesFocused;

            serializedObject.ApplyModifiedProperties();
        }

        static void MarkDirty(VersionConfig cfg)
        {
            EditorUtility.SetDirty(cfg);
            AssetDatabase.SaveAssetIfDirty(cfg);
        }
    }
}
