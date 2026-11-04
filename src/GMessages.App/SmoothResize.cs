using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace GMessages.App;

/// <summary>
/// Keeps a WinUI window from flickering while it is resized or moved by dragging.
/// </summary>
/// <remarks>
/// The approach from microsoft-ui-xaml issue 5148, for the length of a drag (between WM_ENTERSIZEMOVE and
/// WM_EXITSIZEMOVE): Windows' own background erase is skipped (DirectComposition draws the window, so the erase only
/// flashes the plain background), and the window is double buffered by the system (WS_EX_COMPOSITED, put back to what
/// it was when the drag ends). Once it ends, the window lays itself out again after a short pause, so the last frame
/// settles. Maximize, restore and snap layouts don't drag, so they're left as they are. The subclass runs before
/// WinUI's own handler and removes itself when the window is destroyed.
/// </remarks>
/// <author>Devin Green (Artistro08)</author>
/// <seealso href="https://github.com/microsoft/microsoft-ui-xaml/issues/5148#issuecomment-4814421524"/>
internal sealed class SmoothResize
{
    // How Long After A Drag Ends The Window Lays Itself Out Again (From The Issue's Fix)
    private static readonly TimeSpan RelayoutDelay = TimeSpan.FromMilliseconds(60);

    private readonly Window _window;
    private readonly NativeMethods.SubclassProc _subclass;
    private readonly DispatcherQueueTimer _relayout;

    // Whether A Drag Is Under Way, And Whether The Window Was Already Double Buffered When It Started
    private bool _dragging;
    private bool _wasComposited;

    /// <summary>
    /// Starts smoothing the window's resizes; the window keeps this object, so the subclass lives as long as it does.
    /// </summary>
    /// <param name="window">The window.</param>
    public SmoothResize(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        _window   = window;
        _subclass = OnMessage;

        // One Timer, Restarted By Each Drag, So Quick Drags Lay Out Once
        _relayout             = window.DispatcherQueue.CreateTimer();
        _relayout.Interval    = RelayoutDelay;
        _relayout.IsRepeating = false;
        _relayout.Tick       += (_, _) => Relayout();

        IntPtr hwnd = Microsoft.UI.Win32Interop.GetWindowFromWindowId(window.AppWindow.Id);
        _ = NativeMethods.SetWindowSubclass(hwnd, _subclass, UIntPtr.Zero, UIntPtr.Zero);
    }

    private IntPtr OnMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr data)
    {
        switch (message)
        {
            // No Background Erase While Dragging: DirectComposition Draws The Window
            case NativeMethods.WM_ERASEBKGND when _dragging:
                return 1;

            // Double Buffered While The Drag Lasts
            case NativeMethods.WM_ENTERSIZEMOVE:
                _dragging      = true;
                _wasComposited = (ExStyle(hwnd) & NativeMethods.WS_EX_COMPOSITED) != 0;
                SetComposited(hwnd, true);
                break;

            // Back To What It Was Once It Ends, Then Laid Out Again After A Short Pause
            case NativeMethods.WM_EXITSIZEMOVE:
                _dragging = false;
                SetComposited(hwnd, _wasComposited);
                _relayout.Stop();
                _relayout.Start();
                break;

            // The Window Is Going: The Subclass Comes Off First
            case NativeMethods.WM_NCDESTROY:
                _relayout.Stop();
                _ = NativeMethods.RemoveWindowSubclass(hwnd, _subclass, UIntPtr.Zero);
                break;
        }

        return NativeMethods.DefSubclassProc(hwnd, message, wParam, lParam);
    }

    private static uint ExStyle(IntPtr hwnd) => unchecked((uint)NativeMethods.GetWindowLongPtrW(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64());

    private static void SetComposited(IntPtr hwnd, bool on)
    {
        uint style = ExStyle(hwnd);
        style      = on ? style | NativeMethods.WS_EX_COMPOSITED : style & ~NativeMethods.WS_EX_COMPOSITED;
        _ = NativeMethods.SetWindowLongPtrW(hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(style));
    }

    // Marked For Layout First, Or UpdateLayout Has Nothing To Do; Skipped Once The Window Closed Or Its Body Was Swapped
    private void Relayout()
    {
        if (_window.Content is not FrameworkElement { XamlRoot: not null } content)
        {
            return;
        }

        content.InvalidateMeasure();
        content.UpdateLayout();
    }
}
