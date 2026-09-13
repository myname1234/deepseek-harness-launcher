using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace DshLauncher
{
    /// <summary>
    /// 只读的界面布局自检：把窗口按若干尺寸布局，用文本测量结果证明
    /// 「文字没有被裁剪」——每个可见文本控件所需的宽高都小于它实际占用的宽高，
    /// 三块区域（头部卡片 / 控制台 / 底部操作区）互不重叠，控件不越出父容器。
    /// 不显示窗口、不触发任何启动流程。
    /// </summary>
    internal static class UiCheck
    {
        private static readonly Size[] Sizes = new Size[]
        {
            new Size(720, 480),
            new Size(820, 560),
            new Size(1000, 700),
            new Size(1280, 820),
        };

        internal static int Run(AppConfig config, Log log)
        {
            StringBuilder report = new StringBuilder();
            int failures = 0;
            int checks = 0;

            Action<string> emit = delegate(string text)
            {
                report.AppendLine(text);
                log.Info(text);
                ConsoleBridge.WriteLine(text);
            };

            emit("界面布局自检：窗口尺寸 " + Describe(Sizes));
            emit("主题：" + config.Theme + "（" + (UiTheme.Select(config.Theme).Dark ? "深色" : "浅色") + "）");
            emit(string.Empty);

            using (LauncherForm form = new LauncherForm(config, log, true, true, false))
            {
                // 需要句柄才能完成真实布局（不需要显示窗口）。
                form.CreateControl();
                form.LoadWorstCaseContent();

                for (int i = 0; i < Sizes.Length; i++)
                {
                    Size size = Sizes[i];
                    int failuresBefore = failures;
                    form.ClientSize = size;
                    form.PerformLayout();

                    emit(string.Format(
                        CultureInfo.InvariantCulture,
                        "— 客户区 {0}×{1}（实际 {2}×{3}）",
                        size.Width,
                        size.Height,
                        form.ClientSize.Width,
                        form.ClientSize.Height));

                    CheckTextFits(form, emit, ref failures, ref checks);
                    CheckRegions(form, emit, ref failures, ref checks);
                    CheckBounds(form, emit, ref failures, ref checks);
                    CheckConsoleWraps(form, emit, ref failures, ref checks);
                    emit(failures == failuresBefore
                        ? "  [通过] 文本不裁剪、区域不重叠、控件不越界、日志自动换行"
                        : string.Format(CultureInfo.InvariantCulture, "  [失败] 本尺寸有 {0} 项不成立", failures - failuresBefore));
                    emit(string.Empty);
                }

                // 颜色与文字可读性只需检查一次（与窗口尺寸无关）。
                emit("— 前景/背景对比度（WCAG，阈值 4.5:1）");
                CheckContrast(form, emit, ref failures, ref checks);

                emit("— 自绘控件渲染像素（圆角外不得出现黑边）");
                CheckRenderedPixels(form, emit, ref failures, ref checks);

                emit("— 按钮点击事件已连接到处理器");
                CheckClickWiring(form, emit, ref failures, ref checks);

                emit("— 目标目录弹出菜单（结构与渲染）");
                CheckTargetMenu(form, emit, ref failures, ref checks);

                emit(string.Empty);
            }

            emit(string.Format(CultureInfo.InvariantCulture, "自检完成：{0} 项检查，{1} 项不成立。", checks, failures));

            string path = null;
            try
            {
                Directory.CreateDirectory(config.LogDirectory);
                path = Path.Combine(config.LogDirectory, "ui-check-report.txt");
                StringBuilder header = new StringBuilder();
                header.AppendLine("DeepSeek Harness 启动器 - 界面布局自检报告");
                header.AppendLine("生成时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
                header.AppendLine("说明：本报告只做布局测量，不显示窗口、不访问源码目录。");
                header.AppendLine();
                header.Append(report);
                File.WriteAllText(path, header.ToString(), new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                log.Warn("无法写入自检报告：" + ex.Message);
                path = null;
            }

            if (path != null)
            {
                log.Info("自检报告：" + path);
                ConsoleBridge.WriteLine("自检报告：" + path);
            }

            return failures == 0 ? 0 : 1;
        }

        private static string Describe(Size[] sizes)
        {
            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < sizes.Length; i++)
            {
                if (i > 0)
                {
                    builder.Append("、");
                }

                builder.Append(sizes[i].Width).Append('×').Append(sizes[i].Height);
            }

            return builder.ToString();
        }

        /// <summary>每个可见文本控件：测得所需尺寸必须小于等于实际尺寸。</summary>
        private static void CheckTextFits(Control root, Action<string> emit, ref int failures, ref int checks)
        {
            foreach (Control control in Walk(root))
            {
                if (!control.Visible || string.IsNullOrEmpty(control.Text))
                {
                    continue;
                }

                checks++;
                string name = Name(control);
                Size available = control.ClientSize;
                Size needed;

                if (control is Button || control is CheckBox)
                {
                    // 单行控件：按整行测量（复选框还要留出勾选框与文字间距）。
                    needed = TextRenderer.MeasureText(control.Text, control.Font);
                    int extra = control is CheckBox ? 22 : control.Padding.Horizontal;
                    if (needed.Width + extra > available.Width + 2 || needed.Height > available.Height + 2)
                    {
                        failures++;
                        emit(string.Format(
                            CultureInfo.InvariantCulture,
                            "  [失败] {0} 文字超出控件：需要 {1}+{2}×{3}，实际 {4}×{5}（\"{6}\"）",
                            name, needed.Width, extra, needed.Height, available.Width, available.Height, control.Text));
                    }

                    continue;
                }

                if (control is StatusPill)
                {
                    Size preferred = control.GetPreferredSize(Size.Empty);
                    if (preferred.Width > available.Width + 2 || preferred.Height > available.Height + 2)
                    {
                        failures++;
                        emit(string.Format(
                            CultureInfo.InvariantCulture,
                            "  [失败] {0} 徽标尺寸不足：需要 {1}×{2}，实际 {3}×{4}",
                            name, preferred.Width, preferred.Height, available.Width, available.Height));
                    }

                    continue;
                }


                // 标签按可用宽度换行后测量高度。
                int wrapWidth = control.MaximumSize.Width > 0 ? control.MaximumSize.Width : available.Width;
                needed = TextRenderer.MeasureText(
                    control.Text,
                    control.Font,
                    new Size(Math.Max(1, wrapWidth), int.MaxValue),
                    TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.TextBoxControl);

                if (needed.Height > available.Height + 2 || needed.Width > available.Width + 2)
                {
                    failures++;
                    emit(string.Format(
                        CultureInfo.InvariantCulture,
                        "  [失败] {0} 文字被裁剪：需要 {1}×{2}，实际 {3}×{4}",
                        name, needed.Width, needed.Height, available.Width, available.Height));
                }
            }
        }

        /// <summary>头部 / 控制台 / 底部三块区域不得重叠。</summary>
        private static void CheckRegions(LauncherForm form, Action<string> emit, ref int failures, ref int checks)
        {
            TableLayoutPanel root = form.Controls.Count > 0 ? form.Controls[0] as TableLayoutPanel : null;
            if (root == null)
            {
                failures++;
                emit("  [失败] 未找到根布局容器");
                return;
            }

            Control header = root.GetControlFromPosition(0, 0);
            Control console = root.GetControlFromPosition(0, 1);
            Control footer = root.GetControlFromPosition(0, 2);
            checks += 2;

            if (header == null || console == null || footer == null)
            {
                failures++;
                emit("  [失败] 根布局缺少区域");
                return;
            }

            if (header.Bottom > console.Top)
            {
                failures++;
                emit(string.Format(CultureInfo.InvariantCulture, "  [失败] 头部与日志区重叠：头部底 {0} > 日志顶 {1}", header.Bottom, console.Top));
            }

            if (console.Bottom > footer.Top)
            {
                failures++;
                emit(string.Format(CultureInfo.InvariantCulture, "  [失败] 日志区与底部重叠：日志底 {0} > 底部顶 {1}", console.Bottom, footer.Top));
            }

            checks++;
            if (console.Height < 80)
            {
                failures++;
                emit(string.Format(CultureInfo.InvariantCulture, "  [失败] 日志区高度不足：{0}", console.Height));
            }

            // 紧凑度：头部与底部按内容占高，日志区吃掉剩余空间。
            emit(string.Format(
                CultureInfo.InvariantCulture,
                "  [尺寸] 头部 {0}px · 日志 {1}px · 底部 {2}px",
                header.Height, console.Height, footer.Height));

            TableLayoutPanel footerTable = footer as TableLayoutPanel;
            if (footerTable != null)
            {
                Control buttonsRow = footerTable.GetControlFromPosition(0, 0);
                Control optionsRow = footerTable.GetControlFromPosition(0, 1);
                emit(string.Format(
                    CultureInfo.InvariantCulture,
                    "  [尺寸] 底部明细：按钮行 {0}px · 选项行 {1}px",
                    buttonsRow == null ? 0 : buttonsRow.Height,
                    optionsRow == null ? 0 : optionsRow.Height));
            }

            checks += 2;
            if (header.Height > 150)
            {
                failures++;
                emit(string.Format(CultureInfo.InvariantCulture, "  [失败] 头部过高（不紧凑）：{0}px", header.Height));
            }

            if (footer.Height > 120)
            {
                failures++;
                emit(string.Format(CultureInfo.InvariantCulture, "  [失败] 底部过高（不紧凑）：{0}px", footer.Height));
            }
        }

        /// <summary>任何控件都不得越出父容器。</summary>
        private static void CheckBounds(Control root, Action<string> emit, ref int failures, ref int checks)
        {
            foreach (Control control in Walk(root))
            {
                if (control.Parent == null)
                {
                    continue;
                }

                checks++;
                Size parent = control.Parent.ClientSize;
                if (control.Right > parent.Width + 1 || control.Bottom > parent.Height + 1)
                {
                    failures++;
                    emit(string.Format(
                        CultureInfo.InvariantCulture,
                        "  [失败] {0} 越出父容器：右下 ({1},{2}) > 父容器 {3}×{4}",
                        Name(control), control.Right, control.Bottom, parent.Width, parent.Height));
                }
            }
        }

        /// <summary>日志区必须开启自动换行，否则长行会横向被截断。</summary>
        private static void CheckConsoleWraps(Control root, Action<string> emit, ref int failures, ref int checks)
        {
            foreach (Control control in Walk(root))
            {
                RichTextBox box = control as RichTextBox;
                if (box == null)
                {
                    continue;
                }

                checks++;
                if (!box.WordWrap)
                {
                    failures++;
                    emit("  [失败] 日志区未开启自动换行");
                }
            }
        }

        /// <summary>
        /// 按钮每种状态（常态/悬停/按下/禁用）与状态徽标的前景-背景对比度，
        /// 这是“按钮上的字看不见”这类问题的自动防线。
        /// </summary>
        private static void CheckContrast(Control root, Action<string> emit, ref int failures, ref int checks)
        {
            const double Minimum = 4.5;
            string[] stateNames = new string[] { "常态", "悬停", "按下", "禁用" };

            foreach (Control control in Walk(root))
            {
                IThemedControl themed = control as IThemedControl;
                if (themed != null)
                {
                    for (int i = 0; i < stateNames.Length; i++)
                    {
                        ButtonVisualState state = (ButtonVisualState)i;
                        Color background;
                        Color foreground;
                        Color border;
                        themed.ResolveColors(state, out background, out foreground, out border);

                        checks++;
                        double ratio = UiTheme.ContrastRatio(foreground, background);
                        if (ratio < Minimum)
                        {
                            failures++;
                            emit(string.Format(
                                CultureInfo.InvariantCulture,
                                "  [失败] {0} 的{1}状态对比度不足：{2:0.00}:1（前景 {3} / 背景 {4}）",
                                Describe(control), stateNames[i], ratio, Hex(foreground), Hex(background)));
                        }
                        else
                        {
                            emit(string.Format(
                                CultureInfo.InvariantCulture,
                                "  [通过] {0} 的{1}：{2:0.00}:1",
                                Describe(control), stateNames[i], ratio));
                        }
                    }

                    continue;
                }

                StatusPill pill = control as StatusPill;
                if (pill != null && pill.Visible)
                {
                    checks++;
                    double ratio = UiTheme.ContrastRatio(pill.TextColor, pill.FillColor);
                    if (ratio < Minimum)
                    {
                        failures++;
                        emit(string.Format(
                            CultureInfo.InvariantCulture,
                            "  [失败] 状态徽标「{0}」对比度不足：{1:0.00}:1（前景 {2} / 背景 {3}）",
                            pill.Text, ratio, Hex(pill.TextColor), Hex(pill.FillColor)));
                    }
                    else
                    {
                        emit(string.Format(CultureInfo.InvariantCulture, "  [通过] 状态徽标「{0}」：{1:0.00}:1", pill.Text, ratio));
                    }
                }
            }
        }


        /// <summary>
        /// 把自绘按钮渲染成位图后逐像素检查：每个像素必须是按钮底色、边框色、
        /// 文字色或它们与父容器底色之间的抗锯齿过渡色。任何其它颜色（尤其是纯黑）
        /// 都意味着圆角路径之外没有被绘制 —— 也就是肉眼看到的“黑边/阴影”。
        /// </summary>
        private static void CheckRenderedPixels(Control root, Action<string> emit, ref int failures, ref int checks)
        {
            const double Tolerance = 26.0;

            foreach (Control control in Walk(root))
            {
                AppButton button = control as AppButton;
                if (button == null || button.Width < 4 || button.Height < 4)
                {
                    continue;
                }

                Color fill;
                Color foreground;
                Color border;
                button.ResolveColors(button.CurrentState, out fill, out foreground, out border);
                Color parentBackground = button.Parent != null ? button.Parent.BackColor : button.BackColor;

                checks++;
                int unexpected = 0;
                string firstSample = null;
                using (Bitmap bitmap = new Bitmap(button.Width, button.Height))
                {
                    control.DrawToBitmap(bitmap, new Rectangle(0, 0, control.Width, control.Height));

                    // 只检查最外 2 像素的边框区域：那里没有文字，可以严格排除
                    // 任何不属于该控件配色的像素（也就是肉眼看到的黑边/阴影）。
                    const int Frame = 2;
                    for (int y = 0; y < bitmap.Height; y++)
                    {
                        for (int x = 0; x < bitmap.Width; x++)
                        {
                            bool inFrame = x < Frame || y < Frame
                                || x >= bitmap.Width - Frame || y >= bitmap.Height - Frame;
                            if (!inFrame)
                            {
                                continue;
                            }

                            Color pixel = bitmap.GetPixel(x, y);
                            if (IsExpected(pixel, fill, border, parentBackground, Tolerance))
                            {
                                continue;
                            }

                            unexpected++;
                            if (firstSample == null)
                            {
                                firstSample = string.Format(
                                    CultureInfo.InvariantCulture, "({0},{1}) {2} A={3}", x, y, Hex(pixel), pixel.A);
                            }
                        }
                    }

                    if (unexpected > 0)
                    {
                        failures++;
                        emit(string.Format(
                            CultureInfo.InvariantCulture,
                            "  [失败] {0} 边框区域有 {1} 个异常像素（首个 {2}）；底色 {3} 边框 {4} 父底色 {5}；四角 {6}",
                            Describe(control), unexpected, firstSample, Hex(fill), Hex(border), Hex(parentBackground), Corners(bitmap)));
                    }
                    else
                    {
                        emit(string.Format(
                            CultureInfo.InvariantCulture,
                            "  [通过] {0} {1}×{2} 边框无异常像素，四角 {3}",
                            Describe(control), control.Width, control.Height, Corners(bitmap)));
                    }
                }
            }
        }

        /// <summary>
        /// 目标目录弹出菜单：先验证行结构（分组标题 + 默认目录 + 最近使用 + 固定动作，
        /// 且当前目标被标记为选中），再把它渲染成位图逐像素检查——
        /// 菜单表面上的任何“不认识的颜色”（尤其是未绘制的黑色）都会被发现。
        /// </summary>
        private static void CheckTargetMenu(LauncherForm form, Action<string> emit, ref int failures, ref int checks)
        {
            UiTheme theme = form.Theme;
            System.Collections.Generic.List<TargetMenuItem> items = TargetMenu.BuildItems(
                @"D:\default\deepseek-harness",
                new System.Collections.Generic.List<string>(new string[] { @"E:\alt\harness-a", @"F:\alt\harness-b" }),
                @"E:\alt\harness-a");

            checks++;
            int headings = 0;
            int targets = 0;
            int actions = 0;
            bool selectedIsCurrent = false;
            bool defaultPresent = false;
            for (int i = 0; i < items.Count; i++)
            {
                TargetMenuItem item = items[i];
                if (item.Kind == TargetItemKind.Heading)
                {
                    headings++;
                }
                else if (item.Kind == TargetItemKind.Action)
                {
                    actions++;
                }
                else
                {
                    targets++;
                    if (item.Selected && item.Path == @"E:\alt\harness-a")
                    {
                        selectedIsCurrent = true;
                    }

                    if (item.IsDefault)
                    {
                        defaultPresent = true;
                    }
                }
            }

            if (headings != 2 || targets != 3 || actions != 1 || !selectedIsCurrent || !defaultPresent)
            {
                failures++;
                emit(string.Format(
                    CultureInfo.InvariantCulture,
                    "  [失败] 菜单结构不符：标题 {0}、目标 {1}、动作 {2}、选中当前 {3}、含默认 {4}",
                    headings, targets, actions, selectedIsCurrent, defaultPresent));
            }
            else
            {
                emit("  [通过] 菜单结构：2 个分组标题 + 默认目录 + 2 个最近使用 + 1 个浏览动作，当前目标已打勾");
            }

            using (TargetMenu menu = TargetMenu.CreateForCheck(theme, @"D:\default\deepseek-harness",
                new System.Collections.Generic.List<string>(new string[] { @"E:\alt\harness-a" }), @"E:\alt\harness-a"))
            {
                menu.CreateControl();
                menu.PerformLayout();

                checks++;
                using (Bitmap bitmap = new Bitmap(menu.Width, menu.Height))
                {
                    menu.DrawToBitmap(bitmap, new Rectangle(0, 0, menu.Width, menu.Height));
                    Color[] allowed = new Color[]
                    {
                        theme.MenuSurface, theme.MenuHover, theme.MenuBorder, theme.MenuSeparator,
                        theme.TextPrimary, theme.TextSecondary, theme.TextTertiary, theme.Accent,
                        SystemColors.Control,
                    };

                    int unexpected = 0;
                    string firstSample = null;
                    for (int y = 0; y < bitmap.Height && unexpected == 0; y++)
                    {
                        for (int x = 0; x < bitmap.Width; x++)
                        {
                            Color pixel = bitmap.GetPixel(x, y);
                            if (MatchesAny(pixel, allowed, 26.0))
                            {
                                continue;
                            }

                            unexpected++;
                            firstSample = string.Format(CultureInfo.InvariantCulture, "({0},{1}) {2}", x, y, Hex(pixel));
                            break;
                        }
                    }

                    if (unexpected > 0)
                    {
                        failures++;
                        emit(string.Format(
                            CultureInfo.InvariantCulture,
                            "  [失败] 菜单渲染出现异常像素：{0}（尺寸 {1}×{2}）",
                            firstSample, menu.Width, menu.Height));
                    }
                    else
                    {
                        emit(string.Format(
                            CultureInfo.InvariantCulture,
                            "  [通过] 菜单 {0}×{1} 渲染像素全部属于菜单配色",
                            menu.Width, menu.Height));
                    }
                }
            }
        }

        /// <summary>像素是否等于给定颜色之一，或落在任意两色的渐变之间（抗锯齿）。</summary>
        private static bool MatchesAny(Color pixel, Color[] palette, double tolerance)
        {
            for (int i = 0; i < palette.Length; i++)
            {
                if (PaintUtil.IsBlend(pixel, palette[i], palette[i], tolerance))
                {
                    return true;
                }
            }

            for (int i = 0; i < palette.Length; i++)
            {
                for (int j = i + 1; j < palette.Length; j++)
                {
                    if (PaintUtil.IsBlend(pixel, palette[i], palette[j], tolerance))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static string Describe(Control control)
        {
            TargetSelector selector = control as TargetSelector;
            if (selector != null)
            {
                return "目标目录选择器（" + selector.CurrentPath + "）";
            }

            AppButton button = control as AppButton;
            if (button != null)
            {
                return "按钮「" + button.Text + "」";
            }

            return control.GetType().Name;
        }

        private static string Corners(Bitmap bitmap)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0}/{1}/{2}/{3}",
                Hex(bitmap.GetPixel(0, 0)),
                Hex(bitmap.GetPixel(bitmap.Width - 1, 0)),
                Hex(bitmap.GetPixel(0, bitmap.Height - 1)),
                Hex(bitmap.GetPixel(bitmap.Width - 1, bitmap.Height - 1)));
        }

        /// <summary>
        /// 按钮改成自绘 Control 之后不再有 ButtonBase 的事件管线，
        /// 这里用「清空」按钮做一次真实点击：日志区被预填 → 点击 → 应变空，
        /// 以此证明 Click 事件确实接到了处理器。
        /// </summary>
        private static void CheckClickWiring(LauncherForm form, Action<string> emit, ref int failures, ref int checks)
        {
            checks++;
            AppButton clear = null;
            foreach (Control control in Walk(form))
            {
                AppButton button = control as AppButton;
                if (button != null && string.Equals(button.Text, "清空", StringComparison.Ordinal))
                {
                    clear = button;
                    break;
                }
            }

            if (clear == null)
            {
                failures++;
                emit("  [失败] 未找到「清空」按钮");
                return;
            }

            int seeded = form.SeedConsoleForCheck("click-check");
            if (seeded == 0)
            {
                failures++;
                emit("  [失败] 无法预填日志区");
                return;
            }

            clear.PerformClick();
            int remaining = form.ConsoleLength;
            if (remaining != 0)
            {
                failures++;
                emit(string.Format(CultureInfo.InvariantCulture, "  [失败] 点击「清空」未触发处理器，日志区仍有 {0} 字符", remaining));
                return;
            }

            emit("  [通过] 点击「清空」后日志区被清空，Click 事件链路正常");
        }

        private static bool IsExpected(Color pixel, Color fill, Color border, Color parentBackground, double tolerance)
        {
            if (PaintUtil.IsBlend(pixel, fill, parentBackground, tolerance))
            {
                return true;
            }

            if (PaintUtil.IsBlend(pixel, border, parentBackground, tolerance))
            {
                return true;
            }

            if (PaintUtil.IsBlend(pixel, border, fill, tolerance))
            {
                return true;
            }

            return PaintUtil.IsBlend(pixel, border, fill, tolerance);
        }

        private static string Hex(Color color)        {
            return string.Format(CultureInfo.InvariantCulture, "#{0:X2}{1:X2}{2:X2}", color.R, color.G, color.B);
        }

        private static IEnumerable<Control> Walk(Control root)
        {
            for (int i = 0; i < root.Controls.Count; i++)
            {
                Control child = root.Controls[i];
                yield return child;
                foreach (Control grandChild in Walk(child))
                {
                    yield return grandChild;
                }
            }
        }

        private static string Name(Control control)
        {
            string text = control.Text == null ? string.Empty : control.Text;
            if (text.Length > 18)
            {
                text = text.Substring(0, 18) + "…";
            }

            return control.GetType().Name + "(" + text + ")";
        }
    }
}
