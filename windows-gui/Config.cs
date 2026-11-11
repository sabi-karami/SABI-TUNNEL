using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace TT
{
    /// <summary>Paths, the client TOML file, the bypass list and GUI settings.</summary>
    static class Cfg
    {
        public static readonly string Dir = AppDomain.CurrentDomain.BaseDirectory;
        public static string Toml => Path.Combine(Dir, "trusttunnel_client.toml");
        public static string Bypass => Path.Combine(Dir, "bypass.txt");
        public static string Client => Path.Combine(Dir, "trusttunnel_client.exe");
        public static string Wizard => Path.Combine(Dir, "setup_wizard.exe");
        public static string Wintun => Path.Combine(Dir, "wintun.dll");
        static string IniPath => Path.Combine(Dir, "gui.ini");
        static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        public const string DefaultBypass =
            "# Sites listed here open directly (not through the VPN). One per line.\r\n" +
            "# \"*.ir\" = every .ir domain.  For .com sites write both \"site.com\" and \"*.site.com\".\r\n" +
            "*.ir\r\ndigikala.com\r\n*.digikala.com\r\naparat.com\r\n*.aparat.com\r\neitaa.com\r\n*.eitaa.com\r\n" +
            "bale.ai\r\n*.bale.ai\r\nfilimo.com\r\n*.filimo.com\r\ntelewebion.com\r\n*.telewebion.com\r\n" +
            "varzesh3.com\r\n*.varzesh3.com\r\ntorob.com\r\n*.torob.com\r\n";

        public static bool Exists => File.Exists(Toml);
        public static string NewToml => Path.Combine(Dir, "trusttunnel_client.new.toml");

        /// <summary>The copy handed to the client: the live config with host-name addresses replaced by resolved IPs.
        /// The live file (and the saved server) keep the names.</summary>
        public static string RunToml => Path.Combine(Dir, "trusttunnel_client.run.toml");

        public static string WriteRunToml(List<string> addresses)
        {
            var t = File.ReadAllText(Toml);
            var re = new Regex(@"(?m)^([ \t]*addresses[ \t]*=)[^\r\n]*");
            if (!re.IsMatch(t)) return Toml;
            t = re.Replace(t, m => m.Groups[1].Value + " [" + string.Join(", ", addresses.Select(a => "\"" + a + "\"")) + "]", 1);
            File.WriteAllText(RunToml, t, Utf8);
            return RunToml;
        }

        public static string Get(string key) => Exists ? GetFrom(Toml, key) : null;

        /// <summary>Reads a top-level "key = value" from any client TOML file.</summary>
        public static string GetFrom(string path, string key)
        {
            if (!File.Exists(path)) return null;
            var re = new Regex("^\\s*" + Regex.Escape(key) + "\\s*=\\s*(.*)$");
            foreach (var raw in File.ReadAllLines(path))
            {
                var m = re.Match(raw);
                if (!m.Success) continue;
                var v = m.Groups[1].Value.Trim();
                if (v.StartsWith("\""))
                {
                    int end = v.IndexOf('"', 1);
                    return end > 0 ? v.Substring(1, end - 1) : v.Trim('"');
                }
                return v;
            }
            return null;
        }

        public static void Set(string key, string literal)
        {
            if (!Exists) return;
            var t = File.ReadAllText(Toml);
            var re = new Regex("(?m)^[ \\t]*" + Regex.Escape(key) + "[ \\t]*=[^\\r\\n]*");
            if (!re.IsMatch(t)) return;
            t = re.Replace(t, m => key + " = " + literal, 1);
            File.WriteAllText(Toml, t, Utf8);
        }

        public static List<string> Addresses()
        {
            var raw = Get("addresses") ?? "";
            return Regex.Matches(raw, "\"([^\"]+)\"").Cast<Match>().Select(m => m.Groups[1].Value).ToList();
        }

        public static HashSet<string> ServerIps()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var a in Addresses())
            {
                if (a.StartsWith("[")) { int e = a.IndexOf(']'); if (e > 1) set.Add(a.Substring(1, e - 1)); }
                else { int c = a.LastIndexOf(':'); set.Add(c > 0 ? a.Substring(0, c) : a); }
            }
            return set;
        }

        public static int ServerPort()
        {
            foreach (var a in Addresses())
            {
                int c = a.LastIndexOf(':');
                int b = a.LastIndexOf(']');
                if (c > 0 && c > b && int.TryParse(a.Substring(c + 1), out var port) && port > 0 && port < 65536) return port;
            }
            return 443;
        }

        public static string ReadBypass()
        {
            if (!File.Exists(Bypass)) File.WriteAllText(Bypass, DefaultBypass, Utf8);
            return File.ReadAllText(Bypass);
        }

        public static void WriteBypass(string text) => File.WriteAllText(Bypass, text.Replace("\r\n", "\n").Replace("\n", "\r\n"), Utf8);

        public static void ApplyBypass()
        {
            var items = ReadBypass().Split('\n').Select(l => l.Trim())
                .Where(l => l.Length > 0 && !l.StartsWith("#"))
                .Select(l => "\"" + l.Replace("\"", "").Replace("\\", "") + "\"");
            Set("exclusions", "[" + string.Join(", ", items) + "]");
        }

        /// <summary>Imports a tt:// deep link with the official setup_wizard. Returns null on success or an error text.</summary>
        public static string ImportLink(string link)
        {
            link = DeepLink.Normalize(link);
            if (!link.StartsWith("tt://", StringComparison.OrdinalIgnoreCase)) return "لینک باید با tt:// شروع شود.";
            if (!Regex.IsMatch(link, "^tt://\\?[A-Za-z0-9_\\-=]+$")) return "لینک ناقص یا خراب است؛ کامل کپی کنید.";
            var bad = DeepLink.Check(link); if (bad != null) return bad;
            if (!File.Exists(Wizard)) return "فایل setup_wizard.exe کنار برنامه نیست.";
            var tmp = NewToml;
            try { File.Delete(tmp); } catch { }
            var psi = new ProcessStartInfo(Wizard, "--mode non-interactive --deeplink \"" + link + "\" --settings \"" + tmp + "\"")
            {
                UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Dir,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            using (var p = Process.Start(psi))
            {
                var o = p.StandardOutput.ReadToEndAsync();
                var e = p.StandardError.ReadToEndAsync();
                if (!p.WaitForExit(30000)) { try { p.Kill(); } catch { } return "setup_wizard پاسخ نداد."; }
                Task.WaitAll(new Task[] { o, e }, 3000);
                if (p.ExitCode != 0 || !File.Exists(tmp))
                {
                    var msg = (e.Result + "\n" + o.Result).Trim();
                    return msg.Length > 500 ? msg.Substring(msg.Length - 500) : msg;
                }
            }
            return null;   // the new settings are left in NewToml; Profiles.Add() stores them
        }
    }

    /// <summary>Reads the display name (tag 0x0C) from a tt:// deep link (TLV with varints, base64url).</summary>
    static class DeepLink
    {
        /// <summary>Windows hands URL-protocol links over as "tt:///?…" (and may percent-encode); bring them back to "tt://?…".</summary>
        public static string Normalize(string link)
        {
            // Telegram and RTL editors add invisible direction marks / zero-width spaces (Unicode Cf) around or inside links.
            var s = Regex.Replace(link ?? "", @"\p{Cf}", "").Trim().Trim('"', '\'', ' ', '`', '<', '>', '«', '»');
            if (s.IndexOf('%') >= 0) { try { s = Uri.UnescapeDataString(s); } catch { } }
            var m = Regex.Match(s, @"tt:/*\?[A-Za-z0-9_\-=]+", RegexOptions.IgnoreCase);   // the link may sit inside a longer message
            if (m.Success) s = m.Value;
            s = Regex.Replace(s, "^tt:/*\\?", "tt://?", RegexOptions.IgnoreCase);
            return s.TrimEnd('/');
        }

        const string Cut = "لینک ناقص کپی شده (انتهایش جا مانده)؛ دوباره و کامل کپی کنید.";

        /// <summary>Walks the TLV payload so a cut-off or too-new link gets a clear message instead of the wizard's
        /// English error. Returns null when the link looks fine.</summary>
        public static string Check(string link)
        {
            byte[] b;
            try
            {
                var s = link.Substring(link.IndexOf('?') + 1).Replace('-', '+').Replace('_', '/').TrimEnd('=');
                if (s.Length % 4 == 1) return Cut;
                b = Convert.FromBase64String(s.PadRight(s.Length + (4 - s.Length % 4) % 4, '='));
            }
            catch { return "لینک خراب است؛ دوباره و کامل کپی کنید."; }
            int i = 0, version = -1;
            try
            {
                while (i < b.Length)
                {
                    ulong tag = Varint(b, ref i), len = Varint(b, ref i);
                    if (len > (ulong)(b.Length - i)) return Cut;
                    if (tag == 0x00 && len > 0) version = b[i];
                    i += (int)len;
                }
            }
            catch (IndexOutOfRangeException) { return Cut; }
            if (version > 1)
                return "این لینک برای نسخه‌ی جدیدتر TrustTunnel ساخته شده و این برنامه هنوز آن را نمی‌شناسد؛ از سرویس‌دهنده لینک معمولی بخواهید.";
            return null;
        }

        public static string Name(string link)
        {
            try
            {
                var s = link.Substring(link.IndexOf('?') + 1).Replace('-', '+').Replace('_', '/').TrimEnd('=');
                s = s.PadRight(s.Length + (4 - s.Length % 4) % 4, '=');
                var b = Convert.FromBase64String(s);
                int i = 0;
                while (i < b.Length)
                {
                    ulong tag = Varint(b, ref i), len = Varint(b, ref i);
                    if (i + (int)len > b.Length) break;
                    if (tag == 0x0C) return Encoding.UTF8.GetString(b, i, (int)len);
                    i += (int)len;
                }
            }
            catch { }
            return null;
        }

        static ulong Varint(byte[] b, ref int i)
        {
            // QUIC-style variable-length integer (RFC 9000 §16)
            int prefix = b[i] >> 6, n = 1 << prefix;
            ulong v = (ulong)(b[i] & 0x3F);
            for (int k = 1; k < n; k++) v = (v << 8) | b[i + k];
            i += n;
            return v;
        }
    }

    static class Ini
    {
        static string PathIni => System.IO.Path.Combine(Cfg.Dir, "gui.ini");

        static Dictionary<string, string> Load()
        {
            var d = new Dictionary<string, string>();
            try
            {
                if (File.Exists(PathIni))
                    foreach (var l in File.ReadAllLines(PathIni))
                    {
                        int k = l.IndexOf('=');
                        if (k > 0) d[l.Substring(0, k).Trim()] = l.Substring(k + 1).Trim();
                    }
            }
            catch { }
            return d;
        }

        public static string Get(string key, string def = null) => Load().TryGetValue(key, out var v) ? v : def;

        public static void Set(string key, string value)
        {
            var d = Load();
            d[key] = value;
            File.WriteAllLines(PathIni, d.Select(kv => kv.Key + "=" + kv.Value), new UTF8Encoding(false));
        }
    }
}
