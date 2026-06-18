using UnityEngine;

namespace Gosuman.BuildTools
{
    public class VersionConfig : ScriptableObject
    {
        public int major = 1;
        public int minor = 0;

        // Folder holding the external release-notes files (<major.minor>.md + Template.md),
        // resolved relative to the Unity project directory. The default matches the Gosuman
        // repo layout where the Unity project lives at Code/<Project>.Unity and Docs/ sits at
        // the repo root.
        public string releaseNotesFolder = "../../Docs/ReleaseNotes";
    }
}
