using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;

namespace newgdq
{
    /// <summary>
    /// App.xaml 的交互逻辑
    /// 启动时通过命名 Mutex 保证单实例：
    /// 同一份 exe（按完整路径）只允许一个进程运行，避免两个实例同时写 history.db / settings.json 造成污染。
    /// 不同位置的 exe 互不影响（U 盘和本地装的可以同时跑）。
    /// </summary>
    public partial class App : Application
    {
        // 必须用字段持有 Mutex 引用，否则会被 GC 回收导致互斥失效
        private static Mutex _singleInstanceMutex;

        protected override void OnStartup(StartupEventArgs e)
        {
            Services.KeyHook.DiagnosticLog = s => Diag("KEY", s);

            // 全局异常处理：UI 线程 + 非 UI 线程 + Task 内部
            this.DispatcherUnhandledException     += (s, ev) => { LogException("UI", ev.Exception); ev.Handled = true; ShowFatal(ev.Exception); };
            AppDomain.CurrentDomain.UnhandledException += (s, ev) => LogException("AppDomain", ev.ExceptionObject as Exception);
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, ev) => { LogException("Task", ev.Exception); ev.SetObserved(); };

            // 把 exe 完整路径转成可作 Mutex 名的字符串（路径分隔符替换 + 加前缀）
            string exePath = Assembly.GetExecutingAssembly().Location ?? "newgdq";
            string mutexName = "Local\\newgdq_" + exePath
                .Replace(Path.DirectorySeparatorChar, '_')
                .Replace(':', '_');

            bool createdNew;
            _singleInstanceMutex = new Mutex(true, mutexName, out createdNew);
            if (!createdNew)
            {
                MessageBox.Show(
                    "州州跟打器已经在运行了，看看任务栏或托盘~",
                    "州州跟打器",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                Shutdown();
                return;
            }

            // 主题：根据 settings.json 中 ThemeName 替换 App.xaml 里默认 Dark 资源字典
            Services.SettingsService.Load();
            bool initialDiag = Services.SettingsService.Instance.InputDiagnosticsEnabled == true
                || string.Equals(Environment.GetEnvironmentVariable("NEWGDQ_INPUT_DIAG"), "1", StringComparison.OrdinalIgnoreCase)
                || e.Args.Any(a => string.Equals(a, "--diag-input", StringComparison.OrdinalIgnoreCase));
            SetInputDiagnostics(initialDiag, clearOnEnable: false, out _);
            ApplyTheme(Services.SettingsService.Instance.ThemeName);

            // 跟随系统深浅色：当 ThemeName == "System" 时，系统切换深浅色后实时重应用
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

            // 全局界面缩放：确定初始倍数并钩住所有窗口，使各窗体字体/控件一起缩放
            Services.UiScaleManager.Initialize();

            base.OnStartup(e);
        }

        public static string LogPath { get; } = Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".",
            "newgdq.log");

        private static readonly object LogLock = new object();

        /// <summary>诊断开关：true 时把输入/IME/键盘钩子的实时状态写入 newgdq.log（排错用，平时关）。</summary>
        public static bool DiagInput { get; private set; }

        public static bool SetInputDiagnostics(bool enabled, bool clearOnEnable, out string error)
        {
            error = null;
            lock (LogLock)
            {
                if (DiagInput == enabled) return true;
                if (!enabled)
                {
                    DiagInput = false;
                    return true;
                }

                try
                {
                    string line = $"[{DateTime.Now:HH:mm:ss.fff}] [INIT] input diagnostics enabled\r\n";
                    if (clearOnEnable)
                        File.WriteAllText(LogPath, line);
                    else
                        File.AppendAllText(LogPath, line);
                    DiagInput = true;
                    return true;
                }
                catch (Exception ex)
                {
                    DiagInput = false;
                    error = $"{(clearOnEnable ? "清空并初始化" : "初始化")}输入诊断日志失败：{ex.Message}";
                    return false;
                }
            }
        }

        /// <summary>诊断日志：仅 DiagInput 为 true 时写入，失败静默。</summary>
        public static void Diag(string tag, string msg)
        {
            if (!DiagInput) return;
            try
            {
                lock (LogLock)
                    File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss.fff}] [{tag}] {msg}\r\n");
            }
            catch { }
        }

        private static void LogException(string source, Exception ex)
        {
            try
            {
                string line = $"[{DateTime.Now:O}] [{source}] {ex}\r\n";
                File.AppendAllText(LogPath, line);
            }
            catch { /* 日志写入失败也不能再抛 */ }
        }

        private static void ShowFatal(Exception ex)
        {
            try
            {
                MessageBox.Show(
                    "程序遇到未捕获错误，已写入 newgdq.log，可继续使用：\n\n" + ex.Message,
                    "州州跟打器",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            catch { }
        }

        /// <summary>切换主题资源字典。已在 App.xaml 加载了 Dark 作为默认；这里按名替换。
        /// 名称："Light" 用浅色；"System" 跟随系统深浅色；其它（含 null/Dark）保留默认 Dark 不变。</summary>
        public static void ApplyTheme(string themeName)
        {
            string resolved = ResolveTheme(themeName);
            string targetUri;
            if (string.Equals(resolved, "Light", StringComparison.OrdinalIgnoreCase))
                targetUri = "pack://application:,,,/newgdq;component/Themes/Light.xaml";
            else
                targetUri = "pack://application:,,,/newgdq;component/Themes/Dark.xaml";

            var dicts = Current.Resources.MergedDictionaries;
            var newDict = new System.Windows.ResourceDictionary { Source = new Uri(targetUri) };
            // 清掉所有 Dark/Light 已加载条目，再插入新的
            for (int i = dicts.Count - 1; i >= 0; i--)
            {
                var s = dicts[i].Source?.ToString() ?? "";
                // 仅移除 Dark/Light 主题色字典；Shared.xaml 等共享样式保留不动
                if (s.IndexOf("Themes/Dark.xaml", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    s.IndexOf("Themes/Light.xaml", StringComparison.OrdinalIgnoreCase) >= 0)
                    dicts.RemoveAt(i);
            }
            dicts.Insert(0, newDict);
        }

        /// <summary>把主题名解析为实际的 "Light" / "Dark"。"System" 读注册表跟随系统深浅色。</summary>
        public static string ResolveTheme(string themeName)
        {
            if (string.Equals(themeName, "System", StringComparison.OrdinalIgnoreCase))
                return IsSystemLightTheme() ? "Light" : "Dark";
            if (string.Equals(themeName, "Light", StringComparison.OrdinalIgnoreCase))
                return "Light";
            return "Dark";
        }

        /// <summary>读注册表判断系统“应用”主题是否为浅色。读不到时默认深色。</summary>
        private static bool IsSystemLightTheme()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                var v = key?.GetValue("AppsUseLightTheme");
                if (v is int i) return i != 0;
            }
            catch { }
            return false;
        }

        /// <summary>系统深浅色变化时，若用户选的是“跟随系统”，实时重应用主题。</summary>
        private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category != UserPreferenceCategory.General) return;
            if (!string.Equals(Services.SettingsService.Instance.ThemeName, "System", StringComparison.OrdinalIgnoreCase)) return;
            try { Current?.Dispatcher?.Invoke(() => ApplyTheme("System")); } catch { }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try { _singleInstanceMutex?.ReleaseMutex(); } catch { /* 已被回收 */ }
            _singleInstanceMutex?.Dispose();
            base.OnExit(e);
        }
    }
}
