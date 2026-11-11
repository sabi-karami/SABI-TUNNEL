using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace TT
{
    /// <summary>HTTPS helpers for the feed and the update downloads. Direct connections (no system proxy) like the
    /// rest of the app; while the tunnel is up they go through it.</summary>
    static class Http
    {
        public static string UA => "SabiTunnel-Windows/" + MainForm.GuiVersion + (Updater.Build > 0 ? "." + Updater.Build : "");

        static HttpWebRequest Req(string url, int timeoutMs)
        {
            var rq = (HttpWebRequest)WebRequest.Create(url);
            rq.Timeout = timeoutMs; rq.ReadWriteTimeout = timeoutMs; rq.Proxy = null; rq.KeepAlive = false; rq.UserAgent = UA;
            rq.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            return rq;
        }

        static string Why(Exception ex)
        {
            if (ex is WebException we)
            {
                var code = we.Response is HttpWebResponse r ? "HTTP " + (int)r.StatusCode : we.Status.ToString();
                try { we.Response?.Close(); } catch { }
                return code;
            }
            return ex.GetType().Name + ": " + ex.Message;
        }

        public static string GetText(string url, int timeoutMs, out string why)
        {
            why = null;
            try
            {
                using (var rs = (HttpWebResponse)Req(url, timeoutMs).GetResponse())
                {
                    if (rs.ContentLength > 4 << 20) { why = "response too large"; return null; }
                    using (var sr = new StreamReader(rs.GetResponseStream(), Encoding.UTF8)) return sr.ReadToEnd();
                }
            }
            catch (Exception ex) { why = Why(ex); return null; }
        }

        /// <summary>Streams url into dest (through dest.part), checking the size and the SHA-256 on the way.</summary>
        public static bool Download(string url, string dest, long size, string sha256, int timeoutMs, out string why)
        {
            why = null;
            var part = dest + ".part";
            try
            {
                using (var rs = (HttpWebResponse)Req(url, timeoutMs).GetResponse())
                using (var s = rs.GetResponseStream())
                using (var f = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var sha = SHA256.Create())
                {
                    var buf = new byte[81920];
                    long total = 0;
                    int n;
                    while ((n = s.Read(buf, 0, buf.Length)) > 0)
                    {
                        total += n;
                        if (total > size) { why = "larger than " + size + " bytes"; break; }
                        sha.TransformBlock(buf, 0, n, null, 0);
                        f.Write(buf, 0, n);
                    }
                    sha.TransformFinalBlock(buf, 0, 0);
                    if (why == null && total != size) why = "got " + total + " of " + size + " bytes";
                    if (why == null && !string.Equals(Hex(sha.Hash), sha256, StringComparison.OrdinalIgnoreCase)) why = "SHA-256 mismatch";
                }
                if (why == null)
                {
                    if (File.Exists(dest)) File.Delete(dest);
                    File.Move(part, dest);
                    return true;
                }
            }
            catch (Exception ex) { why = Why(ex); }
            try { File.Delete(part); } catch { }
            return false;
        }

        public static string Sha256File(string path)
        {
            using (var f = File.OpenRead(path))
            using (var sha = SHA256.Create()) return Hex(sha.ComputeHash(f));
        }

        public static string Hex(byte[] b)
        {
            var sb = new StringBuilder(b.Length * 2);
            foreach (var x in b) sb.Append(x.ToString("x2"));
            return sb.ToString();
        }
    }

    /// <summary>Minimal JSON access over JavaScriptSerializer's dictionaries and arrays.</summary>
    static class Js
    {
        static JavaScriptSerializer S() => new JavaScriptSerializer { MaxJsonLength = 8 << 20, RecursionLimit = 32 };
        public static object Parse(string s) => S().DeserializeObject(s);
        public static string Write(object o) => S().Serialize(o);
        public static object Get(object d, string k) => d is IDictionary<string, object> m && m.TryGetValue(k, out var v) ? v : null;
        public static string Str(object d, string k) => Get(d, k) as string;
        public static bool Bool(object d, string k) => Get(d, k) is bool b && b;

        public static long Long(object d, string k)
        {
            var v = Get(d, k);
            if (v is string s) return long.TryParse(s, out var n) ? n : 0;
            if (v == null || v is bool) return 0;
            try { return Convert.ToInt64(v); } catch { return 0; }
        }

        public static IEnumerable<object> List(object v) => v is IList l ? l.Cast<object>() : Enumerable.Empty<object>();
    }

    sealed class FeedConfig { public string Id, Name, Link, Hash; }
    sealed class FeedFile { public string Name, Url, Sha256; public long Size; }
    sealed class FeedApp { public string Version = "", Notes = ""; public long Build; public readonly Dictionary<string, FeedFile> Files = new Dictionary<string, FeedFile>(); }

    sealed class FeedData
    {
        public string Source, Rev = "";
        public readonly List<FeedConfig> Configs = new List<FeedConfig>();
        public List<string> Feeds = new List<string>();
        public FeedApp Windows;
    }

    /// <summary>Default servers from the panel (GET /app/v1/feed). Each one becomes a saved server that follows the
    /// panel: added, updated (new password / address) and removed with it. servers\feed.json remembers which saved
    /// server belongs to which feed entry. A server the user already had (same host + user) is adopted instead of
    /// duplicated, and simply stays as the user's own if the panel drops it. A default the user deletes comes back
    /// on the next sync.</summary>
    static class Feed
    {
        /// <summary>Only keeps casual scanners off the public feed; anyone with the app has it.</summary>
        public const string AppKey = "st1-Vb7Qm2Kx9Rf4Lp8Zt3Hw";
        public static readonly string[] BuiltIn = { "https://sabitunnel.sahebkarami.ir/app/v1/feed", "https://ttpanel-oh.sahebkarami.ir/app/v1/feed" };
        const int MaxConfigs = 40;
        static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);
        static string IndexPath => Path.Combine(Profiles.Dir, "feed.json");
        static string StageDir => Path.Combine(Profiles.Dir, ".feed");

        /// <summary>Settings the user may have changed on a default server; a new version from the panel keeps them.</summary>
        static readonly string[] Choices = { "upstream_protocol", "killswitch_enabled", "post_quantum_group_enabled" };

        sealed class Item { public string Profile, Hash = "", Name = ""; public bool Adopted; }

        sealed class Index
        {
            public readonly Dictionary<string, Item> Items = new Dictionary<string, Item>(StringComparer.Ordinal);
            public List<string> Feeds = new List<string>();
            public string Rev = "";
            public long Synced;
        }

        public static bool GoodUrl(string u) => u != null && u.Length < 300 && Uri.TryCreate(u, UriKind.Absolute, out var x) && x.Scheme == Uri.UriSchemeHttps;
        static bool SafeId(string id) => id != null && Regex.IsMatch(id, "^[0-9A-Za-z_-]{1,40}$");
        static string Host(string u) { try { return new Uri(u).Host; } catch { return u; } }
        static bool Same(Profile p, string host, string user) =>
            host.Length > 0 && string.Equals(p.Host, host, StringComparison.OrdinalIgnoreCase) && p.User == user;

        static Index Load()
        {
            var ix = new Index();
            try
            {
                if (!File.Exists(IndexPath)) return ix;
                var d = Js.Parse(File.ReadAllText(IndexPath, Encoding.UTF8));
                if (Js.Get(d, "items") is IDictionary<string, object> items)
                    foreach (var kv in items)
                    {
                        var p = Js.Str(kv.Value, "profile");
                        if (SafeId(p))
                            ix.Items[kv.Key] = new Item { Profile = p, Hash = Js.Str(kv.Value, "hash") ?? "", Name = Js.Str(kv.Value, "name") ?? "", Adopted = Js.Bool(kv.Value, "adopted") };
                    }
                ix.Feeds = Js.List(Js.Get(d, "feeds")).OfType<string>().Where(GoodUrl).Take(6).ToList();
                ix.Rev = Js.Str(d, "rev") ?? "";
                ix.Synced = Js.Long(d, "synced");
            }
            catch { }
            return ix;
        }

        static void Save(Index ix)
        {
            var items = new Dictionary<string, object>();
            foreach (var kv in ix.Items)
                items[kv.Key] = new Dictionary<string, object> { { "profile", kv.Value.Profile }, { "hash", kv.Value.Hash }, { "name", kv.Value.Name }, { "adopted", kv.Value.Adopted } };
            var d = new Dictionary<string, object> { { "v", 1 }, { "items", items }, { "feeds", ix.Feeds }, { "rev", ix.Rev }, { "synced", ix.Synced } };
            Directory.CreateDirectory(Profiles.Dir);
            File.WriteAllText(IndexPath, Js.Write(d), Utf8);
        }

        /// <summary>Saved-server ids that come from the panel (they return after a delete).</summary>
        public static HashSet<string> Managed() => new HashSet<string>(Load().Items.Values.Select(i => i.Profile));

        /// <summary>The user imported this server's link themselves: keep it if the panel drops it later.</summary>
        public static void MarkAdopted(string profileId)
        {
            try
            {
                var ix = Load();
                var hit = ix.Items.Values.Where(i => i.Profile == profileId && !i.Adopted).ToList();
                foreach (var i in hit) i.Adopted = true;
                if (hit.Count > 0) Save(ix);
            }
            catch { }
        }

        /// <summary>Tries the feed addresses the panel announced last time, then the built-in ones (the Cloudflare
        /// mirror first). Returns null when none answers with a valid feed.</summary>
        public static FeedData Fetch(int timeoutMs, Action<string> log)
        {
            var urls = Load().Feeds.Concat(BuiltIn).Distinct(StringComparer.OrdinalIgnoreCase).Take(6).ToList();
            foreach (var u in urls)
            {
                var text = Http.GetText(u + (u.IndexOf('?') >= 0 ? "&" : "?") + "k=" + Uri.EscapeDataString(AppKey), timeoutMs, out var why);
                var d = text == null ? null : Parse(text, u, out why);
                if (d == null) { log?.Invoke("[feed] " + Host(u) + ": " + why); continue; }
                log?.Invoke("[feed] " + Host(u) + ": " + d.Configs.Count + " default servers" + (d.Windows != null ? ", windows build " + d.Windows.Build : ""));
                return d;
            }
            return null;
        }

        public static FeedData Parse(string json, string source, out string why)
        {
            why = null;
            object d;
            try { d = Js.Parse(json); }
            catch (Exception ex) { why = "bad JSON (" + ex.GetType().Name + ")"; return null; }
            var list = Js.Get(d, "configs");
            if (Js.Long(d, "v") != 1 || !(list is IList)) { why = "not a SABI TUNNEL feed"; return null; }
            var f = new FeedData { Source = source, Rev = Js.Str(d, "rev") ?? "" };
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var c in Js.List(list))
            {
                var id = Js.Str(c, "id");
                var link = DeepLink.Normalize(Js.Str(c, "link") ?? "");
                if (string.IsNullOrEmpty(id) || id.Length > 80 || !Regex.IsMatch(link, "^tt://\\?[A-Za-z0-9_\\-=]+$") || !seen.Add(id)) continue;
                var name = Regex.Replace(Js.Str(c, "name") ?? "", @"[\r\n\t]+", " ").Trim();
                if (name.Length == 0) name = (DeepLink.Name(link) ?? "SABI TUNNEL").Trim();
                if (name.Length > 80) name = name.Substring(0, 80);
                var hash = Js.Str(c, "hash");
                if (string.IsNullOrEmpty(hash))
                    using (var sha = SHA256.Create()) hash = Http.Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(link))).Substring(0, 16);
                f.Configs.Add(new FeedConfig { Id = id, Name = name, Link = link, Hash = hash });
                if (f.Configs.Count >= MaxConfigs) break;
            }
            f.Feeds = Js.List(Js.Get(d, "feeds")).OfType<string>().Where(GoodUrl).Take(6).ToList();
            var w = Js.Get(Js.Get(d, "apps"), "windows");
            if (w != null)
            {
                var ver = Regex.Replace(Js.Str(w, "version") ?? "", "[^0-9A-Za-z.+-]", "");
                var a = new FeedApp { Version = ver.Length > 20 ? ver.Substring(0, 20) : ver, Notes = Js.Str(w, "notes") ?? "", Build = Js.Long(w, "build") };
                if (Js.Get(w, "files") is IDictionary<string, object> files)
                    foreach (var kv in files)
                    {
                        var x = new FeedFile { Name = Js.Str(kv.Value, "name") ?? "", Url = Js.Str(kv.Value, "url") ?? "", Sha256 = (Js.Str(kv.Value, "sha256") ?? "").ToLowerInvariant(), Size = Js.Long(kv.Value, "size") };
                        if (x.Url.Length > 0 && x.Size > 0 && x.Size < 400L << 20 && Regex.IsMatch(x.Sha256, "^[0-9a-f]{64}$")) a.Files[kv.Key] = x;
                    }
                if (a.Build > 0 && a.Files.Count > 0) f.Windows = a;
            }
            return f;
        }

        public sealed class Staged { public FeedConfig C; public string Toml, Error; }
        public sealed class Plan { public FeedData Data; public readonly List<Staged> Items = new List<Staged>(); public int Failed; }
        public sealed class Result { public int Added, Updated, Removed, Deferred, Failed; public bool ActiveChanged; }

        /// <summary>Background part: the official setup_wizard turns every new or changed link into settings
        /// (servers\.feed\N.toml). Nothing the app uses is touched yet.</summary>
        public static Plan Prepare(FeedData data, Action<string> log)
        {
            var ix = Load();
            var plan = new Plan { Data = data };
            try { if (Directory.Exists(StageDir)) Directory.Delete(StageDir, true); } catch { }
            int n = 0;
            foreach (var c in data.Configs)
            {
                var s = new Staged { C = c };
                plan.Items.Add(s);
                if (ix.Items.TryGetValue(c.Id, out var it) && it.Hash == c.Hash && File.Exists(Path.Combine(Profiles.Dir, it.Profile + ".toml"))) continue;
                Directory.CreateDirectory(StageDir);
                var dest = Path.Combine(StageDir, (++n) + ".toml");
                string err;
                try { err = Cfg.ImportLink(c.Link, dest); } catch (Exception ex) { err = ex.Message; }
                if (err == null && !File.Exists(dest)) err = "no settings written";
                if (err != null) { s.Error = err; plan.Failed++; log?.Invoke("[feed] " + c.Id + ": " + err); }
                else s.Toml = dest;
            }
            return plan;
        }

        /// <summary>UI thread: makes the saved servers match the feed. busy = a tunnel is up or starting; anything that
        /// would change the server in use then waits (Deferred) and is redone after the disconnect.</summary>
        public static Result Apply(Plan plan, bool busy, Action<string> log)
        {
            var r = new Result { Failed = plan.Failed };
            var ix = Load();
            var activeId = Profiles.ActiveId;
            var profiles = Profiles.List();
            var claimed = new HashSet<string>(ix.Items.Values.Select(i => i.Profile));
            bool activeGone = false;
            Directory.CreateDirectory(Profiles.Dir);

            foreach (var s in plan.Items)
            {
                var c = s.C;
                ix.Items.TryGetValue(c.Id, out var it);
                var cur = it == null ? null : profiles.FirstOrDefault(p => p.Id == it.Profile);
                if (s.Toml == null)
                {
                    if (s.Error == null && it != null && cur != null && it.Name != c.Name)   // renamed in the panel
                    {
                        File.WriteAllText(cur.NameFile, c.Name, Utf8);
                        it.Name = c.Name;
                        if (cur.Id == activeId) { Ini.Set("name", c.Name); r.ActiveChanged = true; }
                    }
                    continue;
                }
                string host = Cfg.GetFrom(s.Toml, "hostname") ?? "", user = Cfg.GetFrom(s.Toml, "username") ?? "";
                bool adopted = it != null && it.Adopted;
                var target = cur;
                if (target != null && adopted && !Same(target, host, user))
                {
                    // it was the user's own server; this default now points somewhere else, so theirs stays as it is
                    claimed.Remove(target.Id); target = null; adopted = false;
                }
                if (target == null)
                {
                    var twin = profiles.FirstOrDefault(p => !claimed.Contains(p.Id) && Same(p, host, user));
                    if (twin != null) { target = twin; adopted = true; }
                }
                if (target != null && target.Id == activeId && busy) { r.Deferred++; continue; }
                var id = target?.Id ?? (it != null && cur == null ? it.Profile : Profiles.NewId());
                var toml = Path.Combine(Profiles.Dir, id + ".toml");
                bool active = id == activeId;
                if (active) Profiles.SyncActive();          // the live file has the latest protocol / kill-switch choice
                var text = File.ReadAllText(s.Toml);
                if (File.Exists(toml))
                    foreach (var k in Choices)
                    {
                        var v = Cfg.GetRaw(toml, k);
                        if (v != null) text = Cfg.SetIn(text, k, v, addIfMissing: k == "post_quantum_group_enabled");
                    }
                File.WriteAllText(toml, text, Utf8);
                File.WriteAllText(Path.Combine(Profiles.Dir, id + ".name"), c.Name, Utf8);
                bool newlyAdopted = adopted && (it == null || !it.Adopted || it.Profile != id);
                ix.Items[c.Id] = new Item { Profile = id, Hash = c.Hash, Name = c.Name, Adopted = adopted };
                claimed.Add(id);
                if (target == null) r.Added++; else r.Updated++;
                log?.Invoke("[feed] " + (target == null ? "added " : newlyAdopted ? "adopted " : "updated ") + c.Id);
                if (active) { File.Copy(toml, Cfg.Toml, true); Ini.Set("name", c.Name); r.ActiveChanged = true; }
            }

            var wanted = new HashSet<string>(plan.Data.Configs.Select(c => c.Id), StringComparer.Ordinal);
            foreach (var key in ix.Items.Keys.ToList())
            {
                if (wanted.Contains(key)) continue;
                var it = ix.Items[key];
                var p = profiles.FirstOrDefault(x => x.Id == it.Profile);
                if (p != null && !it.Adopted)
                {
                    if (p.Id == activeId && busy) { r.Deferred++; continue; }
                    Profiles.Delete(p);
                    r.Removed++;
                    if (p.Id == activeId) activeGone = true;
                    log?.Invoke("[feed] removed " + key);
                }
                ix.Items.Remove(key);
            }

            // the server in use was removed, or there was none yet (first start): use a default one
            var rest = Profiles.List();
            if (activeGone || (!Cfg.Exists && rest.Count > 0))
            {
                var mine = new HashSet<string>(ix.Items.Values.Select(i => i.Profile));
                var pick = rest.FirstOrDefault(p => mine.Contains(p.Id)) ?? rest.FirstOrDefault();
                if (pick != null) Profiles.Activate(pick, sync: false);
                else { try { File.Delete(Cfg.Toml); } catch { } Ini.Set("name", ""); }
                r.ActiveChanged = true;
            }
            if (plan.Data.Feeds.Count > 0) ix.Feeds = plan.Data.Feeds;
            ix.Rev = plan.Data.Rev;
            ix.Synced = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
            Save(ix);
            try { Directory.Delete(StageDir, true); } catch { }
            return r;
        }
    }

    sealed class UpdateInfo { public long Build; public string Version = "", Kind = "", FilePath = "", Sha256 = "", Notes = ""; }

    /// <summary>App updates from the feed. CI stamps every build with a number (SabiBuild); a higher number in the
    /// feed is downloaded to update\, checked against its SHA-256 and installed while the app is idle: installed
    /// copies run the new Setup silently, portable copies swap their files in place. Build 0 (local builds) never
    /// updates.</summary>
    static class Updater
    {
        public static readonly long Build = ReadBuild();
        public static bool Enabled => Build > 0;
        /// <summary>Installed with Setup (its uninstaller sits next to the exe) rather than unzipped.</summary>
        public static bool Installed => File.Exists(Path.Combine(Cfg.Dir, "unins000.exe"));
        public static string Kind => Installed ? "setup" : "zip";
        public static string Exe => Path.Combine(Cfg.Dir, "SabiTunnel.exe");
        static string Dir => Path.Combine(Cfg.Dir, "update");
        static string ReadyPath => Path.Combine(Dir, "ready.txt");
        static readonly string[] KeepNames = { "bypass.txt", "gui.ini", "pending_link.txt" };

        static long ReadBuild()
        {
            try
            {
                foreach (AssemblyMetadataAttribute a in typeof(Updater).Assembly.GetCustomAttributes(typeof(AssemblyMetadataAttribute), false))
                    if (a.Key == "SabiBuild" && long.TryParse(a.Value, out var b) && b > 0) return b;
            }
            catch { }
            return 0;
        }

        /// <summary>The staged download as Stage() recorded it (even if it is not newer), or null.</summary>
        public static UpdateInfo ReadReady()
        {
            try
            {
                if (!File.Exists(ReadyPath)) return null;
                var l = File.ReadAllLines(ReadyPath, Encoding.UTF8);
                var f = l[0].Split('|');
                if (f.Length < 5 || f[4].Length == 0 || Path.GetFileName(f[4]) != f[4]) return null;
                return new UpdateInfo { Build = long.Parse(f[0]), Kind = f[1], Sha256 = f[2], Version = f[3], FilePath = Path.Combine(Dir, f[4]), Notes = l.Length > 1 ? l[1] : "" };
            }
            catch { return null; }
        }

        /// <summary>A verified download newer than this build, of the right kind for this copy; null if none.</summary>
        public static UpdateInfo Current()
        {
            var u = ReadReady();
            return Enabled && u != null && u.Build > Build && u.Kind == Kind && File.Exists(u.FilePath) ? u : null;
        }

        static int Tries(long build)
        {
            var p = (Ini.Get("update_tries", "") ?? "").Split(':');
            return p.Length == 2 && p[0] == build.ToString() && int.TryParse(p[1], out var n) ? n : 0;
        }

        /// <summary>Automatic installs stop after two failed attempts at the same build (the menu item still works).</summary>
        public static bool MayAuto(UpdateInfo u) => u != null && Tries(u.Build) < 2;

        public static void Succeeded() { try { Ini.Set("update_tries", ""); } catch { } }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        struct LastInput { public uint Size, Time; }
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LastInput li);

        /// <summary>How long nobody has touched the keyboard or mouse (0 if unknown).</summary>
        public static TimeSpan UserIdle()
        {
            try
            {
                var li = new LastInput { Size = 8 };
                if (!GetLastInputInfo(ref li)) return TimeSpan.Zero;
                return TimeSpan.FromMilliseconds(unchecked((uint)Environment.TickCount - li.Time));
            }
            catch { return TimeSpan.Zero; }
        }

        /// <summary>Background: downloads the newer build named in the feed (Setup for installed copies, the zip for
        /// portable ones) and records it in update\ready.txt once size and SHA-256 match.</summary>
        public static UpdateInfo Stage(FeedData d, Action<string> log)
        {
            var w = d.Windows;
            if (!Enabled || w == null || w.Build <= Build || !w.Files.TryGetValue(Kind, out var f)) return null;
            var cur = Current();
            if (cur != null && cur.Build == w.Build && string.Equals(cur.Sha256, f.Sha256, StringComparison.OrdinalIgnoreCase)) return cur;
            Directory.CreateDirectory(Dir);
            var name = "SabiTunnel-" + w.Build + (Kind == "setup" ? ".exe" : ".zip");
            var dest = Path.Combine(Dir, name);
            bool ok = false;
            foreach (var b in new[] { d.Source }.Concat(d.Feeds).Concat(Feed.BuiltIn).Where(Feed.GoodUrl).Distinct(StringComparer.OrdinalIgnoreCase).Take(3))
            {
                string url;
                try { url = new Uri(new Uri(b), f.Url).ToString(); } catch { continue; }
                if (!Feed.GoodUrl(url)) continue;
                log?.Invoke("[update] downloading " + w.Version + " (build " + w.Build + ") from " + new Uri(url).Host);
                if (Http.Download(url, dest, f.Size, f.Sha256, 30000, out var why)) { ok = true; break; }
                log?.Invoke("[update] download failed: " + why);
            }
            if (!ok) return null;
            File.WriteAllText(ReadyPath, w.Build + "|" + Kind + "|" + f.Sha256 + "|" + w.Version + "|" + name + "\r\n" + Regex.Replace(w.Notes ?? "", @"\s+", " "), new UTF8Encoding(false));
            foreach (var x in Directory.GetFiles(Dir, "SabiTunnel-*"))
                if (!string.Equals(Path.GetFileName(x), name, StringComparison.OrdinalIgnoreCase)) { try { File.Delete(x); } catch { } }
            return Current();
        }

        /// <summary>Starts installing a staged build; the caller exits right after. The app is started again with
        /// --updated (+ --tray / --connect to come back the way it was), also when Setup fails.</summary>
        public static string Install(UpdateInfo u, bool tray, bool connect)
        {
            try
            {
                if (!File.Exists(u.FilePath) || !string.Equals(Http.Sha256File(u.FilePath), u.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    try { File.Delete(u.FilePath); File.Delete(ReadyPath); } catch { }
                    return "the downloaded update was damaged; it will be downloaded again";
                }
                Ini.Set("update_tries", u.Build + ":" + (Tries(u.Build) + 1));
                var args = "--updated" + (tray ? " --tray" : "") + (connect ? " --connect" : "");
                if (u.Kind == "setup")
                {
                    var log = Path.Combine(Dir, "setup-" + u.Build + ".log");
                    var line = "/d /s /c \"start \"\" /wait \"" + u.FilePath + "\" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- \"/LOG=" + log +
                               "\" & start \"\" \"" + Exe + "\" " + args + "\"";
                    Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"), line)
                    { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Cfg.Dir });
                }
                else
                {
                    Swap(u.FilePath);
                    Process.Start(new ProcessStartInfo(Exe, args) { UseShellExecute = false, WorkingDirectory = Cfg.Dir });
                }
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        }

        /// <summary>Portable copy: unpacks the zip and swaps the files in. Running files can't be overwritten but can be
        /// renamed, so each old file becomes *.old first (removed on the next start). The user's settings stay.</summary>
        static void Swap(string zip)
        {
            var x = Path.Combine(Dir, "x");
            if (Directory.Exists(x)) Directory.Delete(x, true);
            ZipFile.ExtractToDirectory(zip, x);
            var exe = Directory.GetFiles(x, "SabiTunnel.exe", SearchOption.AllDirectories).OrderBy(p => p.Length).FirstOrDefault();
            if (exe == null) throw new Exception("SabiTunnel.exe is missing from the update");
            var root = Path.GetDirectoryName(exe);
            var files = new List<string>();
            foreach (var src in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
            {
                var rel = src.Substring(root.Length).TrimStart('\\', '/');
                var top = rel.Split('\\', '/')[0];
                if (KeepNames.Contains(rel, StringComparer.OrdinalIgnoreCase) || rel.EndsWith(".toml", StringComparison.OrdinalIgnoreCase) ||
                    rel.EndsWith(".log", StringComparison.OrdinalIgnoreCase) || top.Equals("servers", StringComparison.OrdinalIgnoreCase) ||
                    top.Equals("update", StringComparison.OrdinalIgnoreCase)) continue;
                files.Add(rel);
            }
            var moved = new List<string>();
            try
            {
                foreach (var rel in files)
                {
                    var dst = Path.Combine(Cfg.Dir, rel);
                    if (!File.Exists(dst)) continue;
                    if (File.Exists(dst + ".old")) File.Delete(dst + ".old");
                    File.Move(dst, dst + ".old");
                    moved.Add(rel);
                }
                foreach (var rel in files)
                {
                    var dst = Path.Combine(Cfg.Dir, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(dst));
                    File.Move(Path.Combine(root, rel), dst);
                }
            }
            catch
            {
                foreach (var rel in moved)
                {
                    var dst = Path.Combine(Cfg.Dir, rel);
                    try { if (File.Exists(dst)) File.Delete(dst); File.Move(dst + ".old", dst); } catch { }
                }
                throw;
            }
            try { Directory.Delete(x, true); } catch { }
        }

        /// <summary>Start-up: removes what an earlier update left behind.</summary>
        public static void Cleanup()
        {
            try
            {
                foreach (var f in Directory.GetFiles(Cfg.Dir, "*.old"))
                    if (!f.EndsWith(".log.old", StringComparison.OrdinalIgnoreCase)) { try { File.Delete(f); } catch { } }
                if (!Directory.Exists(Dir)) return;
                foreach (var f in Directory.GetFiles(Dir, "*.part")) { try { File.Delete(f); } catch { } }
                var x = Path.Combine(Dir, "x");
                if (Directory.Exists(x)) { try { Directory.Delete(x, true); } catch { } }
                if (Current() == null)
                {
                    foreach (var f in Directory.GetFiles(Dir, "SabiTunnel-*")) { try { File.Delete(f); } catch { } }
                    try { File.Delete(ReadyPath); } catch { }
                }
                foreach (var f in Directory.GetFiles(Dir, "setup-*.log").OrderByDescending(File.GetLastWriteTimeUtc).Skip(3)) { try { File.Delete(f); } catch { } }
            }
            catch { }
        }
    }
}
