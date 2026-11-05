using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace Span.DocumentPreview.Worker
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length != 3) return 2;
            string input = Path.GetFullPath(args[0]);
            string output = Path.GetFullPath(args[1]);
            string ownerFile = Path.GetFullPath(args[2]);
            string pendingOutput = Path.Combine(Path.GetDirectoryName(output), "preview.pending.pdf");
            // The service creates one private staging folder for each request.
            if (Path.GetDirectoryName(input) != Path.GetDirectoryName(output) ||
                Path.GetDirectoryName(input) != Path.GetDirectoryName(ownerFile)) return 2;
            string extension = Path.GetExtension(input).ToLowerInvariant();
            string name, progId;
            if (extension == ".doc" || extension == ".docx") { name = "WINWORD"; progId = "Word.Application"; }
            else if (extension == ".xls" || extension == ".xlsx") { name = "EXCEL"; progId = "Excel.Application"; }
            else if (extension == ".ppt" || extension == ".pptx") { name = "POWERPNT"; progId = "PowerPoint.Application"; }
            else return 2;

            dynamic application = null;
            dynamic document = null;
            OfficeSession session = null;
            try
            {
                session = OfficeSession.Create(name, progId);
                application = session.Application;
                var marker = new JavaScriptSerializer().Serialize(new
                {
                    Pid = session.Process.Id,
                    StartTicks = session.Process.StartTime.ToUniversalTime().Ticks,
                    Name = name,
                    AllowTerminate = false
                });
                File.WriteAllText(ownerFile + ".pending", marker);
                File.Move(ownerFile + ".pending", ownerFile);

                // Force-disable macros before opening any document. These settings belong to our instance.
                application.AutomationSecurity = 3;
                if (name == "WINWORD")
                {
                    application.Visible = false;
                    application.DisplayAlerts = 0;
                    application.Options.UpdateLinksAtOpen = false;
                    application.Options.UpdateFieldsAtPrint = false;
                    application.Options.UpdateLinksAtPrint = false;
                    ReportProgress("SPAN_PREVIEW:OPENING_DOCUMENT");
                    document = application.Documents.Open(FileName: input, ConfirmConversions: false,
                        ReadOnly: true, AddToRecentFiles: false, PasswordDocument: "", WritePasswordDocument: "",
                        Visible: false, OpenAndRepair: false, NoEncodingDialog: true);
                    ReportProgress("SPAN_PREVIEW:EXPORTING");
                    document.ExportAsFixedFormat(OutputFileName: pendingOutput, ExportFormat: 17,
                        OpenAfterExport: false, IncludeDocProps: false, KeepIRM: false);
                }
                else if (name == "EXCEL")
                {
                    application.Visible = false;
                    application.DisplayAlerts = false;
                    application.EnableEvents = false;
                    application.AskToUpdateLinks = false;
                    ReportProgress("SPAN_PREVIEW:OPENING_DOCUMENT");
                    document = application.Workbooks.Open(Filename: input, UpdateLinks: 0, ReadOnly: true,
                        Password: "", WriteResPassword: "", IgnoreReadOnlyRecommended: true,
                        Notify: false, AddToMru: false);
                    ReportProgress("SPAN_PREVIEW:EXPORTING");
                    document.ExportAsFixedFormat(Type: 0, Filename: pendingOutput, Quality: 0,
                        IncludeDocProperties: false, IgnorePrintAreas: false, OpenAfterPublish: false);
                }
                else
                {
                    ExportPresentation((object)application, input, pendingOutput, ReportProgress);
                }
                if (!File.Exists(pendingOutput) || new FileInfo(pendingOutput).Length <= 5) return 20;
                File.Move(pendingOutput, output);
                return 0;
            }
            catch { return 20; } // No document contents, paths, or Office diagnostics are persisted.
            finally
            {
                // Never close documents or call Quit on an instance we did not create.
                if (session != null)
                {
                    try
                    {
                        if (document != null)
                        {
                            if (name == "POWERPNT") document.Close();
                            else document.Close(false);
                        }
                    }
                    catch { }
                }
                if ((object)document != null && Marshal.IsComObject((object)document)) Marshal.FinalReleaseComObject((object)document);
                if (session != null) session.Dispose();
            }
        }

        // Shared with the native integration harness so both legacy/current PowerPoint
        // formats can exercise production export code in one independently owned session.
        private static void ReportProgress(string stage)
        {
            // Stdout carries only these fixed milestones, never Office diagnostics.
            if (stage != "SPAN_PREVIEW:OPENING_DOCUMENT" && stage != "SPAN_PREVIEW:EXPORTING") return;
            try { Console.Out.WriteLine(stage); Console.Out.Flush(); }
            catch (IOException) { } // A closed preview must not prevent owned Office cleanup.
        }

        internal static void ExportPresentation(dynamic application, string input, string output, Action<string> progress = null)
        {
            dynamic presentation = null;
            try
            {
                application.AutomationSecurity = 3;
                application.DisplayAlerts = 1;
                if (progress != null) progress("SPAN_PREVIEW:OPENING_DOCUMENT");
                presentation = application.Presentations.Open(input, -1, -1, 0);
                // ppSaveAsPDF avoids ExportAsFixedFormat's required PrintRange COM
                // parameter. The read-only source was opened as an untitled copy.
                if (progress != null) progress("SPAN_PREVIEW:EXPORTING");
                presentation.SaveAs(output, 32);
            }
            finally
            {
                try { if ((object)presentation != null) presentation.Close(); } catch { }
                if ((object)presentation != null && Marshal.IsComObject((object)presentation))
                    Marshal.FinalReleaseComObject((object)presentation);
            }
        }
    }

    internal sealed class OfficeSession : IDisposable
    {
        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
        public dynamic Application { get; private set; }
        public Process Process { get; private set; }
        internal string CleanupResult = "preserved";
        private string name;
        private long startTicks;

        public static OfficeSession Create(string name, string progId)
        {
            var existing = new HashSet<int>();
            foreach (var item in Process.GetProcessesByName(name)) using (item) existing.Add(item.Id);
            if (name == "POWERPNT" && existing.Count != 0)
            {
                // A preceding private automation server can still be releasing COM after
                // its worker exits. Only wait; never attach to or change an existing app.
                foreach (int pid in existing)
                {
                    try { using (var prior = Process.GetProcessById(pid)) if (!prior.WaitForExit(1000)) throw new InvalidOperationException("PowerPoint is already running."); }
                    catch (ArgumentException) { }
                }
            }

            dynamic app = null, blank = null, windows = null, window = null, documents = null;
            Process process = null;
            try
            {
                var type = Type.GetTypeFromProgID(progId, true);
                long startTicks = 0;
                if (name == "POWERPNT")
                {
                    // PowerPoint is MultiUse. Snapshot/recent-start checks cannot distinguish
                    // a racing user launch. Capture the actual process we start independently.
                    string executable;
                    using (var key = Registry.ClassesRoot.OpenSubKey("CLSID\\" + type.GUID.ToString("B") + "\\LocalServer32"))
                    {
                        string command = (string)key.GetValue(null);
                        int end = command.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
                        if (end < 0) throw new InvalidOperationException("Office executable is unavailable.");
                        executable = command.Substring(0, end + 4).Trim('"', ' ');
                    }
                    // Start the registered server without opening a startup document.
                    process = Process.Start(new ProcessStartInfo(executable, "/automation")
                    { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden });
                    startTicks = process.StartTime.ToUniversalTime().Ticks;
                    process.WaitForInputIdle(10000);
                }
                // Microsoft documents Word and Excel as SingleUse: activation creates a
                // separate server. PowerPoint activation may attach to a racing user
                // launch, so only its read-only HWND may be inspected before PID proof.
                app = Activator.CreateInstance(type);
                int hwnd;
                if (name == "WINWORD")
                {
                    // Word.Application has no Hwnd property. Its Window does. This blank
                    // document belongs to the SingleUse instance just created above.
                    app.AutomationSecurity = 3;
                    documents = app.Documents;
                    // A document created with Visible:false has no native Window to query.
                    blank = documents.Add();
                    app.Visible = false;
                    windows = blank.Windows;
                    window = windows[1];
                    hwnd = (int)window.Hwnd;
                }
                else if (name == "POWERPNT")
                {
                    VerifyPowerPointProcess((object)app, process, startTicks);
                    return new OfficeSession { Application = app, Process = process, name = name, startTicks = startTicks };
                }
                else hwnd = (int)app.Hwnd;
                uint pid;
                GetWindowThreadProcessId(new IntPtr(hwnd), out pid);
                if (pid == 0 || existing.Contains((int)pid)) throw new InvalidOperationException("Office instance is shared.");
                process = Process.GetProcessById((int)pid);
                if (!process.ProcessName.Equals(name, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Office process did not match.");
                return new OfficeSession { Application = app, Process = process, name = name, startTicks = process.StartTime.ToUniversalTime().Ticks };
            }
            catch
            {
                if ((object)app != null && Marshal.IsComObject((object)app)) Marshal.FinalReleaseComObject((object)app);
                if (process != null) process.Dispose();
                throw;
            }
            finally
            {
                // Close only the exact blank document we created, never a collection entry
                // selected later after the user may have opened another document.
                try { if (blank != null) blank.Close(false); } catch { }
                Release((object)window); Release((object)windows); Release((object)blank); Release((object)documents);
            }
        }

        private static void Release(object item)
        {
            if (item != null && Marshal.IsComObject(item)) Marshal.FinalReleaseComObject(item);
        }

        internal static int PowerPointWindow(object application)
        {
            // HWND is restricted from IDispatch (DISP_E_MEMBERNOTFOUND) in Office 16.
            // Invoke the installed official PIA interface getter through its COM vtable.
            // Missing PIA support safely falls back; no package or Office settings change.
            var pia = Type.GetType("Microsoft.Office.Interop.PowerPoint._Application, Microsoft.Office.Interop.PowerPoint, Version=15.0.0.0, Culture=neutral, PublicKeyToken=71e9bce111e9429c", true);
            return (int)pia.GetProperty("HWND").GetValue(application, null);
        }

        internal static void VerifyPowerPointProcess(object application, Process process, long startTicks)
        {
            uint pid;
            GetWindowThreadProcessId(new IntPtr(PowerPointWindow(application)), out pid);
            if (pid != process.Id || process.HasExited ||
                process.StartTime.ToUniversalTime().Ticks != startTicks ||
                !process.ProcessName.Equals("POWERPNT", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("PowerPoint instance is shared.");
        }

        public void Dispose()
        {
            // The user authorized graceful cleanup only for our own empty, invisible,
            // non-user-controlled instances. Never terminate a process or quit PowerPoint.
            try
            {
                if ((name == "WINWORD" || name == "EXCEL") && (object)Application != null &&
                    !Process.HasExited && Process.StartTime.ToUniversalTime().Ticks == startTicks &&
                    Process.ProcessName.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    dynamic documents = null;
                    int count;
                    try
                    {
                        documents = name == "WINWORD" ? Application.Documents : Application.Workbooks;
                        count = (int)documents.Count;
                    }
                    finally { Release((object)documents); }
                    if (count == 0 && !(bool)Application.Visible && !(bool)Application.UserControl)
                    {
                        if (name == "WINWORD") Application.Quit(0);
                        else Application.Quit();
                        CleanupResult = "graceful Quit called";
                    }
                }
            }
            catch (Exception failure) { CleanupResult = failure.GetType().Name + ":" + failure.HResult.ToString("X8"); }
            Release((object)Application);
            Application = null;
            // Dynamic Office property calls can leave temporary RCWs. This worker owns
            // no UI and exits after one conversion, so finish releasing them here.
            GC.Collect();
            GC.WaitForPendingFinalizers();
            if (Process != null) Process.Dispose();
        }
    }
}
