using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Span.Models;

namespace Span.Services;

/// <summary>Portable preferences only: no credentials, cache or session history.</summary>
public static class ConfigurationBackupService
{
    public sealed class Backup
    {
        public string Format { get; set; } = "";
        public int Version { get; set; }
        public Dictionary<string, JsonElement> Preferences { get; set; } = new();
        public List<WorkspaceDto> Workspaces { get; set; } = new();
    }

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true
    };
    public static IReadOnlyDictionary<string, Type> PreferenceTypes { get; } = BuildPreferenceTypes();

    private static Dictionary<string, Type> BuildPreferenceTypes()
    {
        var types = new Dictionary<string, Type>(StringComparer.Ordinal);
        void Add(Type type, string keys) { foreach (string key in keys.Split(' ', StringSplitOptions.RemoveEmptyEntries)) types.Add(key, type); }
        Add(typeof(bool), "UseCustomAccent FolderCustomIconsEnabled FolderTagsEnabled ColorLabelsExpanded SkipOfficeTemporaryFilesDuringCopy AnimationsEnabled ShowHiddenFiles ShowFileExtensions ShowCheckboxes ShowThumbnails UseIsolatedThumbnails EnableQuickLook EnableWasdNavigation ConfirmDelete PreviewShowFolderInfo ShowContextMenu MinimizeToTray RememberWindowPosition ShowFavoritesTree SidebarShowHome SidebarShowFavorites SidebarShowLocalDrives SidebarShowCloud SidebarShowNetwork SidebarShowRecycleBin ShowDeveloperMenu ShowGitIntegration ShowHexPreview ShowFileHash EnableCrashReporting ShowShellExtensions ShowWindowsShellExtras ShowCopilotMenu ExecutableIconsEnabled AutoFitColumnWidth DefaultPreviewEnabled ShelfSaveEnabled ShelfEnabled ListShowSize ListShowDate OnboardingDisabled MillerSortAsc");
        Add(typeof(string), "ProductIcon Theme MillerColumnWidthsJson CustomAccentColor Density FontFamily IconPack IconFontScale MillerClickBehavior DefaultTerminal Tab1StartupPath Tab2StartupPath Language KeyBindingsJson FavoritesOrderJson MillerSortBy ViewGroupBy ExplicitViewGroupBy PreviewLabelPresetsJson PreviewToolbarOrderJson");
        Add(typeof(int), "UndoHistorySize StartupBehavior DefaultViewMode LastUsedFolderViewMode MillerFolderOpenAlignment MultiPaneLayoutMode MultiPaneTileArrangement Tab1StartupBehavior Tab2StartupBehavior Tab1StartupViewMode Tab2StartupViewMode ListColumnWidth PreviewZoomModifier ZipLegacyCodePage");
        Add(typeof(double), "CustomSidebarWidth");
        return types;
    }

    public static string Export(IDictionary<string, object> settings, List<WorkspaceDto> workspaces)
    {
        var backup = new Backup { Format = "OrielFolder Configuration", Version = 1, Workspaces = workspaces };
        foreach (var entry in settings)
            if (PreferenceTypes.TryGetValue(entry.Key, out var type) && entry.Value?.GetType() == type)
                backup.Preferences.Add(entry.Key, JsonSerializer.SerializeToElement(entry.Value, type));
        Validate(backup);
        return JsonSerializer.Serialize(backup, JsonOptions);
    }

    public static Backup Parse(string json)
    {
        try
        {
            var backup = JsonSerializer.Deserialize<Backup>(json, JsonOptions);
            Validate(backup);
            return backup!;
        }
        catch (JsonException ex) { throw new InvalidDataException("Invalid configuration backup.", ex); }
    }

    public static async Task WriteFileAsync(string path, string json)
    {
        Parse(json);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        await File.WriteAllTextAsync(temporary, json);
        File.Move(temporary, path, overwrite: true);
    }

    private static void Validate(Backup? backup)
    {
        if (backup == null || (backup.Format != "OrielFolder Configuration" && backup.Format != "SPAN Dev Configuration") || backup.Version != 1
            || backup.Preferences == null || backup.Workspaces == null)
            throw new InvalidDataException("Unsupported configuration backup.");
        foreach (var entry in backup.Preferences)
        {
            if (!PreferenceTypes.TryGetValue(entry.Key, out var type)) throw new InvalidDataException("Unsupported preference.");
            object value = ReadValue(entry.Value, type);
            if (value is int number)
            {
                if (number < 0 || number > 100000) throw new InvalidDataException("Invalid numeric preference.");
                if (entry.Key == "PreviewZoomModifier" && number > 2) throw new InvalidDataException("Invalid preview zoom modifier.");
                if (entry.Key == "ZipLegacyCodePage" && number is not (0 or 949 or 936 or 950 or 932 or 1252))
                    throw new InvalidDataException("Invalid ZIP filename encoding.");
                if (entry.Key is "MillerFolderOpenAlignment" or "MultiPaneLayoutMode" or "MultiPaneTileArrangement"
                    or "StartupBehavior" or "Tab1StartupBehavior" or "Tab2StartupBehavior")
                    if (number > 2) throw new InvalidDataException("Invalid layout preference.");
                if (entry.Key == "DefaultViewMode" && number > 4) throw new InvalidDataException("Invalid default view choice.");
                if (entry.Key == "LastUsedFolderViewMode" && number is not (0 or 1 or 2 or 3 or 4 or 5 or 8))
                    throw new InvalidDataException("Invalid last folder view.");
                if (entry.Key.EndsWith("ViewMode", StringComparison.Ordinal) || entry.Key is "ViewMode" or "IconSize")
                    if (!Enum.IsDefined(typeof(ViewMode), number)) throw new InvalidDataException("Invalid view mode.");
            }
            if (value is double width && (!double.IsFinite(width) || width < 0 || width > 100000))
                throw new InvalidDataException("Invalid width preference.");
            if (value is string text)
            {
                if (entry.Key == PreviewLabelPresets.SettingsKey) PreviewLabelPresets.Parse(text);
                if (entry.Key == PreviewToolbarOrder.SettingsKey) PreviewToolbarOrder.Parse(text);
                if (entry.Key.EndsWith("Path", StringComparison.Ordinal)) ValidatePath(text);
                if (entry.Key is "KeyBindingsJson" or "FavoritesOrderJson" && text.Length > 0)
                {
                    try
                    {
                        using var document = JsonDocument.Parse(text);
                        if (entry.Key == "FavoritesOrderJson")
                        {
                            if (document.RootElement.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Invalid favorites order.");
                            foreach (var path in document.RootElement.EnumerateArray()) ValidatePath(path.GetString() ?? throw new InvalidDataException("Invalid favorite path."));
                        }
                        else ValidateShortcuts(document.RootElement);
                    }
                    catch (JsonException ex) { throw new InvalidDataException("Invalid preference data.", ex); }
                    catch (InvalidOperationException ex) { throw new InvalidDataException("Invalid preference data.", ex); }
                }
            }
        }
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var workspace in backup.Workspaces)
        {
            if (workspace == null || string.IsNullOrWhiteSpace(workspace.Id) || workspace.Id == "_autosave" || !ids.Add(workspace.Id)
                || string.IsNullOrWhiteSpace(workspace.Name) || workspace.Tabs == null || workspace.Tabs.Count == 0
                || workspace.ActiveTabIndex < 0 || workspace.ActiveTabIndex >= workspace.Tabs.Count)
                throw new InvalidDataException("Invalid saved workspace.");
            var tabIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var tab in workspace.Tabs)
            {
                if (tab == null || string.IsNullOrWhiteSpace(tab.Id) || !tabIds.Add(tab.Id) || tab.Path == null || tab.Header == null
                    || !Enum.IsDefined(typeof(ViewMode), tab.ViewMode) || !Enum.IsDefined(typeof(ViewMode), tab.IconSize))
                    throw new InvalidDataException("Invalid workspace tab.");
                ValidatePath(tab.Path);
            }
        }
    }

    private static void ValidatePath(string path)
    {
        if (Uri.TryCreate(path, UriKind.Absolute, out var uri) && uri.UserInfo.Contains(':'))
            throw new InvalidDataException("Paths containing embedded credentials cannot be backed up.");
    }

    private static void ValidateShortcuts(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("version", out var version)
            || !version.TryGetInt32(out int number) || number != 1
            || !root.TryGetProperty("overrides", out var overrides) || overrides.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Invalid shortcuts.");
        foreach (var entry in overrides.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object || !entry.TryGetProperty("command", out var command)
                || command.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(command.GetString())
                || !entry.TryGetProperty("keys", out var keys) || keys.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("Invalid shortcut entry.");
            foreach (var key in keys.EnumerateArray())
                if (key.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(key.GetString()))
                    throw new InvalidDataException("Invalid shortcut key.");
        }
    }

    private static object ReadValue(JsonElement element, Type type)
    {
        try
        {
            if (type == typeof(string)) return element.GetString() ?? throw new InvalidDataException("Null preference.");
            if (type == typeof(bool)) return element.GetBoolean();
            if (type == typeof(int)) return element.GetInt32();
            return element.GetDouble();
        }
        catch (InvalidOperationException ex) { throw new InvalidDataException("Incorrect preference type.", ex); }
        catch (FormatException ex) { throw new InvalidDataException("Incorrect preference value.", ex); }
    }

    public static async Task ApplyAsync(Backup backup, IDictionary<string, object> settings,
        Func<List<WorkspaceDto>, Task> saveWorkspaces)
    {
        Validate(backup); // Validate the entire file before the first write.
        var originals = new Dictionary<string, object>();
        var written = new List<string>();
        try
        {
            foreach (var entry in backup.Preferences)
            {
                if (settings.TryGetValue(entry.Key, out var original)) originals[entry.Key] = original;
                written.Add(entry.Key);
                settings[entry.Key] = ReadValue(entry.Value, PreferenceTypes[entry.Key]);
            }
            if (backup.Workspaces.Count > 0) await saveWorkspaces(backup.Workspaces);
        }
        catch
        {
            foreach (string key in written.AsEnumerable().Reverse())
                if (originals.TryGetValue(key, out var original)) settings[key] = original;
                else settings.Remove(key);
            throw;
        }
    }
}
