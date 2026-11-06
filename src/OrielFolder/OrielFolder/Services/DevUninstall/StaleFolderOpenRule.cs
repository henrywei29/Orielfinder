using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace OrielFolder.Services.DevUninstall;

/// <summary>
/// Decides whether an HKCU <c>{Folder|Directory|Drive}\shell\open\command</c> override is a leftover of an
/// uninstalled file manager (e.g. the upstream SPAN Finder alias <c>spanfinder.exe</c>). Such an override makes
/// Explorer show "application not found" for every folder. Only the exact shape a file manager writes is
/// eligible; anything else may belong to another app and is preserved.
/// Mirrored in <c>Assets/DevUninstall/Uninstall-OrielFolderDev.ps1</c> (Test-StaleOpenCommand).
/// </summary>
public static class StaleFolderOpenRule
{
    public static IReadOnlyList<string> Classes { get; } = new[] { "Folder", "Directory", "Drive" };

    private static readonly Regex CommandShape = new("^\"(?<path>[^\"]+)\" \"%1\"$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    /// <summary>Executable path of a <c>"&lt;path&gt;" "%1"</c> command, otherwise null.</summary>
    public static string? ParseCommandTarget(string? command)
    {
        if (string.IsNullOrEmpty(command)) return null;
        var match = CommandShape.Match(command);
        return match.Success ? match.Groups["path"].Value : null;
    }

    public static bool IsStale(string? command, string? delegateExecute,
        IReadOnlyCollection<string> valueNames, IReadOnlyCollection<string> subKeyNames,
        Func<string, bool> fileExists)
    {
        var target = ParseCommandTarget(command);
        if (target == null || !Path.IsPathRooted(target)) return false;
        if (!string.IsNullOrEmpty(delegateExecute)) return false;
        if (subKeyNames.Count != 0) return false;
        if (!valueNames.Contains("")) return false;
        if (valueNames.Any(n => n != "" && !n.Equals("DelegateExecute", StringComparison.OrdinalIgnoreCase))) return false;
        return !fileExists(target);
    }
}
