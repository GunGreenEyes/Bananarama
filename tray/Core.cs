// Bananarama tray: non-UI pieces (paths, settings file, startup entry, middleware process).
// Written for the C# 5 compiler that ships with .NET Framework 4.x, so it builds on any
// Windows machine without an SDK.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace Bananarama
{
    public static class AppInfo
    {
        public const string Name = "Bananarama";
        public const string Website = "https://gungreeneyes.github.io/Bananarama/";
        public const string Repo = "https://github.com/GunGreenEyes/Bananarama";
        public const string KeyUrl = "https://aistudio.google.com/apikey";

        /// <summary>Whole-number version shown to people: "v2".</summary>
        public static string Version
        {
            get { return "v" + typeof(AppInfo).Assembly.GetName().Version.Major; }
        }
    }

    /// <summary>Where things live. Installed layout: {app}\Bananarama.exe, {app}\node\node.exe, {app}\app\src\server.js.</summary>
    public class AppPaths
    {
        public string ExeDir { get; private set; }
        public string AppRoot { get; private set; }
        public string Node { get; private set; }

        public AppPaths(string exeDir)
        {
            ExeDir = Path.GetFullPath(exeDir);
            AppRoot = FindAppRoot(ExeDir);
            Node = FindNode(ExeDir, AppRoot);
        }

        public static string FindAppRoot(string exeDir)
        {
            foreach (var c in new[] { Path.Combine(exeDir, "app"), exeDir, Path.Combine(exeDir, ".."), Path.Combine(exeDir, "..", "..") })
            {
                if (File.Exists(Path.Combine(c, "src", "server.js"))) return Path.GetFullPath(c);
            }
            return null;
        }

        public static string FindNode(string exeDir, string appRoot)
        {
            var bundled = Path.Combine(exeDir, "node", "node.exe");
            if (File.Exists(bundled)) return bundled;
            var path = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var dir in path.Split(Path.PathSeparator))
            {
                try
                {
                    var f = Path.Combine(dir.Trim().Trim('"'), "node.exe");
                    if (dir.Trim().Length > 0 && File.Exists(f)) return f;
                }
                catch (ArgumentException) { }
            }
            return null;
        }

        public string Script(string name) { return AppRoot == null ? null : Path.Combine(AppRoot, "src", name); }

        public static string DataDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppInfo.Name); }
        }

        public static string SettingsFile
        {
            get
            {
                var env = Environment.GetEnvironmentVariable("BANANARAMA_SETTINGS");
                return string.IsNullOrEmpty(env) ? Path.Combine(DataDir, "settings.json") : env;
            }
        }

        public static string LogFile { get { return Path.Combine(Path.GetDirectoryName(SettingsFile), "server.log"); } }

        [DllImport("shell32.dll")]
        static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, IntPtr token, out IntPtr path);

        /// <summary>The real Downloads folder (follows relocation to another drive / OneDrive).</summary>
        public static string Downloads
        {
            get
            {
                IntPtr p;
                if (SHGetKnownFolderPath(new Guid("374DE290-123F-4565-9164-39C4925E467B"), 0, IntPtr.Zero, out p) == 0)
                {
                    try { return Marshal.PtrToStringUni(p); } finally { Marshal.FreeCoTaskMem(p); }
                }
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            }
        }
    }

    /// <summary>settings.json, shared with the Node side. Unknown keys are kept as they are.</summary>
    public class SettingsStore
    {
        public static readonly string[] Models = { "gemini-3.1-flash-image", "gemini-3-pro-image", "gemini-3.1-flash-lite-image", "gemini-2.5-flash-image" };
        public static readonly string[] AspectRatios = { "1:1", "2:3", "3:2", "3:4", "4:3", "4:5", "5:4", "9:16", "16:9", "21:9" };
        public static readonly string[] Sizes = { "1K", "2K", "4K" };

        static readonly KeyValuePair<string, object>[] Defaults = {
            Pair("apiKey", ""), Pair("model", "gemini-3.1-flash-image"), Pair("aspectRatio", "1:1"), Pair("imageSize", "1K"),
            Pair("host", "127.0.0.1"), Pair("port", 7861), Pair("outputDir", ""), Pair("outputSubfolder", "Nano"),
            Pair("filenamePrefix", "image"), Pair("requestTimeoutMs", 180000), Pair("autoStartServer", true),
            Pair("openAfterSave", false), Pair("apiBase", "https://generativelanguage.googleapis.com/v1beta")
        };
        static KeyValuePair<string, object> Pair(string k, object v) { return new KeyValuePair<string, object>(k, v); }

        readonly List<KeyValuePair<string, object>> values = new List<KeyValuePair<string, object>>();
        public string FilePath { get; private set; }

        public SettingsStore(string file) { FilePath = file; }

        public static SettingsStore Load(string file)
        {
            var s = new SettingsStore(file);
            foreach (var d in Defaults) s.values.Add(d);
            if (File.Exists(file))
            {
                var text = File.ReadAllText(file, Encoding.UTF8).TrimStart('﻿');
                var parsed = new JavaScriptSerializer().DeserializeObject(text) as Dictionary<string, object>;
                if (parsed == null) throw new InvalidDataException(file + " is not a JSON object");
                foreach (var kv in parsed) s.Set(kv.Key, kv.Value);
            }
            return s;
        }

        public object Get(string key)
        {
            foreach (var kv in values) if (kv.Key == key) return kv.Value;
            return null;
        }

        public string GetString(string key) { var v = Get(key); return v == null ? "" : Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture); }
        public int GetInt(string key, int fallback) { int n; return int.TryParse(GetString(key), out n) ? n : fallback; }
        public bool GetBool(string key) { var v = Get(key); return v is bool ? (bool)v : string.Equals(GetString(key), "true", StringComparison.OrdinalIgnoreCase); }

        public void Set(string key, object value)
        {
            for (int i = 0; i < values.Count; i++)
                if (values[i].Key == key) { values[i] = Pair(key, value); return; }
            values.Add(Pair(key, value));
        }

        public bool HasApiKey
        {
            get { return GetString("apiKey").Trim().Length > 0 || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GEMINI_API_KEY")); }
        }

        public string OutputFolder
        {
            get
            {
                var dir = GetString("outputDir").Trim();
                if (dir.Length == 0) dir = AppPaths.Downloads;
                var sub = GetString("outputSubfolder").Trim();
                return sub.Length == 0 ? dir : Path.Combine(dir, sub);
            }
        }

        public string ToJson()
        {
            var js = new JavaScriptSerializer();
            var sb = new StringBuilder("{\n");
            for (int i = 0; i < values.Count; i++)
            {
                sb.Append("  ").Append(js.Serialize(values[i].Key)).Append(": ").Append(js.Serialize(values[i].Value));
                sb.Append(i < values.Count - 1 ? ",\n" : "\n");
            }
            return sb.Append("}\n").ToString();
        }

        public void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, ToJson(), new UTF8Encoding(false));
            if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null); else File.Move(tmp, FilePath);
        }
    }

    /// <summary>The "start with Windows" entry under HKCU\...\Run.</summary>
    public class StartupEntry
    {
        public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        readonly string keyPath, valueName, command;

        public StartupEntry(string exePath) : this(RunKey, AppInfo.Name, exePath) { }
        public StartupEntry(string keyPath, string valueName, string exePath)
        {
            this.keyPath = keyPath; this.valueName = valueName; command = "\"" + exePath + "\"";
        }

        public bool Enabled
        {
            get
            {
                using (var k = Registry.CurrentUser.OpenSubKey(keyPath))
                    return k != null && k.GetValue(valueName) != null;
            }
            set
            {
                using (var k = Registry.CurrentUser.CreateSubKey(keyPath))
                {
                    if (value) k.SetValue(valueName, command);
                    else if (k.GetValue(valueName) != null) k.DeleteValue(valueName);
                }
            }
        }

        public string Command { get { return command; } }
    }

    public enum ServerState { Starting, Running, Stopped, Failed }

    /// <summary>Runs `node src\server.js` and keeps it alive. The child is in a job object, so it dies with the tray.</summary>
    public class ServerHost : IDisposable
    {
        readonly AppPaths paths;
        readonly Func<SettingsStore> settings;
        Process child;
        bool stopping;
        readonly List<DateTime> restarts = new List<DateTime>();
        IntPtr job = IntPtr.Zero;

        public ServerState State { get; private set; }
        public string LastError { get; private set; }
        public event EventHandler StateChanged;

        public ServerHost(AppPaths paths, Func<SettingsStore> settings)
        {
            this.paths = paths; this.settings = settings; State = ServerState.Stopped;
        }

        public string Endpoint
        {
            get { var s = settings(); return string.Format("{0}:{1}", s.GetString("host"), s.GetInt("port", 7861)); }
        }

        /// <summary>GET /health. Returns the middleware's pid, or 0 if nothing (or something else) answers.</summary>
        public int Health()
        {
            string name;
            int pid = Probe(out name);
            return name == "Bananarama" ? pid : 0;
        }

        /// <summary>Who answers /health: name and pid ("BananaInterop" = the v1 middleware).</summary>
        int Probe(out string name)
        {
            name = null;
            try
            {
                var req = (HttpWebRequest)WebRequest.Create("http://" + Endpoint + "/health");
                req.Timeout = 1500; req.ReadWriteTimeout = 1500; req.Proxy = null;
                using (var res = (HttpWebResponse)req.GetResponse())
                using (var r = new StreamReader(res.GetResponseStream()))
                {
                    var body = new JavaScriptSerializer().DeserializeObject(r.ReadToEnd()) as Dictionary<string, object>;
                    if (body == null || !body.ContainsKey("name") || !body.ContainsKey("pid")) return 0;
                    name = Convert.ToString(body["name"]);
                    return Convert.ToInt32(body["pid"]);
                }
            }
            catch (Exception) { return 0; }
        }

        public void Start()
        {
            stopping = false;
            string name;
            int pid = Probe(out name);
            if (name == "Bananarama") { SetState(ServerState.Running, null); return; } // already up (started by /image or a previous tray)
            if (name == "BananaInterop") // v1 middleware holding the port: replace it
            {
                Log("[tray] stopping the v1 BananaInterop middleware (pid " + pid + ")");
                try { Process.GetProcessById(pid).Kill(); } catch (Exception) { }
                for (int i = 0; i < 20 && Probe(out name) > 0; i++) System.Threading.Thread.Sleep(150);
            }
            if (paths.Node == null) { SetState(ServerState.Failed, "node.exe not found"); return; }
            if (paths.AppRoot == null) { SetState(ServerState.Failed, "server.js not found next to Bananarama.exe"); return; }

            restarts.RemoveAll(t => (DateTime.Now - t).TotalSeconds > 60);
            if (restarts.Count >= 5) { SetState(ServerState.Failed, "middleware keeps crashing; see the log"); return; }
            restarts.Add(DateTime.Now);

            var psi = new ProcessStartInfo(paths.Node, "\"" + paths.Script("server.js") + "\"")
            {
                UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = paths.AppRoot,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            try
            {
                child = Process.Start(psi);
                AddToJob(child);
                child.OutputDataReceived += (s, e) => Log(e.Data);
                child.ErrorDataReceived += (s, e) => Log(e.Data);
                child.BeginOutputReadLine(); child.BeginErrorReadLine();
                SetState(ServerState.Starting, null);
            }
            catch (Exception e) { SetState(ServerState.Failed, e.Message); }
        }

        /// <summary>Called by the tray's timer: refresh state and restart a crashed middleware.</summary>
        public void Poll()
        {
            if (stopping) return;
            if (Health() > 0) { SetState(ServerState.Running, null); return; }
            bool ours = child != null;
            bool exited = ours && child.HasExited;
            if (State == ServerState.Starting && ours && !exited) return; // still booting
            if (State == ServerState.Failed && LastError != null && LastError.StartsWith("middleware keeps")) return;
            if (exited) Log(string.Format("[tray] middleware exited with code {0}; restarting", child.ExitCode));
            child = null;
            Start();
        }

        public void Restart()
        {
            int pid = Health();
            stopping = true;
            KillChild();
            if (pid > 0) { try { Process.GetProcessById(pid).Kill(); } catch (Exception) { } } // one started outside the tray
            for (int i = 0; i < 20 && Health() > 0; i++) System.Threading.Thread.Sleep(150);
            restarts.Clear();
            Start();
        }

        public void Stop() { stopping = true; KillChild(); SetState(ServerState.Stopped, null); }

        void KillChild()
        {
            if (child == null) return;
            try { if (!child.HasExited) { child.Kill(); child.WaitForExit(3000); } } catch (Exception) { }
            child = null;
        }

        void SetState(ServerState s, string error)
        {
            bool changed = s != State || error != LastError;
            State = s; LastError = error;
            if (error != null && changed) Log("[tray] " + error);
            if (changed && StateChanged != null) StateChanged(this, EventArgs.Empty);
        }

        static readonly object logLock = new object();
        public static void Log(string line)
        {
            if (line == null) return;
            try
            {
                lock (logLock)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(AppPaths.LogFile));
                    File.AppendAllText(AppPaths.LogFile, line + Environment.NewLine);
                }
            }
            catch (IOException) { }
        }

        // --- job object: children are killed when the tray exits, even if it crashes ---
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateJobObject(IntPtr a, string name);
        [DllImport("kernel32.dll")] static extern bool SetInformationJobObject(IntPtr job, int cls, IntPtr info, uint len);
        [DllImport("kernel32.dll")] static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

        [StructLayout(LayoutKind.Sequential)]
        struct BasicLimit { public long a, b; public uint LimitFlags; public UIntPtr c, d; public uint e; public UIntPtr f; public uint g, h; }
        [StructLayout(LayoutKind.Sequential)]
        struct IoCounters { public ulong a, b, c, d, e, f; }
        [StructLayout(LayoutKind.Sequential)]
        struct ExtendedLimit { public BasicLimit Basic; public IoCounters Io; public UIntPtr a, b, c, d; }

        void AddToJob(Process p)
        {
            try
            {
                if (job == IntPtr.Zero)
                {
                    job = CreateJobObject(IntPtr.Zero, null);
                    var info = new ExtendedLimit();
                    info.Basic.LimitFlags = 0x2000; // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
                    int len = Marshal.SizeOf(typeof(ExtendedLimit));
                    IntPtr ptr = Marshal.AllocHGlobal(len);
                    try { Marshal.StructureToPtr(info, ptr, false); SetInformationJobObject(job, 9, ptr, (uint)len); }
                    finally { Marshal.FreeHGlobal(ptr); }
                }
                AssignProcessToJobObject(job, p.Handle);
            }
            catch (Exception) { /* best effort */ }
        }

        public void Dispose() { Stop(); }
    }

    /// <summary>Runs one of the Node scripts hidden and returns its output.</summary>
    public static class NodeRunner
    {
        public static int Run(AppPaths paths, string script, string args, out string stdout, out string stderr, int timeoutMs)
        {
            stdout = ""; stderr = "";
            if (paths.Node == null || paths.AppRoot == null) { stderr = "node.exe or the app files are missing"; return -1; }
            var psi = new ProcessStartInfo(paths.Node, "\"" + paths.Script(script) + "\" " + args)
            {
                UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = paths.AppRoot,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            };
            using (var p = Process.Start(psi))
            {
                var err = p.StandardError.ReadToEndAsync();
                stdout = p.StandardOutput.ReadToEnd();
                if (!p.WaitForExit(timeoutMs)) { try { p.Kill(); } catch (Exception) { } stderr = "timed out"; return -1; }
                stderr = err.Result;
                return p.ExitCode;
            }
        }

        /// <summary>Quote one argument for the Windows command line (CommandLineToArgvW rules).</summary>
        public static string Quote(string s)
        {
            if (s.Length > 0 && s.IndexOfAny(new[] { ' ', '\t', '"' }) < 0) return s;
            var sb = new StringBuilder("\"");
            int slashes = 0;
            foreach (char c in s)
            {
                if (c == '\\') { slashes++; continue; }
                if (c == '"') { sb.Append('\\', slashes * 2 + 1).Append('"'); slashes = 0; continue; }
                sb.Append('\\', slashes).Append(c); slashes = 0;
            }
            return sb.Append('\\', slashes * 2).Append('"').ToString();
        }

        public static string SavedPath(string stdout)
        {
            var line = (stdout ?? "").Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.StartsWith("SAVED: "));
            return line == null ? null : line.Substring(7).Trim();
        }
    }
}
