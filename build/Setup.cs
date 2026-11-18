// Installationsprogramm fuer Evo-WarPath (Evolutionary Warpath).
// Das Spiel ist als Spiel.zip eingebettet. Installation ohne Adminrechte nach %LOCALAPPDATA%\Programs\Evo-WarPath,
// Verknuepfungen auf dem Desktop und im Startmenue, Eintrag unter "Apps" (Deinstallieren moeglich).
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

static class Setup
{
    const string Name = "Evo-WarPath";
    const string Title = "Evolutionary Warpath";
    const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Evo-WarPath";

    static string testTarget;   // Testmodus: nur entpacken (keine Verknuepfungen, keine Registry)
    static string Target { get { return testTarget ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", Name); } }
    static string Exe { get { return Path.Combine(Target, Name + ".exe"); } }
    static string DesktopLink { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Name + ".lnk"); } }
    static string StartLink { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), Name + ".lnk"); } }

    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "/test")
        {
            testTarget = Path.GetFullPath(args[1]);
            try { Install(p => { }); return 0; } catch (Exception e) { File.WriteAllText(Path.Combine(Path.GetTempPath(), "evo-setup-test.txt"), e.ToString()); return 2; }
        }
        Application.EnableVisualStyles();
        var ask = MessageBox.Show(Title + " wird installiert.\n\nZiel: " + Target + "\nDazu kommen Verknüpfungen auf dem Desktop und im Startmenü.\n\nFortfahren?",
            Title + " – Installation", MessageBoxButtons.OKCancel, MessageBoxIcon.Information);
        if (ask != DialogResult.OK) return 1;

        var form = new Form { Text = Title + " – Installation", Width = 460, Height = 140, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, StartPosition = FormStartPosition.CenterScreen };
        var label = new Label { Left = 16, Top = 14, Width = 410, Text = "Dateien werden entpackt …" };
        var bar = new ProgressBar { Left = 16, Top = 44, Width = 410, Height = 22, Minimum = 0, Maximum = 100 };
        form.Controls.Add(label); form.Controls.Add(bar);
        string error = null;
        var worker = new Thread(() =>
        {
            try { Install(p => form.BeginInvoke((Action)(() => { bar.Value = Math.Min(100, p); label.Text = p < 100 ? "Dateien werden entpackt … " + p + " %" : "Verknüpfungen werden angelegt …"; }))); }
            catch (Exception e) { error = e.Message; }
            form.BeginInvoke((Action)(() => form.Close()));
        });
        form.Shown += (s, e) => worker.Start();
        Application.Run(form);
        worker.Join();

        if (error != null)
        {
            MessageBox.Show("Die Installation ist fehlgeschlagen:\n\n" + error + "\n\nLäuft das Spiel noch? Dann bitte schließen und das Setup erneut starten.", Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 2;
        }
        var start = MessageBox.Show(Title + " wurde installiert.\n\nJetzt starten?", Title, MessageBoxButtons.YesNo, MessageBoxIcon.Information);
        if (start == DialogResult.Yes) Process.Start(new ProcessStartInfo(Exe) { WorkingDirectory = Target });
        return 0;
    }

    static void Install(Action<int> progress)
    {
        // alte Version entfernen (Spielstaende liegen nicht im Spielordner, bleiben also erhalten)
        if (Directory.Exists(Target)) Directory.Delete(Target, true);
        Directory.CreateDirectory(Target);

        using (var res = Assembly.GetExecutingAssembly().GetManifestResourceStream("Spiel.zip"))
        using (var zip = new ZipArchive(res, ZipArchiveMode.Read))
        {
            int total = zip.Entries.Count, done = 0;
            foreach (var entry in zip.Entries)
            {
                string path = Path.GetFullPath(Path.Combine(Target, entry.FullName));
                if (!path.StartsWith(Target, StringComparison.OrdinalIgnoreCase)) continue;
                if (entry.FullName.EndsWith("/") || entry.FullName.EndsWith("\\")) { Directory.CreateDirectory(path); continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                entry.ExtractToFile(path, true);
                progress(++done * 100 / Math.Max(1, total));
            }
        }
        progress(100);

        if (testTarget != null) return;
        string icon = File.Exists(Path.Combine(Target, Name + ".ico")) ? Path.Combine(Target, Name + ".ico") : Exe;
        Shortcut(DesktopLink, icon);
        Shortcut(StartLink, icon);

        // Deinstallation: kleines Skript im Spielordner + Eintrag unter "Apps"
        string uninstall = Path.Combine(Target, "Deinstallieren.cmd");
        File.WriteAllText(uninstall,
            "@echo off\r\n" +
            "echo Evo-WarPath wird deinstalliert ...\r\n" +
            "del \"" + DesktopLink + "\" 2>nul\r\n" +
            "del \"" + StartLink + "\" 2>nul\r\n" +
            "reg delete \"HKCU\\" + UninstallKey + "\" /f >nul 2>nul\r\n" +
            "cd /d \"%TEMP%\"\r\n" +
            "start \"\" /b cmd /c \"timeout /t 2 >nul & rmdir /s /q \"" + Target + "\"\"\r\n");
        using (var key = Registry.CurrentUser.CreateSubKey(UninstallKey))
        {
            key.SetValue("DisplayName", Title + " (Evo-WarPath)");
            key.SetValue("DisplayIcon", icon);
            key.SetValue("InstallLocation", Target);
            key.SetValue("UninstallString", "cmd /c \"" + uninstall + "\"");
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            long size = 0; foreach (var f in new DirectoryInfo(Target).GetFiles("*", SearchOption.AllDirectories)) size += f.Length;
            key.SetValue("EstimatedSize", (int)(size / 1024), RegistryValueKind.DWord);
        }
    }

    static void Shortcut(string lnk, string icon)
    {
        var type = Type.GetTypeFromProgID("WScript.Shell");
        object shell = Activator.CreateInstance(type);
        object link = type.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnk });
        var lt = link.GetType();
        lt.InvokeMember("TargetPath", BindingFlags.SetProperty, null, link, new object[] { Exe });
        lt.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, link, new object[] { Target });
        lt.InvokeMember("IconLocation", BindingFlags.SetProperty, null, link, new object[] { icon + ",0" });
        lt.InvokeMember("Description", BindingFlags.SetProperty, null, link, new object[] { Title });
        lt.InvokeMember("Save", BindingFlags.InvokeMethod, null, link, null);
    }
}
