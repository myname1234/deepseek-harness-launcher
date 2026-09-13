using System;
using System.Drawing;
using Microsoft.Win32;

namespace DshLauncher
{
    /// <summary>
    /// 界面配色与字体。取值对应 DSH Web 客户端的设计令牌
    /// （packages/client/ui-theme 的 --dsw-alias-* / --dsw-static-*）：
    /// 平台底色、卡片层、细边框、三级文字、deepseek 蓝强调色与状态色。
    ///
    /// 每个前景/背景组合都经 <see cref="ContrastRatio"/> 校验（见 --ui-check），
    /// 保证任何状态下按钮与徽标上的文字都看得见。
    /// </summary>
    internal sealed class UiTheme
    {
        internal bool Dark;

        internal Color AppBackground;
        internal Color Surface;
        internal Color SurfaceAlt;
        internal Color Border;
        internal Color BorderStrong;
        internal Color TextPrimary;
        internal Color TextSecondary;
        internal Color TextTertiary;

        /// <summary>强调色按钮（启动）。</summary>
        internal Color Accent;
        internal Color AccentHover;
        internal Color AccentPressed;
        internal Color AccentText;
        internal Color AccentDisabled;
        internal Color AccentDisabledText;

        /// <summary>次要按钮（幽灵按钮）。</summary>
        internal Color GhostBackground;
        internal Color GhostHover;
        internal Color GhostPressed;
        internal Color GhostBorder;
        internal Color GhostText;
        internal Color GhostDisabledText;

        internal Color Success;
        internal Color Warn;
        internal Color Error;

        /// <summary>选择器（目标目录字段）的表面色，取自 DSH 的 selector 令牌。</summary>
        internal Color SelectorFill;
        internal Color SelectorHover;
        internal Color SelectorPressed;
        internal Color SelectorBorder;

        /// <summary>弹出菜单卡片表面（DSH menu = bg-layer-3）。</summary>
        internal Color MenuSurface;
        internal Color MenuHover;
        internal Color MenuBorder;
        internal Color MenuSeparator;

        internal Color ConsoleBackground;
        internal Color ConsoleBorder;

        internal Color PillSuccessFill;
        internal Color PillWarnFill;
        internal Color PillErrorFill;
        internal Color PillInfoFill;
        internal Color PillNeutralFill;

        internal Font TitleFont;
        internal Font SubtitleFont;
        internal Font BodyFont;
        internal Font MetaFont;
        internal Font ButtonFont;
        internal Font MonoFont;
        internal Font PillFont;

        private UiTheme()
        {
        }

        /// <summary>按配置（auto/dark/light）选择主题；auto 时跟随 Windows 的应用模式。</summary>
        internal static UiTheme Select(string preference)
        {
            if (string.Equals(preference, "dark", StringComparison.OrdinalIgnoreCase))
            {
                return DarkTheme();
            }

            if (string.Equals(preference, "light", StringComparison.OrdinalIgnoreCase))
            {
                return LightTheme();
            }

            return SystemPrefersDark() ? DarkTheme() : LightTheme();
        }

        /// <summary>读取 Windows 的“应用模式”设置；读不到时按浅色处理。</summary>
        internal static bool SystemPrefersDark()
        {
            try
            {
                object value = Registry.GetValue(
                    @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                    "AppsUseLightTheme",
                    null);
                if (value is int)
                {
                    return (int)value == 0;
                }
            }
            catch (Exception)
            {
                // 注册表不可读（受限环境）时退回浅色。
            }

            return false;
        }

        internal static UiTheme LightTheme()
        {
            UiTheme theme = new UiTheme();
            theme.Dark = false;
            theme.AppBackground = Rgb(245, 246, 247);
            theme.Surface = Rgb(255, 255, 255);
            theme.SurfaceAlt = Rgb(235, 238, 242);
            theme.Border = Rgb(226, 229, 235);
            theme.BorderStrong = Rgb(207, 211, 214);
            theme.TextPrimary = Rgb(15, 17, 21);
            theme.TextSecondary = Rgb(97, 102, 107);
            theme.TextTertiary = Rgb(133, 138, 146);

            theme.Accent = Rgb(53, 103, 214);
            theme.AccentHover = Rgb(43, 87, 188);
            theme.AccentPressed = Rgb(35, 72, 160);
            theme.AccentText = Rgb(255, 255, 255);
            theme.AccentDisabled = Rgb(235, 238, 242);
            theme.AccentDisabledText = Rgb(97, 102, 107);

            theme.GhostBackground = Rgb(255, 255, 255);
            theme.GhostHover = Rgb(241, 243, 245);
            theme.GhostPressed = Rgb(233, 236, 242);
            theme.GhostBorder = Rgb(222, 226, 233);
            theme.GhostText = Rgb(15, 17, 21);
            theme.GhostDisabledText = Rgb(107, 114, 128);

            theme.Success = Rgb(34, 138, 76);
            theme.Warn = Rgb(154, 103, 0);
            theme.Error = Rgb(179, 23, 23);
            theme.SelectorFill = Rgb(247, 248, 250);
            theme.SelectorHover = Rgb(239, 241, 244);
            theme.SelectorPressed = Rgb(232, 235, 240);
            theme.SelectorBorder = Rgb(226, 229, 235);
            theme.MenuSurface = Rgb(255, 255, 255);
            theme.MenuHover = Rgb(244, 245, 247);
            theme.MenuBorder = Rgb(226, 229, 235);
            theme.MenuSeparator = Rgb(235, 238, 242);
            theme.ConsoleBackground = Rgb(255, 255, 255);
            theme.ConsoleBorder = Rgb(226, 229, 235);

            theme.PillSuccessFill = Rgb(230, 250, 237);
            theme.PillWarnFill = Rgb(254, 245, 231);
            theme.PillErrorFill = Rgb(254, 233, 233);
            theme.PillInfoFill = Rgb(232, 240, 254);
            theme.PillNeutralFill = Rgb(235, 238, 242);
            theme.BuildFonts();
            return theme;
        }

        internal static UiTheme DarkTheme()
        {
            UiTheme theme = new UiTheme();
            theme.Dark = true;
            theme.AppBackground = Rgb(21, 21, 23);
            theme.Surface = Rgb(35, 35, 36);
            theme.SurfaceAlt = Rgb(53, 54, 56);
            theme.Border = Rgb(49, 49, 51);
            theme.BorderStrong = Rgb(58, 58, 60);
            theme.TextPrimary = Rgb(249, 250, 251);
            theme.TextSecondary = Rgb(207, 211, 214);
            theme.TextTertiary = Rgb(163, 168, 176);

            theme.Accent = Rgb(103, 158, 254);
            theme.AccentHover = Rgb(134, 180, 255);
            theme.AccentPressed = Rgb(78, 134, 233);
            theme.AccentText = Rgb(15, 17, 21);
            theme.AccentDisabled = Rgb(67, 69, 74);
            theme.AccentDisabledText = Rgb(207, 211, 214);

            theme.GhostBackground = Rgb(35, 35, 36);
            theme.GhostHover = Rgb(44, 44, 46);
            theme.GhostPressed = Rgb(53, 54, 56);
            theme.GhostBorder = Rgb(56, 56, 59);
            theme.GhostText = Rgb(249, 250, 251);
            theme.GhostDisabledText = Rgb(154, 160, 168);

            theme.Success = Rgb(78, 209, 126);
            theme.Warn = Rgb(247, 196, 107);
            theme.Error = Rgb(245, 138, 138);
            theme.SelectorFill = Rgb(46, 46, 48);
            theme.SelectorHover = Rgb(58, 58, 60);
            theme.SelectorPressed = Rgb(67, 69, 74);
            theme.SelectorBorder = Rgb(56, 56, 59);
            theme.MenuSurface = Rgb(53, 54, 56);
            theme.MenuHover = Rgb(68, 68, 71);
            theme.MenuBorder = Rgb(70, 70, 73);
            theme.MenuSeparator = Rgb(70, 70, 73);
            theme.ConsoleBackground = Rgb(27, 27, 28);
            theme.ConsoleBorder = Rgb(44, 44, 46);

            theme.PillSuccessFill = Rgb(35, 60, 44);
            theme.PillWarnFill = Rgb(48, 41, 28);
            theme.PillErrorFill = Rgb(66, 25, 25);
            theme.PillInfoFill = Rgb(38, 51, 78);
            theme.PillNeutralFill = Rgb(48, 48, 50);
            theme.BuildFonts();
            return theme;
        }

        /// <summary>紧凑的字号梯度：标题 12.5pt、正文 9pt、次要 8.25pt。</summary>
        private void BuildFonts()
        {
            TitleFont = new Font("Segoe UI Semibold", 12.5F, FontStyle.Regular, GraphicsUnit.Point);
            SubtitleFont = new Font("Segoe UI", 8.75F, FontStyle.Regular, GraphicsUnit.Point);
            BodyFont = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            MetaFont = new Font("Segoe UI", 8.25F, FontStyle.Regular, GraphicsUnit.Point);
            ButtonFont = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            PillFont = new Font("Segoe UI", 8.5F, FontStyle.Regular, GraphicsUnit.Point);
            MonoFont = MonoFontFor(9F);
        }

        /// <summary>等宽字体按可用性回退，避免退到比例字体。</summary>
        private static Font MonoFontFor(float size)
        {
            string[] candidates = new string[] { "Cascadia Mono", "Consolas", "Courier New" };
            for (int i = 0; i < candidates.Length; i++)
            {
                Font font = new Font(candidates[i], size, FontStyle.Regular, GraphicsUnit.Point);
                if (string.Equals(font.Name, candidates[i], StringComparison.OrdinalIgnoreCase))
                {
                    return font;
                }

                font.Dispose();
            }

            return new Font(FontFamily.GenericMonospace, size, FontStyle.Regular, GraphicsUnit.Point);
        }

        /// <summary>日志级别对应的前景色（在控制台底色上同样满足对比度要求）。</summary>
        internal Color LevelColor(LogLevel level)
        {
            switch (level)
            {
                case LogLevel.Command:
                    return Dark ? Rgb(134, 180, 255) : Rgb(31, 79, 168);
                case LogLevel.Success:
                    return Success;
                case LogLevel.Warn:
                    return Warn;
                case LogLevel.Error:
                    return Error;
                default:
                    return Dark ? Rgb(195, 201, 210) : Rgb(75, 85, 99);
            }
        }

        /// <summary>徽标文字色：与对应胶囊底色配对，保证可读。</summary>
        internal Color PillText(LauncherState state)
        {
            switch (state)
            {
                case LauncherState.Running:
                    return Success;
                case LauncherState.Failed:
                    return Error;
                case LauncherState.Stopped:
                case LauncherState.Idle:
                    return Dark ? Rgb(207, 211, 214) : Rgb(75, 85, 99);
                default:
                    return Dark ? Rgb(163, 196, 255) : Rgb(31, 79, 168);
            }
        }

        internal static Color Rgb(int r, int g, int b)
        {
            return Color.FromArgb(255, r, g, b);
        }

        /// <summary>按比例把 from 混向 to（用于 hover / 浅底等派生色）。</summary>
        internal static Color Mix(Color from, Color to, double ratio)
        {
            if (ratio < 0)
            {
                ratio = 0;
            }

            if (ratio > 1)
            {
                ratio = 1;
            }

            return Color.FromArgb(
                from.A,
                (int)Math.Round(from.R + ((to.R - from.R) * ratio)),
                (int)Math.Round(from.G + ((to.G - from.G) * ratio)),
                (int)Math.Round(from.B + ((to.B - from.B) * ratio)));
        }

        /// <summary>WCAG 2.x 相对对比度；1 = 完全相同，21 = 黑白。</summary>
        internal static double ContrastRatio(Color foreground, Color background)
        {
            double a = RelativeLuminance(foreground);
            double b = RelativeLuminance(background);
            double lighter = Math.Max(a, b);
            double darker = Math.Min(a, b);
            return (lighter + 0.05) / (darker + 0.05);
        }

        private static double RelativeLuminance(Color color)
        {
            return (0.2126 * Channel(color.R)) + (0.7152 * Channel(color.G)) + (0.0722 * Channel(color.B));
        }

        private static double Channel(int value)
        {
            double c = value / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }
    }
}
