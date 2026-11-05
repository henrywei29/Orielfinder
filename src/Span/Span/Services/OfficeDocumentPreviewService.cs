using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

namespace Span.Services;

public enum DocumentPreviewStage { Copying, Checking, Queued, Converting, Ready, OpeningDocument, Exporting }

public sealed record DocumentPreviewProgress(DocumentPreviewStage Stage, long BytesCopied = 0, long TotalBytes = 0, long OutputBytes = 0);

public sealed record DocumentPreviewResult(string? PdfPath, string Text, string Limitation) : IDisposable
{
    private Action? _releaseStaging;

    internal DocumentPreviewResult(string pdfPath, Action releaseStaging) : this(pdfPath, "", "") =>
        _releaseStaging = releaseStaging;

    public void Dispose() => Interlocked.Exchange(ref _releaseStaging, null)?.Invoke();
}

public sealed class OfficeDocumentPreviewService
{
    private static readonly SemaphoreSlim OfficeConversion = new(1, 1);
    private readonly string _cacheRoot;
    private readonly string _workerPath;
    private const int TextLimit = 1_000_000;
    private const long MaximumSourceBytes = 100L * 1024 * 1024;

    public OfficeDocumentPreviewService(string? cacheRoot = null, string? workerPath = null)
    {
        _cacheRoot = cacheRoot ?? Helpers.AppIdentity.PreviewCacheRoot;
        _workerPath = workerPath ?? Path.Combine(AppContext.BaseDirectory, "OrielFolder.DocumentPreview.Worker.exe");
    }

    private readonly record struct SessionKey(string Source, long Length, long Written, string Root, string Worker);
    private sealed class SessionEntry(SessionKey key)
    {
        internal readonly SessionKey Key = key;
        internal readonly CancellationTokenSource Cancellation = new();
        internal Task<DocumentPreviewResult> Work = null!;
        internal int Users;
        internal long Used;
        internal bool Retired;
        internal bool Converting;
        internal DocumentPreviewProgress? Progress;
        internal long ProgressRevision;
        internal readonly List<ProgressObserver> Observers = new();
    }

    private sealed class ProgressObserver(IProgress<DocumentPreviewProgress> progress) : IDisposable
    {
        private readonly object _sync = new();
        private long _revision;
        private bool _disposed;

        internal void Report(long revision, DocumentPreviewProgress value)
        {
            lock (_sync)
            {
                if (_disposed || revision <= _revision) return;
                _revision = revision;
                progress.Report(value);
            }
        }

        public void Dispose() { lock (_sync) _disposed = true; }
    }

    private static void PublishProgress(SessionEntry entry, DocumentPreviewProgress value)
    {
        ProgressObserver[] observers;
        long revision;
        lock (SessionSync)
        {
            entry.Progress = value;
            revision = ++entry.ProgressRevision;
            observers = entry.Observers.ToArray();
        }
        foreach (var observer in observers) observer.Report(revision, value);
    }
    private static readonly object SessionSync = new();
    private static readonly Dictionary<SessionKey, SessionEntry> SessionCache = new();
    private static long _sessionUse;

    static OfficeDocumentPreviewService()
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            lock (SessionSync)
                foreach (var entry in SessionCache.Values)
                    if (entry.Work.IsCompletedSuccessfully) entry.Work.Result.Dispose();
        };
    }

    /// <summary>Reuse only this process's private snapshots; live previews hold independent leases.</summary>
    public async Task<DocumentPreviewResult> PrepareCachedAsync(string sourcePath, CancellationToken cancellationToken,
        IProgress<DocumentPreviewProgress>? progress = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var info = new FileInfo(Path.GetFullPath(sourcePath));
        var key = new SessionKey(info.FullName.ToUpperInvariant(), info.Length, info.LastWriteTimeUtc.Ticks,
            Path.GetFullPath(_cacheRoot).ToUpperInvariant(), Path.GetFullPath(_workerPath).ToUpperInvariant());
        SessionEntry entry;
        using var observer = progress == null ? null : new ProgressObserver(progress);
        DocumentPreviewProgress? latest;
        long revision;
        lock (SessionSync)
        {
            if (SessionCache.TryGetValue(key, out var cached) && cached.Work.IsCompleted &&
                (!cached.Work.IsCompletedSuccessfully || !File.Exists(cached.Work.Result.PdfPath))) Retire(cached);
            if (!SessionCache.TryGetValue(key, out entry!))
            {
                entry = new SessionEntry(key);
                SessionCache.Add(key, entry);
                var started = entry;
                entry.Work = Task.Run(() => PrepareCoreAsync(sourcePath, started.Cancellation.Token, () =>
                {
                    lock (SessionSync)
                    {
                        started.Cancellation.Token.ThrowIfCancellationRequested();
                        started.Converting = true;
                    }
                }, value => PublishProgress(started, value)));
                _ = entry.Work.ContinueWith(_ =>
                {
                    lock (SessionSync)
                    {
                        var current = new FileInfo(sourcePath);
                        bool unchanged;
                        try { unchanged = current.Exists && current.Length == key.Length && current.LastWriteTimeUtc.Ticks == key.Written; }
                        catch (IOException) { unchanged = false; }
                        catch (UnauthorizedAccessException) { unchanged = false; }
                        if (!unchanged || !started.Work.IsCompletedSuccessfully || started.Work.Result.PdfPath == null) Retire(started);
                        if (started.Retired && started.Users == 0) DisposeEntry(started);
                        TrimSessionCache();
                    }
                }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
            }
            entry.Users++;
            entry.Used = ++_sessionUse;
            if (observer != null) entry.Observers.Add(observer);
            latest = entry.Progress;
            revision = entry.ProgressRevision;
        }
        try
        {
            if (latest != null) observer?.Report(revision, latest);
            var result = await entry.Work.WaitAsync(cancellationToken);
            if (result.PdfPath != null) return new DocumentPreviewResult(result.PdfPath, () => Release(entry));
            Release(entry);
            return new(null, result.Text, result.Limitation);
        }
        catch { Release(entry); throw; }
        finally
        {
            observer?.Dispose();
            if (observer != null) lock (SessionSync) entry.Observers.Remove(observer);
        }
    }

    private static void Release(SessionEntry entry)
    {
        lock (SessionSync)
        {
            entry.Users--;
            entry.Used = ++_sessionUse;
            if (entry.Users == 0)
            {
                if (!entry.Work.IsCompleted)
                {
                    // Queued work can stop; Office already running must finish its private
                    // conversion/cleanup and keep the result for a view reopened meanwhile.
                    if (!entry.Converting) { entry.Cancellation.Cancel(); Retire(entry); }
                }
                else if (!entry.Work.IsCompletedSuccessfully || entry.Work.Result.PdfPath == null) Retire(entry);
                if (entry.Retired) DisposeEntry(entry);
            }
            TrimSessionCache();
        }
    }

    private static void Retire(SessionEntry entry)
    {
        entry.Retired = true;
        if (SessionCache.TryGetValue(entry.Key, out var current) && ReferenceEquals(current, entry)) SessionCache.Remove(entry.Key);
        if (entry.Users == 0 && entry.Work.IsCompleted) DisposeEntry(entry);
    }

    private static void DisposeEntry(SessionEntry entry)
    {
        if (entry.Work.IsCompletedSuccessfully) entry.Work.Result.Dispose();
        if (entry.Work.IsCompleted) entry.Cancellation.Dispose();
    }

    private static void TrimSessionCache()
    {
        // Keep at most four idle snapshots. Never evict a live view or an in-flight conversion.
        var idle = SessionCache.Values.Where(e => e.Users == 0 && e.Work.IsCompleted).OrderByDescending(e => e.Used).ToArray();
        foreach (var entry in idle.Skip(4)) Retire(entry);
    }

    public Task<DocumentPreviewResult> PrepareAsync(string sourcePath, CancellationToken cancellationToken,
        IProgress<DocumentPreviewProgress>? progress = null) =>
        PrepareCoreAsync(sourcePath, cancellationToken, null, progress == null ? null : progress.Report);

    private async Task<DocumentPreviewResult> PrepareCoreAsync(string sourcePath, CancellationToken cancellationToken,
        Action? conversionStarting, Action<DocumentPreviewProgress>? progress)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string extension = Path.GetExtension(sourcePath).ToLowerInvariant();
        if (extension is not (".pdf" or ".doc" or ".docx" or ".xls" or ".xlsx" or ".ppt" or ".pptx"))
            throw new NotSupportedException("Unsupported document type.");
        if (new FileInfo(sourcePath).Length > MaximumSourceBytes)
            throw new IOException("文档超过 100 MB 预览大小限制。");

        string cacheRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(_cacheRoot));
        EnsureDirectoryNotRedirected(cacheRoot);
        string folder = Path.Combine(cacheRoot, Guid.NewGuid().ToString("N"));
        if (Directory.Exists(folder) || File.Exists(folder)) throw new IOException("Preview staging directory already exists.");
        Directory.CreateDirectory(folder);
        bool retained = false;
        Task<bool>? conversion = null;
        void ReleaseStaging()
        {
            // A complete PDF may be displayed while our worker closes Office. Keep
            // its private source and output alive until that cleanup has finished.
            if (conversion == null || conversion.IsCompleted)
            {
                _ = conversion?.Exception;
                CleanupStaging(cacheRoot, folder, extension);
            }
            else _ = conversion.ContinueWith(task =>
            {
                _ = task.Exception;
                CleanupStaging(cacheRoot, folder, extension);
            }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        }
        DocumentPreviewResult RetainPdf(string path)
        {
            var result = new DocumentPreviewResult(path, ReleaseStaging);
            retained = true;
            progress?.Invoke(new(DocumentPreviewStage.Ready));
            return result;
        }

        try
        {
            EnsureDirectoryNotRedirected(folder);
            string staged = Path.Combine(folder, "source" + extension);
            // Share the original with its editor. Office only receives our private copy.
            await using (var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 81920, true))
            await using (var output = new FileStream(staged, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                byte[] buffer = new byte[81920];
                long total = 0;
                long sourceLength = input.Length;
                var progressWatch = Stopwatch.StartNew();
                progress?.Invoke(new(DocumentPreviewStage.Copying, 0, sourceLength));
                int count;
                while ((count = await input.ReadAsync(buffer, cancellationToken)) != 0)
                {
                    total += count;
                    if (total > MaximumSourceBytes) throw new IOException("文档超过 100 MB 预览大小限制。");
                    await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                    if (progressWatch.ElapsedMilliseconds >= 100)
                    {
                        progress?.Invoke(new(DocumentPreviewStage.Copying, total, Math.Max(total, sourceLength)));
                        progressWatch.Restart();
                    }
                }
                progress?.Invoke(new(DocumentPreviewStage.Copying, total, total));
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (extension == ".pdf") return RetainPdf(staged);

            progress?.Invoke(new(DocumentPreviewStage.Checking));
            bool modern = extension is ".docx" or ".xlsx" or ".pptx";
            string limitation = "Office 版式预览暂不可用。请确认已安装对应 Office 应用，或将文档副本另存为 PDF 后预览。";
            // Only inert hyperlink metadata may be removed from our private copy.
            // Linked images/templates/workbooks and unknown uses remain blocked.
            bool external = modern && !await Task.Run(() => RemoveInertHyperlinks(staged), cancellationToken);
            if (external)
                limitation = "文档包含外部资源或无法安全处理的链接，版式预览暂不可用。请将文档副本另存为 PDF 后预览。";
            else if (File.Exists(_workerPath))
            {
                string pdfPath = Path.Combine(folder, "preview.pdf");
                // The uncached API retains its original wait-for-exit contract.
                var published = conversionStarting == null ? null : new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                progress?.Invoke(new(DocumentPreviewStage.Queued));
                conversion = ConvertAsync(staged, pdfPath, folder, cancellationToken, conversionStarting, published, progress);
                bool converted = published == null ? await conversion : await published.Task.WaitAsync(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (converted) return RetainPdf(pdfPath);
            }

            if (!modern)
                return new(null, "", "此旧版 Office 文档暂时无法安全转换。请关闭对应 Office 应用后重试，或将副本另存为 DOCX/XLSX/PPTX。");

            cancellationToken.ThrowIfCancellationRequested();
            return new(null, "", limitation);
        }
        finally
        {
            if (!retained) ReleaseStaging();
        }
    }

    private static void EnsureDirectoryNotRedirected(string path)
    {
        for (var directory = new DirectoryInfo(path); directory != null; directory = directory.Parent)
            if (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Preview staging cannot use a redirected directory.");
    }

    private static void CleanupStaging(string cacheRoot, string folder, string extension)
    {
        try
        {
            // Only this request's direct GUID child is eligible. Never sweep older requests.
            string fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
            if (!string.Equals(Path.GetDirectoryName(fullPath), cacheRoot, StringComparison.OrdinalIgnoreCase) ||
                !Guid.TryParseExact(Path.GetFileName(fullPath), "N", out _) || !Directory.Exists(fullPath)) return;
            EnsureDirectoryNotRedirected(fullPath);
            var entries = Directory.GetFileSystemEntries(fullPath);
            foreach (string entry in entries)
            {
                var attributes = File.GetAttributes(entry);
                string name = Path.GetFileName(entry);
                if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0 ||
                    (name != "source" + extension && name is not ("preview.pdf" or "preview.pending.pdf" or "office-owner.json" or "office-owner.json.pending"))) return;
            }
            // Leaf-only deletion cannot traverse an unfamiliar child directory or a junction.
            foreach (string entry in entries)
            {
                EnsureDirectoryNotRedirected(fullPath);
                if ((File.GetAttributes(entry) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0) return;
                File.Delete(entry);
            }
            EnsureDirectoryNotRedirected(fullPath);
            Directory.Delete(fullPath, recursive: false);
        }
        catch (IOException) { } // A document still held by Office is retained rather than forcing its process closed.
        catch (UnauthorizedAccessException) { }
    }

    private async Task<bool> ConvertAsync(string staged, string output, string folder, CancellationToken cancellationToken,
        Action? conversionStarting, TaskCompletionSource<bool>? published, Action<DocumentPreviewProgress>? progress)
    {
        bool acquired = false;
        try
        {
            await OfficeConversion.WaitAsync(cancellationToken);
            acquired = true;
            cancellationToken.ThrowIfCancellationRequested();
            conversionStarting?.Invoke();
            progress?.Invoke(new(DocumentPreviewStage.Converting));
            string ownerFile = Path.Combine(folder, "office-owner.json");
            var start = new ProcessStartInfo(_workerPath)
            { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = folder, RedirectStandardOutput = true };
            start.ArgumentList.Add(staged);
            start.ArgumentList.Add(output);
            start.ArgumentList.Add(ownerFile);
            using var worker = new Process { StartInfo = start };
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            using var reading = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
            Task stdout = Task.CompletedTask;
            var signals = new ConcurrentQueue<DocumentPreviewStage>();
            var currentStage = DocumentPreviewStage.Converting;
            bool pdfPublished = false;
            long outputBytes = 0;
            var outputWatch = Stopwatch.StartNew();
            void ReportWorkerProgress()
            {
                while (signals.TryDequeue(out var stage))
                {
                    if (pdfPublished) continue;
                    currentStage = stage;
                    progress?.Invoke(new(stage));
                }
                if (pdfPublished || currentStage != DocumentPreviewStage.Exporting || outputWatch.ElapsedMilliseconds < 100) return;
                outputWatch.Restart();
                try
                {
                    var pending = new FileInfo(Path.Combine(folder, "preview.pending.pdf"));
                    if (pending.Exists && pending.Length > 0 && pending.Length != outputBytes)
                    {
                        outputBytes = pending.Length;
                        progress?.Invoke(new(currentStage, OutputBytes: outputBytes));
                    }
                }
                catch (IOException) { } // Export may rename or exclusively hold the pending file.
                catch (UnauthorizedAccessException) { }
            }
            try
            {
                if (!worker.Start()) return false;
                stdout = ReadWorkerProgressAsync(worker.StandardOutput, signals, reading.Token);
                // Once Office starts, let the worker close its private document and release
                // COM even if selection changes. Only the conversion timeout may stop it.
                var exit = worker.WaitForExitAsync(timeout.Token);
                while (!exit.IsCompleted)
                {
                    ReportWorkerProgress();
                    // Production publishes this name atomically after export returns.
                    if (!pdfPublished && File.Exists(output) && new FileInfo(output).Length > 5)
                    {
                        pdfPublished = true;
                        published?.TrySetResult(true);
                    }
                    await Task.WhenAny(exit, Task.Delay(25, timeout.Token));
                    timeout.Token.ThrowIfCancellationRequested();
                }
                await exit;
                // Office may inherit the pipe. Its lifetime must not delay releasing the
                // conversion slot after our worker has exited and completed cleanup.
                reading.Cancel();
                try { await stdout; }
                catch (OperationCanceledException) { }
                ReportWorkerProgress();
                bool converted = worker.ExitCode == 0 && File.Exists(output) && new FileInfo(output).Length > 5;
                published?.TrySetResult(converted);
                return converted;
            }
            catch (OperationCanceledException)
            {
                await StopWorkerAsync(worker);
                cancellationToken.ThrowIfCancellationRequested();
                return false;
            }
            catch (System.ComponentModel.Win32Exception) { return false; }
            finally
            {
                reading.Cancel();
                try { await stdout; }
                catch (OperationCanceledException) { }
            }
        }
        finally
        {
            if (acquired) OfficeConversion.Release();
            published?.TrySetResult(false);
        }
    }

    private static async Task ReadWorkerProgressAsync(StreamReader reader, ConcurrentQueue<DocumentPreviewStage> signals,
        CancellationToken cancellationToken)
    {
        // A worker must emit the two exact lines in order. Bound malformed lines while
        // draining the pipe so stdout can never block Office export or retain content.
        char[] buffer = new char[256], line = new char[64];
        int length = 0, expected = 0;
        bool oversized = false;
        try
        {
            int count;
            while ((count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken)) != 0)
                for (int index = 0; index < count; index++)
                {
                    char value = buffer[index];
                    if (value == '\n')
                    {
                        int end = length > 0 && line[length - 1] == '\r' ? length - 1 : length;
                        if (!oversized && expected < 2)
                        {
                            string token = new(line, 0, end);
                            if (expected == 0 && token == "SPAN_PREVIEW:OPENING_DOCUMENT")
                            { signals.Enqueue(DocumentPreviewStage.OpeningDocument); expected++; }
                            else if (expected == 1 && token == "SPAN_PREVIEW:EXPORTING")
                            { signals.Enqueue(DocumentPreviewStage.Exporting); expected++; }
                        }
                        length = 0;
                        oversized = false;
                    }
                    else if (length < line.Length) line[length++] = value;
                    else oversized = true;
                }
        }
        catch (IOException) { } // Worker exit can close the pipe before its final read.
    }

    private static async Task StopWorkerAsync(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill();
            await process.WaitForExitAsync();
        }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
    }

    public static bool HasExternalRelationships(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        foreach (var entry in zip.Entries.Where(e => e.FullName.EndsWith(".rels", StringComparison.OrdinalIgnoreCase)))
            if (ReadXml(entry).Descendants().Any(e => string.Equals((string?)e.Attribute("TargetMode"), "External", StringComparison.OrdinalIgnoreCase)))
                return true;
        return false;
    }

    private static bool RemoveInertHyperlinks(string staged)
    {
        const string transitional = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        const string strict = "http://purl.oclc.org/ooxml/officeDocument/relationships";
        const string package = "http://schemas.openxmlformats.org/package/2006/relationships";
        using var zip = ZipFile.Open(staged, ZipArchiveMode.Update);
        var changes = new Dictionary<ZipArchiveEntry, XDocument>();
        foreach (var entry in zip.Entries.Where(e => e.FullName.EndsWith(".rels", StringComparison.OrdinalIgnoreCase)))
        {
            var relationships = ReadXml(entry);
            foreach (var relationship in relationships.Descendants().Where(e =>
                string.Equals((string?)e.Attribute("TargetMode"), "External", StringComparison.OrdinalIgnoreCase)).ToArray())
            {
                string? type = (string?)relationship.Attribute("Type");
                string? id = (string?)relationship.Attribute("Id");
                if (relationship.Name != XName.Get("Relationship", package) || string.IsNullOrEmpty(id) ||
                    (type != transitional + "/hyperlink" && type != strict + "/hyperlink")) return false;
                if (relationships.Descendants().Count(e => (string?)e.Attribute("Id") == id) != 1) return false;
                int separator = entry.FullName.LastIndexOf("/_rels/", StringComparison.Ordinal);
                if (separator < 0) return false;
                string ownerName = entry.FullName[..(separator + 1)] + entry.FullName[(separator + 7)..^5];
                var owner = zip.GetEntry(ownerName);
                if (owner == null) return false;
                if (!changes.TryGetValue(owner, out var document)) document = ReadXml(owner);
                var references = document.Descendants().Attributes().Where(a =>
                    (a.Name.NamespaceName == transitional || a.Name.NamespaceName == strict) && a.Value == id).ToArray();
                foreach (var reference in references)
                {
                    var element = reference.Parent!;
                    string ns = element.Name.NamespaceName;
                    bool word = element.Name.LocalName == "hyperlink" &&
                        (ns == "http://schemas.openxmlformats.org/wordprocessingml/2006/main" || ns == "http://purl.oclc.org/ooxml/wordprocessingml/main");
                    bool sheet = element.Name.LocalName == "hyperlink" &&
                        (ns == "http://schemas.openxmlformats.org/spreadsheetml/2006/main" || ns == "http://purl.oclc.org/ooxml/spreadsheetml/main");
                    bool drawing = element.Name.LocalName is "hlinkClick" or "hlinkMouseOver" &&
                        (ns == "http://schemas.openxmlformats.org/drawingml/2006/main" || ns == "http://purl.oclc.org/ooxml/drawingml/main");
                    if (reference.Name.LocalName != "id" || !(word || sheet || drawing)) return false;
                    if (word) element.ReplaceWith(element.Nodes()); // Keep the visible runs and their formatting.
                    else element.Remove(); // Cell contents and drawing text are stored separately.
                }
                changes[owner] = document;
                relationship.Remove();
                changes[entry] = relationships;
            }
        }
        foreach (var change in changes)
        {
            using var stream = change.Key.Open();
            stream.SetLength(0);
            change.Value.Save(stream, SaveOptions.DisableFormatting);
        }
        return true;
    }

    private static XDocument ReadXml(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 16_000_000
        });
        return XDocument.Load(reader, LoadOptions.PreserveWhitespace);
    }

    public static string ExtractOpenXmlText(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        var text = new StringBuilder();
        string extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension == ".docx")
        {
            var entry = zip.GetEntry("word/document.xml");
            if (entry != null)
                foreach (var paragraph in ReadXml(entry).Descendants().Where(e => e.Name.LocalName == "p"))
                {
                    text.AppendLine(string.Concat(paragraph.Descendants().Where(e => e.Name.LocalName == "t").Select(e => e.Value)));
                    if (text.Length >= TextLimit) break;
                }
        }
        else if (extension == ".pptx")
        {
            foreach (var slide in zip.Entries.Where(e => System.Text.RegularExpressions.Regex.IsMatch(e.FullName, @"^ppt/slides/slide\d+\.xml$"))
                .OrderBy(e => int.Parse(System.Text.RegularExpressions.Regex.Match(e.FullName, @"\d+").Value)))
            {
                text.AppendLine($"— {Path.GetFileNameWithoutExtension(slide.FullName)} —");
                foreach (var paragraph in ReadXml(slide).Descendants().Where(e => e.Name.LocalName == "p"))
                    text.AppendLine(string.Concat(paragraph.Descendants().Where(e => e.Name.LocalName == "t").Select(e => e.Value)));
                if (text.Length >= TextLimit) break;
            }
        }
        else if (extension == ".xlsx")
        {
            var sharedEntry = zip.GetEntry("xl/sharedStrings.xml");
            string[] shared = sharedEntry == null ? Array.Empty<string>() : ReadXml(sharedEntry).Descendants()
                .Where(e => e.Name.LocalName == "si")
                .Select(e => string.Concat(e.Descendants().Where(t => t.Name.LocalName == "t").Select(t => t.Value))).ToArray();
            foreach (var sheet in zip.Entries.Where(e => System.Text.RegularExpressions.Regex.IsMatch(e.FullName, @"^xl/worksheets/sheet\d+\.xml$"))
                .OrderBy(e => int.Parse(System.Text.RegularExpressions.Regex.Match(e.FullName, @"\d+").Value)))
            {
                text.AppendLine($"— {Path.GetFileNameWithoutExtension(sheet.FullName)} —");
                foreach (var row in ReadXml(sheet).Descendants().Where(e => e.Name.LocalName == "row"))
                {
                    text.AppendLine(string.Join("\t", row.Elements().Where(e => e.Name.LocalName == "c").Select(cell =>
                    {
                        string value = cell.Elements().FirstOrDefault(e => e.Name.LocalName == "v")?.Value ?? "";
                        string? type = (string?)cell.Attribute("t");
                        if (type == "s" && int.TryParse(value, out int index) && index >= 0 && index < shared.Length) return shared[index];
                        if (type == "inlineStr") return string.Concat(cell.Descendants().Where(e => e.Name.LocalName == "t").Select(e => e.Value));
                        return value;
                    })));
                    if (text.Length >= TextLimit) break;
                }
                if (text.Length >= TextLimit) break;
            }
        }
        if (text.Length > TextLimit) return text.ToString(0, TextLimit) + "\n[Text preview truncated]";
        return text.ToString();
    }
}
