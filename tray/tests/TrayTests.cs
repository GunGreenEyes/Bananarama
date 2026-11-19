// Unit tests for the tray's non-UI code. Tiny self-contained runner (no NuGet, no SDK):
//   powershell -File tray\build.ps1 -Test
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Win32;

namespace Bananarama.Tests
{
    static class Runner
    {
        static int passed, failed;

        static int Main(string[] args)
        {
            string repo = args.Length > 0 ? args[0] : Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", ".."));
            var tests = new List<KeyValuePair<string, Action>>
            {
                T("settings: missing file gives defaults", SettingsDefaults),
                T("settings: values, unknown keys and key order survive a round trip", SettingsRoundTrip),
                T("settings: BOM, ints and bools are read", SettingsTypes),
                T("settings: invalid JSON throws", SettingsInvalid),
                T("settings: output folder = outputDir + subfolder, Downloads when empty", SettingsOutputFolder),
                T("settings: HasApiKey", SettingsHasKey),
                T("startup: enable writes quoted exe to Run key, disable removes it", StartupToggle),
                T("paths: app root found in installed and dev layouts", PathsAppRoot),
                T("paths: bundled node.exe preferred", PathsNode),
                T("paths: Downloads known folder exists", PathsDownloads),
                T("node runner: argument quoting", Quoting),
                T("node runner: SAVED path parsing", SavedPath),
                T("version: whole number", VersionWhole),
                T("server host: health is 0 when nothing listens", HealthNothing),
                T("server host: starts, adopts and stops the real middleware", () => ServerLifecycle(repo)),
            };
            foreach (var t in tests)
            {
                try { t.Value(); passed++; Console.WriteLine("ok   " + t.Key); }
                catch (Exception e) { failed++; Console.WriteLine("FAIL " + t.Key + "\n     " + e.Message.Replace("\n", "\n     ")); }
            }
            Console.WriteLine(string.Format("\ntray tests: {0} passed, {1} failed", passed, failed));
            return failed == 0 ? 0 : 1;
        }

        static KeyValuePair<string, Action> T(string n, Action a) { return new KeyValuePair<string, Action>(n, a); }

        static void Eq(object want, object got, string what)
        {
            if (!Equals(want, got)) throw new Exception(string.Format("{0}: expected <{1}>, got <{2}>", what, want, got));
        }
        static void True(bool c, string what) { if (!c) throw new Exception(what); }

        static string TempDir()
        {
            var d = Path.Combine(Path.GetTempPath(), "bananarama-tray-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(d); return d;
        }

        static void SettingsDefaults()
        {
            var s = SettingsStore.Load(Path.Combine(TempDir(), "nope.json"));
            Eq("gemini-3.1-flash-image", s.GetString("model"), "model");
            Eq(7861, s.GetInt("port", 0), "port");
            Eq("Nano", s.GetString("outputSubfolder"), "subfolder");
            Eq(false, s.GetBool("openAfterSave"), "openAfterSave");
        }

        static void SettingsRoundTrip()
        {
            var f = Path.Combine(TempDir(), "settings.json");
            File.WriteAllText(f, "{\"zeta\": \"keep me\", \"model\": \"gemini-3-pro-image\", \"port\": 9000}");
            var s = SettingsStore.Load(f);
            s.Set("aspectRatio", "16:9"); s.Set("openAfterSave", true); s.Set("note", "a \"quoted\" \\ value");
            s.Save();
            var again = SettingsStore.Load(f);
            Eq("keep me", again.GetString("zeta"), "unknown key");
            Eq("gemini-3-pro-image", again.GetString("model"), "model");
            Eq("16:9", again.GetString("aspectRatio"), "aspect");
            Eq(true, again.GetBool("openAfterSave"), "bool");
            Eq(9000, again.GetInt("port", 0), "port");
            Eq("a \"quoted\" \\ value", again.GetString("note"), "escaping");
            var text = File.ReadAllText(f);
            True(text.IndexOf("\"apiKey\"") < text.IndexOf("\"zeta\""), "defaults first, extra keys after");
            True(text.StartsWith("{\n  \"apiKey\": "), "pretty printed: " + text.Substring(0, 20));
            True(!File.Exists(f + ".tmp"), "temp file cleaned up");
        }

        static void SettingsTypes()
        {
            var f = Path.Combine(TempDir(), "settings.json");
            File.WriteAllText(f, "{\"port\": 8123, \"openAfterSave\": true, \"apiKey\": \"k\"}", new UTF8Encoding(true));
            var s = SettingsStore.Load(f);
            Eq(8123, s.GetInt("port", 0), "port");
            Eq(true, s.GetBool("openAfterSave"), "bool");
            Eq("k", s.GetString("apiKey"), "key");
        }

        static void SettingsInvalid()
        {
            var f = Path.Combine(TempDir(), "settings.json");
            File.WriteAllText(f, "{ nope");
            try { SettingsStore.Load(f); }
            catch (Exception) { return; }
            throw new Exception("no exception");
        }

        static void SettingsOutputFolder()
        {
            var s = SettingsStore.Load(Path.Combine(TempDir(), "x.json"));
            s.Set("outputDir", @"X:\pics"); s.Set("outputSubfolder", "Nano");
            Eq(@"X:\pics\Nano", s.OutputFolder, "with subfolder");
            s.Set("outputSubfolder", "");
            Eq(@"X:\pics", s.OutputFolder, "without subfolder");
            s.Set("outputDir", ""); s.Set("outputSubfolder", "Nano");
            Eq(Path.Combine(AppPaths.Downloads, "Nano"), s.OutputFolder, "downloads");
        }

        static void SettingsHasKey()
        {
            var old = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
            Environment.SetEnvironmentVariable("GEMINI_API_KEY", null);
            try
            {
                var s = SettingsStore.Load(Path.Combine(TempDir(), "x.json"));
                True(!s.HasApiKey, "empty key");
                s.Set("apiKey", "  "); True(!s.HasApiKey, "blank key");
                s.Set("apiKey", "abc"); True(s.HasApiKey, "key set");
                s.Set("apiKey", ""); Environment.SetEnvironmentVariable("GEMINI_API_KEY", "env");
                True(s.HasApiKey, "env key");
            }
            finally { Environment.SetEnvironmentVariable("GEMINI_API_KEY", old); }
        }

        static void StartupToggle()
        {
            const string key = @"Software\Bananarama.Tests\Run";
            var e = new StartupEntry(key, "Bananarama", @"C:\Apps\Bananarama\Bananarama.exe");
            try
            {
                e.Enabled = false; True(!e.Enabled, "starts disabled");
                e.Enabled = true; True(e.Enabled, "enabled");
                using (var k = Registry.CurrentUser.OpenSubKey(key))
                    Eq("\"C:\\Apps\\Bananarama\\Bananarama.exe\"", k.GetValue("Bananarama"), "value");
                e.Enabled = false; True(!e.Enabled, "disabled again");
                e.Enabled = false; // idempotent
            }
            finally { Registry.CurrentUser.DeleteSubKeyTree(@"Software\Bananarama.Tests", false); }
        }

        static void PathsAppRoot()
        {
            var installed = TempDir();
            Directory.CreateDirectory(Path.Combine(installed, "app", "src"));
            File.WriteAllText(Path.Combine(installed, "app", "src", "server.js"), "");
            Eq(Path.Combine(installed, "app"), AppPaths.FindAppRoot(installed), "installed");

            var repo = TempDir();
            Directory.CreateDirectory(Path.Combine(repo, "src")); Directory.CreateDirectory(Path.Combine(repo, "dist"));
            File.WriteAllText(Path.Combine(repo, "src", "server.js"), "");
            Eq(repo, AppPaths.FindAppRoot(Path.Combine(repo, "dist")), "dev");
            Eq(null, AppPaths.FindAppRoot(TempDir()), "missing");
        }

        static void PathsNode()
        {
            var d = TempDir();
            Directory.CreateDirectory(Path.Combine(d, "node"));
            File.WriteAllText(Path.Combine(d, "node", "node.exe"), "");
            Eq(Path.Combine(d, "node", "node.exe"), AppPaths.FindNode(d, null), "bundled");
        }

        static void PathsDownloads() { True(Directory.Exists(AppPaths.Downloads), "exists: " + AppPaths.Downloads); }

        static void Quoting()
        {
            Eq("plain", NodeRunner.Quote("plain"), "plain");
            Eq("\"two words\"", NodeRunner.Quote("two words"), "space");
            Eq("\"say \\\"hi\\\"\"", NodeRunner.Quote("say \"hi\""), "quotes");
            Eq("\"C:\\dir with space\\\\\"", NodeRunner.Quote("C:\\dir with space\\"), "trailing backslash");
            Eq("\"\"", NodeRunner.Quote(""), "empty");
        }

        static void SavedPath()
        {
            Eq(@"C:\x\a.png", NodeRunner.SavedPath("… stuff\r\nSAVED: C:\\x\\a.png\r\n"), "found");
            Eq(null, NodeRunner.SavedPath("ERROR: nope"), "missing");
            Eq(null, NodeRunner.SavedPath(null), "null");
        }

        static void VersionWhole() { True(System.Text.RegularExpressions.Regex.IsMatch(AppInfo.Version, @"^v\d+$"), AppInfo.Version); }

        static int FreePort()
        {
            var l = new TcpListener(IPAddress.Loopback, 0); l.Start();
            int p = ((IPEndPoint)l.LocalEndpoint).Port; l.Stop(); return p;
        }

        static void HealthNothing()
        {
            var s = SettingsStore.Load(Path.Combine(TempDir(), "x.json")); s.Set("port", FreePort());
            var host = new ServerHost(new AppPaths(TempDir()), () => s);
            Eq(0, host.Health(), "health");
        }

        static void ServerLifecycle(string repo)
        {
            var paths = new AppPaths(Path.Combine(repo, "dist"));
            if (paths.Node == null) throw new Exception("node.exe not on PATH");
            if (paths.AppRoot == null) throw new Exception("repo not found from " + repo);
            var f = Path.Combine(TempDir(), "settings.json");
            Environment.SetEnvironmentVariable("BANANARAMA_SETTINGS", f); // the child reads the same file
            var s = SettingsStore.Load(f); s.Set("port", FreePort()); s.Save();
            var host = new ServerHost(paths, () => s);
            try
            {
                host.Start();
                var sw = Stopwatch.StartNew();
                while (host.Health() == 0 && sw.ElapsedMilliseconds < 10000) System.Threading.Thread.Sleep(100);
                True(host.Health() > 0, "middleware answered /health");
                host.Poll(); Eq(ServerState.Running, host.State, "state");

                var second = new ServerHost(paths, () => s); // a second host adopts instead of spawning
                second.Start(); Eq(ServerState.Running, second.State, "adopted");

                host.Stop();
                sw.Restart();
                while (host.Health() > 0 && sw.ElapsedMilliseconds < 5000) System.Threading.Thread.Sleep(100);
                Eq(0, host.Health(), "stopped");
            }
            finally { host.Stop(); Environment.SetEnvironmentVariable("BANANARAMA_SETTINGS", null); }
        }
    }
}
