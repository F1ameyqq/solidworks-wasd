// SolidWorks WASD 视角插件
// 编译：csc /target:library /platform:x64 /out:SwWasd.dll ...
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swpublished;

[assembly: System.Reflection.AssemblyVersion("1.0.0.0")]

namespace SwWasd
{
    [Guid("7B2E5C41-9A3D-4F18-B6E2-1C8D4A7F0E93")]
    [ComVisible(true)]
    [ProgId("SwWasd.Addin")]
    public class SwWasdAddin : ISwAddin
    {
        // ---------- Win32 ----------
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYUP = 0x0105;

        private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
        [DllImport("user32.dll")]
        private static extern IntPtr GetFocus();

        // ---------- 日志 ----------
        /// 程序所在目录（插件 dll 旁边），日志和配置都放这里
        internal static readonly string AppDir =
            System.IO.Path.GetDirectoryName(
                System.Reflection.Assembly.GetExecutingAssembly().Location);

        private static readonly string LOG = System.IO.Path.Combine(AppDir, "addin.log");
        private static void L(string m)
        {
            try
            {
                System.IO.File.AppendAllText(LOG, DateTime.Now.ToString("HH:mm:ss.fff") + "  " + m + System.Environment.NewLine);
            }
            catch { }
        }

        private int tickCount = 0;
        private int moveLog = 0;
        private int panLog = 0;
        private double panScale = -1.0;

        /// 平移步长按零件包围盒对角线自适应，避免"一按就飞"
        /// 读 swwasd.ini，每次 F9 打开时重读，改完参数不用重编译
        private void LoadConfig()
        {
            try
            {
                if (!System.IO.File.Exists(INI)) return;
                foreach (string raw in System.IO.File.ReadAllLines(INI))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    int eq = line.IndexOf('=');
                    if (eq < 0) continue;
                    string k = line.Substring(0, eq).Trim().ToLowerInvariant();
                    string v = line.Substring(eq + 1).Trim();
                    double d;
                    if (!double.TryParse(v, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out d)) continue;
                    if (k == "pan") { panFactor = d; panScale = -1; }
                    else if (k == "zoom") { zoomStep = d; }
                    else if (k == "frame") { if (timer != null) timer.Interval = (int)d; }
                }
                L("配置已读: pan=" + panFactor + " zoom=" + zoomStep);
            }
            catch (Exception ex) { L("读配置失败: " + ex.Message); }
        }

        private double GetPanStep()
        {
            if (panScale > 0) return panScale;
            double diag = 90.0;   // 兜底：按 90 m 的楼算
            try
            {
                IModelDoc2 d = iSwApp == null ? null : iSwApp.ActiveDoc as IModelDoc2;
                if (d != null)
                {
                    IModelView mvv = d.ActiveView as IModelView;
                    if (mvv != null)
                    {
                        double[] bb = mvv.GetVisibleBox() as double[];
                        if (bb != null && bb.Length >= 6)
                        {
                            double dx = Math.Abs(bb[3] - bb[0]);
                            double dy = Math.Abs(bb[4] - bb[1]);
                            double dz = Math.Abs(bb[5] - bb[2]);
                            double dg = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                            if (dg > 0.5) diag = dg;
                        }
                    }
                }
            }
            catch (Exception ex) { L("取包围盒失败: " + ex.Message + "，用兜底值"); }
            panScale = Math.Min(4.0, Math.Max(0.0005, diag * panFactor));
            L("自适应平移步长 = " + panScale.ToString("0.####") + "  (对角线 " + diag.ToString("0.##") + ")");
            return panScale;
        }

        // ---------- 状态 ----------
        private ISldWorks iSwApp;
        private int addinId;
        private Timer timer;
        private IntPtr hook = IntPtr.Zero;
        private HookProc hookProc;          // 必须留引用，否则被 GC
        private readonly bool[] held = new bool[256];
        private volatile bool enabled = false;
        private bool dynamicOn = false;
        private uint myPid = 0;

        // 手感参数
        // 默认值，会被 swwasd.ini 覆盖
        private double panFactor = 0.0003;   // 平移步长 = 视野对角线 × 这个系数
        private double zoomStep = 1.03;      // 每帧缩放倍率
        private static readonly string INI = System.IO.Path.Combine(AppDir, "swwasd.ini");

        // ---------- ISwAddin ----------
        public bool ConnectToSW(object ThisSW, int Cookie)
        {
            L("=== ConnectToSW 进入, ThisSW=" + (ThisSW == null ? "null" : ThisSW.GetType().Name) + " Cookie=" + Cookie);
            try
            {
                iSwApp = (ISldWorks)ThisSW;
                addinId = Cookie;
                iSwApp.SetAddinCallbackInfo2(0, this, addinId);
                L("iSwApp 已保存, callbackInfo OK");
            }
            catch (Exception ex) { L("iSwApp 设置失败: " + ex.Message); }

            myPid = (uint)Process.GetCurrentProcess().Id;
            L("PID=" + myPid);

            hookProc = HookCallback;
            hook = SetWindowsHookEx(WH_KEYBOARD_LL, hookProc,
                                    GetModuleHandle(null), 0);
            L("钩子句柄=" + hook.ToInt64() + " err=" + Marshal.GetLastWin32Error());

            LoadConfig();
            timer = new Timer();
            timer.Interval = 16;
            timer.Tick += OnTick;
            timer.Start();

            return true;
        }

        public bool DisconnectFromSW()
        {
            try { if (timer != null) { timer.Stop(); timer.Dispose(); timer = null; } }
            catch { }
            try { if (hook != IntPtr.Zero) { UnhookWindowsHookEx(hook); hook = IntPtr.Zero; } }
            catch { }
            StopDynamics();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            return true;
        }

        // ---------- 键盘钩子 ----------
        private static int Vk(char c)
        {
            return (int)char.ToUpperInvariant(c);
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int msg = wParam.ToInt32();

                if (msg == WM_KEYUP || msg == WM_SYSKEYUP)
                {
                    KBDLLHOOKSTRUCT du =
                        (KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(KBDLLHOOKSTRUCT));
                    int vku = (int)du.vkCode;
                    if (enabled && IsKey(vku))
                    {
                        held[vku] = false;
                        return (IntPtr)1;
                    }
                }

                if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
                {
                    KBDLLHOOKSTRUCT d =
                        (KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(KBDLLHOOKSTRUCT));
                    int vk = (int)d.vkCode;

                    // F9 开关：无论焦点在哪都吃
                    if (vk == 0x78) // VK_F9
                    {
                        enabled = !enabled;
                        if (enabled) LoadConfig();
                        L("F9 -> " + (enabled ? "开" : "关") + "  IsKey拦截生效=" + enabled);
                        if (!enabled) StopDynamics();
                        return (IntPtr)1;
                    }

                    if (enabled && IsKey(vk))
                    {
                        bool si = ShouldIntercept();
                        if (tickCount < 40) { L("按键 vk=" + vk + " 拦截=" + si); tickCount++; }
                        if (si)
                        {
                            held[vk] = true;
                            return (IntPtr)1;      // 吞掉，别给 SW
                        }
                    }
                }
            }
            return CallNextHookEx(hook, nCode, wParam, lParam);
        }

        private static bool IsKey(int vk)
        {
            return vk == 0x57 || vk == 0x41 || vk == 0x53 || vk == 0x44  // W A S D
                || vk == 0x51 || vk == 0x45;                             // Q E
        }

        /// 只在「本进程是前台 + 焦点不是文本框」时拦截
        private bool ShouldIntercept()
        {
            IntPtr fg = GetForegroundWindow();
            if (fg == IntPtr.Zero) return false;
            uint pid;
            GetWindowThreadProcessId(fg, out pid);
            if (pid != myPid) return false;

            IntPtr focus = GetFocus();
            if (focus != IntPtr.Zero)
            {
                string cls = GetClassNameOf(focus);
                if (cls != null && (cls.IndexOf("Edit", StringComparison.OrdinalIgnoreCase) >= 0
                                 || cls.IndexOf("RichEdit", StringComparison.OrdinalIgnoreCase) >= 0))
                    return false;
            }
            return true;
        }

        [DllImport("user32.dll")]
        private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

        private static string GetClassNameOf(IntPtr h)
        {
            var sb = new System.Text.StringBuilder(256);
            int n = GetClassName(h, sb, sb.Capacity);
            return n > 0 ? sb.ToString() : null;
        }

        // ---------- 每帧运动 ----------
        private int tickLog = 0;

        private void OnTick(object sender, EventArgs e)
        {
            if (tickLog < 3)
            {
                L("Tick enabled=" + enabled + " iSwApp=" + (iSwApp == null ? "null" : "ok"));
                tickLog++;
            }
            if (!enabled || iSwApp == null) return;

            bool any = held[0x57] || held[0x41] || held[0x53] || held[0x44]
                    || held[0x51] || held[0x45];

            if (!any) { StopDynamics(); return; }

            IModelDoc2 doc = iSwApp.ActiveDoc as IModelDoc2;
            if (doc == null) return;
            IModelView mv = doc.ActiveView as IModelView;
            if (mv == null) return;

            if (!dynamicOn)
            {
                try { mv.StartDynamics(); dynamicOn = true; } catch { }
            }

            double k = IsDown(0x10) ? 2.0 : 1.0;   // Shift 加速

            if (moveLog < 5) { L("执行运动 k=" + k + " W=" + held[0x57] + " A=" + held[0x41] + " S=" + held[0x53] + " D=" + held[0x44]); moveLog++; }
            try { DoMove(mv, k); }
            catch (Exception ex) { if (tickLog < 10) { L("运动异常: " + ex.Message); tickLog++; } dynamicOn = false; }
        }

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        private static bool IsDown(int vk)
        {
            return (GetAsyncKeyState(vk) & 0x8000) != 0;
        }

        private void DoMove(IModelView mv, double k)
        {
            bool w = held[0x57], s = held[0x53], a = held[0x41], d = held[0x44];
            bool q = held[0x51], ee = held[0x45];

            double pan = GetPanStep() * k;

            // W/S 上下平移
            if (w || s)
            {
                double dy = (w ? pan : 0.0) - (s ? pan : 0.0);
                mv.TranslateBy(0.0, -dy);
                if (panLog < 5) { L("上下平移 dy=" + (-dy)); panLog++; }
            }
            // A/D 左右平移
            if (a || d)
            {
                double dx = (d ? pan : 0.0) - (a ? pan : 0.0);
                mv.TranslateBy(-dx, 0.0);
                if (panLog < 5) { L("左右平移 dx=" + (-dx)); panLog++; }
            }
            // Q/E 缩放
            if (q || ee)
            {
                double f = 1.0;
                if (ee) f *= Math.Pow(zoomStep, k);
                if (q) f /= Math.Pow(zoomStep, k);
                mv.ZoomByFactor(f);
                if (panLog < 5) { L("缩放 f=" + f); panLog++; }
            }
        }

        private void StopDynamics()
        {
            if (!dynamicOn) return;
            dynamicOn = false;
            try
            {
                IModelDoc2 doc = iSwApp == null ? null : iSwApp.ActiveDoc as IModelDoc2;
                if (doc == null) return;
                IModelView mv = doc.ActiveView as IModelView;
                if (mv != null) mv.StopDynamics();
            }
            catch { }
        }

        // 注册表用
        [ComRegisterFunctionAttribute]
        public static void RegisterFunction(Type t)
        {
            Microsoft.Win32.Registry.SetValue(
                @"HKEY_CURRENT_USER\Software\SolidWorks\Addins\{" + t.GUID.ToString() + "}",
                "", 1, Microsoft.Win32.RegistryValueKind.DWord);
            Microsoft.Win32.Registry.SetValue(
                @"HKEY_CURRENT_USER\Software\SolidWorks\Addins\{" + t.GUID.ToString() + "}",
                "Title", "WASD 视角");
            Microsoft.Win32.Registry.SetValue(
                @"HKEY_CURRENT_USER\Software\SolidWorks\Addins\{" + t.GUID.ToString() + "}",
                "Description", "键盘 WASD 控制视角");
        }

        [ComUnregisterFunctionAttribute]
        public static void UnregisterFunction(Type t)
        {
            try
            {
                Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(
                    @"Software\SolidWorks\Addins\{" + t.GUID.ToString() + "}", false);
            }
            catch { }
        }
    }
}
