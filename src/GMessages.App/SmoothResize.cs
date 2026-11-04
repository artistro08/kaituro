using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace GMessages.App;

/// <summary>
/// Keeps a WinUI window from flickering while it is resized or moved.
/// </summary>
/// <remarks>
/// The approach from microsoft-ui-xaml issue 5148: Windows' own background erase is skipped (DirectComposition draws
/// the window, so the erase only flashes the plain background), the window is double buffered by the system
/// (WS_EX_COMPOSITED) only while a size or move is under way, and once it ends the window lays itself out again after
/// a short pause, so the last frame of the drag settles. The subclass runs before WinUI's own handler.
/// </remarks>
/// <author>Devin Green (Artistro08)</author>
/// <seealso href="https://github.com/microsoft/microsoft-ui-xaml/issues/5148#issuecomment-4814421524"/>
internal sealed class SmoothResize
{
    // How Long After A Resize Ends The Window Lays Itself Out Again (From The Issue's Fix)
    private static readonly TimeSpan RelayoutDelay = TimeSpan.FromMilliseconds(60);

    private readonly Window _window;
    private readonly NativeMethods.SubclassProc _subclass;

    /// <summary>
    /// Starts smoothing the window's resizes; the window keeps this object, so the subclass lives as long as it does.
    /// </summary>
    /// <param name="window">The window.</param>
    /// <param name="hwnd">Its handle.</param>
    public SmoothResize(Window window, IntPtr hwnd)
    {
        ArgumentNullException.ThrowIfNull(window);

        _window   = window;
        _subclass = OnMessage;
        _ = NativeMethods.SetWindowSubclass(hwnd, _subclass, UIntPtr.Zero, UIntPtr.Zero);
    }

    private IntPtr OnMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr data)
    {
        switch (message)
        {
            // No Background Erase: DirectComposition Draws The Window
            case NativeMethods.WM_ERASEBKGND:
                return 1;

            // Double Buffered While The Drag Lasts
            case NativeMethods.WM_ENTERSIZEMOVE:
                SetComposited(hwnd, true);
                break;

            // Back To Normal Once It Ends, Then Laid Out Again After A Short Pause
            case NativeMethods.WM_EXITSIZEMOVE:
                SetComposited(hwnd, false);
                RelayoutSoon();
                break;
        }

        return NativeMethods.DefSubclassProc(hwnd, message, wParam, lParam);
    }

    private static void SetComposited(IntPtr hwnd, bool on)
    {
        long style = NativeMethods.GetWindowLongPtrW(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
        style      = on ? style | NativeMethods.WS_EX_COMPOSITED : style & ~NativeMethods.WS_EX_COMPOSITED;
        _ = NativeMethods.SetWindowLongPtrW(hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(style));
    }

    // A One-Shot Timer On The Window's Thread, So The Message Handler Returns At Once
    private void RelayoutSoon()
    {
        if (_window.Content is not FrameworkElement content)
        {
            return;
        }

        DispatcherQueueTimer timer = content.DispatcherQueue.CreateTimer();
        timer.Interval             = RelayoutDelay;
        timer.IsRepeating          = false;
        timer.Tick                += (_, _) => content.UpdateLayout();
        timer.Start();
    }
}
