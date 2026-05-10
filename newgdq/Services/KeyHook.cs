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

        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN     = 0x0100;
        private const int WM_KEYUP       = 0x0101;
        private const int WM_SYSKEYDOWN  = 0x0104;
        private const int WM_SYSKEYUP    = 0x0105;

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
                int vk  = Marshal.ReadInt32(lParam);
                if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
                    KeyDown?.Invoke(this, vk);
                else if (msg == WM_KEYUP || msg == WM_SYSKEYUP)
                    KeyUp?.Invoke(this, vk);
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
