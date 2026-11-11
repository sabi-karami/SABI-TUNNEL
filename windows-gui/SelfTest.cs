using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;

namespace TT
{
    /// <summary>TrustTunnel.exe --selftest=out.txt (used by CI): link clean-up, the deep-link pre-check, host-name
    /// resolution (Windows DNS and DNS-over-HTTPS) and the wizard import of a relay-style link. Writes one line per
    /// check; exit code 0 = all passed, 3 = something failed.</summary>
    static class SelfTest
    {
        public static int Run(string outFile)
        {
            var log = new List<string>(); int bad = 0;
            void Ok(string name, bool ok, string detail = null)
            {
                log.Add((ok ? "ok    " : "FAIL  ") + name + (string.IsNullOrEmpty(detail) ? "" : "  | " + detail));
                if (!ok) bad++;
            }
            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                var L = Link(1);
                Ok("normalize: plain link", DeepLink.Normalize(L) == L);
                Ok("normalize: RLM/ZWSP/LRM/BOM from Telegram", DeepLink.Normalize("\u200F" + L.Substring(0, 9) + "\u200B" + L.Substring(9) + "\u200E\uFEFF") == L);
                Ok("normalize: link inside a message", DeepLink.Normalize("کانفیگ آلمان:\r\n" + L + "\r\nممنون") == L);
                Ok("normalize: tt:/// and percent-encoding", DeepLink.Normalize("\"tt:///%3F" + L.Substring(6) + "/\"") == L);
                Ok("check: complete link", DeepLink.Check(L) == null);
                Ok("check: cut-off link", DeepLink.Check(L.Substring(0, L.Length - 10)) != null);
                Ok("check: garbage", DeepLink.Check("tt://?A") != null);
                Ok("check: version 2 link", DeepLink.Check(Link(2)) != null);
                Ok("name tag", DeepLink.Name(L) == "TestRelay");
                Ok("filtered answer 10.10.34.36", Resolver.Bogus(IPAddress.Parse("10.10.34.36")));
                Ok("filtered answer 0.0.0.0", Resolver.Bogus(IPAddress.Parse("0.0.0.0")));
                Ok("real answer 1.1.1.1", !Resolver.Bogus(IPAddress.Parse("1.1.1.1")));
                var sys = Resolver.SystemDns("one.one.one.one", 8000, out var why);
                Ok("windows dns", sys != null && sys.Contains("1.1.1.1"), why ?? string.Join(", ", sys));
                int doh = 0;
                foreach (var url in Resolver.Doh)
                {
                    var r = Resolver.QueryDoh(url, "one.one.one.one", 8000, out var w);
                    bool ok = r != null && r.Contains("1.1.1.1");
                    if (ok) doh++;
                    log.Add((ok ? "ok    " : "warn  ") + "doh " + new Uri(url).Host + "  | " + (w ?? string.Join(", ", r)));
                }
                Ok("doh: at least 2 of " + Resolver.Doh.Length + " resolvers answer", doh >= 2);
                var steps = new List<string>();
                var run = Resolver.ForRun(new List<string> { "one.one.one.one:8443", "203.0.113.7:443", "[2001:db8::1]:443" }, steps.Add);
                Ok("for-run: names to IPs, IPs kept", run != null && run.Contains("1.1.1.1:8443") && run.Contains("203.0.113.7:443") && run.Contains("[2001:db8::1]:443"),
                   run == null ? "null" : string.Join(" ", run));
                log.AddRange(steps.Select(s => "        " + s));
                Ok("for-run: only IPs, unchanged", Resolver.ForRun(new List<string> { "203.0.113.7:443" }, null) == null);
                Ok("message for DNS failure", (MainForm.Translate("Failed to resolve any endpoint or relay addresses. Check network connectivity and configuration.") ?? "").Contains("DNS"));

                var err = Cfg.ImportLink("\u200F" + L + "\u200E");
                Ok("wizard import of a relay link", err == null, err);
                if (err == null)
                {
                    var a = Cfg.GetFrom(Cfg.NewToml, "addresses") ?? "";
                    Ok("wizard keeps the host name", a.Contains("relay-test.example.net:443"), a);
                    Ok("wizard keeps dns_upstreams", (Cfg.GetFrom(Cfg.NewToml, "dns_upstreams") ?? "").Contains("8.8.8.8"));
                    File.Copy(Cfg.NewToml, Cfg.Toml, true);
                    var path = Cfg.WriteRunToml(new List<string> { "1.1.1.1:443", "1.0.0.1:443" });
                    var ra = Cfg.GetFrom(path, "addresses") ?? "";
                    Ok("run config gets the IPs", path == Cfg.RunToml && ra == "[\"1.1.1.1:443\", \"1.0.0.1:443\"]", ra);
                    var live = Cfg.GetFrom(Cfg.Toml, "addresses") ?? "";
                    Ok("live config keeps the name", live.Contains("relay-test.example.net"), live);
                    Ok("run config keeps the rest", File.ReadAllText(path).Replace(ra, "") == File.ReadAllText(Cfg.Toml).Replace(live, ""));
                    foreach (var f in new[] { Cfg.NewToml, Cfg.Toml, Cfg.RunToml }) { try { File.Delete(f); } catch { } }
                }
            }
            catch (Exception ex) { Ok("no exception", false, ex.ToString()); }
            log.Add(bad == 0 ? "ALL PASSED" : bad + " FAILED");
            File.WriteAllLines(outFile, log, new UTF8Encoding(false));
            return bad == 0 ? 0 : 3;
        }

        /// <summary>A deep link shaped like third-party relay links: host-name address, explicit protocol,
        /// DNS upstreams and an unusual tag order. Dummy credentials.</summary>
        static string Link(int version)
        {
            var b = new List<byte>();
            void T(int tag, byte[] v) { b.Add((byte)tag); b.Add((byte)v.Length); b.AddRange(v); }
            byte[] S(string s) => Encoding.UTF8.GetBytes(s);
            T(0x00, new[] { (byte)version });
            T(0x01, S("vpn.example.top"));
            T(0x05, S("user-test-123"));
            T(0x06, S("P4ss-synthetic-000"));
            T(0x02, S("relay-test.example.net:443"));
            T(0x0C, S("TestRelay"));
            T(0x04, new byte[] { 0 });
            T(0x09, new byte[] { 1 });
            T(0x0D, new byte[] { 7 }.Concat(S("1.1.1.1")).Concat(new byte[] { 7 }).Concat(S("8.8.8.8")).ToArray());
            return "tt://?" + Convert.ToBase64String(b.ToArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }
    }
}
