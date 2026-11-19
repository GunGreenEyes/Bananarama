// Bananarama tray: Settings window and the "New image" prompt.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace Bananarama
{
    static class AppIcon
    {
        public static Icon Get()
        {
            var s = typeof(AppIcon).Assembly.GetManifestResourceStream("banana.ico");
            return s != null ? new Icon(s) : SystemIcons.Application;
        }
    }

    class SettingsForm : Form
    {
        readonly SettingsStore store;
        readonly StartupEntry startup;
        readonly TextBox apiKey, folder, subfolder, prefix;
        readonly ComboBox model, aspect, size;
        readonly NumericUpDown port;
        readonly CheckBox showKey, openAfter, startWithWindows;
        readonly Label keyStatus;

        public SettingsForm(SettingsStore store, StartupEntry startup, string message)
        {
            this.store = store; this.startup = startup;
            Text = AppInfo.Name + " Settings";
            Icon = AppIcon.Get();
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen; AutoScaleMode = AutoScaleMode.Dpi;
            Font = SystemFonts.MessageBoxFont; ShowInTaskbar = true;

            var grid = new TableLayoutPanel { ColumnCount = 3, AutoSize = true, Padding = new Padding(12), Dock = DockStyle.Fill };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            if (!string.IsNullOrEmpty(message))
            {
                var banner = new Label { Text = message, AutoSize = true, MaximumSize = new Size(480, 0), Margin = new Padding(3, 0, 3, 10), Font = new Font(Font, FontStyle.Bold) };
                grid.Controls.Add(banner, 0, grid.RowCount++); grid.SetColumnSpan(banner, 3);
            }

            apiKey = new TextBox { Text = store.GetString("apiKey"), UseSystemPasswordChar = true, Dock = DockStyle.Fill };
            showKey = new CheckBox { Text = "Show", AutoSize = true };
            showKey.CheckedChanged += (s, e) => apiKey.UseSystemPasswordChar = !showKey.Checked;
            Row(grid, "Gemini API key", apiKey, showKey);

            var keyRow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0) };
            var test = new Button { Text = "Test key", AutoSize = true };
            test.Click += (s, e) => TestKey();
            var link = new LinkLabel { Text = "Get a key", AutoSize = true, Margin = new Padding(6, 8, 3, 3) };
            link.LinkClicked += (s, e) => System.Diagnostics.Process.Start(AppInfo.KeyUrl);
            keyStatus = new Label { AutoSize = true, Margin = new Padding(6, 8, 3, 3), MaximumSize = new Size(300, 0) };
            keyRow.Controls.AddRange(new Control[] { test, link, keyStatus });
            Row(grid, "", keyRow, null);

            model = Combo(SettingsStore.Models, store.GetString("model"), true);
            Row(grid, "Model", model, null);
            aspect = Combo(SettingsStore.AspectRatios, store.GetString("aspectRatio"), false);
            Row(grid, "Aspect ratio", aspect, null);
            size = Combo(SettingsStore.Sizes, store.GetString("imageSize"), false);
            Row(grid, "Image size", size, null);

            folder = new TextBox { Text = store.GetString("outputDir"), Dock = DockStyle.Fill };
            SetCue(folder, "Downloads (" + AppPaths.Downloads + ")");
            var browse = new Button { Text = "Browse…", AutoSize = true };
            browse.Click += (s, e) => Browse();
            Row(grid, "Save to folder", folder, browse);
            subfolder = new TextBox { Text = store.GetString("outputSubfolder"), Dock = DockStyle.Fill };
            Row(grid, "Subfolder", subfolder, null);
            prefix = new TextBox { Text = store.GetString("filenamePrefix"), Dock = DockStyle.Fill };
            Row(grid, "File name prefix", prefix, null);

            openAfter = new CheckBox { Text = "Open each image after saving", Checked = store.GetBool("openAfterSave"), AutoSize = true };
            Row(grid, "", openAfter, null);
            startWithWindows = new CheckBox { Text = "Start Bananarama with Windows", Checked = startup.Enabled, AutoSize = true };
            Row(grid, "", startWithWindows, null);

            port = new NumericUpDown { Minimum = 1024, Maximum = 65535, Value = Math.Max(1024, Math.Min(65535, store.GetInt("port", 7861))), Width = 90 };
            Row(grid, "Port", port, null);

            var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 12, 0, 0) };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
            var save = new Button { Text = "Save", AutoSize = true };
            save.Click += (s, e) => Save();
            var fileLink = new LinkLabel { Text = "Open settings.json", AutoSize = true, Margin = new Padding(3, 8, 20, 3) };
            fileLink.LinkClicked += (s, e) => { if (File.Exists(store.FilePath)) System.Diagnostics.Process.Start("notepad.exe", "\"" + store.FilePath + "\""); };
            buttons.Controls.AddRange(new Control[] { cancel, save, fileLink });
            grid.Controls.Add(buttons, 0, grid.RowCount++); grid.SetColumnSpan(buttons, 3);

            AcceptButton = save; CancelButton = cancel;
            AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Controls.Add(grid);
        }

        static void Row(TableLayoutPanel grid, string label, Control field, Control extra)
        {
            int row = grid.RowCount++;
            grid.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 7, 10, 3) }, 0, row);
            grid.Controls.Add(field, 1, row);
            if (extra != null) grid.Controls.Add(extra, 2, row);
        }

        static ComboBox Combo(string[] items, string value, bool editable)
        {
            var c = new ComboBox { DropDownStyle = editable ? ComboBoxStyle.DropDown : ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
            c.Items.AddRange(items);
            if (!items.Contains(value) && !string.IsNullOrEmpty(value)) c.Items.Add(value);
            if (editable) c.Text = value; else c.SelectedItem = string.IsNullOrEmpty(value) ? items[0] : value;
            return c;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, string l);
        static void SetCue(TextBox t, string cue) { t.HandleCreated += (s, e) => SendMessage(t.Handle, 0x1501, (IntPtr)1, cue); }

        void Browse()
        {
            using (var d = new FolderBrowserDialog { Description = "Save images to", SelectedPath = folder.Text.Length > 0 ? folder.Text : AppPaths.Downloads })
                if (d.ShowDialog(this) == DialogResult.OK) folder.Text = d.SelectedPath == AppPaths.Downloads ? "" : d.SelectedPath;
        }

        void TestKey()
        {
            keyStatus.ForeColor = SystemColors.ControlText;
            var key = apiKey.Text.Trim();
            if (key.Length == 0) { keyStatus.Text = "Enter a key first."; return; }
            Cursor = Cursors.WaitCursor; keyStatus.Text = "Checking…"; keyStatus.Refresh();
            try
            {
                ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; // TLS 1.2
                var req = (HttpWebRequest)WebRequest.Create(store.GetString("apiBase").TrimEnd('/') + "/models?pageSize=200");
                req.Headers["x-goog-api-key"] = key; req.Timeout = 15000;
                using (var res = req.GetResponse())
                using (var r = new StreamReader(res.GetResponseStream()))
                {
                    var body = new JavaScriptSerializer().DeserializeObject(r.ReadToEnd()) as Dictionary<string, object>;
                    var names = ((object[])body["models"]).Cast<Dictionary<string, object>>().Select(m => ((string)m["name"]).Replace("models/", "")).ToList();
                    bool has = names.Contains(model.Text.Trim());
                    keyStatus.ForeColor = has ? Color.ForestGreen : Color.DarkOrange;
                    keyStatus.Text = has ? "Key works ✓" : "Key works, but " + model.Text + " isn't available to it.";
                }
            }
            catch (WebException e)
            {
                var http = e.Response as HttpWebResponse;
                keyStatus.ForeColor = Color.Firebrick;
                keyStatus.Text = http != null && (int)http.StatusCode == 400 ? "That key isn't valid." : "Couldn't check: " + e.Message;
            }
            catch (Exception e) { keyStatus.ForeColor = Color.Firebrick; keyStatus.Text = "Couldn't check: " + e.Message; }
            finally { Cursor = Cursors.Default; }
        }

        void Save()
        {
            store.Set("apiKey", apiKey.Text.Trim());
            store.Set("model", model.Text.Trim().Length > 0 ? model.Text.Trim() : SettingsStore.Models[0]);
            store.Set("aspectRatio", (string)aspect.SelectedItem);
            store.Set("imageSize", (string)size.SelectedItem);
            store.Set("outputDir", folder.Text.Trim());
            store.Set("outputSubfolder", subfolder.Text.Trim());
            store.Set("filenamePrefix", prefix.Text.Trim().Length > 0 ? prefix.Text.Trim() : "image");
            store.Set("openAfterSave", openAfter.Checked);
            store.Set("port", (int)port.Value);
            try
            {
                store.Save();
                if (startWithWindows.Checked != startup.Enabled) startup.Enabled = startWithWindows.Checked;
            }
            catch (Exception e) { MessageBox.Show(this, "Could not save: " + e.Message, AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    class PromptForm : Form
    {
        readonly TextBox prompt;
        readonly ComboBox aspect, size;

        public string Prompt { get { return prompt.Text.Trim(); } }

        /// <summary>CLI flags for the options picked here ("" = use settings).</summary>
        public string ExtraArgs
        {
            get
            {
                var a = "";
                if (aspect.SelectedIndex > 0) a += " --ar " + aspect.SelectedItem;
                if (size.SelectedIndex > 0) a += " --size " + size.SelectedItem;
                return a.Trim();
            }
        }

        public PromptForm()
        {
            Text = "New image — " + AppInfo.Name;
            Icon = AppIcon.Get();
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen; AutoScaleMode = AutoScaleMode.Dpi; Font = SystemFonts.MessageBoxFont;
            TopMost = true;

            var panel = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, Padding = new Padding(12), Dock = DockStyle.Fill };
            panel.Controls.Add(new Label { Text = "Describe the image:", AutoSize = true });
            prompt = new TextBox { Multiline = true, Width = 420, Height = 80, ScrollBars = ScrollBars.Vertical };
            panel.Controls.Add(prompt);

            var opts = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 6, 0, 0) };
            aspect = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
            aspect.Items.Add("Default ratio"); aspect.Items.AddRange(SettingsStore.AspectRatios); aspect.SelectedIndex = 0;
            size = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
            size.Items.Add("Default size"); size.Items.AddRange(SettingsStore.Sizes); size.SelectedIndex = 0;
            opts.Controls.AddRange(new Control[] { aspect, size });
            panel.Controls.Add(opts);

            var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 10, 0, 0) };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
            var go = new Button { Text = "Make it", AutoSize = true };
            go.Click += (s, e) => { if (Prompt.Length > 0) { DialogResult = DialogResult.OK; Close(); } else prompt.Focus(); };
            buttons.Controls.AddRange(new Control[] { cancel, go });
            panel.Controls.Add(buttons);

            AcceptButton = null; CancelButton = cancel; // Enter makes a new line in the prompt
            AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Controls.Add(panel);
            prompt.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter && e.Control) go.PerformClick(); };
        }
    }
}
