using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace TT
{
    sealed class Profile
    {
        public string Id, Name, Host, User;
        public string Toml => Path.Combine(Profiles.Dir, Id + ".toml");
        public string NameFile => Path.Combine(Profiles.Dir, Id + ".name");
    }

    /// <summary>Saved servers: servers\&lt;id&gt;.toml (+ .name). The active one is copied to trusttunnel_client.toml.</summary>
    static class Profiles
    {
        public static string Dir => Path.Combine(Cfg.Dir, "servers");
        static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        public static string ActiveId
        {
            get => Ini.Get("profile", "");
            set => Ini.Set("profile", value ?? "");
        }

        public static List<Profile> List()
        {
            var res = new List<Profile>();
            if (!Directory.Exists(Dir)) return res;
            foreach (var f in Directory.GetFiles(Dir, "*.toml").OrderBy(x => Path.GetFileName(x), StringComparer.Ordinal))
            {
                var p = new Profile { Id = Path.GetFileNameWithoutExtension(f) };
                p.Host = Cfg.GetFrom(f, "hostname") ?? "";
                p.User = Cfg.GetFrom(f, "username") ?? "";
                try { if (File.Exists(p.NameFile)) p.Name = File.ReadAllText(p.NameFile, Utf8).Trim(); } catch { }
                if (string.IsNullOrWhiteSpace(p.Name)) p.Name = string.IsNullOrEmpty(p.Host) ? "سرور" : p.Host;
                res.Add(p);
            }
            return res;
        }

        public static Profile Active => List().FirstOrDefault(p => p.Id == ActiveId);

        static string NewId() => DateTime.UtcNow.ToString("yyMMddHHmmssfff");

        /// <summary>First run of v2.1: turn the existing single config into the first saved server.</summary>
        public static void Migrate()
        {
            try
            {
                if (!Cfg.Exists) return;
                if (List().Count > 0) { if (Active == null) ActiveId = List()[0].Id; return; }
                Directory.CreateDirectory(Dir);
                var p = new Profile { Id = NewId() };
                File.Copy(Cfg.Toml, p.Toml, true);
                var name = Ini.Get("name", "");
                if (string.IsNullOrWhiteSpace(name)) name = Cfg.Get("name") ?? Cfg.Get("hostname") ?? "";
                File.WriteAllText(p.NameFile, name.Trim(), Utf8);
                ActiveId = p.Id;
            }
            catch { }
        }

        /// <summary>Copies the live config (protocol / kill switch edits) back into the active saved server.</summary>
        public static void SyncActive()
        {
            try
            {
                var a = Active;
                if (a != null && Cfg.Exists) File.Copy(Cfg.Toml, a.Toml, true);
            }
            catch { }
        }

        /// <summary>Stores a freshly imported config. Same host + user replaces the old entry (e.g. a rotated password).</summary>
        public static Profile Add(string toml, string name)
        {
            Directory.CreateDirectory(Dir);
            var host = Cfg.GetFrom(toml, "hostname") ?? "";
            var user = Cfg.GetFrom(toml, "username") ?? "";
            var p = List().FirstOrDefault(x => string.Equals(x.Host, host, StringComparison.OrdinalIgnoreCase) && x.User == user)
                    ?? new Profile { Id = NewId() };
            File.Copy(toml, p.Toml, true);
            if (string.IsNullOrWhiteSpace(name)) name = Cfg.GetFrom(toml, "name");
            if (string.IsNullOrWhiteSpace(name)) name = host;
            File.WriteAllText(p.NameFile, name.Trim(), Utf8);
            p.Name = name.Trim(); p.Host = host; p.User = user;
            return p;
        }

        /// <summary>Makes a saved server the live config. sync=false skips saving the old live config first.</summary>
        public static void Activate(Profile p, bool sync = true)
        {
            if (sync && p.Id != ActiveId) SyncActive();
            File.Copy(p.Toml, Cfg.Toml, true);
            ActiveId = p.Id;
            Ini.Set("name", p.Name);
        }

        public static void Delete(Profile p)
        {
            try { File.Delete(p.Toml); } catch { }
            try { File.Delete(p.NameFile); } catch { }
            if (p.Id == ActiveId) ActiveId = "";
        }
    }

    /// <summary>tt:// URL scheme: registration and hand-over of a link between instances.</summary>
    static class Proto
    {
        static string PendingPath => Path.Combine(Cfg.Dir, "pending_link.txt");

        public static bool IsLink(string a) => a != null && a.StartsWith("tt:", StringComparison.OrdinalIgnoreCase);

        public static void Stash(string link)
        {
            try { File.WriteAllText(PendingPath, link.Trim(), new UTF8Encoding(false)); } catch { }
        }

        public static string TakePending()
        {
            try
            {
                if (!File.Exists(PendingPath)) return null;
                var s = File.ReadAllText(PendingPath).Trim();
                File.Delete(PendingPath);
                if (s.StartsWith("tt:", StringComparison.OrdinalIgnoreCase) && !s.StartsWith("tt://", StringComparison.OrdinalIgnoreCase))
                    s = "tt://" + s.Substring(3).TrimStart('/');
                return s.Length > 0 ? s : null;
            }
            catch { return null; }
        }

        /// <summary>Registers tt:// for the current user (HKCU), pointing at this exe. No-op if already correct.</summary>
        public static void Register()
        {
            try
            {
                var exe = Application.ExecutablePath;
                var cmd = "\"" + exe + "\" \"%1\"";
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Classes\tt\shell\open\command"))
                    if (k != null && string.Equals(k.GetValue("") as string, cmd, StringComparison.OrdinalIgnoreCase)) return;
                using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Classes\tt"))
                {
                    k.SetValue("", "URL:TrustTunnel link");
                    k.SetValue("URL Protocol", "");
                }
                using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Classes\tt\DefaultIcon")) k.SetValue("", "\"" + exe + "\",0");
                using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Classes\tt\shell\open\command")) k.SetValue("", cmd);
            }
            catch { }
        }
    }
}
