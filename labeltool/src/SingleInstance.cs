// One window per program and user session (2026-11-09): a second start of QL Label Maker or QL Label Formats hands
// its command line to the running one and exits; that one comes to the front (restored when minimised) and opens what it
// was given. The programs are apart: each has its own mutex and pipe, so both may run side by side.
//   - Local\QLUnidrv.<app>.Instance (a mutex, this session only): whoever creates it first is the running instance.
//   - \\.\pipe\QLUnidrv.<app>.<session>.<user SID>: the running instance listens there from its first moment (before its
//     window is built), so two quick double-clicks never find the mutex without a listener for long. The pipe allows only
//     this user; the second start checks that the listener is in this session and is the same program (a pipe of that
//     name from someone else gets nothing).
//   - What doesn't work (an elevated window, which a normal start can't reach; a running one that doesn't answer within
//     10 s): the second start opens a window of its own, as before. Nothing ever waits on a hidden window.
// Only the window starts take part: the programs' command modes (--apply, --sync, --plan, ...) never call this.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace QlLabels
{
    static class SingleInstance
    {
        internal static string Suffix = "";   // tests: names of their own, never the installed programs'
        const int MaxArgs = 64, MaxArgBytes = 32768;
        const int WaitMs = 10000;

        static Mutex mutex;
        static string pipeName;
        static volatile bool released;
        static Form window;
        static Action<string[]> received;
        static readonly Queue<string[]> early = new Queue<string[]>();   // arrived before the window was shown
        static readonly object gate = new object();

        static string MutexName(string app) { return @"Local\QLUnidrv." + app + Suffix + ".Instance"; }

        internal static string PipeName(string app)
        {
            int session = Process.GetCurrentProcess().SessionId;
            string sid;
            using (var me = WindowsIdentity.GetCurrent()) sid = me.User.Value;
            return "QLUnidrv." + app + Suffix + "." + session + "." + sid;
        }

        // True: a running instance of this program took args (this start ends now). False: this is the running
        // instance from now on (it listens; Serve hands it its window), or one that couldn't be reached (a window of its own).
        public static bool HandOff(string app, string[] args)
        {
            string pipe;
            try { pipe = PipeName(app); } catch (Exception) { return false; }
            var deadline = Stopwatch.StartNew();
            while (true)
            {
                bool created;
                Mutex m;
                try { m = new Mutex(false, MutexName(app), out created); }
                catch (Exception) { return false; }   // e.g. an elevated instance's mutex, out of reach: as before
                if (created)
                {
                    mutex = m;
                    pipeName = pipe;
                    released = false;
                    new Thread(Listen) { IsBackground = true, Name = "single instance" }.Start();
                    return false;
                }
                m.Dispose();
                int result = Send(pipe, args, Math.Max(100, WaitMs - (int)deadline.ElapsedMilliseconds));
                if (result > 0) return true;
                if (result < 0 || deadline.ElapsedMilliseconds >= WaitMs) return false;
                Thread.Sleep(100);   // the running one is just ending (its mutex goes with it): try again
            }
        }

        // The running instance's window: what arrives from now on (and what came before it was shown) is handed to
        // received on its thread, after the window came to the front.
        public static void Serve(Form form, Action<string[]> onReceived)
        {
            if (mutex == null) return;
            form.Shown += delegate
            {
                lock (gate) { window = form; received = onReceived; }
                Drain();
            };
            // closed: later starts get a window of their own (this process is ending, what it took would be lost)
            form.FormClosed += delegate { Release(); };
        }

        // Before this program starts itself again (language switch): the new start becomes the running one.
        public static void Release()
        {
            if (released) return;
            released = true;
            lock (gate) window = null;
            var m = mutex;
            mutex = null;
            if (m != null) try { m.Dispose(); } catch (Exception) { }
            // the listener waits for a connection: one of our own ends it (else the next start would talk to it)
            if (pipeName != null) try { using (var c = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut)) c.Connect(200); } catch (Exception) { }
        }

        // 1: handed over; 0: nobody listening (yet), or it closed without taking them (a window just closing); -1:
        // unreachable or not ours.
        internal static int Send(string pipe, string[] args, int timeoutMs)
        {
            try
            {
                using (var c = new NamedPipeClientStream(".", pipe, PipeDirection.InOut))
                {
                    try { c.Connect(Math.Min(timeoutMs, 2000)); }
                    catch (TimeoutException) { return 0; }
                    catch (IOException) { return 0; }
                    uint pid;
                    if (!GetNamedPipeServerProcessId(c.SafePipeHandle.DangerousGetHandle(), out pid) || !SameProgram(pid)) return -1;
                    AllowSetForegroundWindow((int)pid);   // its window may come to the front (we were started by the user)
                    var w = new BinaryWriter(c, Encoding.UTF8);
                    w.Write(args.Length);
                    foreach (string a in args) w.Write(a ?? "");
                    w.Flush();
                    int ack = -1;
                    var t = new Thread(() => { try { ack = c.ReadByte(); } catch (Exception) { } }) { IsBackground = true };
                    t.Start();
                    if (!t.Join(timeoutMs)) return -1;
                    return ack == 1 ? 1 : 0;   // taken by the running one (what it does then is its business); else try again
                }
            }
            catch (UnauthorizedAccessException) { return -1; }   // an elevated window: a normal start can't reach it
            catch (Exception) { return 0; }
        }

        // The listener: this session and the very same exe.
        static bool SameProgram(uint pid)
        {
            uint session;
            if (!ProcessIdToSessionId(pid, out session) || session != (uint)Process.GetCurrentProcess().SessionId) return false;
            IntPtr h = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, pid);
            if (h == IntPtr.Zero) return false;
            try
            {
                var b = new StringBuilder(1024);
                int size = b.Capacity;
                if (!QueryFullProcessImageName(h, 0, b, ref size)) return false;
                return SameFile(b.ToString(), Application.ExecutablePath);
            }
            catch (Exception) { return false; }
            finally { CloseHandle(h); }
        }

        // The same exe file, also when one path is a mapped drive and the other its network path: name, size, time.
        static bool SameFile(string a, string b)
        {
            if (string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase)) return true;
            var x = new FileInfo(a);
            var y = new FileInfo(b);
            return string.Equals(x.Name, y.Name, StringComparison.OrdinalIgnoreCase) && x.Exists && y.Exists &&
                   x.Length == y.Length && x.LastWriteTimeUtc == y.LastWriteTimeUtc;
        }

        static void Listen()
        {
            PipeSecurity security;
            try
            {
                security = new PipeSecurity();
                using (var me = WindowsIdentity.GetCurrent())
                    security.AddAccessRule(new PipeAccessRule(me.User, PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance, AccessControlType.Allow));
            }
            catch (Exception) { return; }
            while (!released)
            {
                try
                {
                    using (var s = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.None, 4096, 4096, security))
                    {
                        s.WaitForConnection();
                        if (released) return;
                        var r = new BinaryReader(s, Encoding.UTF8);
                        int n = r.ReadInt32();
                        if (n < 0 || n > MaxArgs) continue;
                        var args = new string[n];
                        for (int i = 0; i < n; i++)
                        {
                            args[i] = r.ReadString();
                            if (args[i].Length > MaxArgBytes) { args = null; break; }
                        }
                        if (args == null) continue;
                        if (released) return;   // the window closed meanwhile: no answer, the second start goes on by itself
                        s.WriteByte(1);
                        s.Flush();
                        lock (gate) early.Enqueue(args);
                        Drain();
                    }
                }
                catch (Exception)
                {
                    if (released) return;
                    Thread.Sleep(200);   // a broken connection: listen again
                }
            }
        }

        // What arrived, on the window's thread, once it is shown.
        static void Drain()
        {
            Form f;
            lock (gate) f = window;
            if (f == null || f.IsDisposed || !f.IsHandleCreated) return;
            try
            {
                f.BeginInvoke((MethodInvoker)delegate
                {
                    while (true)
                    {
                        string[] args;
                        Action<string[]> handle;
                        lock (gate)
                        {
                            if (early.Count == 0 || window == null) return;
                            args = early.Dequeue();
                            handle = received;
                        }
                        ToFront(f);
                        if (handle != null) handle(args);
                    }
                });
            }
            catch (Exception) { }   // the window is just closing
        }

        // The window to the front, restored when minimised; with a dialog open (Save as, a question) that dialog.
        public static void ToFront(Form f)
        {
            if (f.WindowState == FormWindowState.Minimized) f.WindowState = FormWindowState.Normal;
            IntPtr top = GetLastActivePopup(f.Handle);
            if (top == IntPtr.Zero) top = f.Handle;
            SetForegroundWindow(top);
            if (top == f.Handle) f.Activate();
        }

        // A dialog of this window is open (its own or Windows' Open/Save/Print dialogs): the window is disabled then.
        public static bool Busy(Form f) { return f.IsHandleCreated && !IsWindowEnabled(f.Handle); }

        [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetNamedPipeServerProcessId(IntPtr pipe, out uint pid);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool ProcessIdToSessionId(uint pid, out uint session);
        [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder name, ref int size);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
        [DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(int pid);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] static extern IntPtr GetLastActivePopup(IntPtr h);
        [DllImport("user32.dll")] static extern bool IsWindowEnabled(IntPtr h);
    }
}
