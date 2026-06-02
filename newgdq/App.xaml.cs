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
            ApplyTheme(Services.SettingsService.Instance.ThemeName);

            // ScottPlot 全局默认字体设为中文可渲染字体，避免图表中文标题/标签变方块乱码
            ScottPlot.Fonts.Default = "Microsoft YaHei";

            // 全局界面缩放：确定初始倍数并钩住所有窗口，使各窗体字体/控件一起缩放
            Services.UiScaleManager.Initialize();

            base.OnStartup(e);
        }

        private static readonly string LogPath = Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".",
            "newgdq.log");

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
        /// 名称："Light" 用浅色；其它（含 null/Dark）保留默认 Dark 不变。</summary>
        public static void ApplyTheme(string themeName)
        {
            string targetUri;
            if (string.Equals(themeName, "Light", StringComparison.OrdinalIgnoreCase))
                targetUri = "pack://application:,,,/newgdq;component/Themes/Light.xaml";
            else
                targetUri = "pack://application:,,,/newgdq;component/Themes/Dark.xaml";

            var dicts = Current.Resources.MergedDictionaries;
            var newDict = new System.Windows.ResourceDictionary { Source = new Uri(targetUri) };
            // 清掉所有 Dark/Light 已加载条目，再插入新的
            for (int i = dicts.Count - 1; i >= 0; i--)
            {
                var s = dicts[i].Source?.ToString() ?? "";
                if (s.IndexOf("newgdq;component/Themes/", StringComparison.OrdinalIgnoreCase) >= 0)
                    dicts.RemoveAt(i);
            }
            dicts.Insert(0, newDict);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try { _singleInstanceMutex?.ReleaseMutex(); } catch { /* 已被回收 */ }
            _singleInstanceMutex?.Dispose();
            base.OnExit(e);
        }
    }
}
