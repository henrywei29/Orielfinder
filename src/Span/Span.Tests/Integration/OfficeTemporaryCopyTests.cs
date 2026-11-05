using Span.Services.FileOperations;

namespace Span.Tests.Integration;

[TestClass]
public class OfficeTemporaryCopyTests
{
    [TestMethod]
    public async Task RecursiveCopy_SkipsOfficeLocks_ButKeepsHiddenDocuments()
    {
        string root = Path.Combine(@"D:\codex\Orielfolder-development\office-copy-fixtures", Guid.NewGuid().ToString("N"));
        string source = Path.Combine(root, "source"), dest = Path.Combine(root, "dest");
        Directory.CreateDirectory(Path.Combine(source, "nested"));
        Directory.CreateDirectory(dest);
        File.WriteAllText(Path.Combine(source, "~$report.docx"), "lock");
        File.WriteAllText(Path.Combine(source, "nested", "~$book.xlsx"), "lock");
        string document = Path.Combine(source, "nested", "report.docx");
        File.WriteAllText(document, "legitimate hidden document");
        File.SetAttributes(document, FileAttributes.Hidden);
        var operation = new CopyFileOperation(new() { source }, dest);
        var result = await operation.ExecuteAsync();
        Assert.IsTrue(result.Success, result.ErrorMessage);
        Assert.IsFalse(File.Exists(Path.Combine(dest, "source", "~$report.docx")));
        Assert.IsFalse(File.Exists(Path.Combine(dest, "source", "nested", "~$book.xlsx")));
        Assert.AreEqual("legitimate hidden document", File.ReadAllText(Path.Combine(dest, "source", "nested", "report.docx")));
        Assert.IsTrue(File.Exists(Path.Combine(source, "~$report.docx")));

        string unfiltered = Path.Combine(root, "unfiltered");
        Directory.CreateDirectory(unfiltered);
        var normalCopy = new CopyFileOperation(new() { source }, unfiltered) { SkipOfficeTemporaryFiles = false };
        Assert.IsTrue((await normalCopy.ExecuteAsync()).Success);
        Assert.IsTrue(File.Exists(Path.Combine(unfiltered, "source", "nested", "~$book.xlsx")));

        string moved = Path.Combine(root, "moved");
        Directory.CreateDirectory(moved);
        var move = new MoveFileOperation(new() { source }, moved);
        Assert.IsTrue((await move.ExecuteAsync()).Success);
        Assert.IsTrue(File.Exists(Path.Combine(moved, "source", "~$report.docx")));
    }
}
