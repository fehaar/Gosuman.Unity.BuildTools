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

            // Major / minor
            EditorGUILayout.LabelField("Version", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            cfg.major = EditorGUILayout.IntField("Major", cfg.major);
            cfg.minor = EditorGUILayout.IntField("Minor", cfg.minor);
            if (EditorGUI.EndChangeCheck())
                MarkDirty(cfg);

            // Bump buttons
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Bump Major"))
            {
                Undo.RecordObject(cfg, "Bump Major");
                cfg.major++;
                cfg.minor = 0;
                cfg.releaseNotes = "";
                MarkDirty(cfg);
            }
            if (GUILayout.Button("Bump Minor"))
            {
                Undo.RecordObject(cfg, "Bump Minor");
                cfg.minor++;
                cfg.releaseNotes = "";
                MarkDirty(cfg);
            }
            EditorGUILayout.EndHorizontal();

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
