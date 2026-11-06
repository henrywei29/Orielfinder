using System;
using System.IO;

namespace OrielFolder.Helpers;

/// <summary>Identity shared by the development application and its thumbnail worker.</summary>
public static class AppIdentity
{
    public const string DisplayName = "OrielFolder Dev";
    public const string DataFolderName = "OrielFolderDev";
    public const string SingleInstanceKey = "ORIEL_FOLDER_DEV_MAIN";
    public const string MainExecutableName = "OrielFolderDev.exe";
    public const string WorkerExecutableName = "OrielFolderDev.Thumbs.exe";
    public const string DocumentPreviewWorkerExecutableName = "OrielFolderDev.DocumentPreview.Worker.exe";
    public const bool AllowsDefaultAssociations = false;

    public static string PreviewCacheRoot => Path.Combine(
        Directory.Exists(@"D:\codex") ? @"D:\codex\OrielFolderDev" : LocalDataRoot, "preview-cache");
    public static string MarkedOutputRoot => Path.Combine(
        Directory.Exists(@"D:\codex") ? @"D:\codex\OrielFolderDev" :
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), DataFolderName), "marked-files");

    public static string LocalDataRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), DataFolderName);
}
