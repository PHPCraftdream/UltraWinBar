using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;

namespace UltraWinBar.Utilities
{
    class ExePath
    {
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        static extern uint GetModuleFileName(IntPtr hModule, StringBuilder lpFilename, int nSize);
        static readonly int MAX_PATH = 260;

        internal static string GetExecutablePath()
        {
            var sb = new StringBuilder(MAX_PATH);
            GetModuleFileName(IntPtr.Zero, sb, MAX_PATH);
            return sb.ToString();
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern uint GetLongPathName(string shortPath, StringBuilder longPath, uint size);

        static readonly ConcurrentDictionary<string, string> LongPaths = new(StringComparer.Ordinal);
        const int MaxCachedPaths = 512;

        // One file, one spelling: a process can report its image as C:\PROGRA~1\Tool\x.exe or C:\Program Files\Tool\x.exe
        // depending on how its TEMP was expanded. 8.3 names always contain '~', so other paths cost nothing.
        internal static string ToLongPath(string path)
        {
            if (string.IsNullOrEmpty(path) || path.IndexOf('~') < 0) return path;
            if (LongPaths.TryGetValue(path, out string cached)) return cached;
            string expanded = Expand(path);
            // A miss (offline drive) is not remembered, so a later call can still succeed.
            if (expanded != path)
            {
                if (LongPaths.Count >= MaxCachedPaths) LongPaths.Clear();
                LongPaths[path] = expanded;
            }
            return expanded;
        }

        // The file may be gone (a temp extraction): expand the longest existing prefix and keep the rest.
        static string Expand(string path)
        {
            string head = path, rest = "";
            while (!string.IsNullOrEmpty(head))
            {
                string longHead = Query(head);
                if (longHead != null) return rest.Length == 0 ? longHead : longHead.TrimEnd('\\') + "\\" + rest;
                string name = Path.GetFileName(head);
                if (name.Length == 0) break;
                rest = rest.Length == 0 ? name : name + "\\" + rest;
                head = Path.GetDirectoryName(head);
            }
            return path;
        }

        static string Query(string path)
        {
            var sb = new StringBuilder(MAX_PATH);
            uint length = GetLongPathName(path, sb, (uint)sb.Capacity);
            if (length > sb.Capacity)
            {
                sb = new StringBuilder((int)length);
                length = GetLongPathName(path, sb, (uint)sb.Capacity);
            }
            return length == 0 || length > sb.Capacity ? null : sb.ToString();
        }

        // Task identifiers are "exe:<path>" and "pin:exe:<path>"; anything else is returned as is.
        internal static string CanonicalIdentifier(string identifier)
        {
            if (string.IsNullOrEmpty(identifier)) return identifier;
            int prefix = identifier.StartsWith("pin:exe:", StringComparison.Ordinal) ? 8
                : identifier.StartsWith("exe:", StringComparison.Ordinal) ? 4 : -1;
            if (prefix < 0) return identifier;
            string path = identifier.Substring(prefix);
            string expanded = ToLongPath(path);
            return expanded == path ? identifier : identifier.Substring(0, prefix) + expanded;
        }
	}
}
