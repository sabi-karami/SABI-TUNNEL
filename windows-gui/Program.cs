using System;
using System.Threading;
using System.Windows.Forms;

namespace TT
{
    static class Program
    {
        public const string ShowEventName = "TrustTunnelGUI_SabiVPN_Show";

        [STAThread]
        static void Main(string[] args)
        {
            using (var mutex = new Mutex(true, "TrustTunnelGUI_SabiVPN_Mutex", out bool first))
            {
                if (!first)
                {
                    try { EventWaitHandle.OpenExisting(ShowEventName).Set(); } catch { }
                    return;
                }
                System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12;
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.ThreadException += (s, e) => Ui.Error(null, "خطای غیرمنتظره:\n" + e.Exception.Message);
                bool tray = Array.IndexOf(args, "--tray") >= 0;
                Application.Run(new MainForm(tray));
                GC.KeepAlive(mutex);
            }
        }
    }
}
