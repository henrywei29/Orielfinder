using System.IO.Compression;
using System.Diagnostics;
using System.Text;
using System.Xml.Linq;
using Span.Services;

namespace Span.Tests.Services;

[TestClass]
public class OfficeDocumentPreviewServiceTests
{
    private static string NewFolder()
    {
        string folder = Path.Combine(@"D:\codex\Orielfolder-development\document-preview-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static Task<DocumentPreviewResult> Cached(OfficeDocumentPreviewService service, string source, CancellationToken token = default) =>
        service.PrepareCachedAsync(source, token);

    [TestMethod]
    public void PreparationExposesOptionalProgressWithoutChangingExistingCallers()
    {
        foreach (string name in new[] { "PrepareAsync", "PrepareCachedAsync" })
        {
            var method = typeof(OfficeDocumentPreviewService).GetMethods().SingleOrDefault(method =>
                method.Name == name && method.GetParameters().Length == 3);
            Assert.IsNotNull(method, name + " does not expose actual preparation progress.");
            Assert.IsTrue(method.GetParameters()[2].IsOptional);
        }
    }

    private sealed class ProgressCapture : IProgress<DocumentPreviewProgress>
    {
        private readonly List<DocumentPreviewProgress> _values = new();
        public void Report(DocumentPreviewProgress value) { lock (_values) _values.Add(value); }
        public DocumentPreviewProgress[] Values { get { lock (_values) return _values.ToArray(); } }
    }

    [TestMethod]
    public async Task PdfProgressUsesActualCopiedBytesAndWarmCacheIsReady()
    {
        string folder = NewFolder(), source = Path.Combine(folder, "original.pdf");
        byte[] bytes = Encoding.ASCII.GetBytes("%PDF-" + new string('p', 400_000));
        await File.WriteAllBytesAsync(source, bytes);
        var service = new OfficeDocumentPreviewService(Path.Combine(folder, "cache"));
        var progress = new ProgressCapture();
        using var result = await service.PrepareCachedAsync(source, CancellationToken.None, progress);
        var copied = progress.Values.Where(value => value.Stage == DocumentPreviewStage.Copying).ToArray();
        Assert.IsTrue(copied.Length > 1, "Actual copy progress was not reported.");
        Assert.AreEqual(0L, copied[0].BytesCopied);
        Assert.AreEqual((long)bytes.Length, copied[^1].BytesCopied);
        Assert.IsTrue(copied.All(value => value.TotalBytes == bytes.Length && value.BytesCopied <= value.TotalBytes));
        CollectionAssert.AreEqual(copied.Select(value => value.BytesCopied).Order().ToArray(), copied.Select(value => value.BytesCopied).ToArray());
        Assert.AreEqual(DocumentPreviewStage.Ready, progress.Values[^1].Stage);

        var warm = new ProgressCapture();
        using var reopened = await service.PrepareCachedAsync(source, CancellationToken.None, warm);
        Assert.AreEqual(result.PdfPath, reopened.PdfPath);
        Assert.AreEqual(DocumentPreviewStage.Ready, warm.Values.Single().Stage);
    }

    [TestMethod]
    public async Task OfficeProgressReportsQueueConversionAndPublicationWithoutPercentages()
    {
        string folder = NewFolder(), first = Path.Combine(folder, "first.doc"), second = Path.Combine(folder, "second.doc");
        await File.WriteAllTextAsync(first, "first synthetic document");
        await File.WriteAllTextAsync(second, "second synthetic document");
        await File.WriteAllTextAsync(Path.Combine(folder, "slow-cleanup"), "test-only cleanup delay");
        var service = new OfficeDocumentPreviewService(Path.Combine(folder, "cache"), await CreateFakeWorkerAsync(folder));
        var firstProgress = new ProgressCapture();
        using var published = await service.PrepareCachedAsync(first, CancellationToken.None, firstProgress);
        Assert.IsTrue(firstProgress.Values.Length > 0, "The published PDF did not report its actual stage.");
        Assert.AreEqual(DocumentPreviewStage.Ready, firstProgress.Values[^1].Stage);
        Assert.AreEqual(0, Directory.GetFiles(folder, "completed-*").Length);
        var progress = new ProgressCapture();
        var queued = service.PrepareCachedAsync(second, CancellationToken.None, progress);
        await Task.Delay(100);
        Assert.AreEqual(DocumentPreviewStage.Queued, progress.Values[^1].Stage);
        using var converted = await queued;
        Assert.IsTrue(progress.Values.Any(value => value.Stage == DocumentPreviewStage.Converting));
        Assert.AreEqual(DocumentPreviewStage.Ready, progress.Values[^1].Stage);
        Assert.IsTrue(progress.Values.Where(value => value.Stage != DocumentPreviewStage.Copying)
            .All(value => value.BytesCopied == 0 && value.TotalBytes == 0), "Unknown Office work must not claim a percentage.");
    }

    [TestMethod]
    public async Task SharedConversionDetachesCanceledProgressAndKeepsOtherSubscriber()
    {
        string folder = NewFolder(), source = Path.Combine(folder, "original.doc");
        await File.WriteAllTextAsync(source, "synthetic");
        var service = new OfficeDocumentPreviewService(Path.Combine(folder, "cache"), await CreateFakeWorkerAsync(folder));
        using var cancellation = new CancellationTokenSource();
        var canceled = new ProgressCapture();
        var first = service.PrepareCachedAsync(source, cancellation.Token, canceled);
        await WaitForWorkerAsync(folder);
        var retained = new ProgressCapture();
        var second = service.PrepareCachedAsync(source, CancellationToken.None, retained);
        Assert.IsTrue(retained.Values.Length > 0, "A joining view did not receive shared progress.");
        Assert.AreEqual(DocumentPreviewStage.Converting, retained.Values[^1].Stage, "A joining view needs the current shared stage.");
        cancellation.Cancel();
        try { using var unused = await first; Assert.Fail("The closed view kept waiting for conversion."); }
        catch (OperationCanceledException) { }
        int previousCount = canceled.Values.Length;
        using var result = await second;
        Assert.AreEqual(previousCount, canceled.Values.Length, "The closed view kept receiving conversion progress.");
        Assert.AreEqual(DocumentPreviewStage.Ready, retained.Values[^1].Stage);
        Assert.AreEqual(1, Directory.GetFiles(folder, "started-*").Length);
    }

    [DataTestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task WorkerMilestonesAndActualOutputBytesArriveBeforePublication(bool cached)
    {
        string folder = NewFolder(), source = Path.Combine(folder, "original.doc");
        await File.WriteAllTextAsync(source, "synthetic Office source");
        await File.WriteAllTextAsync(Path.Combine(folder, "progress-fixture"), "gated synthetic worker");
        var service = new OfficeDocumentPreviewService(Path.Combine(folder, "cache"), await CreateFakeWorkerAsync(folder));
        var progress = new ProgressCapture();
        using var cancellation = new CancellationTokenSource();
        var request = cached ? service.PrepareCachedAsync(source, cancellation.Token, progress)
            : service.PrepareAsync(source, CancellationToken.None, progress);
        Task<DocumentPreviewResult>? joined = null;
        var replay = new ProgressCapture();
        try
        {
            await WaitUntilAsync(() => progress.Values.Any(value => value.Stage.ToString() == "OpeningDocument"));
            Assert.IsFalse(request.IsCompleted, "Opening the document must be visible before export finishes.");
            Assert.AreEqual(1, progress.Values.Count(value => value.Stage.ToString() == "OpeningDocument"),
                "Malformed, out-of-order or duplicate worker messages advanced progress.");
            if (cached)
            {
                joined = service.PrepareCachedAsync(source, CancellationToken.None, replay);
                Assert.AreEqual("OpeningDocument", replay.Values.Single().Stage.ToString());
                cancellation.Cancel();
                try { using var unexpected = await request; Assert.Fail("The canceled observer kept waiting."); }
                catch (OperationCanceledException) { }
            }
            int detachedCount = progress.Values.Length;
            var active = cached ? replay : progress;
            await File.WriteAllTextAsync(Path.Combine(folder, "allow-export"), "continue fixture");
            await WaitUntilAsync(() => active.Values.Any(value => value.Stage.ToString() == "Exporting"));
            Assert.IsFalse((joined ?? request).IsCompleted, "Export must report before the PDF is published.");
            var outputBytes = typeof(DocumentPreviewProgress).GetProperty("OutputBytes");
            Assert.IsNotNull(outputBytes, "Observable PDF output bytes are missing.");
            await WaitUntilAsync(() => active.Values.Any(value => (long)outputBytes.GetValue(value)! == 8));
            await File.WriteAllTextAsync(Path.Combine(folder, "allow-grow"), "continue fixture");
            await WaitUntilAsync(() => active.Values.Any(value => (long)outputBytes.GetValue(value)! == 19));
            Assert.IsTrue(active.Values.Where(value => value.Stage.ToString() == "Exporting")
                .All(value => value.BytesCopied == 0 && value.TotalBytes == 0), "Output size is not a completion percentage.");
            Assert.AreEqual(cached ? detachedCount : progress.Values.Length, progress.Values.Length,
                "The canceled observer received worker progress.");
            await File.WriteAllTextAsync(Path.Combine(folder, "allow-publish"), "continue fixture");
            if (!cached) await File.WriteAllTextAsync(Path.Combine(folder, "allow-cleanup"), "continue fixture");
            using var result = await (joined ?? request);
            Assert.AreEqual(DocumentPreviewStage.Ready, active.Values[^1].Stage);
            await File.WriteAllTextAsync(Path.Combine(folder, "allow-cleanup"), "continue fixture");
            await WaitUntilAsync(() => Directory.GetFiles(folder, "completed-*").Length == 1);
            await Task.Delay(100);
            Assert.AreEqual(DocumentPreviewStage.Ready, active.Values[^1].Stage, "Late stdout regressed a published preview.");
            Assert.AreEqual(1, active.Values.Count(value => value.Stage.ToString() == "Exporting" &&
                (long)outputBytes.GetValue(value)! == 0), "Repeated stage tokens must not be forwarded.");
            if (cached)
            {
                var warm = new ProgressCapture();
                using var reopened = await service.PrepareCachedAsync(source, CancellationToken.None, warm);
                Assert.AreEqual(DocumentPreviewStage.Ready, warm.Values.Single().Stage);
                Assert.AreEqual(1, Directory.GetFiles(folder, "started-*").Length);
            }
        }
        finally
        {
            foreach (string gate in new[] { "allow-export", "allow-grow", "allow-publish", "allow-cleanup" })
                await File.WriteAllTextAsync(Path.Combine(folder, gate), "release fixture");
            try { using var result = await request; } catch (OperationCanceledException) { }
            if (joined != null) { using var result = await joined; }
            await WaitUntilAsync(() => Directory.GetFiles(folder, "completed-*").Length == 1);
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        while (!condition()) await Task.Delay(10, timeout.Token);
    }

    [TestMethod]
    public async Task WorkerExitDoesNotWaitForAnOfficeChildHoldingItsStdoutPipe()
    {
        string folder = NewFolder(), source = Path.Combine(folder, "original.doc");
        await File.WriteAllTextAsync(source, "synthetic source");
        await File.WriteAllTextAsync(Path.Combine(folder, "hold-pipe-fixture"), "child pipe fixture");
        var service = new OfficeDocumentPreviewService(Path.Combine(folder, "cache"), await CreateFakeWorkerAsync(folder));
        var request = service.PrepareAsync(source, CancellationToken.None);
        try
        {
            await WaitUntilAsync(() => Directory.GetFiles(folder, "completed-*").Length == 1);
            Assert.IsTrue(File.Exists(Path.Combine(folder, "child-started")));
            using var result = await request.WaitAsync(TimeSpan.FromSeconds(1));
            Assert.IsNotNull(result.PdfPath);
            Assert.IsFalse(File.Exists(Path.Combine(folder, "child-completed")), "The fixture child exited before testing inherited stdout.");
        }
        finally
        {
            await File.WriteAllTextAsync(Path.Combine(folder, "allow-child-exit"), "release fixture child");
            using var result = await request;
            await WaitUntilAsync(() => File.Exists(Path.Combine(folder, "child-completed")));
        }
    }

    [TestMethod]
    public async Task SessionCacheReusesConvertedOfficeAcrossViewsAndReopen()
    {
        string folder = NewFolder(), source = Path.Combine(folder, "original.doc");
        await File.WriteAllTextAsync(source, "synthetic Office source");
        string worker = await CreateFakeWorkerAsync(folder), cache = Path.Combine(folder, "cache");
        var service = new OfficeDocumentPreviewService(cache, worker);
        using var first = await Cached(service, source);
        var watch = Stopwatch.StartNew();
        using var second = await Cached(new OfficeDocumentPreviewService(cache, worker), source);
        Assert.AreEqual(first.PdfPath, second.PdfPath, "Side panel and Quick Look converted the same source twice.");
        first.Dispose(); second.Dispose();
        using var reopened = await Cached(service, source);
        Assert.AreEqual(first.PdfPath, reopened.PdfPath);
        Assert.AreEqual(1, Directory.GetFiles(folder, "started-*").Length);
        Console.WriteLine($"Repeated cached lookup milliseconds={watch.ElapsedMilliseconds}");
    }

    [TestMethod]
    public async Task SessionCacheSharesConcurrentConversionAndCancellationDoesNotCancelOtherView()
    {
        string folder = NewFolder(), source = Path.Combine(folder, "original.doc");
        await File.WriteAllTextAsync(source, "synthetic");
        var service = new OfficeDocumentPreviewService(Path.Combine(folder, "cache"), await CreateFakeWorkerAsync(folder));
        using var cancellation = new CancellationTokenSource();
        var first = Cached(service, source, cancellation.Token);
        var second = Cached(service, source);
        await WaitForWorkerAsync(folder);
        cancellation.Cancel();
        try { using var unused = await first; Assert.Fail("The canceled preview kept waiting for conversion."); }
        catch (OperationCanceledException) { }
        using var retained = await second;
        Assert.IsTrue(File.Exists(retained.PdfPath));
        Assert.AreEqual(1, Directory.GetFiles(folder, "started-*").Length);
    }

    [TestMethod]
    public async Task SessionCacheInvalidatesChangedSourceAndProtectsLiveLeaseDuringEviction()
    {
        string folder = NewFolder(), source = Path.Combine(folder, "original.pdf");
        await File.WriteAllTextAsync(source, "%PDF-first");
        var service = new OfficeDocumentPreviewService(Path.Combine(folder, "cache"));
        using var first = await Cached(service, source);
        await File.WriteAllTextAsync(source, "%PDF-changed-content");
        using var changed = await Cached(service, source);
        Assert.AreNotEqual(first.PdfPath, changed.PdfPath);
        Assert.AreEqual("%PDF-changed-content", await File.ReadAllTextAsync(changed.PdfPath!));
        var idlePaths = new List<string>();
        for (int index = 0; index < 7; index++)
        {
            string other = Path.Combine(folder, $"other-{index}.pdf");
            await File.WriteAllTextAsync(other, "%PDF-other");
            using var result = await Cached(service, other);
            idlePaths.Add(result.PdfPath!);
        }
        Assert.IsTrue(File.Exists(first.PdfPath), "Eviction removed a live view's private PDF.");
        Assert.AreEqual(4, idlePaths.Count(File.Exists), "Keep four warm idle previews, without evicting live views.");
    }

    [TestMethod]
    public async Task SessionCacheRefreshesSameLengthEditsAndMissingSnapshots()
    {
        string folder = NewFolder(), source = Path.Combine(folder, "original.pdf");
        await File.WriteAllTextAsync(source, "%PDF-first");
        var service = new OfficeDocumentPreviewService(Path.Combine(folder, "cache"));
        using var first = await Cached(service, source);
        await File.WriteAllTextAsync(source, "%PDF-other");
        File.SetLastWriteTimeUtc(source, File.GetLastWriteTimeUtc(source).AddSeconds(2));
        using var edited = await Cached(service, source);
        Assert.AreNotEqual(first.PdfPath, edited.PdfPath);
        Assert.AreEqual("%PDF-other", await File.ReadAllTextAsync(edited.PdfPath!));
        File.Move(edited.PdfPath!, Path.Combine(folder, "retained-test-snapshot.pdf"));
        using var recovered = await Cached(service, source);
        Assert.AreNotEqual(edited.PdfPath, recovered.PdfPath);
        Assert.IsTrue(File.Exists(recovered.PdfPath));
    }

    [TestMethod]
    public async Task SessionCacheDoesNotRememberConversionFailure()
    {
        string folder = NewFolder(), source = Path.Combine(folder, "original.doc");
        await File.WriteAllTextAsync(source, "synthetic");
        var service = new OfficeDocumentPreviewService(Path.Combine(folder, "cache"), await CreateFakeWorkerAsync(folder));
        string failure = Path.Combine(folder, "fail-conversion");
        await File.WriteAllTextAsync(failure, "fail");
        using var first = await Cached(service, source);
        Assert.IsNull(first.PdfPath);
        // The test owns this disposable signal; changing its name avoids deleting fixtures.
        File.Move(failure, failure + ".disabled");
        using var recovered = await Cached(service, source);
        Assert.IsNotNull(recovered.PdfPath, "A failed conversion was cached.");
    }

    [TestMethod]
    public async Task SessionCacheCancellationRacingCompletionKeepsCleanupSafe()
    {
        string folder = NewFolder(), source = Path.Combine(folder, "original.pdf");
        await File.WriteAllTextAsync(source, "%PDF-race-fixture");
        var service = new OfficeDocumentPreviewService(Path.Combine(folder, "cache"));
        for (int index = 0; index < 200; index++)
        {
            File.SetLastWriteTimeUtc(source, DateTime.UtcNow.AddSeconds(index));
            using var cancellation = new CancellationTokenSource();
            var request = Cached(service, source, cancellation.Token);
            await Task.Yield();
            cancellation.Cancel();
            try { using var result = await request; }
            catch (OperationCanceledException) { }
        }
        using var final = await Cached(service, source);
        Assert.IsTrue(File.Exists(final.PdfPath), "Cancellation left the shared cache unusable.");
    }

    [TestMethod]
    public async Task SessionCacheReopenDuringActiveConversionReusesWorkAfterLastViewCancels()
    {
        string folder = NewFolder(), source = Path.Combine(folder, "original.doc");
        await File.WriteAllTextAsync(source, "synthetic");
        var service = new OfficeDocumentPreviewService(Path.Combine(folder, "cache"), await CreateFakeWorkerAsync(folder));
        using var cancellation = new CancellationTokenSource();
        var first = Cached(service, source, cancellation.Token);
        await WaitForWorkerAsync(folder);
        cancellation.Cancel();
        try { using var unused = await first; Assert.Fail("The closed view kept waiting."); }
        catch (OperationCanceledException) { }
        using var reopened = await Cached(service, source);
        Assert.IsNotNull(reopened.PdfPath);
        Assert.AreEqual(1, Directory.GetFiles(folder, "started-*").Length,
            "Closing and reopening a view wasted the already-running Office conversion.");
    }

    [TestMethod]
    public async Task CachedOfficePageIsAvailableBeforeCleanupWithoutReleasingItsStagingOrConversionSlot()
    {
        string folder = NewFolder(), source = Path.Combine(folder, "first.doc");
        await File.WriteAllTextAsync(source, "synthetic first");
        await File.WriteAllTextAsync(Path.Combine(folder, "slow-cleanup"), "test-only cleanup delay");
        string cache = Path.Combine(folder, "cache");
        var service = new OfficeDocumentPreviewService(cache, await CreateFakeWorkerAsync(folder));
        using var first = await Cached(service, source);
        Assert.IsNotNull(first.PdfPath);
        Assert.AreEqual(0, Directory.GetFiles(folder, "completed-*").Length,
            "The finished PDF waited for unrelated Office shutdown before becoming available.");
        string firstFolder = Path.GetDirectoryName(first.PdfPath)!;
        first.Dispose();
        for (int index = 0; index < 5; index++)
        {
            string other = Path.Combine(folder, $"other-{index}.pdf");
            await File.WriteAllTextAsync(other, "%PDF-synthetic");
            using var idle = await Cached(service, other);
        }
        Assert.IsTrue(Directory.Exists(firstFolder), "Eviction removed files while Office was still cleaning up.");
        string second = Path.Combine(folder, "second.doc");
        await File.WriteAllTextAsync(second, "synthetic second");
        var next = Cached(service, second);
        await Task.Delay(100);
        Assert.AreEqual(1, Directory.GetFiles(folder, "started-*").Length,
            "A second Office conversion overlapped the first worker's cleanup.");
        using var secondPreview = await next;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        while (Directory.Exists(firstFolder)) await Task.Delay(20, timeout.Token);
        Assert.IsFalse(File.Exists(Path.Combine(folder, "overlap")));
        CollectionAssert.AreEqual(Encoding.UTF8.GetBytes("synthetic first"), await File.ReadAllBytesAsync(source));
        // Do not leave this test's second delayed worker running into another case.
        while (Directory.GetFiles(folder, "completed-*").Length < 2) await Task.Delay(20, timeout.Token);
    }

    [TestMethod]
    public async Task PdfIsCopiedIntoIsolatedFolderWithoutLockingOrChangingSource()
    {
        string folder = NewFolder();
        string source = Path.Combine(folder, "source.pdf");
        byte[] content = Encoding.ASCII.GetBytes("%PDF-1.7\nsynthetic fixture");
        await File.WriteAllBytesAsync(source, content);
        var written = File.GetLastWriteTimeUtc(source);
        using var editor = new FileStream(source, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
        var result = await new OfficeDocumentPreviewService(Path.Combine(folder, "cache")).PrepareAsync(source, CancellationToken.None);
        Assert.IsNotNull(result.PdfPath);
        Assert.AreNotEqual(source, result.PdfPath);
        CollectionAssert.AreEqual(content, await File.ReadAllBytesAsync(result.PdfPath));
        editor.Dispose();
        CollectionAssert.AreEqual(content, await File.ReadAllBytesAsync(source));
        Assert.AreEqual(written, File.GetLastWriteTimeUtc(source));
        Assert.AreEqual(1, Directory.GetFiles(folder).Length);
    }

    [TestMethod]
    public async Task SourceLargerThan100MiBIsRejectedWithoutCopying()
    {
        string folder = NewFolder();
        string source = Path.Combine(folder, "oversize.pdf");
        using (var file = File.Create(source)) file.SetLength(100L * 1024 * 1024 + 1);
        string cache = Path.Combine(folder, "cache");
        await Assert.ThrowsExceptionAsync<IOException>(() =>
            new OfficeDocumentPreviewService(cache).PrepareAsync(source, CancellationToken.None));
        Assert.IsFalse(Directory.Exists(cache));
    }

    [TestMethod]
    public async Task CancelledRequestDoesNotStageDocument()
    {
        string folder = NewFolder();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsExceptionAsync<OperationCanceledException>(() =>
            new OfficeDocumentPreviewService(folder).PrepareAsync(Path.Combine(folder, "missing.pdf"), cancellation.Token));
        Assert.AreEqual(0, Directory.GetFiles(folder).Length);
    }

    [TestMethod]
    public async Task WordReportsVisualPreviewUnavailableWhenWorkerIsUnavailable()
    {
        string folder = NewFolder();
        string source = Path.Combine(folder, "example.docx");
        using (var zip = ZipFile.Open(source, ZipArchiveMode.Create))
        {
            using var writer = new StreamWriter(zip.CreateEntry("word/document.xml").Open());
            writer.Write("<w:document xmlns:w='http://schemas.openxmlformats.org/wordprocessingml/2006/main'><w:body><w:p><w:r><w:t>Hello &amp; 世界</w:t></w:r></w:p><w:p><w:r><w:t>Second paragraph</w:t></w:r></w:p></w:body></w:document>");
        }
        var result = await new OfficeDocumentPreviewService(Path.Combine(folder, "cache"), Path.Combine(folder, "absent.ps1"))
            .PrepareAsync(source, CancellationToken.None);
        Assert.IsNull(result.PdfPath);
        Assert.AreEqual("", result.Text, "Extracted text must never replace the document's layout.");
        StringAssert.Contains(result.Limitation, "版式预览暂不可用");
    }

    [DataTestMethod]
    [DataRow(".docx", "word/document.xml", "<w:document xmlns:w='http://schemas.openxmlformats.org/wordprocessingml/2006/main'><w:body><w:p><w:hyperlink r:id='rId1'><w:r><w:rPr><w:b/></w:rPr><w:t>Visible label</w:t></w:r></w:hyperlink></w:p><w:sectPr><w:pgSz w:w='12240'/></w:sectPr></w:body></w:document>")]
    [DataRow(".xlsx", "xl/worksheets/sheet1.xml", "<worksheet xmlns='http://schemas.openxmlformats.org/spreadsheetml/2006/main'><sheetData><row><c r='A1' t='inlineStr' s='2'><is><t>Visible label</t></is></c></row></sheetData><hyperlinks><hyperlink ref='A1' r:id='rId1'/></hyperlinks></worksheet>")]
    [DataRow(".pptx", "ppt/slides/slide1.xml", "<p:sld xmlns:p='http://schemas.openxmlformats.org/presentationml/2006/main' xmlns:a='http://schemas.openxmlformats.org/drawingml/2006/main'><p:cSld><p:spTree><a:p><a:r><a:rPr b='1'><a:hlinkClick r:id='rId1'/></a:rPr><a:t>Visible label</a:t></a:r></a:p></p:spTree></p:cSld></p:sld>")]
    public async Task InertHyperlinksAreRemovedOnlyFromPrivateCopyBeforeVisualConversion(string extension, string part, string markup)
    {
        string folder = NewFolder();
        string source = Path.Combine(folder, "original" + extension);
        const string relationshipNamespace = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        string xml = markup.Replace("<" + (extension == ".docx" ? "w:document" : extension == ".xlsx" ? "worksheet" : "p:sld"),
            "<" + (extension == ".docx" ? "w:document" : extension == ".xlsx" ? "worksheet" : "p:sld") + " xmlns:r='" + relationshipNamespace + "'");
        using (var zip = ZipFile.Open(source, ZipArchiveMode.Create))
        {
            Write(zip, part, xml);
            Write(zip, RelationshipPart(part), "<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'><Relationship TargetMode='External' Target='https://example.invalid/' Id='rId1' Type='" + relationshipNamespace + "/hyperlink'/></Relationships>");
        }
        byte[] original = await File.ReadAllBytesAsync(source);
        var timestamp = File.GetLastWriteTimeUtc(source);
        string worker = await CreateFakeWorkerAsync(folder);
        using var result = await new OfficeDocumentPreviewService(Path.Combine(folder, "cache"), worker)
            .PrepareAsync(source, CancellationToken.None);

        Assert.IsNotNull(result.PdfPath, result.Limitation);
        Assert.AreEqual("", result.Text);
        string staged = Path.Combine(Path.GetDirectoryName(result.PdfPath)!, "source" + extension);
        Assert.IsFalse(OfficeDocumentPreviewService.HasExternalRelationships(staged));
        using (var zip = ZipFile.OpenRead(staged))
        using (var reader = new StreamReader(zip.GetEntry(part)!.Open()))
        {
            var sanitized = XDocument.Parse(await reader.ReadToEndAsync());
            var expected = XDocument.Parse(xml);
            foreach (var link in expected.Descendants().Where(e => e.Attributes().Any(a => a.Name.NamespaceName == relationshipNamespace && a.Value == "rId1")).ToArray())
            {
                if (extension == ".docx") link.ReplaceWith(link.Nodes());
                else link.Remove();
            }
            Assert.IsTrue(XNode.DeepEquals(expected, sanitized), "Visible content, formatting, and layout must survive unchanged.");
        }
        CollectionAssert.AreEqual(original, await File.ReadAllBytesAsync(source));
        Assert.AreEqual(timestamp, File.GetLastWriteTimeUtc(source));
    }

    [DataTestMethod]
    [DataRow("image")]
    [DataRow("attachedTemplate")]
    [DataRow("externalLink")]
    public async Task LinkedResourcesStillBlockOfficeWithoutSubstitutingText(string resourceType)
    {
        string folder = NewFolder();
        string source = Path.Combine(folder, "linked.docx");
        using (var zip = ZipFile.Open(source, ZipArchiveMode.Create))
        {
            Write(zip, "word/document.xml", "<document><p><t>Do not replace the visual document with this text</t></p></document>");
            Write(zip, "word/_rels/document.xml.rels", "<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'><Relationship TargetMode='External' Target='https://example.invalid/resource' Id='rId1' Type='http://schemas.openxmlformats.org/officeDocument/2006/relationships/" + resourceType + "'/></Relationships>");
        }
        byte[] original = await File.ReadAllBytesAsync(source);
        string worker = await CreateFakeWorkerAsync(folder);
        string cache = Path.Combine(folder, "cache");
        using var result = await new OfficeDocumentPreviewService(cache, worker).PrepareAsync(source, CancellationToken.None);
        Assert.IsNull(result.PdfPath);
        Assert.AreEqual("", result.Text);
        StringAssert.Contains(result.Limitation, "外部");
        Assert.AreEqual(0, Directory.GetFiles(folder, "started-*").Length);
        Assert.AreEqual(0, Directory.GetDirectories(cache).Length);
        CollectionAssert.AreEqual(original, await File.ReadAllBytesAsync(source));
    }

    [TestMethod]
    public async Task HyperlinkTypeUsedByLinkedImageIsBlockedRatherThanReinterpreted()
    {
        string folder = NewFolder();
        string source = Path.Combine(folder, "unknown.docx");
        using (var zip = ZipFile.Open(source, ZipArchiveMode.Create))
        {
            Write(zip, "word/document.xml", "<document xmlns:r='http://schemas.openxmlformats.org/officeDocument/2006/relationships'><blip r:link='rId1'/></document>");
            Write(zip, "word/_rels/document.xml.rels", "<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'><Relationship TargetMode='External' Target='https://example.invalid/image' Id='rId1' Type='http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink'/></Relationships>");
        }
        string worker = await CreateFakeWorkerAsync(folder);
        using var result = await new OfficeDocumentPreviewService(Path.Combine(folder, "cache"), worker)
            .PrepareAsync(source, CancellationToken.None);
        Assert.IsNull(result.PdfPath);
        Assert.AreEqual("", result.Text);
        Assert.AreEqual(0, Directory.GetFiles(folder, "started-*").Length);
    }

    private static string RelationshipPart(string part) =>
        part[..(part.LastIndexOf('/') + 1)] + "_rels/" + part[(part.LastIndexOf('/') + 1)..] + ".rels";

    [TestMethod]
    public async Task StrictWordHyperlinkInHeaderKeepsWhitespaceAndFormattingInVisualCopy()
    {
        string folder = NewFolder();
        string source = Path.Combine(folder, "strict.docx");
        const string markup = "<w:hdr xmlns:w='http://purl.oclc.org/ooxml/wordprocessingml/main' xmlns:r='http://purl.oclc.org/ooxml/officeDocument/relationships'><w:p><w:hyperlink r:id='link'><w:r><w:rPr><w:i/></w:rPr><w:t xml:space='preserve'>  Header label  </w:t></w:r></w:hyperlink></w:p></w:hdr>";
        using (var zip = ZipFile.Open(source, ZipArchiveMode.Create))
        {
            Write(zip, "word/header1.xml", markup);
            Write(zip, "word/_rels/header1.xml.rels", "<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'><Relationship Id='link' TargetMode='External' Target='https://example.invalid/' Type='http://purl.oclc.org/ooxml/officeDocument/relationships/hyperlink'/></Relationships>");
        }
        string worker = await CreateFakeWorkerAsync(folder);
        using var result = await new OfficeDocumentPreviewService(Path.Combine(folder, "cache"), worker)
            .PrepareAsync(source, CancellationToken.None);
        Assert.IsNotNull(result.PdfPath, result.Limitation);
        using var sanitized = ZipFile.OpenRead(Path.Combine(Path.GetDirectoryName(result.PdfPath)!, "source.docx"));
        using var reader = new StreamReader(sanitized.GetEntry("word/header1.xml")!.Open());
        var actual = XDocument.Parse(await reader.ReadToEndAsync(), LoadOptions.PreserveWhitespace);
        Assert.AreEqual("  Header label  ", actual.Descendants().Single(e => e.Name.LocalName == "t").Value);
        Assert.AreEqual(1, actual.Descendants().Count(e => e.Name.LocalName == "i"));
        Assert.IsFalse(actual.Descendants().Any(e => e.Name.LocalName == "hyperlink"));
    }

    [TestMethod]
    public async Task HyperlinkAlongsideExternalTemplateStillPreventsWorkerStart()
    {
        string folder = NewFolder();
        string source = Path.Combine(folder, "mixed.docx");
        using (var zip = ZipFile.Open(source, ZipArchiveMode.Create))
        {
            Write(zip, "word/document.xml", "<w:document xmlns:w='http://schemas.openxmlformats.org/wordprocessingml/2006/main' xmlns:r='http://schemas.openxmlformats.org/officeDocument/2006/relationships'><w:body><w:p><w:hyperlink r:id='link'><w:r><w:t>Visible label</w:t></w:r></w:hyperlink></w:p></w:body></w:document>");
            Write(zip, "word/_rels/document.xml.rels", "<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'><Relationship Id='link' TargetMode='External' Target='https://example.invalid/' Type='http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink'/><Relationship Id='template' TargetMode='External' Target='https://example.invalid/template.dotx' Type='http://schemas.openxmlformats.org/officeDocument/2006/relationships/attachedTemplate'/></Relationships>");
        }
        byte[] original = await File.ReadAllBytesAsync(source);
        string worker = await CreateFakeWorkerAsync(folder);
        using var result = await new OfficeDocumentPreviewService(Path.Combine(folder, "cache"), worker)
            .PrepareAsync(source, CancellationToken.None);
        Assert.IsNull(result.PdfPath);
        Assert.AreEqual("", result.Text);
        Assert.AreEqual(0, Directory.GetFiles(folder, "started-*").Length);
        CollectionAssert.AreEqual(original, await File.ReadAllBytesAsync(source));
    }

    [TestMethod]
    public async Task FailedModernConversionReturnsVisualLimitationAndReleasesPrivateCopy()
    {
        string folder = NewFolder();
        string source = Path.Combine(folder, "failed.docx");
        using (var zip = ZipFile.Open(source, ZipArchiveMode.Create))
            Write(zip, "word/document.xml", "<document><p><t>Never substitute this text</t></p></document>");
        string worker = await CreateFakeWorkerAsync(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, "fail-conversion"), "synthetic failure");
        string cache = Path.Combine(folder, "cache");
        using var result = await new OfficeDocumentPreviewService(cache, worker).PrepareAsync(source, CancellationToken.None);
        Assert.IsNull(result.PdfPath);
        Assert.AreEqual("", result.Text);
        StringAssert.Contains(result.Limitation, "版式预览暂不可用");
        Assert.AreEqual(1, Directory.GetFiles(folder, "started-*").Length);
        Assert.AreEqual(0, Directory.GetDirectories(cache).Length);
    }

    [TestMethod]
    public void WorkbookFallbackResolvesSharedStringsAndKeepsCellSeparation()
    {
        string path = Path.Combine(NewFolder(), "example.xlsx");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            Write(zip, "xl/sharedStrings.xml", "<sst xmlns='http://schemas.openxmlformats.org/spreadsheetml/2006/main'><si><t>Shared value</t></si></sst>");
            Write(zip, "xl/worksheets/sheet1.xml", "<worksheet xmlns='http://schemas.openxmlformats.org/spreadsheetml/2006/main'><sheetData><row><c t='s'><v>0</v></c><c><v>42</v></c><c t='inlineStr'><is><t>Inline</t></is></c></row></sheetData></worksheet>");
        }
        StringAssert.Contains(OfficeDocumentPreviewService.ExtractOpenXmlText(path), "Shared value\t42\tInline");
    }

    [TestMethod]
    public void ExternalRelationshipsAreDetectedBeforeOfficeIsStarted()
    {
        string path = Path.Combine(NewFolder(), "linked.pptx");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
            Write(zip, "ppt/_rels/presentation.xml.rels", "<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'><Relationship TargetMode='External' Target='https://example.invalid/data' Id='rId1' Type='image'/></Relationships>");
        Assert.IsTrue(OfficeDocumentPreviewService.HasExternalRelationships(path));
    }

    [TestMethod]
    public async Task DisposingRepeatedPdfPreviewsReleasesOnlyTheirOwnCopies()
    {
        string folder = NewFolder();
        string source = Path.Combine(folder, "original.pdf");
        byte[] content = Encoding.ASCII.GetBytes("%PDF-1.7\noriginal remains untouched");
        await File.WriteAllBytesAsync(source, content);
        string cache = Path.Combine(folder, "cache");
        Directory.CreateDirectory(cache);
        string foreign = Path.Combine(cache, "unrelated.txt");
        await File.WriteAllTextAsync(foreign, "keep");
        string earlierFolder = Path.Combine(cache, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(earlierFolder);
        string earlierCopy = Path.Combine(earlierFolder, "source.pdf");
        await File.WriteAllTextAsync(earlierCopy, "earlier request must remain untouched");
        var service = new OfficeDocumentPreviewService(cache);

        for (int attempt = 0; attempt < 3; attempt++)
        {
            var result = await service.PrepareAsync(source, CancellationToken.None);
            string stagedFolder = Path.GetDirectoryName(result.PdfPath!)!;
            Assert.IsTrue(File.Exists(result.PdfPath));
            ((object)result as IDisposable)?.Dispose();
            ((object)result as IDisposable)?.Dispose();
            Assert.IsFalse(Directory.Exists(stagedFolder), "Disposed preview retained its private document copy.");
        }

        Assert.AreEqual(1, Directory.GetDirectories(cache).Length);
        Assert.AreEqual("keep", await File.ReadAllTextAsync(foreign));
        Assert.AreEqual("earlier request must remain untouched", await File.ReadAllTextAsync(earlierCopy));
        CollectionAssert.AreEqual(content, await File.ReadAllBytesAsync(source));
    }

    [TestMethod]
    public async Task CancellationAfterStagingBeginsReleasesPartialCopy()
    {
        string folder = NewFolder();
        string source = Path.Combine(folder, "original.pdf");
        using (var file = File.Create(source)) file.SetLength(100L * 1024 * 1024);
        string cache = Path.Combine(folder, "cache");
        Directory.CreateDirectory(cache);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var copyStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var watcher = new FileSystemWatcher(cache, "source.pdf") { IncludeSubdirectories = true };
        watcher.Created += (_, _) => { copyStarted.TrySetResult(); cancellation.Cancel(); };
        watcher.EnableRaisingEvents = true;

        try
        {
            var unexpected = await new OfficeDocumentPreviewService(cache).PrepareAsync(source, cancellation.Token);
            ((object)unexpected as IDisposable)?.Dispose();
            Assert.Fail("The request must be canceled after staging begins.");
        }
        catch (OperationCanceledException) { }

        Assert.IsTrue(copyStarted.Task.IsCompletedSuccessfully, "Cancellation must occur after the staged copy is created.");
        Assert.AreEqual(0, Directory.GetDirectories(cache).Length, "Canceled preview retained a partial copy.");
        Assert.AreEqual(100L * 1024 * 1024, new FileInfo(source).Length);
    }

    [TestMethod]
    public async Task FailedOfficePreparationReleasesStagedCopy()
    {
        string folder = NewFolder();
        string source = Path.Combine(folder, "invalid.docx");
        await File.WriteAllTextAsync(source, "not a ZIP package");
        string cache = Path.Combine(folder, "cache");

        await Assert.ThrowsExceptionAsync<InvalidDataException>(() =>
            new OfficeDocumentPreviewService(cache).PrepareAsync(source, CancellationToken.None));

        Assert.AreEqual(0, Directory.GetDirectories(cache).Length, "Failed preview retained its staged original.");
        Assert.AreEqual("not a ZIP package", await File.ReadAllTextAsync(source));
    }

    [TestMethod]
    public async Task CancellingActiveConversionLetsWorkerFinishBeforeReleasingItsCopy()
    {
        string folder = NewFolder();
        string worker = await CreateFakeWorkerAsync(folder);
        string source = Path.Combine(folder, "original.doc");
        await File.WriteAllTextAsync(source, "synthetic document");
        string cache = Path.Combine(folder, "cache");
        using var cancellation = new CancellationTokenSource();
        Task<DocumentPreviewResult> request = new OfficeDocumentPreviewService(cache, worker)
            .PrepareAsync(source, cancellation.Token);
        await WaitForWorkerAsync(folder);

        cancellation.Cancel();
        await Assert.ThrowsExceptionAsync<OperationCanceledException>(() => request);

        Assert.AreEqual(1, Directory.GetFiles(folder, "completed-*").Length,
            "Cancellation killed the worker before its Office cleanup could finish.");
        Assert.AreEqual(0, Directory.GetDirectories(cache).Length);
        Assert.AreEqual("synthetic document", await File.ReadAllTextAsync(source));
    }

    [TestMethod]
    public async Task ConcurrentServiceInstancesSerializeOfficeConversions()
    {
        string folder = NewFolder();
        string worker = await CreateFakeWorkerAsync(folder);
        string source = Path.Combine(folder, "original.doc");
        await File.WriteAllTextAsync(source, "synthetic document");
        string cache = Path.Combine(folder, "cache");
        Task<DocumentPreviewResult> first = new OfficeDocumentPreviewService(cache, worker)
            .PrepareAsync(source, CancellationToken.None);
        await WaitForWorkerAsync(folder);
        Task<DocumentPreviewResult> second = new OfficeDocumentPreviewService(cache, worker)
            .PrepareAsync(source, CancellationToken.None);

        using var firstResult = await first;
        using var secondResult = await second;

        Assert.IsNotNull(firstResult.PdfPath);
        Assert.IsNotNull(secondResult.PdfPath);
        Assert.IsFalse(File.Exists(Path.Combine(folder, "overlap")), "Office workers overlapped.");
        Assert.AreEqual(2, Directory.GetFiles(folder, "completed-*").Length);
    }

    [TestMethod]
    public async Task CancellingQueuedConversionNeverStartsAnotherWorker()
    {
        string folder = NewFolder();
        string worker = await CreateFakeWorkerAsync(folder);
        string source = Path.Combine(folder, "original.doc");
        await File.WriteAllTextAsync(source, "synthetic document");
        string cache = Path.Combine(folder, "cache");
        Task<DocumentPreviewResult> first = new OfficeDocumentPreviewService(cache, worker)
            .PrepareAsync(source, CancellationToken.None);
        await WaitForWorkerAsync(folder);
        using var cancellation = new CancellationTokenSource();
        Task<DocumentPreviewResult> queued = new OfficeDocumentPreviewService(cache, worker)
            .PrepareAsync(source, cancellation.Token);
        await Task.Delay(30);
        Assert.IsFalse(queued.IsCompleted, "The queued conversion must wait for the active worker.");
        cancellation.Cancel();

        try
        {
            using var unexpected = await queued;
            Assert.Fail("The queued request must be canceled.");
        }
        catch (OperationCanceledException) { }
        using var result = await first;

        Assert.AreEqual(1, Directory.GetFiles(folder, "started-*").Length);
        Assert.AreEqual(1, Directory.GetDirectories(cache).Length, "The canceled queued copy was retained.");
    }

    private static async Task WaitForWorkerAsync(string folder)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (Directory.GetFiles(folder, "started-*").Length == 0)
            await Task.Delay(10, timeout.Token);
    }

    private static async Task<string> CreateFakeWorkerAsync(string folder)
    {
        string source = Path.Combine(folder, "FakeWorker.cs");
        string executable = Path.Combine(folder, "FakeWorker.exe");
        await File.WriteAllTextAsync(source, """
            using System;
            using System.IO;
            using System.Threading;
            internal static class FakeWorker
            {
                private static int Main(string[] args)
                {
                    string root = AppDomain.CurrentDomain.BaseDirectory;
                    if (args.Length == 1 && args[0] == "--hold-pipe")
                    {
                        Console.WriteLine("ignored child output"); Console.Out.Flush();
                        File.WriteAllText(Path.Combine(root, "child-started"), "child owns inherited pipe");
                        Wait(root, "allow-child-exit");
                        File.WriteAllText(Path.Combine(root, "child-completed"), "child finished");
                        return 0;
                    }
                    string id = Path.GetFileName(Path.GetDirectoryName(args[0]));
                    try
                    {
                        using (var exclusive = new FileStream(Path.Combine(root, "active.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                        {
                            File.WriteAllText(args[2], "{}");
                            File.WriteAllText(Path.Combine(root, "started-" + id), "started");
                            if (File.Exists(Path.Combine(root, "fail-conversion"))) return 20;
                            if (File.Exists(Path.Combine(root, "progress-fixture")))
                            {
                                Console.WriteLine("SPAN_PREVIEW:EXPORTING"); // Out of order: reject.
                                Console.WriteLine("SPAN_PREVIEW:OPENING_DOCUMENT trailing-junk");
                                Console.WriteLine(new string('x', 200000)); // Must drain without unbounded lines.
                                Console.WriteLine("SPAN_PREVIEW:OPENING_DOCUMENT");
                                Console.WriteLine("SPAN_PREVIEW:OPENING_DOCUMENT"); // Duplicate: reject.
                                Console.Out.Flush();
                                Wait(root, "allow-export");
                                Console.WriteLine("SPAN_PREVIEW:EXPORTING");
                                Console.WriteLine("SPAN_PREVIEW:OPENING_DOCUMENT"); // Regression: reject.
                                Console.WriteLine("SPAN_PREVIEW:EXPORTING");
                                Console.Out.Flush();
                                string pending = Path.Combine(Path.GetDirectoryName(args[1]), "preview.pending.pdf");
                                File.WriteAllText(pending, "%PDF-1.7");
                                Wait(root, "allow-grow");
                                File.AppendAllText(pending, "-more-bytes");
                                Wait(root, "allow-publish");
                                File.Move(pending, args[1]);
                                Wait(root, "allow-cleanup");
                                Console.WriteLine("SPAN_PREVIEW:OPENING_DOCUMENT");
                                Console.WriteLine("SPAN_PREVIEW:EXPORTING");
                                Console.Out.Flush();
                            }
                            else
                            {
                                Thread.Sleep(300);
                                if (File.Exists(Path.Combine(root, "hold-pipe-fixture")))
                                {
                                    var child = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                                        System.Reflection.Assembly.GetExecutingAssembly().Location, "--hold-pipe")
                                        { UseShellExecute = false, CreateNoWindow = true });
                                    child.Dispose();
                                    while (!File.Exists(Path.Combine(root, "child-started"))) Thread.Sleep(10);
                                }
                                File.WriteAllText(args[1], "%PDF-1.7 synthetic fixture");
                            }
                            if (File.Exists(Path.Combine(root, "slow-cleanup"))) Thread.Sleep(1500);
                            File.WriteAllText(Path.Combine(root, "completed-" + id), "cleanup completed");
                        }
                        return 0;
                    }
                    catch (IOException) { File.WriteAllText(Path.Combine(root, "overlap"), "overlap"); return 20; }
                }
                private static void Wait(string root, string gate)
                {
                    var timeout = System.Diagnostics.Stopwatch.StartNew();
                    while (!File.Exists(Path.Combine(root, gate)) && timeout.ElapsedMilliseconds < 15000) Thread.Sleep(10);
                }
            }
            """);
        string compiler = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "Microsoft.NET", "Framework64", "v4.0.30319", "csc.exe");
        var start = new ProcessStartInfo(compiler) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
        start.ArgumentList.Add("/nologo");
        start.ArgumentList.Add("/out:" + executable);
        start.ArgumentList.Add(source);
        using var process = Process.Start(start)!;
        string diagnostics = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.AreEqual(0, process.ExitCode, diagnostics);
        return executable;
    }

    [TestMethod]
    public async Task DisposingPreviewDoesNotTraverseAnUnrecognizedDirectory()
    {
        string folder = NewFolder();
        string source = Path.Combine(folder, "original.pdf");
        await File.WriteAllTextAsync(source, "%PDF-original");
        var result = await new OfficeDocumentPreviewService(Path.Combine(folder, "cache"))
            .PrepareAsync(source, CancellationToken.None);
        string foreign = Path.Combine(Path.GetDirectoryName(result.PdfPath!)!, "foreign", "keep.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(foreign)!);
        await File.WriteAllTextAsync(foreign, "keep");

        ((object)result as IDisposable)?.Dispose();

        Assert.AreEqual("keep", await File.ReadAllTextAsync(foreign));
        Assert.AreEqual("%PDF-original", await File.ReadAllTextAsync(source));
    }

    [TestMethod]
    public async Task DisposingPreviewRefusesAReparsePointInItsStagingFolder()
    {
        string folder = NewFolder();
        string source = Path.Combine(folder, "original.pdf");
        await File.WriteAllTextAsync(source, "%PDF-original");
        var result = await new OfficeDocumentPreviewService(Path.Combine(folder, "cache"))
            .PrepareAsync(source, CancellationToken.None);
        string link = Path.Combine(Path.GetDirectoryName(result.PdfPath!)!, "preview.pdf");
        try { File.CreateSymbolicLink(link, source); }
        catch (UnauthorizedAccessException)
        {
            ((IDisposable)result).Dispose();
            Assert.Inconclusive("Windows did not permit creation of this disposable symbolic-link fixture.");
        }

        ((IDisposable)result).Dispose();

        Assert.IsTrue(File.Exists(result.PdfPath), "A staging folder containing a reparse point must be retained.");
        Assert.IsTrue((File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0);
        Assert.AreEqual("%PDF-original", await File.ReadAllTextAsync(source));
    }

    private static void Write(ZipArchive zip, string name, string value)
    {
        using var writer = new StreamWriter(zip.CreateEntry(name).Open());
        writer.Write(value);
    }
}
