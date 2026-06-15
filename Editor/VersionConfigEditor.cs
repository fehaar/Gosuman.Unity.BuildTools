using UnityEditor;
using UnityEngine;

namespace Gosuman.BuildTools
{
    [CustomEditor(typeof(VersionConfig))]
    public class VersionConfigEditor : Editor
    {
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
                cfg.releaseNotes = "";
                MarkDirty(cfg);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            cfg.minor = EditorGUILayout.IntField("Minor", cfg.minor);
            if (GUILayout.Button("↑", GUILayout.Width(28)))
            {
                Undo.RecordObject(cfg, "Bump Minor");
                cfg.minor++;
                cfg.releaseNotes = "";
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

            // Release notes
            EditorGUILayout.LabelField($"Release notes for {cfg.major}.{cfg.minor}", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            cfg.releaseNotes = EditorGUILayout.TextArea(cfg.releaseNotes, GUILayout.MinHeight(80));
            if (EditorGUI.EndChangeCheck())
                MarkDirty(cfg);

            serializedObject.ApplyModifiedProperties();
        }

        static void MarkDirty(VersionConfig cfg)
        {
            EditorUtility.SetDirty(cfg);
            AssetDatabase.SaveAssetIfDirty(cfg);
        }
    }
}
