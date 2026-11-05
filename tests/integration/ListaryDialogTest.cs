using System;
using System.IO;
using System.Windows.Forms;

internal static class ListaryDialogTest
{
    [STAThread]
    private static void Main(string[] args)
    {
        string directory = args.Length == 1 ? args[0] : @"D:\codex\Orielfolder-development\manual-data\";
        if (!Path.GetFullPath(directory).StartsWith(
            @"D:\codex\Orielfolder-development\manual-data\", StringComparison.OrdinalIgnoreCase)) return;
        Application.EnableVisualStyles();
        using (var owner = new Form())
        {
            owner.Text = "SPAN Dev - Listary Test";
            owner.Width = 1000;
            owner.Height = 720;
            owner.StartPosition = FormStartPosition.CenterScreen;
            owner.Shown += delegate
            {
                using (var dialog = new SaveFileDialog())
                {
                    dialog.Title = "SPAN Dev - Listary Save Dialog Test";
                    dialog.InitialDirectory = directory;
                    dialog.FileName = "Listary-save-check.txt";
                    dialog.Filter = "Text files (*.txt)|*.txt";
                    // This host never writes a file, including when the dialog returns OK.
                    dialog.ShowDialog(owner);
                }
                owner.Close();
            };
            Application.Run(owner);
        }
    }
}
