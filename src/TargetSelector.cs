using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace DshLauncher
{
    /// <summary>
    /// 目标源码目录选择器（锚点字段）。外观对应 DSH 的 workspace 选择入口：
    /// 圆角字段 + 文件夹图标 + 目标名 + 次要路径 + 右侧折角箭头，
    /// 点击弹出 <see cref="TargetMenu"/> 菜单。
    /// </summary>
    internal sealed class TargetSelector : Control, IThemedControl
    {
        private const int CornerRadius = 8;
        private const int IconSize = 16;
        private const int PadX = 10;

        private readonly UiTheme _theme;
        private bool _hover;
        private bool _pressed;
        private TargetMenu _openMenu;
        private DateTime _menuClosedAt = DateTime.MinValue;
        private string _defaultPath = string.Empty;
        private string _currentPath = string.Empty;
        private System.Collections.Generic.List<string> _recent = new System.Collections.Generic.List<string>();

        internal TargetSelector(UiTheme theme)
        {
            _theme = theme;
            Font = theme.BodyFont;
            Height = 30;
            TabStop = true;
            Cursor = Cursors.Hand;
            AccessibleRole = AccessibleRole.ComboBox;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
                | ControlStyles.Selectable | ControlStyles.StandardClick, true);
        }

        /// <summary>选择某个目标目录时触发。</summary>
        internal event Action<string> TargetPicked;

        /// <summary>选择「浏览其他目录…」时触发。</summary>
        internal event Action BrowseRequested;

        /// <summary>设置当前目标、默认目录与最近使用列表。</summary>
        internal void SetTargets(string defaultPath, string currentPath, System.Collections.Generic.List<string> recent)
        {
            _defaultPath = defaultPath ?? string.Empty;
            _currentPath = currentPath ?? string.Empty;
            _recent = recent ?? new System.Collections.Generic.List<string>();
            AccessibleName = "目标源码目录：" + _currentPath;
            Invalidate();
        }

        /// <summary>当前显示的目标（供自检读取）。</summary>
        internal string CurrentPath
        {
            get { return _currentPath; }
        }

        public ButtonVisualState CurrentState
        {
            get
            {
                if (!Enabled)
                {
                    return ButtonVisualState.Disabled;
                }

                if (_pressed)
                {
                    return ButtonVisualState.Pressed;
                }

                return _hover ? ButtonVisualState.Hover : ButtonVisualState.Normal;
            }
        }

        /// <summary>字段本身的配色（前景即主标题色；次要路径用 TextSecondary）。</summary>
        public void ResolveColors(ButtonVisualState state, out Color background, out Color foreground, out Color border)
        {
            switch (state)
            {
                case ButtonVisualState.Hover:
                    background = _theme.SelectorHover;
                    border = _theme.BorderStrong;
                    break;
                case ButtonVisualState.Pressed:
                    background = _theme.SelectorPressed;
                    border = _theme.BorderStrong;
                    break;
                case ButtonVisualState.Disabled:
                    background = _theme.SelectorFill;
                    border = _theme.SelectorBorder;
                    break;
                default:
                    background = _theme.SelectorFill;
                    border = _theme.SelectorBorder;
                    break;
            }

            foreground = state == ButtonVisualState.Disabled ? _theme.GhostDisabledText : _theme.TextPrimary;
        }

        /// <summary>字段里次要文字（路径）用的颜色，供自检校验对比度。</summary>
        internal Color SecondaryTextColor
        {
            get { return _theme.TextSecondary; }
        }

        internal void OpenMenu()
        {
            if (_openMenu != null && !_openMenu.IsDisposed)
            {
                _openMenu.Close();
                return;
            }

            // 点到锚点本身会先让菜单失活关闭；这次点击不应立刻又把它打开。
            if ((DateTime.UtcNow - _menuClosedAt).TotalMilliseconds < 300)
            {
                return;
            }

            _openMenu = TargetMenu.OpenFor(
                FindForm(),
                _theme,
                this,
                _defaultPath,
                _recent,
                _currentPath,
                delegate(string path)
                {
                    Action<string> picked = TargetPicked;
                    if (picked != null)
                    {
                        picked(path);
                    }
                },
                delegate
                {
                    Action browse = BrowseRequested;
                    if (browse != null)
                    {
                        browse();
                    }
                });

            _openMenu.FormClosed += delegate
            {
                _openMenu = null;
                _menuClosedAt = DateTime.UtcNow;
                Invalidate();
            };
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hover = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = false;
            _pressed = false;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs mevent)
        {
            base.OnMouseDown(mevent);
            if (mevent.Button == MouseButtons.Left)
            {
                _pressed = true;
                Focus();
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs mevent)
        {
            base.OnMouseUp(mevent);
            _pressed = false;
            Invalidate();
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            OpenMenu();
        }

        protected override bool IsInputKey(Keys keyData)
        {
            if (keyData == Keys.Space || keyData == Keys.Enter)
            {
                return true;
            }

            return base.IsInputKey(keyData);
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            base.OnKeyUp(e);
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter)
            {
                OpenMenu();
                e.Handled = true;
            }
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            Invalidate();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            _pressed = false;
            Invalidate();
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            Cursor = Enabled ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            Color background;
            Color foreground;
            Color border;
            ResolveColors(CurrentState, out background, out foreground, out border);

            Graphics g = pevent.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            PaintUtil.ClearToParentBackground(this, g);

            using (GraphicsPath fillPath = BorderPanel.RoundedRect(new Rectangle(0, 0, Width, Height), CornerRadius))
            using (SolidBrush brush = new SolidBrush(background))
            {
                g.FillPath(brush, fillPath);
            }

            using (GraphicsPath borderPath = BorderPanel.RoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), CornerRadius))
            using (Pen pen = new Pen(border, 1F))
            {
                g.DrawPath(pen, borderPath);
            }

            Rectangle icon = new Rectangle(PadX, (Height - IconSize) / 2, IconSize, IconSize);
            Glyphs.DrawFolder(g, icon, _theme.TextTertiary);

            string name = FolderName(_currentPath);
            Size nameSize = TextRenderer.MeasureText(name, Font);
            int left = icon.Right + 8;
            int chevronWidth = 18;
            int right = Width - PadX - chevronWidth - 4;

            Rectangle nameRect = new Rectangle(left, 0, Math.Max(1, right - left), Height);
            TextRenderer.DrawText(
                g, name, Font, nameRect, foreground,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding
                | TextFormatFlags.EndEllipsis);

            // 目标名之后的次要路径；空间不够就整段省略，鼠标悬停有完整提示。
            int pathLeft = left + nameSize.Width + 10;
            int pathWidth = right - pathLeft;
            if (pathWidth > 60)
            {
                Rectangle pathRect = new Rectangle(pathLeft, 0, pathWidth, Height);
                string path = HarnessTargets.Shorten(_currentPath, Math.Max(12, pathWidth / 6));
                TextRenderer.DrawText(
                    g, path, _theme.MetaFont, pathRect, _theme.TextSecondary,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding
                    | TextFormatFlags.EndEllipsis);
            }

            Rectangle chevron = new Rectangle(Width - PadX - 12, (Height - 12) / 2, 12, 12);
            Glyphs.DrawChevron(g, chevron, _theme.TextTertiary, _openMenu != null && !_openMenu.IsDisposed);

            if (Focused && Enabled)
            {
                Rectangle focus = new Rectangle(2, 2, Math.Max(1, Width - 5), Math.Max(1, Height - 5));
                using (GraphicsPath focusPath = BorderPanel.RoundedRect(focus, CornerRadius - 2))
                using (Pen pen = new Pen(foreground, 1F))
                {
                    pen.DashStyle = DashStyle.Dot;
                    g.DrawPath(pen, focusPath);
                }
            }
        }

        private static string FolderName(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return "（未指定）";
            }

            try
            {
                string name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                return name.Length == 0 ? path : name;
            }
            catch (ArgumentException)
            {
                return path;
            }
        }
    }
}
