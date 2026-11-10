using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TT
{
    enum VState { NoConfig, Off, Connecting, On, Reconnecting, Stopping }

    sealed class MainForm : Form
    {
        const int W = 400;
        readonly bool startInTray;
        readonly ClientRunner runner = new ClientRunner();
        VState state = VState.Off;
        volatile bool userStop;
        volatile string lastError;
        DateTime connectedAt, connectStart, lastProbe;
        string exitIp;
        HashSet<string> serverIps = new HashSet<string>();
        int probeBusy, probeCount;
        bool loading, logVisible, reallyExit, balloonShown, reconnectAfterStop;

        readonly StatusRing ring;
        readonly Label lblTitle, lblInfo, lblSub, lblIp;
        readonly Button btnMain, btnImport, btnBypass, btnLog;
        readonly ComboBox cmbProto;
        readonly CheckBox chkKill, chkAuto, chkStartup;
        readonly TextBox txtLog;
        readonly NotifyIcon tray;
        readonly ToolStripMenuItem miToggle;
        readonly Icon icoOff, icoOn, icoWait;
        readonly System.Windows.Forms.Timer tick = new System.Windows.Forms.Timer { Interval = 500 };
        readonly Queue<string> pending = new Queue<string>();
        readonly List<string> logLines = new List<string>();
        readonly EventWaitHandle showEvent;
        int collapsedHeight, expandedHeight;

        public MainForm(bool tray)
        {
            startInTray = tray;
            Text = "TrustTunnel · Sabi VPN";
            RightToLeft = RightToLeft.Yes;
            BackColor = Theme.Bg; ForeColor = Theme.Text; Font = Theme.F(9.5f);
            FormBorderStyle = FormBorderStyle.FixedSingle; MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleDimensions = new SizeF(96f, 96f); AutoScaleMode = AutoScaleMode.Dpi;
            SuspendLayout();

            icoOff = Icons.Make(Theme.Gray); icoOn = Icons.Make(Theme.Green); icoWait = Icons.Make(Theme.Orange);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { Icon = icoOff; }

            lblTitle = Ui.Label("TrustTunnel", 0, 14, W, 30, Theme.Text, 14f, FontStyle.Bold, ContentAlignment.MiddleCenter);
            lblInfo = Ui.Label("", 10, 46, W - 20, 22, Theme.Muted, 9f, FontStyle.Regular, ContentAlignment.MiddleCenter);
            ring = new StatusRing { Location = new Point((W - 196) / 2, 76), Size = new Size(196, 196) };
            lblSub = Ui.Label("", 10, 276, W - 20, 22, Theme.Muted, 9f, FontStyle.Regular, ContentAlignment.MiddleCenter);
            btnMain = Ui.Button("اتصال", (W - 300) / 2, 304, 300, 50, Theme.Accent, 12f);
            btnMain.Name = "btnMain"; btnMain.AccessibleName = "btnMain";
            btnMain.Click += (s, e) => ToggleConnect();
            Controls.AddRange(new Control[] { lblTitle, lblInfo, ring, lblSub, btnMain });

            var card = new Panel { Location = new Point(16, 370), Size = new Size(W - 32, 172), BackColor = Theme.Card, RightToLeft = RightToLeft.Yes };
            int cw = card.Width, lw = 92;
            card.Controls.Add(Ui.Label("پروتکل:", cw - 14 - lw, 12, lw, 26, Theme.Muted));
            cmbProto = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(14, 12), Size = new Size(cw - 28 - lw - 6, 26),
                RightToLeft = RightToLeft.No, FlatStyle = FlatStyle.Flat, BackColor = Theme.Bg, ForeColor = Theme.Text, Font = Theme.F(9f)
            };
            cmbProto.Items.AddRange(new object[] { "HTTP/2  (TCP)  - recommended", "HTTP/3  (QUIC / UDP)" });
            card.Controls.Add(cmbProto);
            card.Controls.Add(Ui.Label("IP خروجی:", cw - 14 - lw, 44, lw, 24, Theme.Muted));
            lblIp = new Label { Text = "-", Location = new Point(14, 44), Size = new Size(cw - 28 - lw - 6, 24), ForeColor = Theme.Text,
                                TextAlign = ContentAlignment.MiddleRight, RightToLeft = RightToLeft.No, Font = Theme.F(9f), AutoEllipsis = true };
            card.Controls.Add(lblIp);
            chkKill = Check(card, "Kill switch: قطع اینترنت اگر VPN قطع شد (امن‌تر)", 76);
            chkAuto = Check(card, "اتصال خودکار هنگام باز شدن برنامه", 106);
            chkStartup = Check(card, "اجرا همراه ورود به ویندوز (کنار ساعت)", 136);
            Controls.Add(card);

            int bw = (W - 32 - 16) / 3, by = 556;
            btnImport = Ui.Button("وارد کردن لینک", W - 16 - bw, by, bw, 38, Theme.Card, 9f, true);
            btnBypass = Ui.Button("سایت‌های مستقیم", W - 16 - 2 * bw - 8, by, bw, 38, Theme.Card, 9f, true);
            btnLog = Ui.Button("نمایش گزارش", 16, by, bw, 38, Theme.Card, 9f, true);
            btnImport.Click += (s, e) => { if (state == VState.Off || state == VState.NoConfig) ImportFlow(); };
            btnBypass.Click += (s, e) => EditBypass();
            btnLog.Click += (s, e) => ToggleLog();
            Controls.AddRange(new Control[] { btnImport, btnBypass, btnLog });

            var foot = Ui.Label("TrustTunnel client 1.1.11  ·  GUI 1.0  ·  sabi-karami", 0, 602, W, 18, Theme.Gray, 7.5f, FontStyle.Regular, ContentAlignment.MiddleCenter);
            foot.RightToLeft = RightToLeft.No;
            Controls.Add(foot);
            txtLog = new TextBox
            {
                Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Location = new Point(16, 628), Size = new Size(W - 32, 210),
                BackColor = Color.FromArgb(9, 12, 20), ForeColor = Theme.Muted, Font = new Font("Consolas", 8f), RightToLeft = RightToLeft.No,
                BorderStyle = BorderStyle.FixedSingle, Visible = false
            };
            Controls.Add(txtLog);
            ClientSize = new Size(W, 626);
            ResumeLayout(false);

            var menu = new ContextMenuStrip { RightToLeft = RightToLeft.Yes, Font = Theme.F(9f) };
            miToggle = new ToolStripMenuItem("اتصال", null, (s, e) => ToggleConnect());
            menu.Items.Add(miToggle);
            menu.Items.Add("نمایش برنامه", null, (s, e) => ShowMain());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("خروج", null, (s, e) => ExitApp());
            this.tray = new NotifyIcon { Icon = icoOff, Text = "TrustTunnel", Visible = true, ContextMenuStrip = menu };
            this.tray.DoubleClick += (s, e) => ShowMain();

            runner.Line += OnLine;
            runner.Exited += () => Post(OnExited);
            cmbProto.SelectedIndexChanged += (s, e) => OnProtocolChanged();
            chkKill.CheckedChanged += (s, e) => { if (loading) return; Cfg.Set("killswitch_enabled", chkKill.Checked ? "true" : "false"); OfferReconnect(); };
            chkAuto.CheckedChanged += (s, e) => { if (!loading) Ini.Set("autoconnect", chkAuto.Checked ? "1" : "0"); };
            chkStartup.CheckedChanged += (s, e) => OnStartupChanged();
            tick.Tick += (s, e) => OnTick();
            tick.Start();

            showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, Program.ShowEventName);
            new Thread(() => { while (true) { showEvent.WaitOne(); Post(ShowMain); } }) { IsBackground = true }.Start();

            LoadConfigToUi();
            var once = new System.Windows.Forms.Timer { Interval = 400 };
            once.Tick += (s, e) => { once.Stop(); once.Dispose(); Startup(); };
            once.Start();
        }

        CheckBox Check(Panel card, string text, int y)
        {
            var c = new CheckBox { Text = text, Location = new Point(14, y), Size = new Size(card.Width - 28, 26), RightToLeft = RightToLeft.Yes,
                                   ForeColor = Theme.Text, BackColor = Theme.Card, Font = Theme.F(9f), Cursor = Cursors.Hand };
            card.Controls.Add(c);
            return c;
        }

        // ------------------------------------------------------------------ lifecycle

        protected override void SetVisibleCore(bool value)
        {
            if (startInTray && !IsHandleCreated) { CreateHandle(); value = false; }
            base.SetVisibleCore(value);
        }

        void Startup()
        {
            if (!File.Exists(Cfg.Client) || !File.Exists(Cfg.Wintun) || !File.Exists(Cfg.Wizard))
            {
                ShowMain();
                Ui.Error(this, "فایل‌های trusttunnel_client.exe ، setup_wizard.exe و wintun.dll باید کنار همین برنامه باشند.\nکل پوشه‌ی zip را با هم باز کنید.");
                return;
            }
            var others = Process.GetProcessesByName("trusttunnel_client");
            if (others.Length > 0)
            {
                ShowMain();
                if (Ui.Ask(this, "یک اتصال TrustTunnel دیگر از قبل در حال اجراست (مثلاً از Connect.bat).\nمتوقفش کنم تا این برنامه کنترل را بگیرد؟"))
                    foreach (var p in others) { try { ClientRunner.StopProcess(p, 6000); } catch { } }
                else return;
            }
            if (!Cfg.Exists) { ShowMain(); ImportFlow(); return; }
            if (Ini.Get("autoconnect") == "1") Connect();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.WindowsShutDown || e.CloseReason == CloseReason.TaskManagerClosing)
            {
                userStop = true;
                try { runner.Stop(4000); } catch { }
                tray.Visible = false;
                base.OnFormClosing(e);
                return;
            }
            if (!reallyExit)
            {
                e.Cancel = true;
                Hide();
                if (!balloonShown)
                {
                    tray.ShowBalloonTip(4000, "TrustTunnel", "برنامه کنار ساعت ویندوز فعال ماند. برای خروج روی آیکون راست‌کلیک کنید.", ToolTipIcon.Info);
                    balloonShown = true;
                }
                return;
            }
            tray.Visible = false;
            base.OnFormClosing(e);
        }

        void ShowMain()
        {
            Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
            BringToFront();
        }

        void ExitApp()
        {
            reallyExit = true;
            Disconnect(() => { tray.Visible = false; Application.Exit(); });
        }

        void Post(Action a)
        {
            try { if (IsHandleCreated && !IsDisposed) BeginInvoke(a); } catch { }
        }

        // ------------------------------------------------------------------ config <-> UI

        void LoadConfigToUi()
        {
            loading = true;
            try
            {
                bool has = Cfg.Exists;
                var name = Ini.Get("name", "");
                var user = has ? Cfg.Get("username") : null;
                var host = has ? Cfg.Get("hostname") : null;
                lblTitle.Text = string.IsNullOrWhiteSpace(name) ? "TrustTunnel" : name;
                lblInfo.Text = has ? (host + "   ·   کاربر: " + user) : "هنوز سروری وارد نشده";
                serverIps = has ? Cfg.ServerIps() : new HashSet<string>();
                cmbProto.SelectedIndex = has && (Cfg.Get("upstream_protocol") ?? "").Contains("3") ? 1 : 0;
                chkKill.Checked = !has || (Cfg.Get("killswitch_enabled") ?? "true") != "false";
                chkAuto.Checked = Ini.Get("autoconnect") == "1";
                chkStartup.Checked = AutoStart.Enabled();
                cmbProto.Enabled = chkKill.Enabled = has;
                if (state == VState.Off || state == VState.NoConfig) SetState(has ? VState.Off : VState.NoConfig);
            }
            finally { loading = false; }
        }

        void OnProtocolChanged()
        {
            if (loading || !Cfg.Exists) return;
            Cfg.Set("upstream_protocol", cmbProto.SelectedIndex == 1 ? "\"http3\"" : "\"http2\"");
            OfferReconnect();
        }

        void OfferReconnect()
        {
            if (state == VState.On || state == VState.Connecting || state == VState.Reconnecting)
            {
                reconnectAfterStop = true;
                Disconnect();
            }
        }

        void OnStartupChanged()
        {
            if (loading) return;
            if (!AutoStart.Set(chkStartup.Checked))
            {
                Ui.Error(this, "تنظیم اجرای خودکار ممکن نشد.");
                loading = true; chkStartup.Checked = AutoStart.Enabled(); loading = false;
            }
        }

        bool ImportFlow()
        {
            ShowMain();
            using (var d = new ImportDialog())
            {
                if (d.ShowDialog(this) != DialogResult.OK) return false;
                Cursor = Cursors.WaitCursor;
                string err;
                try { err = Cfg.ImportLink(d.Link); } finally { Cursor = Cursors.Default; }
                if (err != null) { Ui.Error(this, "وارد کردن لینک ناموفق بود:\n\n" + err); return false; }
            }
            try { Cfg.ApplyBypass(); } catch { }
            LoadConfigToUi();
            Ui.Info(this, "سرور با موفقیت اضافه شد. حالا «اتصال» را بزنید.");
            return true;
        }

        void EditBypass()
        {
            using (var d = new BypassDialog(Cfg.ReadBypass()))
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                Cfg.WriteBypass(d.Content);
            }
            if (Cfg.Exists) { try { Cfg.ApplyBypass(); } catch { } }
            if (state == VState.On && Ui.Ask(this, "برای اعمال تغییرات، اتصال یک بار قطع و وصل شود؟")) { reconnectAfterStop = true; Disconnect(); }
        }

        void ToggleLog()
        {
            if (collapsedHeight == 0) { collapsedHeight = ClientSize.Height; expandedHeight = txtLog.Bottom + txtLog.Top - (txtLog.Top - 8) ; }
            logVisible = !logVisible;
            txtLog.Visible = logVisible;
            btnLog.Text = logVisible ? "بستن گزارش" : "نمایش گزارش";
            ClientSize = new Size(ClientSize.Width, logVisible ? txtLog.Bottom + 12 : collapsedHeight);
            if (logVisible) { txtLog.Text = string.Join("\r\n", logLines); txtLog.SelectionStart = txtLog.TextLength; txtLog.ScrollToCaret(); }
        }

        // ------------------------------------------------------------------ connect / disconnect

        void ToggleConnect()
        {
            switch (state)
            {
                case VState.NoConfig: ImportFlow(); break;
                case VState.Off: Connect(); break;
                case VState.Stopping: break;
                default: Disconnect(); break;
            }
        }

        void Connect()
        {
            if (runner.Running) return;
            if (!Cfg.Exists && !ImportFlow()) return;
            try { Cfg.ApplyBypass(); } catch (Exception ex) { AddLog("[gui] bypass: " + ex.Message); }
            serverIps = Cfg.ServerIps();
            lastError = null; userStop = false; exitIp = null; reconnectAfterStop = false;
            connectStart = DateTime.Now; lastProbe = DateTime.MinValue;
            lblIp.Text = "-";
            try { File.WriteAllText(Path.Combine(Cfg.Dir, "trusttunnel_last.log"), "TrustTunnel GUI log " + DateTime.Now.ToString("s") + "\r\n"); } catch { }
            SetState(VState.Connecting);
            AddLog("[gui] starting trusttunnel_client (" + (cmbProto.SelectedIndex == 1 ? "http3" : "http2") + ")");
            try { runner.Start(); }
            catch (Exception ex) { SetState(VState.Off, "اجرای برنامه ممکن نشد: " + ex.Message, Theme.Red); }
        }

        void Disconnect(Action after = null)
        {
            if (!runner.Running) { after?.Invoke(); return; }
            userStop = true;
            SetState(VState.Stopping);
            Task.Run(() => { try { runner.Stop(); } catch { } })
                .ContinueWith(_ => Post(() => after?.Invoke()));
        }

        void OnExited()
        {
            bool unexpected = !userStop;
            var err = lastError;
            AddLog("[gui] client exited");
            SetState(Cfg.Exists ? VState.Off : VState.NoConfig,
                     unexpected ? (err ?? "اتصال به‌طور غیرمنتظره بسته شد؛ گزارش را ببینید.") : "", unexpected ? Theme.Red : Theme.Muted);
            lblIp.Text = "-";
            if (unexpected) tray.ShowBalloonTip(4000, "TrustTunnel", "اتصال قطع شد. " + (err ?? ""), ToolTipIcon.Warning);
            if (reconnectAfterStop && !reallyExit) { reconnectAfterStop = false; Connect(); }
        }

        void MarkConnected()
        {
            if (state == VState.On || !runner.Running) return;
            bool first = state == VState.Connecting;
            connectedAt = DateTime.Now; lastError = null;
            SetState(VState.On);
            exitIp = null; probeCount = 0;
            lastProbe = DateTime.Now.AddSeconds(-7);   // first probe ~3 s later, after routes are installed
            if (first && !Visible) tray.ShowBalloonTip(2500, "TrustTunnel", "وصل شد ✓", ToolTipIcon.Info);
        }

        // ------------------------------------------------------------------ client output

        static string Translate(string why)
        {
            var w = (why ?? "").ToLowerInvariant();
            if (w.Contains("auth") || w.Contains("407")) return "نام کاربری یا رمز پذیرفته نشد (کاربر غیرفعال/حذف شده؟)";
            if (w.Contains("timed out") || w.Contains("timeout")) return "سرور پاسخ نمی‌دهد؛ اینترنت یا مسدودی را بررسی کنید";
            if (w.Contains("refused")) return "اتصال توسط سرور رد شد";
            if (w.Contains("certificate") || w.Contains("verify")) return "خطای گواهی سرور";
            if (w.Contains("bad response")) return "پاسخ نامعتبر از سرور";
            if (w.Contains("network") && w.Contains("unreach")) return "شبکه در دسترس نیست";
            return why;
        }

        void OnLine(string line)
        {
            lock (pending) { pending.Enqueue(line); while (pending.Count > 3000) pending.Dequeue(); }
            if (line.IndexOf("Successfully connected to endpoint", StringComparison.Ordinal) >= 0) { Post(MarkConnected); return; }
            if (line.IndexOf("Waiting recovery", StringComparison.Ordinal) >= 0)
            {
                var m = Regex.Match(line, @"error=-?\d+\s+(.*)$");
                var why = Translate(m.Success ? m.Groups[1].Value.Trim() : "");
                lastError = why;
                Post(() => { if (state == VState.On || state == VState.Reconnecting || state == VState.Connecting) SetState(state == VState.Connecting ? VState.Connecting : VState.Reconnecting, why); });
                return;
            }
            if (line.IndexOf("Proxy authentication required", StringComparison.OrdinalIgnoreCase) >= 0) { lastError = Translate("auth"); return; }
            int k = line.IndexOf("Error: ", StringComparison.Ordinal);
            if (k >= 0) lastError = Translate(line.Substring(k + 7).Trim());
        }

        void AddLog(string line) { lock (pending) pending.Enqueue(line); }

        void OnTick()
        {
            List<string> batch = null;
            lock (pending) { if (pending.Count > 0) { batch = pending.ToList(); pending.Clear(); } }
            if (batch != null)
            {
                logLines.AddRange(batch);
                try { File.AppendAllText(Path.Combine(Cfg.Dir, "trusttunnel_last.log"), string.Join("\r\n", batch) + "\r\n"); } catch { }
                if (logLines.Count > 600) logLines.RemoveRange(0, logLines.Count - 600);
                if (logVisible)
                {
                    if (txtLog.TextLength > 150000) txtLog.Text = string.Join("\r\n", logLines);
                    else txtLog.AppendText((txtLog.TextLength > 0 ? "\r\n" : "") + string.Join("\r\n", batch));
                }
            }
            var now = DateTime.Now;
            if (state == VState.On)
            {
                var t = now - connectedAt;
                lblSub.Text = "مدت اتصال  " + ((int)t.TotalHours).ToString("00") + ":" + t.Minutes.ToString("00") + ":" + t.Seconds.ToString("00");
                bool viaServer = exitIp != null && serverIps.Contains(exitIp);
                if (!viaServer && probeCount < 6 && (now - lastProbe).TotalSeconds > 10) { probeCount++; ProbeIp(false); }
            }
            else if (state == VState.Connecting && (now - connectStart).TotalSeconds > 5 && (now - lastProbe).TotalSeconds > 4)
            {
                ProbeIp(true);   // fallback if the client's log line is buffered
            }
        }

        void ProbeIp(bool markIfServer)
        {
            if (Interlocked.Exchange(ref probeBusy, 1) == 1) return;
            lastProbe = DateTime.Now;
            Task.Run(() =>
            {
                string ip = null;
                try
                {
                    var rq = (HttpWebRequest)WebRequest.Create("https://api.ipify.org");
                    rq.Timeout = 6000; rq.ReadWriteTimeout = 6000; rq.Proxy = null; rq.UserAgent = "TrustTunnelGUI";
                    rq.KeepAlive = false; rq.CachePolicy = new System.Net.Cache.RequestCachePolicy(System.Net.Cache.RequestCacheLevel.NoCacheNoStore);
                    using (var rs = rq.GetResponse())
                    using (var sr = new StreamReader(rs.GetResponseStream())) ip = sr.ReadToEnd().Trim();
                }
                catch { }
                finally { Interlocked.Exchange(ref probeBusy, 0); }
                Post(() =>
                {
                    if (ip == null || ip.Length > 45) return;
                    bool viaServer = serverIps.Contains(ip);
                    if (state == VState.Connecting && markIfServer && viaServer) MarkConnected();
                    if (state == VState.On) { exitIp = ip; lblIp.Text = ip + (viaServer ? "   (سرور شما ✓)" : ""); }
                });
            });
        }

        // ------------------------------------------------------------------ visual state

        void SetState(VState s, string sub = null, Color? subColor = null)
        {
            state = s;
            string title; Color c; bool spin = false; string btn; Color btnColor; bool btnEnabled = true;
            switch (s)
            {
                case VState.NoConfig: title = "بدون سرور"; c = Theme.Gray; btn = "وارد کردن لینک"; btnColor = Theme.Accent; sub = sub ?? "اول لینک tt:// را وارد کنید"; break;
                case VState.Connecting: title = "در حال اتصال"; c = Theme.Orange; spin = true; btn = "لغو"; btnColor = Theme.Gray; break;
                case VState.Reconnecting: title = "اتصال مجدد"; c = Theme.Orange; spin = true; btn = "قطع اتصال"; btnColor = Theme.Red; break;
                case VState.On: title = "وصل"; c = Theme.Green; btn = "قطع اتصال"; btnColor = Theme.Red; break;
                case VState.Stopping: title = "در حال قطع"; c = Theme.Gray; spin = true; btn = "صبر کنید…"; btnColor = Theme.Gray; btnEnabled = false; break;
                default: title = "قطع"; c = Theme.Gray; btn = "اتصال"; btnColor = Theme.Accent; break;
            }
            ring.Set(c, title, s == VState.Reconnecting ? "" : "", spin);
            lblSub.Text = sub ?? "";
            lblSub.ForeColor = subColor ?? (s == VState.Reconnecting ? Theme.Orange : Theme.Muted);
            btnMain.Text = btn; btnMain.BackColor = btnColor; btnMain.Enabled = btnEnabled;
            btnMain.FlatAppearance.MouseOverBackColor = ControlPaint.Light(btnColor, 0.15f);
            btnImport.Enabled = s == VState.Off || s == VState.NoConfig;
            tray.Icon = s == VState.On ? icoOn : (s == VState.Off || s == VState.NoConfig ? icoOff : icoWait);
            var tip = "TrustTunnel - " + title;
            tray.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip;
            miToggle.Text = (s == VState.Off || s == VState.NoConfig) ? "اتصال" : "قطع اتصال";
            miToggle.Enabled = s != VState.Stopping;
        }
    }
}
