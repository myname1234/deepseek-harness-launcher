using System;
using System.Drawing;
using System.Windows.Forms;

namespace DshLauncher
{
    /// <summary>自绘控件共用的绘制辅助。</summary>
    internal static class PaintUtil
    {
        /// <summary>
        /// 用父容器的底色铺满整个客户区。
        ///
        /// 自绘控件（UserPaint）不会自动擦除背景，圆角路径又只覆盖到
        /// (Width-1, Height-1)，如果先不铺底，圆角之外以及最右一列/最下一行
        /// 就会保留未绘制的内容，看起来像一条黑边（阴影）。
        /// </summary>
        internal static void ClearToParentBackground(Control control, Graphics graphics)
        {
            Color color = Color.Empty;
            if (control.Parent != null && control.Parent.BackColor.A == 255)
            {
                color = control.Parent.BackColor;
            }

            if (color.IsEmpty)
            {
                color = control.BackColor;
            }

            if (color.A < 255)
            {
                color = SystemColors.Control;
            }

            using (SolidBrush brush = new SolidBrush(color))
            {
                graphics.FillRectangle(brush, control.ClientRectangle);
            }
        }

        /// <summary>判断颜色是否落在两种颜色之间的渐变上（用于容忍抗锯齿像素）。</summary>
        internal static bool IsBlend(Color pixel, Color from, Color to, double tolerance)
        {
            double ax = from.R;
            double ay = from.G;
            double az = from.B;
            double bx = to.R - ax;
            double by = to.G - ay;
            double bz = to.B - az;
            double lengthSquared = (bx * bx) + (by * by) + (bz * bz);
            if (lengthSquared < 0.0001)
            {
                return Distance(pixel, from) <= tolerance;
            }

            double px = pixel.R - ax;
            double py = pixel.G - ay;
            double pz = pixel.B - az;
            double t = ((px * bx) + (py * by) + (pz * bz)) / lengthSquared;
            if (t < 0)
            {
                t = 0;
            }

            if (t > 1)
            {
                t = 1;
            }

            double dx = px - (t * bx);
            double dy = py - (t * by);
            double dz = pz - (t * bz);
            return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz)) <= tolerance;
        }

        private static double Distance(Color a, Color b)
        {
            double dr = a.R - b.R;
            double dg = a.G - b.G;
            double db = a.B - b.B;
            return Math.Sqrt((dr * dr) + (dg * dg) + (db * db));
        }
    }
}
