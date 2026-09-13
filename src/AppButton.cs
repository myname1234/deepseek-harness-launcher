using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DshLauncher
{
    /// <summary>按钮角色：强调色主按钮 / 次要幽灵按钮。</summary>
    internal enum ButtonRole
    {
        Primary,
        Ghost,
    }

    /// <summary>按钮的视觉状态，用于取出该状态的前景/背景/边框色。</summary>
    internal enum ButtonVisualState
    {
        Normal,
        Hover,
        Pressed,
        Disabled,
    }

    /// <summary>
    /// 自绘按钮。
    ///
    /// 从 <see cref="Control"/> 而不是 <see cref="Button"/> 派生：ButtonBase 除了
    /// OnPaint 之外还会在消息处理里画自己的平面按钮外框，即使设置了 UserPaint，
    /// 按钮四周仍会留一圈深灰描边（看起来像黑色阴影）。普通 Control 没有任何自带
    /// 外观，四种状态完全由这里绘制。
    /// </summary>
    internal sealed class AppButton : Control, IThemedControl
    {
        private const int CornerRadius = 6;

        private readonly UiTheme _theme;
        private readonly ButtonRole _role;
        private bool _hover;
        private bool _pressed;

        internal AppButton(UiTheme theme, string text, ButtonRole role)
        {
            _theme = theme;
            _role = role;

            Text = text;
            Font = theme.ButtonFont;
            Padding = new Padding(12, 3, 12, 3);
            Margin = new Padding(0, 0, 6, 0);
            TabStop = true;
            Cursor = Cursors.Hand;
            AccessibleRole = AccessibleRole.PushButton;

            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
                | ControlStyles.Selectable | ControlStyles.StandardClick, true);
            MinimumSize = new Size(0, 28);
            ApplyPreferredSize();
        }

        /// <summary>
        /// 按文本尺寸调整自身大小。普通 Control 没有 AutoSizeMode，
        /// 这里在文本 / 字体 / 内边距变化时显式重算，效果与自适应用户控件一致。
        /// </summary>
        private void ApplyPreferredSize()
        {
            Size preferred = GetPreferredSize(Size.Empty);
            if (preferred.Width < MinimumSize.Width)
            {
                preferred.Width = MinimumSize.Width;
            }

            if (preferred.Height < MinimumSize.Height)
            {
                preferred.Height = MinimumSize.Height;
            }

            Size = preferred;
        }

        internal ButtonRole Role
        {
            get { return _role; }
        }

        /// <summary>取出某个视觉状态下的（背景, 前景, 边框）。</summary>
        public void ResolveColors(ButtonVisualState state, out Color background, out Color foreground, out Color border)
        {
            if (_role == ButtonRole.Primary)
            {
                switch (state)
                {
                    case ButtonVisualState.Hover:
                        background = _theme.AccentHover;
                        border = _theme.AccentHover;
                        break;
                    case ButtonVisualState.Pressed:
                        background = _theme.AccentPressed;
                        border = _theme.AccentPressed;
                        break;
                    case ButtonVisualState.Disabled:
                        background = _theme.AccentDisabled;
                        border = _theme.AccentDisabled;
                        break;
                    default:
                        background = _theme.Accent;
                        border = _theme.Accent;
                        break;
                }

                foreground = state == ButtonVisualState.Disabled ? _theme.AccentDisabledText : _theme.AccentText;
                return;
            }

            switch (state)
            {
                case ButtonVisualState.Hover:
                    background = _theme.GhostHover;
                    break;
                case ButtonVisualState.Pressed:
                    background = _theme.GhostPressed;
                    break;
                default:
                    background = _theme.GhostBackground;
                    break;
            }

            foreground = state == ButtonVisualState.Disabled ? _theme.GhostDisabledText : _theme.GhostText;
            border = _theme.GhostBorder;
        }

        /// <summary>当前状态（供自检使用）。</summary>
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

        /// <summary>按文本与内边距计算尺寸，任何字号 / DPI 下都不会截断文字。</summary>
        public override Size GetPreferredSize(Size proposedSize)
        {
            Size text = TextRenderer.MeasureText(Text == null ? string.Empty : Text, Font);
            return new Size(text.Width + Padding.Horizontal, text.Height + Padding.Vertical);
        }

        /// <summary>以编程方式触发一次点击（键盘激活也走这里）。</summary>
        internal void PerformClick()
        {
            if (Enabled)
            {
                OnClick(EventArgs.Empty);
            }
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

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter)
            {
                _pressed = true;
                Invalidate();
                e.Handled = true;
            }
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            base.OnKeyUp(e);
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter)
            {
                bool wasPressed = _pressed;
                _pressed = false;
                Invalidate();
                if (wasPressed)
                {
                    PerformClick();
                }

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
            _hover = false;
            _pressed = false;
            Cursor = Enabled ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            ApplyPreferredSize();
            PerformLayout();
            Invalidate();
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            ApplyPreferredSize();
            PerformLayout();
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

            // 先铺父容器底色：圆角之外与最后一列/行必须被明确绘制，否则残留黑边。
            PaintUtil.ClearToParentBackground(this, g);

            // 填充覆盖整个客户区，描边内缩 1px 落在控件内。
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

            Rectangle textRect = new Rectangle(Padding.Left, 0, Math.Max(1, Width - Padding.Horizontal), Height);
            TextRenderer.DrawText(
                g,
                Text,
                Font,
                textRect,
                foreground,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

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
    }
}
