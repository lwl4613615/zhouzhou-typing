using System;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization.Json;
using newgdq.Models;

namespace newgdq.Services
{
    /// <summary>
    /// 应用设置 JSON 读写服务 —— 便携模式。
    /// 配置文件固定写到 exe 同目录的 settings.json，整个程序文件夹拷哪都行。
    /// 读写失败均吞掉（仅 Debug.WriteLine），不影响主程序。
    /// </summary>
    public static class SettingsService
    {
        private static readonly string ExeDir =
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".";

        /// <summary>配置文件绝对路径（exe 同目录\settings.json）。</summary>
        public static string FilePath { get; } = Path.Combine(ExeDir, "settings.json");

        public static AppSettings Instance { get; private set; } = new AppSettings();

        public static void Load()
        {
            if (TryLoadFrom(FilePath)) return;
            // 主文件损坏 → 尝试从 .bak 恢复
            string bak = FilePath + ".bak";
            if (File.Exists(bak) && TryLoadFrom(bak))
            {
                System.Diagnostics.Debug.WriteLine("[SettingsService.Load] 主文件损坏，已从 .bak 恢复");
                try { File.Copy(bak, FilePath, overwrite: true); } catch { }
            }
        }

        private static bool TryLoadFrom(string path)
        {
            try
            {
                if (!File.Exists(path)) return false;
                using (var fs = File.OpenRead(path))
                {
                    var ser = new DataContractJsonSerializer(typeof(AppSettings));
                    var loaded = ser.ReadObject(fs) as AppSettings;
                    if (loaded != null) { Instance = loaded; return true; }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[SettingsService.TryLoadFrom] " + path + " -> " + ex.Message);
            }
            return false;
        }

        private static readonly object SaveLock = new object();

        public static void Save()
        {
            // 原子写入：先写到唯一 .tmp，备份旧文件到 .bak，再替换；整体加锁串行化
            lock (SaveLock)
            {
                string tmp = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    using (var ms = new MemoryStream())
                    {
                        var ser = new DataContractJsonSerializer(typeof(AppSettings));
                        ser.WriteObject(ms, Instance);
                        File.WriteAllBytes(tmp, ms.ToArray());
                    }
                    if (File.Exists(FilePath))
                        File.Replace(tmp, FilePath, FilePath + ".bak", ignoreMetadataErrors: true);
                    else
                        File.Move(tmp, FilePath);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("[SettingsService.Save] " + ex.Message);
                    try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                }
            }
        }
    }
}
