using System;
using System.IO;

namespace OrielFolder.Helpers;

/// <summary>Identity shared by the development application and its thumbnail worker.</summary>
public static class AppIdentity
{
    public const string DisplayName = "OrielFolder";
    public const string DataFolderName = "OrielFolder";
    public const string SingleInstanceKey = "ORIEL_FOLDER_MAIN";
    public const string MainExecutableName = "OrielFolder.exe";
    public const string WorkerExecutableName = "OrielFolder.Thumbs.exe";
    public const string DocumentPreviewWorkerExecutableName = "OrielFolder.DocumentPreview.Worker.exe";
    public const bool AllowsDefaultAssociations = false;

    public static string PreviewCacheRoot => Path.Combine(
        Directory.Exists(@"D:\codex") ? @"D:\codex\OrielFolder" : LocalDataRoot, "preview-cache");
    public static string MarkedOutputRoot => Path.Combine(
        Directory.Exists(@"D:\codex") ? @"D:\codex\OrielFolder" :
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), DataFolderName), "marked-files");

    public static string LocalDataRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), DataFolderName);
}
