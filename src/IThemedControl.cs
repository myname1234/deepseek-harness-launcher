using System.Drawing;

namespace DshLauncher
{
    /// <summary>
    /// 带状态配色的自绘控件（按钮、目标目录选择器）。
    /// 供 --ui-check 统一做「每种状态的前景/背景对比度」与「边框像素」检查。
    /// </summary>
    internal interface IThemedControl
    {
        /// <summary>当前视觉状态。</summary>
        ButtonVisualState CurrentState { get; }

        /// <summary>当前状态（供自检使用）。</summary>

        /// <summary>取出某状态下的（背景, 前景, 边框）。</summary>
        void ResolveColors(ButtonVisualState state, out Color background, out Color foreground, out Color border);
    }
}
