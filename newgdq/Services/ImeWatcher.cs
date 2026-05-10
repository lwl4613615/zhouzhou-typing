using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

namespace newgdq.Services
{
    /// <summary>
    /// 监听 TextBox 上的 IME 合成事件（WM_IME_STARTCOMPOSITION / WM_IME_ENDCOMPOSITION）。
    /// 调用方在合成中应跳过对照染色，避免拼音字母被判为"错字"。
    /// </summary>
    public sealed class ImeWatcher : IDisposable
    {
        private const int WM_IME_STARTCOMPOSITION = 0x010D;
        private const int WM_IME_ENDCOMPOSITION   = 0x010E;

        public bool IsComposing { get; private set; }

        private HwndSource _src;

        public void Attach(TextBox textBox)
        {
            textBox.GotFocus  += OnGotFocus;
            textBox.LostFocus += OnLostFocus;
        }

        private void OnGotFocus(object sender, RoutedEventArgs e)
        {
            var win = Window.GetWindow((TextBox)sender);
            if (win == null) return;
            var helper = new WindowInteropHelper(win);
            _src = HwndSource.FromHwnd(helper.Handle);
            _src?.AddHook(WndProc);
        }

        private void OnLostFocus(object sender, RoutedEventArgs e)
        {
            _src?.RemoveHook(WndProc);
            _src = null;
            IsComposing = false;
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_IME_STARTCOMPOSITION) IsComposing = true;
            else if (msg == WM_IME_ENDCOMPOSITION) IsComposing = false;
            return IntPtr.Zero;
        }

        public void Dispose()
        {
            _src?.RemoveHook(WndProc);
            _src = null;
        }
    }
}
