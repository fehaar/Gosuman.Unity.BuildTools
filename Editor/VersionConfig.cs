using UnityEngine;

namespace Gosuman.BuildTools
{
    public class VersionConfig : ScriptableObject
    {
        public int major = 1;
        public int minor = 0;

        [TextArea(4, 20)]
        public string releaseNotes = "";
    }
}
