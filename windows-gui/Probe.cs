using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace TT
{
    /// <summary>Runs after the client gives up ("Number of connection attempts exceeded") to find out why: a TCP connect
    /// to every server address, then a TLS ClientHello shaped like the client's (SNI, ALPN h2 + http/1.1, TLS 1.3 + 1.2,
    /// x25519, GREASE, the client_random prefix). The hello is built by hand because SChannel on Windows 10 has no
    /// TLS 1.3, and only the first record of the answer is read: a handshake or an alert record both mean the server
    /// heard us. When that works and post-quantum is on (the client's default), a big hello with an X25519MLKEM768
    /// share (~1.5 KB, two TCP segments) follows, because some networks drop big hellos, which breaks only the
    /// client's default handshake.</summary>
    static class Probe
    {
        public enum Kind { NoAddress, Unreachable, TlsBlocked, BigHello, Reachable }

        const int ConnectMs = 5000, AnswerMs = 6000, OverallMs = 25000, MaxAddresses = 6;

        public sealed class Result
        {
            public string Address;
            public volatile bool Tcp, Tls, BigTried, Big;
            public volatile int TcpMs = -1, TlsRecord, BigRecord;   // first byte of the answer: 0x16 handshake, 0x15 alert
            public volatile string Why;

            public Result Copy() => new Result
            {
                Address = Address, Tcp = Tcp, Tls = Tls, BigTried = BigTried, Big = Big,
                TcpMs = TcpMs, TlsRecord = TlsRecord, BigRecord = BigRecord, Why = Why
            };

            public override string ToString()
            {
                var s = new StringBuilder(Address + ": ");
                if (!Tcp) s.Append("no TCP");
                else
                {
                    s.Append("TCP " + TcpMs + " ms, ");
                    s.Append(Tls ? "TLS answers (" + Record(TlsRecord) + ")" : "no TLS answer");
                    if (BigTried) s.Append(Big ? ", big hello answered (" + Record(BigRecord) + ")" : ", big hello not answered");
                }
                if (!string.IsNullOrEmpty(Why)) s.Append(" [" + Why + "]");
                return s.ToString();
            }

            static string Record(int b) => b == 0x16 ? "handshake" : b == 0x15 ? "alert" : "0x" + b.ToString("x2");
        }

        /// <summary>Probes up to six addresses in parallel (about 25 s at most) and logs one line per address.</summary>
        public static List<Result> Run(List<string> addresses, string sni, string clientRandom, bool pq, Action<string> log)
        {
            var list = (addresses ?? new List<string>()).Where(a => !string.IsNullOrWhiteSpace(a)).Select(a => a.Trim())
                       .Distinct(StringComparer.OrdinalIgnoreCase).Take(MaxAddresses).Select(a => new Result { Address = a }).ToList();
            var tasks = list.Select(r => Task.Run(() => One(r, sni, clientRandom, pq))).ToArray();
            try { if (tasks.Length > 0) Task.WaitAll(tasks, OverallMs); } catch { }
            var res = new List<Result>();
            for (int i = 0; i < list.Count; i++)
            {
                var r = list[i].Copy();
                if (!tasks[i].IsCompleted) r.Why = (r.Why == null ? "" : r.Why + "; ") + "still waiting after " + OverallMs / 1000 + " s";
                log?.Invoke("[gui] probe " + r);
                res.Add(r);
            }
            return res;
        }

        static void One(Result r, string sni, string clientRandom, bool pq)
        {
            try
            {
                Resolver.Split(r.Address, out var host, out var portText);
                if (!int.TryParse(portText, out var port) || port <= 0 || port > 65535) port = 443;
                if (!IPAddress.TryParse(host, out var ip))
                {
                    var ips = Resolver.SystemDns(host, 4000, out var why);
                    if (ips == null || ips.Count == 0) { r.Why = "dns: " + (why ?? "no address"); return; }
                    ip = IPAddress.Parse(ips[0]);
                }
                var ep = new IPEndPoint(ip, port);
                int first = Hello(ep, ClientHello(sni, clientRandom, false), r, "hello");
                if (first == 0 && r.Tcp) first = Hello(ep, ClientHello(sni, clientRandom, false), r, "hello (2nd try)");
                if (first == 0) return;
                r.Why = null; r.TlsRecord = first; r.Tls = true;
                if (!pq) return;
                r.BigTried = true;
                // twice, so one lost connection is not taken for a network that drops big hellos
                for (int i = 0; i < 2 && !r.Big; i++)
                {
                    int big = Hello(ep, ClientHello(sni, clientRandom, true), r, i == 0 ? "big hello" : "big hello (2nd try)");
                    if (big != 0) { r.BigRecord = big; r.Big = true; r.Why = null; }
                }
            }
            catch (Exception ex) { if (r.Why == null) r.Why = ex.GetType().Name + ": " + ex.Message; }
        }

        /// <summary>Connects, sends the hello and returns the first byte of the answer when it is a TLS record
        /// (0x16 handshake, 0x15 alert); 0 = no TLS answer (r.Why says what happened).</summary>
        static int Hello(IPEndPoint ep, byte[] hello, Result r, string what)
        {
            using (var s = new Socket(ep.AddressFamily, SocketType.Stream, ProtocolType.Tcp))
            {
                s.NoDelay = true;
                var sw = Stopwatch.StartNew();
                try
                {
                    var ar = s.BeginConnect(ep, null, null);
                    if (!ar.AsyncWaitHandle.WaitOne(ConnectMs)) { r.Why = what + ": TCP connect got no answer in " + ConnectMs / 1000 + " s"; return 0; }
                    s.EndConnect(ar);
                }
                catch (SocketException ex) { r.Why = what + ": TCP " + Code(ex); return 0; }
                if (!r.Tcp) { r.TcpMs = (int)sw.ElapsedMilliseconds; r.Tcp = true; }
                s.SendTimeout = AnswerMs; s.ReceiveTimeout = AnswerMs;
                var buf = new byte[5];
                int n;
                try
                {
                    s.Send(hello);
                    n = s.Receive(buf);
                }
                catch (SocketException ex)
                {
                    r.Why = what + ": " + (ex.SocketErrorCode == SocketError.TimedOut ? "no answer in " + AnswerMs / 1000 + " s" : Code(ex) + " after the hello");
                    return 0;
                }
                if (n <= 0) { r.Why = what + ": connection closed without an answer"; return 0; }
                if (buf[0] == 0x16 || buf[0] == 0x15) return buf[0];
                r.Why = what + ": the answer is not TLS (first byte 0x" + buf[0].ToString("x2") + ")";
                return 0;
            }
        }

        static string Code(SocketException ex)
        {
            switch (ex.SocketErrorCode)
            {
                case SocketError.ConnectionRefused: return "refused";
                case SocketError.ConnectionReset: return "reset";
                case SocketError.ConnectionAborted: return "aborted";
                case SocketError.TimedOut: return "timed out";
                case SocketError.NetworkUnreachable: return "network unreachable";
                case SocketError.HostUnreachable: return "host unreachable";
                default: return ex.SocketErrorCode.ToString();
            }
        }

        // ------------------------------------------------------------------ the ClientHello

        /// <summary>A TLS record holding a ClientHello like the client's BoringSSL one. The small hello is padded to
        /// 512 bytes (517 with the record header) the way BoringSSL pads; the big one adds an X25519MLKEM768 share.</summary>
        public static byte[] ClientHello(string sni, string clientRandom, bool big)
        {
            using (var rng = RandomNumberGenerator.Create())
            {
                byte[] Rand(int n) { var b = new byte[n]; rng.GetBytes(b); return b; }
                var g = Rand(5);
                ushort Grease(int i) => (ushort)(((g[i] & 0xF0) | 0x0A) * 0x0101);
                ushort gSuite = Grease(0), gGroup = Grease(1), gExt1 = Grease(2), gExt2 = Grease(3), gVer = Grease(4);
                if (gExt2 == gExt1) gExt2 ^= 0x1010;

                var body = new List<byte> { 0x03, 0x03 };                  // legacy_version TLS 1.2
                body.AddRange(Random32(clientRandom));
                body.Add(32); body.AddRange(Rand(32));                     // legacy_session_id (middlebox compatibility)
                ushort[] suites = { gSuite, 0x1301, 0x1302, 0x1303, 0xC02B, 0xC02F, 0xC02C, 0xC030, 0xCCA9, 0xCCA8, 0xC013, 0xC014, 0x009C, 0x009D, 0x002F, 0x0035 };
                U16(body, suites.Length * 2); foreach (var x in suites) U16(body, x);
                body.Add(1); body.Add(0);                                  // compression: null only

                var ext = new List<byte>();
                Ext(ext, gExt1, new byte[0]);
                var name = SniName(sni);
                if (name != null)
                {
                    var n = Encoding.ASCII.GetBytes(name);
                    var d = new List<byte>(); U16(d, n.Length + 3); d.Add(0); U16(d, n.Length); d.AddRange(n);
                    Ext(ext, 0x0000, d.ToArray());                         // server_name
                }
                Ext(ext, 0x0017, new byte[0]);                             // extended_master_secret
                Ext(ext, 0xFF01, new byte[] { 0 });                        // renegotiation_info
                var gl = new List<ushort> { gGroup };
                if (big) gl.Add(0x11EC);                                   // X25519MLKEM768
                gl.AddRange(new ushort[] { 0x001D, 0x0017, 0x0018 });      // x25519, P-256, P-384
                var gd = new List<byte>(); U16(gd, gl.Count * 2); foreach (var x in gl) U16(gd, x);
                Ext(ext, 0x000A, gd.ToArray());                            // supported_groups
                Ext(ext, 0x000B, new byte[] { 1, 0 });                     // ec_point_formats: uncompressed
                Ext(ext, 0x0023, new byte[0]);                             // session_ticket
                var alpn = new List<byte>(); U16(alpn, 12); alpn.Add(2); alpn.AddRange(Encoding.ASCII.GetBytes("h2"));
                alpn.Add(8); alpn.AddRange(Encoding.ASCII.GetBytes("http/1.1"));
                Ext(ext, 0x0010, alpn.ToArray());                          // ALPN
                Ext(ext, 0x0005, new byte[] { 1, 0, 0, 0, 0 });            // status_request (OCSP)
                ushort[] sig = { 0x0403, 0x0804, 0x0401, 0x0503, 0x0805, 0x0501, 0x0806, 0x0601 };
                var sd = new List<byte>(); U16(sd, sig.Length * 2); foreach (var x in sig) U16(sd, x);
                Ext(ext, 0x000D, sd.ToArray());                            // signature_algorithms
                Ext(ext, 0x0012, new byte[0]);                             // signed_certificate_timestamp
                var ks = new List<byte>();
                U16(ks, gGroup); U16(ks, 1); ks.Add(0);                    // GREASE share
                if (big) { var pqk = MlKem768X25519(Rand); U16(ks, 0x11EC); U16(ks, pqk.Length); ks.AddRange(pqk); }
                U16(ks, 0x001D); U16(ks, 32); ks.AddRange(Rand(32));
                var kd = new List<byte>(); U16(kd, ks.Count); kd.AddRange(ks);
                Ext(ext, 0x0033, kd.ToArray());                            // key_share
                Ext(ext, 0x002D, new byte[] { 1, 1 });                     // psk_key_exchange_modes: psk_dhe_ke
                var vd = new List<byte> { 6 }; U16(vd, gVer); U16(vd, 0x0304); U16(vd, 0x0303);
                Ext(ext, 0x002B, vd.ToArray());                            // supported_versions: TLS 1.3, 1.2
                Ext(ext, 0x001B, new byte[] { 2, 0, 2 });                  // compress_certificate: brotli
                var alps = new List<byte>(); U16(alps, 3); alps.Add(2); alps.AddRange(Encoding.ASCII.GetBytes("h2"));
                Ext(ext, 0x4469, alps.ToArray());                          // application_settings (ALPS): h2
                Ext(ext, gExt2, new byte[] { 0 });

                // BoringSSL pads a hello shorter than 512 bytes (handshake header included) up to 512.
                int len = 4 + body.Count + 2 + ext.Count;
                if (len < 512) Ext(ext, 0x0015, new byte[512 - len >= 5 ? 512 - len - 4 : 1]);

                int bodyLen = body.Count + 2 + ext.Count;
                var hs = new List<byte> { 1, (byte)(bodyLen >> 16), (byte)(bodyLen >> 8), (byte)bodyLen };   // client_hello
                hs.AddRange(body); U16(hs, ext.Count); hs.AddRange(ext);
                var rec = new List<byte> { 0x16, 0x03, 0x01 };             // record: handshake, legacy version TLS 1.0
                U16(rec, hs.Count); rec.AddRange(hs);
                return rec.ToArray();
            }
        }

        /// <summary>X25519MLKEM768 client share: an ML-KEM-768 encapsulation key (3×256 coefficients below q = 3329,
        /// 12 bits each, then the 32-byte seed rho; random coefficients pass the server's modulus check) followed by
        /// an X25519 public value: 1216 bytes. No private key is kept, only the server's first answer is read.</summary>
        static byte[] MlKem768X25519(Func<int, byte[]> rand)
        {
            var k = new byte[1184 + 32];
            var raw = rand(768 * 2);
            int o = 0;
            for (int i = 0; i < 768; i += 2)
            {
                int c0 = ((raw[2 * i] << 8) | raw[2 * i + 1]) % 3329, c1 = ((raw[2 * i + 2] << 8) | raw[2 * i + 3]) % 3329;
                k[o++] = (byte)c0;
                k[o++] = (byte)((c0 >> 8) | ((c1 & 0x0F) << 4));
                k[o++] = (byte)(c1 >> 4);
            }
            Buffer.BlockCopy(rand(32), 0, k, 1152, 32);
            Buffer.BlockCopy(rand(32), 0, k, 1184, 32);
            return k;
        }

        /// <summary>The 32-byte ClientHello random with the config's client_random applied: "hex" or "hex/mask";
        /// byte i = (prefix &amp; mask) | (random &amp; ~mask), mask bytes default to 0xFF.</summary>
        public static byte[] Random32(string spec)
        {
            var r = new byte[32];
            using (var g = RandomNumberGenerator.Create()) g.GetBytes(r);
            var s = (spec ?? "").Trim();
            if (s.Length == 0) return r;
            var parts = s.Split('/');
            var prefix = Hex(parts[0]);
            var mask = parts.Length > 1 ? Hex(parts[1]) : new byte[0];
            for (int i = 0; i < prefix.Length && i < 32; i++)
            {
                int m = i < mask.Length ? mask[i] : 0xFF;
                r[i] = (byte)((prefix[i] & m) | (r[i] & ~m));
            }
            return r;
        }

        static byte[] Hex(string h)
        {
            h = new string((h ?? "").Where(Uri.IsHexDigit).ToArray());
            if (h.Length % 2 == 1) h = h.Substring(0, h.Length - 1);
            var b = new byte[h.Length / 2];
            for (int i = 0; i < b.Length; i++) b[i] = Convert.ToByte(h.Substring(2 * i, 2), 16);
            return b;
        }

        /// <summary>The SNI the client sends: none for an IP address, punycode for a non-ASCII name.</summary>
        static string SniName(string sni)
        {
            var s = (sni ?? "").Trim().TrimEnd('.');
            if (s.StartsWith("[") && s.EndsWith("]")) s = s.Substring(1, s.Length - 2);
            if (s.Length == 0 || IPAddress.TryParse(s, out _)) return null;
            if (s.Any(c => c > 127)) { try { s = new IdnMapping().GetAscii(s); } catch { return null; } }
            return s;
        }

        static void U16(List<byte> b, int v) { b.Add((byte)(v >> 8)); b.Add((byte)v); }
        static void Ext(List<byte> b, int type, byte[] data) { U16(b, type); U16(b, data.Length); b.AddRange(data); }

        // ------------------------------------------------------------------ verdict

        public static Kind Classify(List<Result> results)
        {
            if (results == null || results.Count == 0 || results.All(r => !r.Tcp && (r.Why ?? "").StartsWith("dns:"))) return Kind.NoAddress;
            if (results.Any(r => r.Tls && (!r.BigTried || r.Big))) return Kind.Reachable;
            if (results.Any(r => r.Tls)) return Kind.BigHello;
            if (results.Any(r => r.Tcp)) return Kind.TlsBlocked;
            return Kind.Unreachable;
        }

        const string Hotspot = "• یک بار با هات‌اسپات گوشی وصل شوید؛ اگر وصل شد، اینترنتِ این کامپیوتر (مودم یا سرویس‌دهنده‌ی اینترنت) راه سرور را بسته است.";
        const string OtherVpn = "• اگر VPN یا پروکسی دیگری روشن است، ببندید.";
        const string NewLink = "• از سرویس‌دهنده لینک یا سرور تازه بخواهید.";
        const string Report = "• «گزارش اتصال» را از تنظیمات › ابزارها برای پشتیبانی بفرستید.";
        const string Firewall = "• شاید آنتی‌ویروس یا فایروال جلوی trusttunnel_client.exe را گرفته باشد؛ آن را مجاز کنید.";
        const string Busy = "• شاید سرور شلوغ یا موقتاً خراب باشد؛ چند دقیقه بعد دوباره امتحان کنید.";
        static string Switch(string other) => "• در تنظیمات، پروتکل را " + other + " کنید و دوباره وصل شوید.";

        /// <summary>Plain-Persian explanation for the dialog, and a short one for the status line.</summary>
        public static string Explain(Kind kind, List<Result> results, bool pinged, bool pingFailed, string otherProto, out string brief)
        {
            var other = string.IsNullOrEmpty(otherProto) ? "HTTP/3" : otherProto;
            string head;
            var tips = new List<string>();
            switch (kind)
            {
                case Kind.Unreachable:
                    brief = "سرور از اینترنتِ این کامپیوتر جواب نمی‌دهد";
                    head = brief + "؛ به هیچ‌کدام از نشانی‌هایش وصل نشد. این خطا ربطی به نام کاربری و رمز ندارد.";
                    tips.AddRange(new[] { Hotspot, OtherVpn, NewLink, Report });
                    break;
                case Kind.TlsBlocked:
                    brief = "ارتباط امن با سرور وسط راه قطع می‌شود";
                    head = "به سرور وصل می‌شود، ولی ارتباط امن (TLS) وسط راه قطع می‌شود؛ معمولاً یعنی این شبکه نام یا نشانی سرور را فیلتر کرده است.";
                    tips.AddRange(new[] { Switch(other), Hotspot, NewLink, Report });
                    break;
                case Kind.BigHello:
                    brief = "بسته‌های بزرگ رمزنگاری به سرور نمی‌رسند";
                    head = "سرور جواب می‌دهد، ولی دست‌دادنِ رمزنگاری «پساکوانتومی» که بسته‌ی بزرگ‌تری دارد، در این شبکه به سرور نمی‌رسد. اگر این گزینه برای همین سرور خاموش شود، دست‌دادن کوچک‌تر می‌شود و معمولاً وصل می‌شود.";
                    tips.Add("• اگر باز هم وصل نشد، با هات‌اسپات گوشی امتحان کنید یا «گزارش اتصال» را از تنظیمات › ابزارها بفرستید.");
                    break;
                case Kind.Reachable:
                    brief = "سرور در دسترس است ولی تونل برقرار نشد";
                    head = !pinged && pingFailed
                        ? "سرور از این کامپیوتر در دسترس است، ولی خودِ کلاینت TrustTunnel به آن نرسید."
                        : "سرور از این کامپیوتر در دسترس است، ولی تونل برقرار نشد.";
                    tips.Add(pinged ? Busy : Firewall);
                    tips.AddRange(new[] { OtherVpn, Switch(other), Report });
                    break;
                default:
                    brief = "اتصال به سرور برقرار نشد";
                    head = "اتصال به سرور برقرار نشد و نشانی سرور هم از این کامپیوتر پیدا نشد (DNS).";
                    tips.AddRange(new[] { Hotspot, OtherVpn, NewLink, Report });
                    break;
            }
            return head + "\n" + string.Join("\n", tips);
        }
    }
}
