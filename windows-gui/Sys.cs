using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Security;
using System.Text;
using System.Windows.Forms;

namespace TT
{
    static class Sys
    {
        public static void OpenFolder()
        {
            try { Process.Start(new ProcessStartInfo("explorer.exe", "\"" + Cfg.Dir.TrimEnd('\\') + "\"") { UseShellExecute = true }); } catch { }
        }
    }

    static class Icons
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);

        /// <summary>Brand mark tinted for a state (used for the tray).</summary>
        public static Icon Make(Color c1, Color c2, bool dot = false)
        {
            int n = Math.Max(16, SystemInformation.SmallIconSize.Width);
            if (n > 16 && n < 32) n = 32;   // let Windows downscale a crisp 32px
            using (var bmp = new Bitmap(n, n))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.Clear(Color.Transparent);
                    Glyph.Brand(g, new RectangleF(0.5f, 0.5f, n - 1, n - 1), c1, c2);
                }
                IntPtr h = bmp.GetHicon();
                var ico = (Icon)Icon.FromHandle(h).Clone();
                DestroyIcon(h);
                return ico;
            }
        }
    }

    /// <summary>"Run at Windows sign-in" via a Task Scheduler task (elevated, no UAC prompt, works on battery).</summary>
    static class AutoStart
    {
        const string TaskName = "TrustTunnelGUI";

        public static bool Enabled() => Run("/Query /TN " + TaskName) == 0;

        public static bool Set(bool on)
        {
            if (!on) return !Enabled() || Run("/Delete /F /TN " + TaskName) == 0;
            var user = Environment.UserDomainName + "\\" + Environment.UserName;
            var xml = "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\n" +
                "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\n" +
                "  <RegistrationInfo><Description>TrustTunnel VPN (Sabi)</Description></RegistrationInfo>\n" +
                "  <Triggers><LogonTrigger><Enabled>true</Enabled><UserId>" + SecurityElement.Escape(user) + "</UserId><Delay>PT10S</Delay></LogonTrigger></Triggers>\n" +
                "  <Principals><Principal id=\"Author\"><UserId>" + SecurityElement.Escape(user) + "</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>\n" +
                "  <Settings><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy><DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>" +
                "<StopIfGoingOnBatteries>false</StopIfGoingOnBatteries><ExecutionTimeLimit>PT0S</ExecutionTimeLimit><Enabled>true</Enabled></Settings>\n" +
                "  <Actions Context=\"Author\"><Exec><Command>\"" + SecurityElement.Escape(Application.ExecutablePath) + "\"</Command><Arguments>--tray</Arguments>" +
                "<WorkingDirectory>" + SecurityElement.Escape(Cfg.Dir.TrimEnd('\\')) + "</WorkingDirectory></Exec></Actions>\n" +
                "</Task>\n";
            var tmp = Path.Combine(Path.GetTempPath(), "trusttunnel_task.xml");
            File.WriteAllText(tmp, xml, Encoding.Unicode);
            try { return Run("/Create /F /TN " + TaskName + " /XML \"" + tmp + "\"") == 0; }
            finally { try { File.Delete(tmp); } catch { } }
        }

        static int Run(string args)
        {
            try
            {
                using (var p = Process.Start(new ProcessStartInfo("schtasks.exe", args)
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true }))
                {
                    var o = p.StandardOutput.ReadToEndAsync();
                    var e = p.StandardError.ReadToEndAsync();
                    if (!p.WaitForExit(15000)) { try { p.Kill(); } catch { } return -1; }
                    return p.ExitCode;
                }
            }
            catch { return -1; }
        }
    }
}
