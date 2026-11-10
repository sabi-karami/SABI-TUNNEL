using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace TT
{
    static class Program
    {
        public static string EventName(string cmd) => "TrustTunnelGUI_SabiVPN_" + cmd;

        [STAThread]
        static void Main(string[] args)
        {
            var cmds = args.Select(a => a.TrimStart('-', '/').ToLowerInvariant()).ToArray();
            using (var mutex = new Mutex(true, "TrustTunnelGUI_SabiVPN_Mutex", out bool first))
            {
                if (!first)
                {
                    var cmd = cmds.FirstOrDefault(c => Array.IndexOf(MainForm.Cmds, c) >= 0) ?? "show";
                    try { EventWaitHandle.OpenExisting(EventName(cmd)).Set(); } catch { }
                    return;
                }
                System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12;
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += (s, e) => { Crash(e.Exception); try { Ui.Error(null, "خطای غیرمنتظره:\n" + e.Exception.Message); } catch { } };
                AppDomain.CurrentDomain.UnhandledException += (s, e) => Crash(e.ExceptionObject as Exception);
                bool tray = cmds.Contains("tray");
                MainForm form;
                try { form = new MainForm(tray); }
                catch (Exception ex)
                {
                    Crash(ex);
                    MessageBox.Show("TrustTunnel could not start:\n\n" + ex.Message + "\n\nDetails: trusttunnel_gui_error.log", "TrustTunnel", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                Application.Run(form);
                GC.KeepAlive(mutex);
            }
        }

        static void Crash(Exception ex)
        {
            try { File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "trusttunnel_gui_error.log"), DateTime.Now.ToString("s") + "  " + ex + "\r\n\r\n"); } catch { }
        }
    }
}
