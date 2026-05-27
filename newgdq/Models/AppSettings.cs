using System.Runtime.Serialization;

namespace newgdq.Models
{
    /// <summary>
    /// 持久化的应用设置（窗口几何 + UI 开关）。
    /// 后续 P1 SettingsWindow 实装时往这里加字段（字体/颜色/输入法/延时等）。
    /// 字段都用可空 double / bool? —— 第一次启动 settings.json 不存在时全部为 null，UI 走默认值。
    /// </summary>
    [DataContract]
    public class AppSettings
    {
        // 主窗口几何
        [DataMember(EmitDefaultValue = false)] public double? WindowLeft   { get; set; }
        [DataMember(EmitDefaultValue = false)] public double? WindowTop    { get; set; }
        [DataMember(EmitDefaultValue = false)] public double? WindowWidth  { get; set; }
        [DataMember(EmitDefaultValue = false)] public double? WindowHeight { get; set; }
        [DataMember(EmitDefaultValue = false)] public bool?   WindowMaximized { get; set; }

        // 底部标记栏开关
        [DataMember(EmitDefaultValue = false)] public bool? TogBmTips  { get; set; }
        [DataMember(EmitDefaultValue = false)] public bool? TogChart   { get; set; }
        [DataMember(EmitDefaultValue = false)] public bool? TogMark    { get; set; }
        [DataMember(EmitDefaultValue = false)] public bool? TogSimple  { get; set; }
        [DataMember(EmitDefaultValue = false)] public bool? TogDetail  { get; set; }
        [DataMember(EmitDefaultValue = false)] public bool? TogSegRuler{ get; set; }
        [DataMember(EmitDefaultValue = false)] public bool? TogMap     { get; set; }
        [DataMember(EmitDefaultValue = false)] public bool? SmartCi    { get; set; }

        // 字体（对照区 / 输入区分别）
        [DataMember(EmitDefaultValue = false)] public string CompareFontFamily { get; set; }
        [DataMember(EmitDefaultValue = false)] public double? CompareFontSize  { get; set; }
        [DataMember(EmitDefaultValue = false)] public string InputFontFamily   { get; set; }
        [DataMember(EmitDefaultValue = false)] public double? InputFontSize    { get; set; }

        // 颜色（均存 #RRGGBB 字符串）
        [DataMember(EmitDefaultValue = false)] public string ColorRight    { get; set; } // 正确字前景
        [DataMember(EmitDefaultValue = false)] public string ColorRightBg  { get; set; } // 正确字背景
        [DataMember(EmitDefaultValue = false)] public string ColorWrong    { get; set; } // 错误字前景
        [DataMember(EmitDefaultValue = false)] public string ColorWrongBg  { get; set; } // 错误字背景
        [DataMember(EmitDefaultValue = false)] public string ColorCompareBg{ get; set; } // 对照区背景
        [DataMember(EmitDefaultValue = false)] public string ColorInputBg  { get; set; } // 输入区背景

        // 个签（显示在信息条最右"签名"列）
        [DataMember(EmitDefaultValue = false)] public bool?  SignEnabled { get; set; }
        [DataMember(EmitDefaultValue = false)] public string SignText    { get; set; }

        // 托盘：最小化时是否藏到托盘
        [DataMember(EmitDefaultValue = false)] public bool?  MinimizeToTray { get; set; }

        // 主题："Dark" / "Light"。默认 Dark。
        [DataMember(EmitDefaultValue = false)] public string ThemeName { get; set; }

        // 长时间未跟打自动重打：0 = 关闭，N 分钟无输入则触发 F3 重打（对齐老版 timer5 + StopUse）
        [DataMember(EmitDefaultValue = false)] public int? AutoRepeatMinutes { get; set; }

        // 速度门槛：完成段时速度（字/分）低于此值不入历史/不发图。0 = 关闭。
        [DataMember(EmitDefaultValue = false)] public double? SpeedLimit { get; set; }

        // 发文参数预设（命名保存）
        [DataMember(EmitDefaultValue = false)] public System.Collections.Generic.List<SendPreset> SendPresets { get; set; }
    }
}
