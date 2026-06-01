using System.Threading;
using System.Windows;

namespace newgdq.Services
{
    /// <summary>
    /// 剪贴板工具：Windows 同一时刻只能被一个进程打开，被输入法/剪贴板工具占用时
    /// Clipboard.SetText 会抛 CLIPBRD_E_CANT_OPEN(0x800401D0)，短暂重试可化解大多数争用。
    /// </summary>
    public static class ClipboardHelper
    {
        public static bool TrySetText(string text, int retries = 5, int delayMs = 60)
        {
            for (int i = 0; i < retries; i++)
            {
                try { Clipboard.SetText(text ?? string.Empty); return true; }
                catch
                {
                    if (i < retries - 1) Thread.Sleep(delayMs);
                }
            }
            return false;
        }
    }
}
