// Bananarama tray: entry point, tray icon and menu.
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace Bananarama
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            bool first;
            using (var mutex = new Mutex(true, @"Local\Bananarama.Tray", out first))
            {
                if (!first) return; // already in the tray
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayContext(Array.IndexOf(args, "--settings") >= 0));
            }
        }
    }

    class TrayContext : ApplicationContext
    {
        const string TestPrompt = "a banana doing a victory dance, confetti, cartoon";

        readonly NotifyIcon icon;
        readonly AppPaths paths;
        readonly ServerHost server;
        readonly StartupEntry startup;
        readonly SynchronizationContext ui;
        readonly System.Threading.Timer poller;
        readonly Icon onIcon, offIcon;
        readonly ToolStripMenuItem statusItem, startupItem, newImageItem, testItem;
        string lastSaved;
        SettingsForm settingsForm;
        int busy;

        public TrayContext(bool openSettings)
        {
            ui = new WindowsFormsSynchronizationContext();
            SynchronizationContext.SetSynchronizationContext(ui);
            paths = new AppPaths(AppDomain.CurrentDomain.BaseDirectory);
            startup = new StartupEntry(Application.ExecutablePath);
            server = new ServerHost(paths, LoadSettings);
            onIcon = LoadIcon("banana.ico"); offIcon = LoadIcon("banana-off.ico");

            var menu = new ContextMenuStrip();
            var title = new ToolStripMenuItem(AppInfo.Name + " " + AppInfo.Version) { Enabled = false };
            statusItem = new ToolStripMenuItem("Starting…") { Enabled = false };
            newImageItem = new ToolStripMenuItem("New image…", null, (s, e) => NewImage());
            var settingsItem = new ToolStripMenuItem("Settings…", null, (s, e) => ShowSettings(null));
            settingsItem.Font = new Font(settingsItem.Font, FontStyle.Bold);
            var folderItem = new ToolStripMenuItem("Open images folder", null, (s, e) => OpenFolder());
            testItem = new ToolStripMenuItem("Make a test image", null, (s, e) => Generate(TestPrompt, null));
            var restartItem = new ToolStripMenuItem("Restart middleware", null, (s, e) => Background(server.Restart));
            var logItem = new ToolStripMenuItem("Open log", null, (s, e) => OpenLog());
            var claudeItem = new ToolStripMenuItem("Connect to Claude", null, (s, e) => RegisterWithClaude());
            startupItem = new ToolStripMenuItem("Start with Windows", null, (s, e) => ToggleStartup());
            var siteItem = new ToolStripMenuItem("Website", null, (s, e) => Open(AppInfo.Website));
            var quitItem = new ToolStripMenuItem("Quit", null, (s, e) => Quit());
            menu.Items.AddRange(new ToolStripItem[] {
                title, statusItem, new ToolStripSeparator(),
                newImageItem, testItem, folderItem, new ToolStripSeparator(),
                settingsItem, startupItem, claudeItem, new ToolStripSeparator(),
                restartItem, logItem, siteItem, new ToolStripSeparator(), quitItem });
            menu.Opening += (s, e) => { startupItem.Checked = startup.Enabled; UpdateStatus(); };

            icon = new NotifyIcon { Icon = offIcon, Text = AppInfo.Name, ContextMenuStrip = menu, Visible = true };
            icon.DoubleClick += (s, e) => ShowSettings(null);
            icon.BalloonTipClicked += (s, e) => { if (lastSaved != null && File.Exists(lastSaved)) Open(lastSaved); };

            server.StateChanged += (s, e) => ui.Post(_ => UpdateStatus(), null);
            Background(server.Start);
            poller = new System.Threading.Timer(_ => server.Poll(), null, 3000, 5000);

            SettingsStore settings = null;
            try { settings = LoadSettings(); } catch (Exception e) { ShowError("Your settings file could not be read:\n" + e.Message); }
            if (openSettings) ShowSettings(null);
            else if (settings != null && !settings.HasApiKey)
                ShowSettings("Welcome to Bananarama! Paste your Gemini API key to start making images.");
        }

        static SettingsStore LoadSettings() { return SettingsStore.Load(AppPaths.SettingsFile); }

        Icon LoadIcon(string name)
        {
            var s = typeof(TrayContext).Assembly.GetManifestResourceStream(name);
            return s != null ? new Icon(s, SystemInformation.SmallIconSize) : SystemIcons.Application;
        }

        void UpdateStatus()
        {
            string text;
            switch (server.State)
            {
                case ServerState.Running: text = "Running on " + server.Endpoint; break;
                case ServerState.Starting: text = "Starting…"; break;
                case ServerState.Failed: text = "Error: " + server.LastError; break;
                default: text = "Stopped"; break;
            }
            if (busy > 0) text = "Making an image…";
            statusItem.Text = text;
            icon.Icon = server.State == ServerState.Running ? onIcon : offIcon;
            var tip = AppInfo.Name + " " + AppInfo.Version + " — " + text;
            icon.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip;
            newImageItem.Enabled = testItem.Enabled = busy == 0;
        }

        void Background(Action work)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try { work(); }
                catch (Exception e) { ServerHost.Log("[tray] " + e); }
                ui.Post(__ => UpdateStatus(), null);
            });
        }

        void ShowSettings(string message)
        {
            if (settingsForm != null && !settingsForm.IsDisposed) { settingsForm.Activate(); return; }
            SettingsStore s;
            try { s = LoadSettings(); }
            catch (Exception e) { ShowError("Your settings file could not be read:\n" + e.Message + "\n\n" + AppPaths.SettingsFile); return; }
            string oldEndpoint = server.Endpoint;
            settingsForm = new SettingsForm(s, startup, message);
            settingsForm.FormClosed += (o, e) =>
            {
                bool saved = settingsForm.DialogResult == DialogResult.OK;
                settingsForm = null;
                if (saved && server.Endpoint != oldEndpoint) Background(server.Restart);
            };
            settingsForm.Show();
            settingsForm.Activate();
        }

        void NewImage()
        {
            using (var f = new PromptForm())
            {
                if (f.ShowDialog() == DialogResult.OK) Generate(f.Prompt, f.ExtraArgs);
            }
        }

        void Generate(string prompt, string extraArgs)
        {
            Interlocked.Increment(ref busy);
            UpdateStatus();
            Background(() =>
            {
                string stdout, stderr;
                int code = NodeRunner.Run(paths, "cli.js", (extraArgs ?? "") + " " + NodeRunner.Quote(prompt), out stdout, out stderr, 5 * 60 * 1000);
                var file = NodeRunner.SavedPath(stdout);
                Interlocked.Decrement(ref busy);
                ui.Post(_ =>
                {
                    if (code == 0 && file != null)
                    {
                        lastSaved = file;
                        icon.ShowBalloonTip(5000, "Image saved", Path.GetFileName(file) + "\nClick to open.", ToolTipIcon.Info);
                    }
                    else
                    {
                        lastSaved = null;
                        var msg = (stderr ?? "").Trim();
                        int i = msg.LastIndexOf("ERROR: ", StringComparison.Ordinal);
                        icon.ShowBalloonTip(8000, "Image failed", i >= 0 ? msg.Substring(i + 7) : (msg.Length > 0 ? msg : "unknown error"), ToolTipIcon.Error);
                    }
                }, null);
            });
        }

        void RegisterWithClaude()
        {
            Background(() =>
            {
                string stdout, stderr;
                int code = NodeRunner.Run(paths, "setup.js", "install", out stdout, out stderr, 60000);
                ui.Post(_ => icon.ShowBalloonTip(6000, code == 0 ? "Connected to Claude" : "Could not connect to Claude",
                    code == 0 ? "Added /image to Claude Desktop and Claude Code. Fully quit and reopen Claude Desktop to load it." : stderr.Trim(),
                    code == 0 ? ToolTipIcon.Info : ToolTipIcon.Error), null);
            });
        }

        void ToggleStartup()
        {
            try { startup.Enabled = !startup.Enabled; }
            catch (Exception e) { ShowError("Could not change the startup setting:\n" + e.Message); }
        }

        void OpenFolder()
        {
            try
            {
                var dir = LoadSettings().OutputFolder;
                Directory.CreateDirectory(dir);
                Open(dir);
            }
            catch (Exception e) { ShowError(e.Message); }
        }

        void OpenLog()
        {
            if (!File.Exists(AppPaths.LogFile)) ServerHost.Log("[tray] log created");
            Open(AppPaths.LogFile);
        }

        static void Open(string target)
        {
            try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
            catch (Exception e) { ShowError(e.Message); }
        }

        static void ShowError(string message)
        {
            MessageBox.Show(message, AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        void Quit()
        {
            poller.Dispose();
            icon.Visible = false;
            server.Stop();
            ExitThread();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { icon.Dispose(); server.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
