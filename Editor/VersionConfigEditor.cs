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

        // Word-wrapping text-area style, built lazily (EditorStyles isn't ready at field init).
        static GUIStyle notesStyle;
        static GUIStyle NotesStyle =>
            notesStyle ??= new GUIStyle(EditorStyles.textArea) { wordWrap = true };

        // External release-notes buffer, loaded lazily and reloaded when major/minor changes.
        string notesBuffer = "";
        int loadedMajor = -1;
        int loadedMinor = -1;

        public override void OnInspectorGUI()
        {
            var cfg = (VersionConfig)target;
            serializedObject.Update();
            GUI.changed = false;

            // Version fields in one row: Major / Minor (bumpable) + computed Commit count.
            // Final version is major.minor.commitCount.
            int z = VersionReader.GetCommitCount();

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
            DrawComputedField("Commit", z);
            EditorGUILayout.EndHorizontal();

            if (GUI.changed) MarkDirty(cfg);

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

            // Word-wrapped text area sized to the available width, so long lines wrap
            // instead of widening the window and forcing a horizontal scrollbar.
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

            serializedObject.ApplyModifiedProperties();
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

        // Column with the label on top and a read-only int field below.
        static void DrawComputedField(string label, int value)
        {
            EditorGUILayout.BeginVertical();
            EditorGUILayout.LabelField(label, EditorStyles.miniLabel);
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.IntField(value);
            EditorGUILayout.EndVertical();
        }

        static void MarkDirty(VersionConfig cfg)
        {
            EditorUtility.SetDirty(cfg);
            AssetDatabase.SaveAssetIfDirty(cfg);
        }
    }
}
