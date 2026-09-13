using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace DshLauncher
{
    /// <summary>菜单行类型：分组标题 / 可选目标 / 固定动作。</summary>
    internal enum TargetItemKind
    {
        Heading,
        Target,
        Action,
    }

    /// <summary>菜单里的一行。</summary>
    internal sealed class TargetMenuItem
    {
        internal TargetItemKind Kind;
        internal string Title;
        internal string Path;
        internal bool Selected;
        internal bool IsDefault;
    }

    /// <summary>
    /// 目标目录弹出菜单，按 DSH 的 Menu 规范实现：卡片带细边与圆角、
    /// 行内圆角悬停填充、文件夹图标 + 标题 + 次要路径、当前项右侧打勾、
    /// 分组标题，以及与底部固定动作之间的分隔线。
    /// 弹出是非模态的：结果通过回调返回。
    /// </summary>
    internal sealed class TargetMenu : Form
    {
        private const int CardPadding = 5;
        private const int ItemHeight = 32;
        private const int HeadingHeight = 22;
        private const int SeparatorHeight = 9;
        private const int IconSize = 16;

        private readonly UiTheme _theme;
        private readonly List<TargetMenuItem> _items;
        private readonly MenuListControl _list;

        private TargetMenu(UiTheme theme, List<TargetMenuItem> items, int width)
        {
            _theme = theme;
            _items = items;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = theme.MenuSurface;
            DoubleBuffered = true;
            AutoScaleMode = AutoScaleMode.None;

            _list = new MenuListControl(theme, items);
            _list.Dock = DockStyle.Fill;
            _list.Activated += OnItemActivated;
            _list.Cancelled += delegate { Close(); };

            Padding = new Padding(CardPadding);
            Controls.Add(_list);

            int contentHeight = MeasureHeight(items);
            ClientSize = new Size(Math.Max(width, 360), contentHeight + (CardPadding * 2));

            // 圆角用 Region 实现：无边框窗口的直角会露在应用背景上。
            using (GraphicsPath path = BorderPanel.RoundedRect(new Rectangle(0, 0, ClientSize.Width, ClientSize.Height), 10))
            {
                Region = new Region(path);
            }
        }

        /// <summary>按 DSH 的分组结构生成菜单行：默认目录 / 最近使用 /（分隔线）浏览其他目录…。</summary>
        internal static List<TargetMenuItem> BuildItems(string defaultPath, List<string> recent, string currentPath)
        {
            List<TargetMenuItem> items = new List<TargetMenuItem>();
            string current = HarnessTargets.Normalize(currentPath);

            TargetMenuItem heading = new TargetMenuItem();
            heading.Kind = TargetItemKind.Heading;
            heading.Title = "默认目录";
            items.Add(heading);
            items.Add(Target(defaultPath, current, true));

            if (recent != null && recent.Count > 0)
            {
                TargetMenuItem recentHeading = new TargetMenuItem();
                recentHeading.Kind = TargetItemKind.Heading;
                recentHeading.Title = "最近使用";
                items.Add(recentHeading);
                for (int i = 0; i < recent.Count; i++)
                {
                    items.Add(Target(recent[i], current, false));
                }
            }

            TargetMenuItem action = new TargetMenuItem();
            action.Kind = TargetItemKind.Action;
            action.Title = "浏览其他目录…";
            items.Add(action);
            return items;
        }

        private static TargetMenuItem Target(string path, string current, bool isDefault)
        {
            TargetMenuItem item = new TargetMenuItem();
            item.Kind = TargetItemKind.Target;
            item.Path = HarnessTargets.Normalize(path);
            item.Title = FolderName(item.Path);
            item.IsDefault = isDefault;
            item.Selected = string.Equals(item.Path, current, StringComparison.OrdinalIgnoreCase);
            return item;
        }

        private static string FolderName(string path)
        {
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

        private static int MeasureHeight(List<TargetMenuItem> items)
        {
            int height = 0;
            for (int i = 0; i < items.Count; i++)
            {
                height += HeightOf(items[i]);
            }

            return height;
        }

        private static int HeightOf(TargetMenuItem item)
        {
            switch (item.Kind)
            {
                case TargetItemKind.Heading:
                    return HeadingHeight;
                case TargetItemKind.Action:
                    return SeparatorHeight + ItemHeight;
                default:
                    return ItemHeight;
            }
        }

        /// <summary>
        /// 在锚点下方（空间不足时上方）弹出菜单。
        /// 选择目标时回调 <paramref name="onPicked"/>；选择「浏览其他目录…」时回调 <paramref name="onBrowse"/>。
        /// </summary>
        internal static TargetMenu OpenFor(
            IWin32Window owner,
            UiTheme theme,
            Control anchor,
            string defaultPath,
            List<string> recent,
            string currentPath,
            Action<string> onPicked,
            Action onBrowse)
        {
            List<TargetMenuItem> items = BuildItems(defaultPath, recent, currentPath);
            TargetMenu menu = new TargetMenu(theme, items, Math.Max(anchor.Width, 360));
            menu.Picked = onPicked;
            menu.BrowseRequested = onBrowse;

            Rectangle screen = Screen.FromControl(anchor).WorkingArea;
            Point origin = anchor.PointToScreen(new Point(0, 0));
            int left = Math.Min(origin.X, screen.Right - menu.Width);
            int top = origin.Y + anchor.Height + 4;
            if (top + menu.Height > screen.Bottom)
            {
                top = origin.Y - menu.Height - 4;
            }

            if (top < screen.Top)
            {
                top = screen.Top;
            }

            menu.Location = new Point(Math.Max(screen.Left, left), top);
            menu.Show(owner);
            menu.Activate();
            menu._list.Focus();
            return menu;
        }

        /// <summary>仅供 --ui-check 使用：构造菜单但不弹出，用于验证渲染。</summary>
        internal static TargetMenu CreateForCheck(UiTheme theme, string defaultPath, List<string> recent, string currentPath)
        {
            List<TargetMenuItem> items = BuildItems(defaultPath, recent, currentPath);
            return new TargetMenu(theme, items, 400);
        }

        /// <summary>仅供 --ui-check 使用：菜单里各行。</summary>
        internal List<TargetMenuItem> ItemsForCheck
        {
            get { return _items; }
        }
        /// <summary>选中某个目标时回调（路径）。</summary>
        internal Action<string> Picked;

        /// <summary>选择「浏览其他目录…」时回调。</summary>
        internal Action BrowseRequested;

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = BorderPanel.RoundedRect(rect, 10))
            {
                using (SolidBrush brush = new SolidBrush(_theme.MenuSurface))
                {
                    g.FillPath(brush, path);
                }

                using (Pen pen = new Pen(_theme.MenuBorder, 1F))
                {
                    g.DrawPath(pen, path);
                }
            }
        }

        private void OnItemActivated(TargetMenuItem item)
        {
            if (item.Kind == TargetItemKind.Action)
            {
                Action browse = BrowseRequested;
                Close();
                if (browse != null)
                {
                    browse();
                }

                return;
            }

            Action<string> picked = Picked;
            Close();
            if (picked != null)
            {
                picked(item.Path);
            }
        }

        /// <summary>点击别处（或窗口失去激活）时收起菜单。</summary>
        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            Close();
        }

        protected override bool ProcessDialogKey(Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                Close();
                return true;
            }

            return base.ProcessDialogKey(keyData);
        }

        /// <summary>菜单内容：逐行自绘并处理悬停 / 键盘选中 / 点击。</summary>
        private sealed class MenuListControl : Control
        {
            private readonly UiTheme _theme;
            private readonly List<TargetMenuItem> _items;
            private readonly List<int> _offsets = new List<int>();
            private int _hover = -1;
            private int _keyboard = -1;

            internal MenuListControl(UiTheme theme, List<TargetMenuItem> items)
            {
                _theme = theme;
                _items = items;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                    | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
                TabStop = true;
                BackColor = theme.MenuSurface;

                int y = 0;
                for (int i = 0; i < items.Count; i++)
                {
                    _offsets.Add(y);
                    y += HeightOf(items[i]);
                    if (items[i].Selected)
                    {
                        _keyboard = i;
                    }
                }
            }

            internal event Action<TargetMenuItem> Activated;

            internal event Action Cancelled;

            internal void MoveSelection(int delta)
            {
                int index = _keyboard;
                for (int step = 0; step < _items.Count; step++)
                {
                    index += delta;
                    if (index < 0)
                    {
                        index = _items.Count - 1;
                    }

                    if (index >= _items.Count)
                    {
                        index = 0;
                    }

                    if (_items[index].Kind != TargetItemKind.Heading)
                    {
                        _keyboard = index;
                        Invalidate();
                        return;
                    }
                }
            }

            internal void ActivateSelection()
            {
                if (_keyboard >= 0 && _keyboard < _items.Count)
                {
                    Action<TargetMenuItem> handler = Activated;
                    if (handler != null)
                    {
                        handler(_items[_keyboard]);
                    }
                }
            }

            protected override bool IsInputKey(Keys keyData)
            {
                if (keyData == Keys.Up || keyData == Keys.Down || keyData == Keys.Enter)
                {
                    return true;
                }

                return base.IsInputKey(keyData);
            }

            protected override void OnKeyDown(KeyEventArgs e)
            {
                base.OnKeyDown(e);
                if (e.KeyCode == Keys.Down)
                {
                    MoveSelection(1);
                    e.Handled = true;
                }
                else if (e.KeyCode == Keys.Up)
                {
                    MoveSelection(-1);
                    e.Handled = true;
                }
                else if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space)
                {
                    ActivateSelection();
                    e.Handled = true;
                }
                else if (e.KeyCode == Keys.Escape)
                {
                    Action cancel = Cancelled;
                    if (cancel != null)
                    {
                        cancel();
                    }

                    e.Handled = true;
                }
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                int index = HitTest(e.Y);
                if (index != _hover)
                {
                    _hover = index;
                    Invalidate();
                }
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                base.OnMouseLeave(e);
                _hover = -1;
                Invalidate();
            }

            protected override void OnMouseUp(MouseEventArgs e)
            {
                base.OnMouseUp(e);
                int index = HitTest(e.Y);
                if (index >= 0)
                {
                    Action<TargetMenuItem> handler = Activated;
                    if (handler != null)
                    {
                        handler(_items[index]);
                    }
                }
            }

            private int HitTest(int y)
            {
                for (int i = 0; i < _items.Count; i++)
                {
                    if (_items[i].Kind == TargetItemKind.Heading)
                    {
                        continue;
                    }

                    int top = _offsets[i];
                    int height = HeightOf(_items[i]);
                    if (_items[i].Kind == TargetItemKind.Action)
                    {
                        top += SeparatorHeight;
                        height -= SeparatorHeight;
                    }

                    if (y >= top && y < top + height)
                    {
                        return i;
                    }
                }

                return -1;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                PaintUtil.ClearToParentBackground(this, g);

                for (int i = 0; i < _items.Count; i++)
                {
                    TargetMenuItem item = _items[i];
                    int top = _offsets[i];

                    if (item.Kind == TargetItemKind.Heading)
                    {
                        Rectangle heading = new Rectangle(9, top, Math.Max(1, Width - 18), HeadingHeight);
                        TextRenderer.DrawText(
                            g, item.Title, _theme.PillFont, heading, _theme.TextTertiary,
                            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                        continue;
                    }

                    int rowTop = top;
                    if (item.Kind == TargetItemKind.Action)
                    {
                        // 固定动作与上方列表之间的细分隔线（对应 DSH 的 footer 上边框）。
                        using (Pen pen = new Pen(_theme.MenuSeparator, 1F))
                        {
                            int line = top + (SeparatorHeight / 2);
                            g.DrawLine(pen, 6, line, Width - 6, line);
                        }

                        rowTop += SeparatorHeight;
                    }

                    // 悬停高亮：鼠标优先，键盘选中在鼠标不在列表时生效。
                    bool highlight = i == _hover || (_hover < 0 && i == _keyboard);
                    Rectangle row = new Rectangle(2, rowTop, Math.Max(1, Width - 4), ItemHeight - 2);
                    if (highlight)
                    {
                        using (GraphicsPath path = BorderPanel.RoundedRect(row, 6))
                        using (SolidBrush brush = new SolidBrush(_theme.MenuHover))
                        {
                            g.FillPath(brush, path);
                        }
                    }

                    int iconX = row.Left + 8;
                    Rectangle icon = new Rectangle(iconX, row.Top + ((row.Height - IconSize) / 2), IconSize, IconSize);
                    if (item.Kind == TargetItemKind.Action)
                    {
                        Glyphs.DrawPlus(g, icon, _theme.TextTertiary);
                    }
                    else
                    {
                        Glyphs.DrawFolder(g, icon, _theme.TextTertiary);
                    }

                    int textLeft = iconX + IconSize + 8;
                    int textRight = row.Right - 10;
                    int checkWidth = item.Selected ? 18 : 0;
                    Size titleSize = TextRenderer.MeasureText(item.Title, _theme.BodyFont);
                    Rectangle titleRect = new Rectangle(
                        textLeft, row.Top, Math.Max(1, textRight - textLeft - checkWidth), row.Height);
                    TextRenderer.DrawText(
                        g, item.Title, _theme.BodyFont, titleRect, _theme.TextPrimary,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding
                        | TextFormatFlags.EndEllipsis);

                    if (item.Kind == TargetItemKind.Target)
                    {
                        int pathLeft = textLeft + titleSize.Width + 10;
                        int pathWidth = textRight - pathLeft - checkWidth;
                        if (pathWidth > 60)
                        {
                            Rectangle pathRect = new Rectangle(pathLeft, row.Top, pathWidth, row.Height);
                            string pathText = HarnessTargets.Shorten(item.Path, Math.Max(12, pathWidth / 6));
                            TextRenderer.DrawText(
                                g, pathText, _theme.MetaFont, pathRect, _theme.TextSecondary,
                                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding
                                | TextFormatFlags.EndEllipsis);
                        }
                    }

                    if (item.Selected)
                    {
                        Rectangle check = new Rectangle(row.Right - 22, row.Top + ((row.Height - 14) / 2), 14, 14);
                        Glyphs.DrawCheck(g, check, _theme.Accent);
                    }
                }
            }
        }
    }
}
