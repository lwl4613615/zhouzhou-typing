using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace newgdq.Services
{
    /// <summary>
    /// 全局键盘钩子 (WH_KEYBOARD_LL = 13)。
    /// 不依赖 WinForms，用于在 WPF 中接收物理按键事件。
    /// </summary>
    public sealed class KeyHook : IDisposable
    {
        public event EventHandler<int> KeyDown;   // 参数：vkCode
        public event EventHandler<int> KeyUp;     // 参数：vkCode

        /// <summary>统一诊断日志出口；由 App 接到 newgdq.log，KeyHook 不再单独维护日志文件/开关。</summary>
        public static Action<string> DiagnosticLog { get; set; }

        public static void LogLine(string s)
        {
            DiagnosticLog?.Invoke(s);
        }

        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN     = 0x0100;
        private const int WM_KEYUP       = 0x0101;
        private const int WM_SYSKEYDOWN  = 0x0104;
        private const int WM_SYSKEYUP    = 0x0105;
        // KBDLLHOOKSTRUCT.flags bits
        private const int LLKHF_INJECTED       = 0x10;
        private const int LLKHF_LOWER_IL_INJECTED = 0x02;

        private IntPtr _hookId = IntPtr.Zero;
        private LowLevelKeyboardProc _proc; // 防止 GC 回收

        public void Start()
        {
            if (_hookId != IntPtr.Zero) return;
            _proc = HookCallback;
            using (var curProcess = Process.GetCurrentProcess())
            using (var curModule = curProcess.MainModule)
            {
                _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc,
                    GetModuleHandle(curModule.ModuleName), 0);
            }
            if (_hookId == IntPtr.Zero)
            {
                // 安装失败（权限/系统限制）：清掉 delegate 避免无谓的 GC 根引用，并记录诊断
                _proc = null;
                Debug.WriteLine($"KeyHook.Start failed, error={Marshal.GetLastWin32Error()}");
            }
        }

        public void Stop()
        {
            if (_hookId == IntPtr.Zero) return;
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }

        public void Dispose() => Stop();

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int msg = wParam.ToInt32();
                int vk        = Marshal.ReadInt32(lParam);       // offset 0
                int scanCode  = Marshal.ReadInt32(lParam, 4);    // offset 4
                int flags     = Marshal.ReadInt32(lParam, 8);    // offset 8

                // 物理键过滤：只过滤明确的"软件模拟"事件，避免误杀真实按键。
                // - LLKHF_INJECTED 是 Win32 唯一可靠的"非物理键"信号
                // - VK_PROCESSKEY (0xE5) 是 IME 占位符，物理键不会是这个值
                // 其他特征（scanCode==0 / 低权限注入位）会误杀部分 IME 候选时真实按键，已禁用。
                bool injected = (flags & LLKHF_INJECTED) != 0;
                bool imeProcess = vk == 0xE5;
                bool isDown = (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN);
                if (isDown)
                    LogLine($"DOWN vk=0x{vk:X2} sc=0x{scanCode:X2} flags=0x{flags:X2}"
                            + (injected ? " INJ" : "") + (imeProcess ? " IME" : ""));
                if (injected || imeProcess)
                    return CallNextHookEx(_hookId, nCode, wParam, lParam);

                // 事件处理器若抛异常，会逃逸回 P/Invoke 回调栈 → 系统可能直接禁用本钩子（全局快捷键失效）。
                // 这里吞掉订阅方异常，保证回调始终正常返回，钩子链不被破坏。
                try
                {
                    if (isDown)
                        KeyDown?.Invoke(this, vk);
                    else if (msg == WM_KEYUP || msg == WM_SYSKEYUP)
                        KeyUp?.Invoke(this, vk);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"KeyHook callback handler threw: {ex.Message}");
                }
            }
            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);
    }
}
