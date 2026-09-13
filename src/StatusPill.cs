using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DshLauncher
{
    /// <summary>
    /// 状态徽标：圆点 + 文本的胶囊形状，尺寸由文本测量得出，因此任何字号下都不会被裁剪。
    /// 对应 DSH Web 界面里的状态标签样式（state-*-tertiary 底色 + 对应前景色）。
    /// </summary>
    internal sealed class StatusPill : Control
    {
        private const int DotSize = 7;
        private const int PadX = 10;
        private const int PadY = 5;
        private const int Gap = 6;

        private Color _fill = Color.Gray;
        private Color _dot = Color.Gray;
        private Color _textColor = Color.Black;

        /// <summary>当前胶囊底色（供 --ui-check 校验对比度）。</summary>
        internal Color FillColor
        {
            get { return _fill; }
        }

        /// <summary>当前文字色（供 --ui-check 校验对比度）。</summary>
        internal Color TextColor
        {
            get { return _textColor; }
        }

        internal StatusPill()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            AutoSize = true;
            Font = SystemFonts.DefaultFont;
        }

        /// <summary>设置徽标内容与配色。</summary>
        internal void Apply(string text, Color dot, Color fill, Color textColor)
        {
            Text = text;
            _dot = dot;
            _fill = fill;
            _textColor = textColor;
            Size preferred = GetPreferredSize(Size.Empty);
            Size = preferred;
            Invalidate();
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            Size textSize = TextRenderer.MeasureText(Text == null ? string.Empty : Text, Font);
            return new Size(PadX + DotSize + Gap + textSize.Width + PadX, textSize.Height + (PadY * 2));
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            Size = GetPreferredSize(Size.Empty);
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            Size = GetPreferredSize(Size.Empty);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            PaintUtil.ClearToParentBackground(this, g);

            using (GraphicsPath fillPath = BorderPanel.RoundedRect(new Rectangle(0, 0, Width, Height), Height / 2))
            using (SolidBrush brush = new SolidBrush(_fill))
            {
                g.FillPath(brush, fillPath);
            }

            int dotY = (Height - DotSize) / 2;
            using (SolidBrush dotBrush = new SolidBrush(_dot))
            {
                g.FillEllipse(dotBrush, PadX, dotY, DotSize, DotSize);
            }

            Rectangle textRect = new Rectangle(PadX + DotSize + Gap, 0, Width - PadX - DotSize - Gap - PadX, Height);
            TextRenderer.DrawText(
                g,
                Text == null ? string.Empty : Text,
                Font,
                textRect,
                _textColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        }
    }
}
