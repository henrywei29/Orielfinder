using System;
using System.IO;
using System.Reflection;
using Listary.FileAppPlugin.Host;

internal static class ListaryHostLoadTest
{
    private static string _listaryDirectory;

    private static int Main(string[] args)
    {
        if (args.Length != 2) return 2;
        _listaryDirectory = args[1];
        AppDomain.CurrentDomain.AssemblyResolve += ResolveInstalledAssembly;
        try { return LoadWithOfficialHost(args[0]); }
        catch (Exception ex) { Console.WriteLine("FAIL: Official host threw " + ex.GetType().Name); return 1; }
    }

    private static Assembly ResolveInstalledAssembly(object sender, ResolveEventArgs args)
    {
        string path = Path.Combine(_listaryDirectory, new AssemblyName(args.Name).Name + ".dll");
        return File.Exists(path) ? Assembly.LoadFrom(path) : null;
    }

    private static int LoadWithOfficialHost(string directory)
    {
        var host = FileAppPluginHostBase.LoadPluginFrom<ReadOnlyHost>(directory, () => new ReadOnlyHost())
            .GetAwaiter().GetResult();
        if (host == null || host.Plugin == null || host.Manifest.Id != "OrielFolder")
        {
            Console.WriteLine("FAIL: Installed Listary host could not discover and initialize the OrielFolder plugin.");
            return 1;
        }
        Console.WriteLine("PASS: Installed Listary host discovers the plugin/window/tab classes and initializes OrielFolder under .NET Framework.");
        return 0;
    }

    // These transports are not involved in discovery or this read-only plugin's Initialize.
    // Discovery/manifest parsing/feature detection/initialization all execute the installed host code.
    private sealed class ReadOnlyHost : FileAppPluginHostBase
    {
        public override bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam) { throw new NotSupportedException(); }
        public override IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam) { throw new NotSupportedException(); }
        public override IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, byte[] data) { throw new NotSupportedException(); }
        public override IntPtr SendCopyData(IntPtr window, IntPtr source, IntPtr dataId, byte[] data, uint size, uint timeout) { throw new NotSupportedException(); }
    }
}
