using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;

namespace TT
{
    /// <summary>TrustTunnel.exe --selftest=out.txt (used by CI): link clean-up, the deep-link pre-check, host-name
    /// resolution (Windows DNS and DNS-over-HTTPS), the wizard import of a relay-style link, map labels and the
    /// connection probe used when the client gives up. Writes one line per check; exit code 0 = all passed,
    /// 3 = something failed.</summary>
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
                Ok("message for code 10 (attempts exceeded)", (MainForm.Translate("10 Number of connection attempts exceeded") ?? "").Contains("برقرار نشد"));
                var steps2 = new List<string>();
                var merged = Resolver.Resolve("one.one.one.one", steps2.Add);
                Ok("resolve: Windows DNS and DoH merged", merged != null && merged.Contains("1.1.1.1") && merged.Count <= 6, merged == null ? "null" : string.Join(", ", merged));
                log.AddRange(steps2.Select(s => "        " + s));

                // route-map labels for other providers' server names
                void Geo1(string hint, string want) { var got = Geo.Find(hint).Name; Ok("map label: " + hint + " -> " + want, got == want, got); }
                Geo1("de.ttnl.top GermanyTT7999", "آلمان");
                Geo1("tt-oh.example.ir Sabi VPN", "اوهایو");
                Geo1("vpn.example.top TestRelay", "سرور");
                Geo1("relay.example.net سرور هلند ۲", "هلند");
                Geo1("x1.example.net \U0001F1EB\U0001F1EE Helsinki", "فنلاند");
                Geo1("vpn.example.de Relay", "سرور");
                Geo1("nl2.example.com Fast", "هلند");
                Geo1("example.co.uk Main", "سرور");
                Geo1("srv.example.com US 3", "آمریکا");

                // connection probe (used when the client gives up)
                bool Framed(byte[] h) => h.Length > 9 && h[0] == 0x16 && (h[3] << 8 | h[4]) == h.Length - 5 && h[5] == 1 && (h[6] << 16 | h[7] << 8 | h[8]) == h.Length - 9;
                var small = Probe.ClientHello("vpn.example.top", "", false);
                var bigHello = Probe.ClientHello("vpn.example.top", "", true);
                Ok("hello: small one framed and padded to 517 bytes", Framed(small) && small.Length == 517, small.Length.ToString());
                Ok("hello: big one (X25519MLKEM768) framed, over 1400 bytes", Framed(bigHello) && bigHello.Length > 1400, bigHello.Length.ToString());
                var r32 = Probe.Random32("a1b2c3");
                Ok("client_random prefix", r32.Length == 32 && r32[0] == 0xA1 && r32[1] == 0xB2 && r32[2] == 0xC3);
                var m32 = Probe.Random32("ff00/f0f0");
                Ok("client_random prefix/mask", m32.Length == 32 && (m32[0] & 0xF0) == 0xF0 && (m32[1] & 0xF0) == 0x00, BitConverter.ToString(m32, 0, 2));
                var steps3 = new List<string>();
                var p1 = Probe.Run(new List<string> { "1.1.1.1:443" }, "one.one.one.one", "", true, steps3.Add);
                Ok("probe 1.1.1.1:443: TCP, TLS, big post-quantum hello answered by a handshake",
                   p1.Count == 1 && p1[0].Tcp && p1[0].Tls && p1[0].Big && p1[0].TlsRecord == 0x16 && p1[0].BigRecord == 0x16, p1.Count == 1 ? p1[0].ToString() : "no result");
                var p2 = Probe.Run(new List<string> { "203.0.113.7:443" }, "vpn.example.top", "", true, steps3.Add);
                Ok("probe 203.0.113.7:443 (unused range): no TCP", p2.Count == 1 && !p2[0].Tcp, p2.Count == 1 ? p2[0].ToString() : "no result");
                log.AddRange(steps3.Select(s => "        " + s));
                Probe.Result Res(bool tcp, bool tls, bool tried, bool big, string why = null) =>
                    new Probe.Result { Address = "192.0.2.1:443", Tcp = tcp, Tls = tls, BigTried = tried, Big = big, Why = why };
                List<Probe.Result> L1(params Probe.Result[] r) => r.ToList();
                Ok("classify: nothing answers", Probe.Classify(L1(Res(false, false, false, false))) == Probe.Kind.Unreachable);
                Ok("classify: TCP but no TLS", Probe.Classify(L1(Res(true, false, false, false))) == Probe.Kind.TlsBlocked);
                Ok("classify: big hello lost", Probe.Classify(L1(Res(true, true, true, false))) == Probe.Kind.BigHello);
                Ok("classify: one address fine", Probe.Classify(L1(Res(false, false, false, false), Res(true, true, true, true))) == Probe.Kind.Reachable);
                Ok("classify: post-quantum off", Probe.Classify(L1(Res(true, true, false, false))) == Probe.Kind.Reachable);
                Ok("classify: name not found", Probe.Classify(L1(Res(false, false, false, false, "dns: no such host"))) == Probe.Kind.NoAddress);
                foreach (Probe.Kind k in Enum.GetValues(typeof(Probe.Kind)))
                {
                    var text = Probe.Explain(k, new List<Probe.Result>(), false, true, "HTTP/3", out var brief);
                    Ok("explain: " + k, text.Length > 40 && text.Length < 600 && brief.Length > 0 && text.Contains("•"), text.Length + " chars");
                }
                Ok("explain: unreachable says the server does not answer", Probe.Explain(Probe.Kind.Unreachable, new List<Probe.Result>(), false, true, "HTTP/3", out _).Contains("جواب نمی‌دهد"));

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
                    Cfg.Set("post_quantum_group_enabled", "false", addIfMissing: true);
                    Ok("post-quantum switch-off", Cfg.Get("post_quantum_group_enabled") == "false");
                    Cfg.Set("tt_selftest_key", "1", addIfMissing: true);
                    Ok("set adds a missing key at the top", Cfg.Get("tt_selftest_key") == "1" && File.ReadAllText(Cfg.Toml).StartsWith("tt_selftest_key = 1"));
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
