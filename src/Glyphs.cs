using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DshLauncher
{
    /// <summary>界面里用到的少量矢量小图标（避免引入图片资源，且随主题着色）。</summary>
    internal static class Glyphs
    {
        /// <summary>文件夹图标：后板 + 前板两个圆角矩形。</summary>
        internal static void DrawFolder(Graphics g, Rectangle bounds, Color color)
        {
            float w = bounds.Width;
            float h = bounds.Height;
            float x = bounds.X;
            float y = bounds.Y;
            using (SolidBrush brush = new SolidBrush(color))
            {
                // 后板（带标签页的轮廓）
                RectangleF back = new RectangleF(x + (w * 0.06f), y + (h * 0.18f), w * 0.52f, h * 0.28f);
                g.FillRectangle(brush, back);
                // 前板
                RectangleF front = new RectangleF(x + (w * 0.04f), y + (h * 0.32f), w * 0.92f, h * 0.52f);
                using (GraphicsPath path = Rounded(front, Math.Max(1.5f, h * 0.14f)))
                {
                    g.FillPath(brush, path);
                }
            }
        }

        /// <summary>向下/向上的折角箭头。</summary>
        internal static void DrawChevron(Graphics g, Rectangle bounds, Color color, bool up)
        {
            float w = bounds.Width;
            float h = bounds.Height;
            float cx = bounds.X + (w / 2f);
            float cy = bounds.Y + (h / 2f);
            float dx = w * 0.26f;
            float dy = h * 0.16f;
            using (Pen pen = new Pen(color, Math.Max(1.2f, h * 0.11f)))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;
                if (up)
                {
                    g.DrawLines(pen, new PointF[]
                    {
                        new PointF(cx - dx, cy + dy),
                        new PointF(cx, cy - dy),
                        new PointF(cx + dx, cy + dy),
                    });
                }
                else
                {
                    g.DrawLines(pen, new PointF[]
                    {
                        new PointF(cx - dx, cy - dy),
                        new PointF(cx, cy + dy),
                        new PointF(cx + dx, cy - dy),
                    });
                }
            }
        }

        /// <summary>加号（用于「浏览其他目录…」这类新增动作）。</summary>
        internal static void DrawPlus(Graphics g, Rectangle bounds, Color color)
        {
            float w = bounds.Width;
            float h = bounds.Height;
            float cx = bounds.X + (w / 2f);
            float cy = bounds.Y + (h / 2f);
            float arm = (w * 0.32f);
            using (Pen pen = new Pen(color, Math.Max(1.2f, h * 0.11f)))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                g.DrawLine(pen, cx - arm, cy, cx + arm, cy);
                g.DrawLine(pen, cx, cy - arm, cx, cy + arm);
            }
        }

        /// <summary>勾选标记（用于菜单里标记当前目标）。</summary>
        internal static void DrawCheck(Graphics g, Rectangle bounds, Color color)
        {
            float w = bounds.Width;
            float h = bounds.Height;
            float x = bounds.X;
            float y = bounds.Y;
            using (Pen pen = new Pen(color, Math.Max(1.4f, h * 0.13f)))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;
                g.DrawLines(pen, new PointF[]
                {
                    new PointF(x + (w * 0.16f), y + (h * 0.52f)),
                    new PointF(x + (w * 0.42f), y + (h * 0.78f)),
                    new PointF(x + (w * 0.86f), y + (h * 0.24f)),
                });
            }
        }

        private static GraphicsPath Rounded(RectangleF bounds, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            float d = Math.Max(1f, Math.Min(radius * 2f, Math.Min(bounds.Width, bounds.Height)));
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
