using UnityEngine;

namespace Gosuman.BuildTools
{
    public class VersionConfig : ScriptableObject
    {
        public int major = 0;
        public int minor = 1;

        [TextArea(4, 20)]
        public string releaseNotes = "";
    }
}
