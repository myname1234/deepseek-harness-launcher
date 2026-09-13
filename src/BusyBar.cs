using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DshLauncher
{
    /// <summary>
    /// 头部那条细进度指示：空闲时只显示轨道，运行中有一段强调色来回滑动。
    /// 自绘以便与 DSH 的圆角与配色一致（系统 ProgressBar 无法着色）。
    /// </summary>
    internal sealed class BusyBar : Control
    {
        private readonly Timer _timer;
        private int _offset;
        private bool _active;

        internal Color TrackColor = Color.Gainsboro;
        internal Color BarColor = Color.SteelBlue;

        internal BusyBar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Height = 4;
            TabStop = false;
            _timer = new Timer();
            _timer.Interval = 22;
            _timer.Tick += OnTick;
        }

        /// <summary>切换运行状态；停止时把指示段复位。</summary>
        internal void SetActive(bool active)
        {
            if (_active == active)
            {
                return;
            }

            _active = active;
            if (!active)
            {
                _offset = 0;
            }

            _timer.Enabled = active && Visible;
            Invalidate();
        }

        private void OnTick(object sender, EventArgs e)
        {
            int span = Width + 120;
            _offset += 14;
            if (_offset > span)
            {
                _offset = -120;
            }

            Invalidate();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            _timer.Enabled = _active && Visible;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // 先铺底，避免轨道右/下 1px 残留未绘制像素。
            PaintUtil.ClearToParentBackground(this, g);

            int height = Math.Max(3, Height);
            Rectangle track = new Rectangle(0, 0, Math.Max(0, Width), height - 1);
            using (GraphicsPath path = BorderPanel.RoundedRect(track, height / 2))
            {
                using (SolidBrush brush = new SolidBrush(TrackColor))
                {
                    g.FillPath(brush, path);
                }

                if (_active && Width > 40)
                {
                    int thumbWidth = Math.Max(60, Width / 5);
                    int thumbX = _offset - thumbWidth;
                    int left = Math.Max(0, thumbX);
                    int right = Math.Min(Width, thumbX + thumbWidth);
                    if (right > left)
                    {
                        Rectangle thumb = new Rectangle(left, 0, right - left, height - 1);
                        using (GraphicsPath thumbPath = BorderPanel.RoundedRect(thumb, height / 2))
                        using (SolidBrush brush = new SolidBrush(BarColor))
                        {
                            g.FillPath(brush, thumbPath);
                        }
                    }
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _timer.Tick -= OnTick;
                _timer.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
