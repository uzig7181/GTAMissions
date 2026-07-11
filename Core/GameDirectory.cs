using System.Diagnostics;
using System.IO;

namespace GTAMissions.Core
{
    /// <summary>
    /// Helpers for locating folders under the game's install directory.
    ///
    /// Deliberately does NOT use Assembly.GetExecutingAssembly().Location: SHVDN loads
    /// script DLLs from raw bytes in memory (so you can rebuild without a file lock),
    /// which makes that property return an empty string. Using the running game
    /// process's own executable path instead is reliable regardless of how our DLL
    /// was loaded.
    /// </summary>
    public static class GameDirectory
    {
        public static string Root => Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName) ?? ".";

        public static string Scripts => Path.Combine(Root, "scripts");

        public static string Combine(params string[] relativeParts)
        {
            string path = Root;
            foreach (string part in relativeParts)
            {
                path = Path.Combine(path, part);
            }

            return path;
        }
    }
}
