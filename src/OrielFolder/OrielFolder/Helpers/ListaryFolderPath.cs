using System;

namespace OrielFolder.Helpers
{
    public static class ListaryFolderPath
    {
        public const string QueryMessageName = "OrielFolder.Listary.GetCurrentFolder.v1";

        public static string Normalize(string path)
        {
            if (string.IsNullOrEmpty(path) || path.IndexOfAny(new[] { '\0', '\r', '\n', '"', '<', '>', '|' }) >= 0
                || path.IndexOf("::", StringComparison.Ordinal) >= 0) return string.Empty;

            if (path.Length >= 3 && char.IsLetter(path[0]) && path[1] == ':' && (path[2] == '\\' || path[2] == '/')
                && path.IndexOf(':', 2) < 0) return path;

            if (path.StartsWith(@"\\", StringComparison.Ordinal))
            {
                var parts = path.Substring(2).Split('\\');
                if (parts.Length >= 2 && parts[0].Length > 0 && parts[0] != "." && parts[0] != "?"
                    && parts[1].Length > 0 && path.IndexOf(':') < 0) return path;
            }
            return string.Empty;
        }
    }
}
