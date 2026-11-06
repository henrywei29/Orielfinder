using System;
using System.Collections.Generic;
using System.IO;

namespace OrielFolder.Helpers;

internal sealed class AppSettings(IDictionary<string, object> values)
{
    internal IDictionary<string, object> Values { get; } = values;
#if ORIEL_STANDALONE
    internal static AppSettings LocalSettings { get; } = new(new DesktopSettingsStore());

    // The development package family for the current user.
    private const string LegacyFamily = "OrielFolder.Desktop_zd9dpwnktq3x8";
    private const string MigrationKey = "OrielDesktopMigrationCompleted";

    internal static void MigrateLegacyData()
    {
        var destination = LocalSettings.Values;
        if (destination.ContainsKey(MigrationKey)) return;

        var packageDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages", LegacyFamily);
        if (!Directory.Exists(packageDirectory)) return;

        var source = Windows.Management.Core.ApplicationDataManager.CreateForPackageFamily(LegacyFamily);
        foreach (var item in source.LocalSettings.Values)
        {
            if (item.Value is not null && !destination.ContainsKey(item.Key))
            {
                try { destination.Add(item.Key, item.Value); }
                catch (NotSupportedException) { DebugLogger.Log("[Migration] Skipped an unsupported legacy setting type."); }
            }
        }

        // Copy only application state. The previous installation keeps its own data.
        foreach (var directory in new[] { Path.Combine(packageDirectory, "LocalCache", "Local", AppIdentity.DisplayName), source.LocalFolder.Path })
        {
            foreach (var name in new[] { "workspaces.json", "folder-tags.json", "action_log.json", "connections.json", "connections.json.bak", "credentials.dat" })
            {
                var from = Path.Combine(directory, name);
                var to = Path.Combine(AppIdentity.LocalDataRoot, name);
                if (!File.Exists(from) || File.Exists(to)) continue;
                Directory.CreateDirectory(AppIdentity.LocalDataRoot);
                File.Copy(from, to, overwrite: false);
            }
        }
        destination[MigrationKey] = true;
    }
#else
    internal static AppSettings LocalSettings => new(Windows.Storage.ApplicationData.Current.LocalSettings.Values);
#endif
}
