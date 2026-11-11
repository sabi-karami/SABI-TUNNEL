using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;

namespace TT
{
    /// <summary>Resolves host-name server addresses (relays) before the client starts.
    /// The client asks one system resolver and gives up when that fails, and filtered networks often answer with
    /// nothing or with the 10.10.34.x block page. Windows DNS is tried first, then DNS-over-HTTPS by IP
    /// (Cloudflare, Google, AdGuard), so the fallback needs no DNS of its own.</summary>
    static class Resolver
    {
        public static readonly string[] Doh =
        {
            "https://1.1.1.1/dns-query?type=A&name=",
            "https://8.8.8.8/resolve?type=A&name=",
            "https://94.140.14.140/resolve?type=A&name=",
        };

        public static bool IsIp(string host) => IPAddress.TryParse(host, out _);

        /// <summary>"host:port", "[v6]:port" or a bare host: host and port (443 when missing).</summary>
        public static void Split(string address, out string host, out string port)
        {
            var a = (address ?? "").Trim();
            host = a; port = "443";
            if (a.StartsWith("["))
            {
                int e = a.IndexOf(']');
                if (e > 0) { host = a.Substring(1, e - 1); if (a.Length > e + 2 && a[e + 1] == ':') port = a.Substring(e + 2); }
                return;
            }
            int c = a.LastIndexOf(':');
            if (c > 0 && a.IndexOf(':') == c) { host = a.Substring(0, c); port = a.Substring(c + 1); }
        }

        /// <summary>Answers that are not a real server: the filtering page (10.10.34.0/24), 0.0.0.0/8, loopback.</summary>
        public static bool Bogus(IPAddress a)
        {
            if (a.IsIPv4MappedToIPv6) a = a.MapToIPv4();
            if (IPAddress.IsLoopback(a) || a.Equals(IPAddress.IPv6Any)) return true;
            if (a.AddressFamily != AddressFamily.InterNetwork) return false;
            var b = a.GetAddressBytes();
            return b[0] == 0 || (b[0] == 10 && b[1] == 10 && b[2] == 34);
        }

        /// <summary>The address list with every host name replaced by its IPv4 addresses (same port), or null when
        /// there is no name or none could be resolved (the client then tries the names itself).</summary>
        public static List<string> ForRun(List<string> addresses, Action<string> log)
        {
            var list = new List<string>(); bool changed = false;
            foreach (var a in addresses)
            {
                Split(a, out var host, out var port);
                var ips = host.Length == 0 || IsIp(host) ? null : Resolve(host, log);
                if (ips == null) { list.Add(a); continue; }
                list.AddRange(ips.Select(ip => ip + ":" + port));
                changed = true;
            }
            return changed ? list.Distinct().ToList() : null;
        }

        public static List<string> Resolve(string host, Action<string> log)
        {
            var ips = SystemDns(host, 5000, out var why);
            if (ips != null && ips.Count > 0) { log?.Invoke("[gui] " + host + " = " + string.Join(", ", ips) + " (Windows DNS)"); return ips; }
            log?.Invoke("[gui] Windows DNS for " + host + ": " + why + "; trying DNS-over-HTTPS");
            foreach (var url in Doh)
            {
                var via = new Uri(url).Host;
                ips = QueryDoh(url, host, 4000, out why);
                if (ips != null && ips.Count > 0) { log?.Invoke("[gui] " + host + " = " + string.Join(", ", ips) + " (DoH " + via + ")"); return ips; }
                log?.Invoke("[gui] DoH " + via + ": " + why);
            }
            log?.Invoke("[gui] " + host + " could not be resolved; the client will try it itself");
            return null;
        }

        public static List<string> SystemDns(string host, int ms, out string why)
        {
            why = null;
            try
            {
                var t = Dns.GetHostAddressesAsync(host);
                if (!t.Wait(ms)) { why = "no answer in " + ms / 1000 + " s"; return null; }
                var all = t.Result;
                var good = all.Where(a => a.AddressFamily == AddressFamily.InterNetwork && !Bogus(a))
                              .Select(a => a.ToString()).Distinct().Take(4).ToList();
                if (good.Count == 0) why = all.Length == 0 ? "empty answer" : "filtered answer " + string.Join(", ", all.Take(3).Select(a => a.ToString()));
                return good;
            }
            catch (AggregateException ex) { why = (ex.InnerException ?? ex).Message; return null; }
            catch (Exception ex) { why = ex.Message; return null; }
        }

        public static List<string> QueryDoh(string url, string host, int ms, out string why)
        {
            why = null;
            try
            {
                var server = IPAddress.Parse(new Uri(url).Host);
                var rq = (HttpWebRequest)WebRequest.Create(url + Uri.EscapeDataString(host));
                rq.Accept = "application/dns-json"; rq.UserAgent = "TrustTunnelGUI"; rq.Proxy = null; rq.KeepAlive = false;
                rq.Timeout = ms; rq.ReadWriteTimeout = ms;
                // The URL is an IP: accept a trusted certificate that is issued to exactly that IP.
                rq.ServerCertificateValidationCallback = (s, cert, chain, errors) =>
                    errors == SslPolicyErrors.None || (errors == SslPolicyErrors.RemoteCertificateNameMismatch && HasIp(cert, server));
                string body;
                using (var rs = rq.GetResponse())
                using (var sr = new StreamReader(rs.GetResponseStream())) body = sr.ReadToEnd();
                var ips = Regex.Matches(body, @"""data""\s*:\s*""(\d{1,3}(?:\.\d{1,3}){3})""").Cast<Match>()
                    .Select(m => m.Groups[1].Value)
                    .Where(v => IPAddress.TryParse(v, out var a) && !Bogus(a)).Distinct().Take(4).ToList();
                if (ips.Count == 0) why = Regex.IsMatch(body, @"""Status""\s*:\s*3") ? "no such name" : "no A record";
                return ips;
            }
            catch (Exception ex) { why = ex.Message; return null; }
        }

        /// <summary>True when the certificate's subjectAltName lists this IP (GeneralName [7] iPAddress).</summary>
        static bool HasIp(X509Certificate cert, IPAddress ip)
        {
            try
            {
                if (cert == null) return false;
                var ext = new X509Certificate2(cert).Extensions["2.5.29.17"];
                if (ext == null) return false;
                byte[] raw = ext.RawData, want = ip.GetAddressBytes();
                for (int i = 0; i + 2 + want.Length <= raw.Length; i++)
                    if (raw[i] == 0x87 && raw[i + 1] == want.Length && raw.Skip(i + 2).Take(want.Length).SequenceEqual(want)) return true;
            }
            catch { }
            return false;
        }
    }
}
