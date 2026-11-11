using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace TT
{
    /// <summary>Runs trusttunnel_client.exe hidden and stops it gracefully with a real Ctrl+C
    /// (the client only cleans up routes/DNS/kill switch on SIGINT).</summary>
    sealed class ClientRunner
    {
        Process p;
        public event Action<string> Line;
        public event Action Exited;
        public bool Running { get { try { return p != null && !p.HasExited; } catch { return false; } } }

        public void Start(string config = null)
        {
            var psi = new ProcessStartInfo(Cfg.Client, "-c \"" + (config ?? Cfg.Toml) + "\"")
            {
                UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Cfg.Dir,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            };
            var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
            proc.OutputDataReceived += (s, e) => { if (e.Data != null) Line?.Invoke(e.Data); };
            proc.ErrorDataReceived += (s, e) => { if (e.Data != null) Line?.Invoke(e.Data); };
            proc.Exited += (s, e) => Exited?.Invoke();
            proc.Start();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
            p = proc;
        }

        public void Stop(int timeoutMs = 10000)
        {
            var proc = p;
            if (proc == null) return;
            try { if (proc.HasExited) return; } catch { return; }
            StopProcess(proc, timeoutMs);
        }

        public static void StopProcess(Process proc, int timeoutMs)
        {
            bool sent = false;
            lock (ConsoleLock) { sent = SendCtrlC(proc.Id); }
            try
            {
                if (!sent || !proc.WaitForExit(timeoutMs)) proc.Kill();
                proc.WaitForExit(3000);
            }
            catch { }
        }

        static readonly object ConsoleLock = new object();

        [DllImport("kernel32.dll", SetLastError = true)] static extern bool AttachConsole(uint pid);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool FreeConsole();
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool SetConsoleCtrlHandler(IntPtr handler, bool add);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool GenerateConsoleCtrlEvent(uint ev, uint group);

        static bool SendCtrlC(int pid)
        {
            FreeConsole();
            if (!AttachConsole((uint)pid)) return false;
            SetConsoleCtrlHandler(IntPtr.Zero, true);          // do not kill ourselves
            bool ok = GenerateConsoleCtrlEvent(0 /*CTRL_C_EVENT*/, 0);
            Thread.Sleep(500);
            FreeConsole();
            Thread.Sleep(500);
            SetConsoleCtrlHandler(IntPtr.Zero, false);
            return ok;
        }
    }
}
