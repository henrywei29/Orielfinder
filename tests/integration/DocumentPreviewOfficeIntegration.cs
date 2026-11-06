using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Text;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using OrielFolder.DocumentPreview.Worker;

internal static class DocumentPreviewOfficeIntegration
{
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    private delegate bool EnumWindow(IntPtr hwnd, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindow callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumWindow callback, IntPtr parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder text, int count);
    [DllImport("oleacc.dll")] private static extern int AccessibleObjectFromWindow(IntPtr hwnd, uint objectId, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object result);
    private static string root;

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--inspect-powerpoint") return InspectPowerPoint();
        if (args.Length == 1 && args[0] == "--cleanup-owned-office") return CleanupOwnedOffice();
        if (args.Length == 1 && args[0] == "--probe-excel-cleanup") return ProbeExcelCleanup();
        if (args.Length < 1 || args.Length > 2) return 2;
        root = args.Length == 2 ? Path.GetFullPath(args[1]) : Path.Combine(@"D:\codex\Orielfolder-development\document-preview-office-tests", Guid.NewGuid().ToString("N"));
        if (!root.StartsWith(@"D:\codex\", StringComparison.OrdinalIgnoreCase)) return 2;
        Directory.CreateDirectory(root);
        Console.WriteLine("Synthetic fixtures retained: " + root);
        try
        {
            if (args.Length == 2 && new[] { "word.doc", "word.docx", "excel.xls", "excel.xlsx", "powerpoint.ppt", "powerpoint.pptx" }.Any(file => !File.Exists(Path.Combine(root, file))))
                throw new Exception("The retained fixture set is incomplete.");
            if (args.Length == 1)
            {
                Create("WINWORD", "Word.Application");
                Create("EXCEL", "Excel.Application");
            }
            ConvertSources(args[0], false);
            if (args.Length == 1) Create("POWERPNT", "PowerPoint.Application");
            if (args.Length == 2) ConvertSources(args[0], true);
            Console.WriteLine("Synthetic fixtures retained: " + root);
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error.ToString()); return 1; }
    }

    private static int ProbeExcelCleanup()
    {
        var session = OfficeSession.Create("EXCEL", "Excel.Application");
        dynamic application = session.Application;
        dynamic document = null;
        string folder = Path.Combine(@"D:\codex\Orielfolder-development\document-preview-office-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            application.AutomationSecurity = 3;
            application.Visible = false;
            application.DisplayAlerts = false;
            application.EnableEvents = false;
            application.AskToUpdateLinks = false;
            document = application.Workbooks.Open(@"D:\codex\Orielfolder-development\document-preview-office-tests\2488971a1f0c4fcd8b1052db83a2437d\excel.xls", UpdateLinks: 0, ReadOnly: true, AddToMru: false);
            document.ExportAsFixedFormat(Type: 0, Filename: Path.Combine(folder, "preview.pdf"), OpenAfterPublish: false);
            document.Close(false);
            Marshal.FinalReleaseComObject((object)document);
            document = null;
            Console.WriteLine("PROBE Excel cleanup count=" + (int)application.Workbooks.Count + "; visible=" + (bool)application.Visible + "; userControl=" + (bool)application.UserControl);
            using (var process = Process.GetProcessById(session.Process.Id))
            {
                session.Dispose();
                Console.WriteLine("PROBE Excel cleanup result=" + session.CleanupResult);
                Console.WriteLine("PROBE Excel after guarded cleanup exited=" + process.WaitForExit(15000));
            }
            return 0;
        }
        finally
        {
            try { if ((object)document != null) document.Close(false); } catch { }
            if ((object)document != null) Marshal.FinalReleaseComObject((object)document);
            session.Dispose();
        }
    }

    private static int CleanupOwnedOffice()
    {
        var seen = new HashSet<int>();
        foreach (string marker in Directory.GetFiles(@"D:\codex\Orielfolder-development\document-preview-office-tests", "office-owner.json", SearchOption.AllDirectories))
        {
            var owner = new JavaScriptSerializer().Deserialize<Owner>(File.ReadAllText(marker));
            if (owner.Name != "WINWORD" && owner.Name != "EXCEL") continue;
            dynamic application = null;
            try
            {
                using (var process = Process.GetProcessById(owner.Pid))
                {
                    if (process.StartTime.ToUniversalTime().Ticks != owner.StartTicks || process.ProcessName != owner.Name || !seen.Add(owner.Pid)) continue;
                    application = FindMarkedApplication(process.Id);
                    if ((object)application == null)
                    {
                        Console.WriteLine("PRESERVED marked " + owner.Name + " pid=" + owner.Pid + ": no verified native application binding");
                        continue;
                    }
                    int documents = owner.Name == "WINWORD" ? (int)application.Documents.Count : (int)application.Workbooks.Count;
                    if (documents != 0 || (bool)application.Visible || (bool)application.UserControl ||
                        process.HasExited || process.StartTime.ToUniversalTime().Ticks != owner.StartTicks)
                    {
                        Console.WriteLine("PRESERVED marked " + owner.Name + " pid=" + owner.Pid + ": document/visibility/user-control guard");
                        continue;
                    }
                    if (owner.Name == "WINWORD") application.Quit(0);
                    else application.Quit();
                    Console.WriteLine("GRACEFUL marked " + owner.Name + " pid=" + owner.Pid + "; exited=" + process.WaitForExit(5000));
                }
            }
            catch (ArgumentException) { }
            catch (Exception failure) { Console.WriteLine("PRESERVED marked " + owner.Name + " pid=" + owner.Pid + ": binding/guard failure=" + failure.GetType().Name); }
            finally { if ((object)application != null && Marshal.IsComObject((object)application)) Marshal.FinalReleaseComObject((object)application); }
        }
        foreach (string name in new[] { "WINWORD", "EXCEL" })
            foreach (var process in Process.GetProcessesByName(name))
                using (process) if (!seen.Contains(process.Id)) Console.WriteLine("PRESERVED unmarked " + name + " pid=" + process.Id);
        return 0;
    }

    private static object FindMarkedApplication(int processId)
    {
        object application = null;
        bool attempted = false;
        EnumWindows((top, ignored) =>
        {
            uint pid;
            GetWindowThreadProcessId(top, out pid);
            if (pid != processId) return true;
            EnumChildWindows(top, (child, unused) =>
            {
                GetWindowThreadProcessId(child, out pid);
                if (pid != processId) return true;
                var text = new StringBuilder(64);
                GetClassName(child, text, text.Capacity);
                if (text.ToString() != "MsoCommandBar") return true;
                attempted = true;
                object commandBar;
                Guid iid = new Guid("00020400-0000-0000-C000-000000000046");
                if (AccessibleObjectFromWindow(child, 0xfffffff0, ref iid, out commandBar) == 0)
                {
                    try
                    {
                        var pia = Type.GetType("Microsoft.Office.Core.CommandBar, office, Version=15.0.0.0, Culture=neutral, PublicKeyToken=71e9bce111e9429c", true);
                        application = pia.GetProperty("Application").GetValue(commandBar, null);
                    }
                    catch { }
                    finally { if (commandBar != null && Marshal.IsComObject(commandBar)) Marshal.FinalReleaseComObject(commandBar); }
                }
                return false; // One bounded documented NativeOM binding attempt per process.
            }, IntPtr.Zero);
            return !attempted;
        }, IntPtr.Zero);
        return application;
    }

    private static int InspectPowerPoint()
    {
        dynamic application = null;
        try
        {
            application = Marshal.GetActiveObject("PowerPoint.Application");
            Console.WriteLine("READ-ONLY PowerPoint ROT available; version=" + (string)application.Version + "; presentations=" + (int)application.Presentations.Count);
            uint pid;
            int hwnd = OfficeSession.PowerPointWindow((object)application);
            GetWindowThreadProcessId(new IntPtr(hwnd), out pid);
            Console.WriteLine("READ-ONLY PIA vtable HWND=" + hwnd + "; pid=" + pid);
            return 0;
        }
        catch (Exception error) { Console.WriteLine("READ-ONLY inspection failure: " + error.GetType().Name + "; HRESULT=" + error.HResult.ToString("X8")); return 1; }
        finally
        {
            if ((object)application != null && Marshal.IsComObject((object)application)) Marshal.FinalReleaseComObject((object)application);
        }
    }

    private static void ConvertSources(string workerPath, bool powerPoint)
    {
            foreach (string source in Directory.GetFiles(root).Where(path => Path.GetExtension(path).StartsWith(".ppt", StringComparison.OrdinalIgnoreCase) == powerPoint))
            {
                string folder = Path.Combine(root, Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(folder);
                string input = Path.Combine(folder, "source" + Path.GetExtension(source));
                File.Copy(source, input);
                byte[] before = File.ReadAllBytes(source);
                DateTime time = File.GetLastWriteTimeUtc(source);
                DateTime inputTime = File.GetLastWriteTimeUtc(input);
                string output = Path.Combine(folder, "preview.pdf"), marker = Path.Combine(folder, "office-owner.json");
                var start = new ProcessStartInfo(workerPath, "\"" + input + "\" \"" + output + "\" \"" + marker + "\"")
                { UseShellExecute = false, CreateNoWindow = true };
                using (var worker = Process.Start(start))
                {
                    if (!worker.WaitForExit(45000)) { worker.Kill(); throw new Exception("Worker timed out: " + Path.GetFileName(source)); }
                    if (worker.ExitCode != 0) throw new Exception("Worker failed: " + Path.GetFileName(source) + " code " + worker.ExitCode);
                }
                byte[] pdf = File.ReadAllBytes(output);
                if (System.Text.Encoding.ASCII.GetString(pdf, 0, 5) != "%PDF-") throw new Exception("Not a PDF.");
                if (!before.SequenceEqual(File.ReadAllBytes(source)) || File.GetLastWriteTimeUtc(source) != time)
                    throw new Exception("Source modified.");
                if (!before.SequenceEqual(File.ReadAllBytes(input)) || File.GetLastWriteTimeUtc(input) != inputTime)
                    throw new Exception("Staged input modified.");
                var owner = new JavaScriptSerializer().Deserialize<Owner>(File.ReadAllText(marker));
                if (owner.AllowTerminate) throw new Exception("Office termination must never be authorized.");
                try
                {
                    using (var office = Process.GetProcessById(owner.Pid))
                        if (office.StartTime.ToUniversalTime().Ticks == owner.StartTicks && !office.WaitForExit(15000))
                            throw new Exception("Private empty Office instance did not exit after graceful cleanup.");
                }
                catch (ArgumentException) { }
                Console.WriteLine("PASS " + Path.GetFileName(source) + ": PDF bytes=" + pdf.Length + "; original and staged input unchanged; no forced Office termination; owned Office exited");
            }
    }

    private static void Create(string name, string progId)
    {
        Console.WriteLine("Creating synthetic fixtures: " + name);
        dynamic app = null, doc = null;
        OfficeSession session = null;
        try
        {
            session = OfficeSession.Create(name, progId);
            app = session.Application;
            app.AutomationSecurity = 3;
            if (name == "WINWORD")
            {
                app.Visible = false; app.DisplayAlerts = 0;
                doc = app.Documents.Add();
                doc.Content.Text = "SPAN synthetic Word\r\nSelectable text 123\r\nSecond paragraph";
                doc.SaveAs2(Path.Combine(root, "word.docx"), 16);
                doc.SaveAs2(Path.Combine(root, "word.doc"), 0);
            }
            else if (name == "EXCEL")
            {
                app.Visible = false; app.DisplayAlerts = false;
                doc = app.Workbooks.Add();
                dynamic sheet = doc.Worksheets[1];
                sheet.Cells[1, 1].Value2 = "SPAN synthetic Excel";
                sheet.Cells[2, 1].Value2 = "Selectable text";
                sheet.Cells[2, 2].Value2 = 123;
                doc.SaveAs(Path.Combine(root, "excel.xlsx"), 51);
                doc.SaveAs(Path.Combine(root, "excel.xls"), 56);
                Marshal.FinalReleaseComObject(sheet);
            }
            else
            {
                app.DisplayAlerts = 1;
                doc = app.Presentations.Add(0);
                dynamic slide = doc.Slides.Add(1, 12);
                dynamic shape = slide.Shapes.AddTextbox(1, 50, 60, 500, 200);
                shape.TextFrame.TextRange.Text = "SPAN synthetic PowerPoint selectable text 123";
                doc.SaveAs(Path.Combine(root, "powerpoint.pptx"), 24);
                doc.SaveAs(Path.Combine(root, "powerpoint.ppt"), 1);
                Marshal.FinalReleaseComObject(shape); Marshal.FinalReleaseComObject(slide);
                doc.Close();
                Marshal.FinalReleaseComObject((object)doc);
                doc = null;
                // The captured session has already passed independent PID ownership checks.
                // Reuse it to test both actual file formats without another user-close cycle.
                foreach (string source in Directory.GetFiles(root, "powerpoint.ppt*"))
                {
                    string folder = Path.Combine(root, Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(folder);
                    string input = Path.Combine(folder, "source" + Path.GetExtension(source));
                    File.Copy(source, input);
                    byte[] before = File.ReadAllBytes(source);
                    DateTime time = File.GetLastWriteTimeUtc(source), inputTime = File.GetLastWriteTimeUtc(input);
                    string output = Path.Combine(folder, "preview.pdf");
                    Program.ExportPresentation(app, input, output);
                    byte[] pdf = File.ReadAllBytes(output);
                    if (pdf.Length < 5 || System.Text.Encoding.ASCII.GetString(pdf, 0, 5) != "%PDF-") throw new Exception("Not a PDF.");
                    if (!before.SequenceEqual(File.ReadAllBytes(source)) || File.GetLastWriteTimeUtc(source) != time ||
                        !before.SequenceEqual(File.ReadAllBytes(input)) || File.GetLastWriteTimeUtc(input) != inputTime)
                        throw new Exception("PowerPoint source or staged input modified.");
                    Console.WriteLine("PASS " + Path.GetFileName(source) + ": PDF bytes=" + pdf.Length + "; original and staged input unchanged; production export in captured session (not separate-worker launch)");
                }
            }
        }
        finally
        {
            if (session != null)
            {
                try { if (doc != null) { if (name == "POWERPNT") doc.Close(); else doc.Close(false); } } catch { }
            }
            if ((object)doc != null && Marshal.IsComObject((object)doc)) Marshal.FinalReleaseComObject((object)doc);
            if (session != null) session.Dispose();
        }
    }

    private sealed class Owner { public int Pid { get; set; } public long StartTicks { get; set; } public bool AllowTerminate { get; set; } public string Name { get; set; } }
}
