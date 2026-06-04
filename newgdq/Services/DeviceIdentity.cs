using System;
using Microsoft.Win32;

namespace newgdq.Services
{
    /// <summary>
    /// 设备身份：群比赛设备路径上传成绩用的稳定机器标识。
    /// 优先读注册表 HKLM\SOFTWARE\Microsoft\Cryptography 的 MachineGuid；
    /// 取不到时回退到一个存 settings 的随机 GUID（生成一次后持久化，保证跨重启一致）。
    /// 上传的是 deviceId 明文，服务端负责 sha256，客户端不哈希。
    /// </summary>
    public static class DeviceIdentity
    {
        private static string _cached;

        /// <summary>取本机稳定设备标识（MachineGuid 或持久化的回退 GUID）。</summary>
        public static string GetDeviceId()
        {
            if (!string.IsNullOrEmpty(_cached)) return _cached;

            string id = ReadMachineGuid();
            if (string.IsNullOrWhiteSpace(id))
                id = GetOrCreateFallback();

            _cached = id.Trim();
            return _cached;
        }

        private static string ReadMachineGuid()
        {
            try
            {
                using (var key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                    .OpenSubKey(@"SOFTWARE\Microsoft\Cryptography"))
                {
                    var val = key?.GetValue("MachineGuid") as string;
                    return string.IsNullOrWhiteSpace(val) ? null : val.Trim();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[DeviceIdentity.ReadMachineGuid] " + ex.Message);
                return null;
            }
        }

        private static string GetOrCreateFallback()
        {
            var s = SettingsService.Instance;
            if (!string.IsNullOrWhiteSpace(s.DeviceIdFallback))
                return s.DeviceIdFallback.Trim();

            string guid = Guid.NewGuid().ToString("N");
            s.DeviceIdFallback = guid;
            SettingsService.Save();
            return guid;
        }
    }
}
