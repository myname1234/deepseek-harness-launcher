using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DshLauncher
{
    /// <summary>圆角卡片面板：填充卡片底色并描一圈细边框。</summary>
    internal sealed class BorderPanel : Panel
    {
        private int _radius = 10;
        private Color _borderColor = Color.Gray;
        private Color _fillColor = Color.Empty;

        internal BorderPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        internal int Radius
        {
            get { return _radius; }
            set { _radius = value; Invalidate(); }
        }

        internal Color BorderColor
        {
            get { return _borderColor; }
            set { _borderColor = value; Invalidate(); }
        }

        /// <summary>卡片填充色；留空则使用 BackColor。</summary>
        internal Color FillColor
        {
            get { return _fillColor; }
            set { _fillColor = value; Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // 父容器底色先铺满：圆角之外与最后一列/行不能留未绘制的像素（会显示成黑边）。
            PaintUtil.ClearToParentBackground(this, g);

            Color fill = _fillColor.IsEmpty ? BackColor : _fillColor;
            using (GraphicsPath fillPath = RoundedRect(new Rectangle(0, 0, Width, Height), _radius))
            using (SolidBrush brush = new SolidBrush(fill))
            {
                g.FillPath(brush, fillPath);
            }

            using (GraphicsPath borderPath = RoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), _radius))
            using (Pen pen = new Pen(_borderColor, 1F))
            {
                g.DrawPath(pen, borderPath);
            }
        }

        /// <summary>生成圆角矩形路径。</summary>
        internal static GraphicsPath RoundedRect(Rectangle bounds, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int d = Math.Max(2, Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height)));
            if (d <= 2)
            {
                path.AddRectangle(bounds);
                return path;
            }

            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
