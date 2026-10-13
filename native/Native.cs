using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace DshSelectionAssistant {
  internal static class Native {
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct Point { public int X, Y; }
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int height, uint flags);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out Rect r);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out Point p);
    [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int index);
    [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr h, int index, int value);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr h, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr h, int id);
    [DllImport("user32.dll")] public static extern bool ReleaseCapture();
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, int message, IntPtr wp, IntPtr lp);
    [DllImport("user32.dll")] static extern IntPtr LoadCursor(IntPtr instance, IntPtr name);
    [DllImport("user32.dll")] static extern IntPtr SetCursor(IntPtr cursor);
    [StructLayout(LayoutKind.Sequential)] internal struct MinMaxInfo {
      public Point Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize;
    }
    public static IntPtr Handle(Window w) { return new WindowInteropHelper(w).EnsureHandle(); }
    public static void ToolWindow(Window w, bool noActivate) {
      IntPtr h = Handle(w);
      SetWindowLong(h, -20, GetWindowLong(h, -20) | 0x80 | (noActivate ? 0x08000000 : 0));
    }
    public static bool Contains(Window w, int x, int y) {
      if (w == null || !w.IsVisible) return false;
      Rect r; GetWindowRect(Handle(w), out r);
      return x >= r.Left && x < r.Right && y >= r.Top && y < r.Bottom;
    }
    public static double Scale(Window w) {
      try { uint dpi = GetDpiForWindow(Handle(w)); return dpi > 0 ? dpi / 96.0 : 1; }
      catch (EntryPointNotFoundException) { return 1; }
    }
    // WindowStyle.None + AllowsTransparency bypasses WPF's visible resize frame.
    // Let Windows own the actual sizing loop, rather than trying to start it from
    // WPF's routed mouse event after a child control has captured the pointer.
    public static void EnableResize(Window w, double shadowGutterDip, Action onResize) {
      new ResizeFrame(w, shadowGutterDip, onResize);
    }
    internal static int ResizeHitTest(Rect r, int x, int y, double scale, double shadowGutterDip) {
      if (x < r.Left || x >= r.Right || y < r.Top || y >= r.Bottom) return 0; // HTNOWHERE
      int edge = Math.Max(1, (int)Math.Ceiling((Math.Max(0, shadowGutterDip) + 5) * scale));
      int corner = Math.Max(edge, (int)Math.Ceiling((Math.Max(0, shadowGutterDip) + 12) * scale));
      bool left = x < r.Left + edge, right = x >= r.Right - edge;
      bool top = y < r.Top + edge, bottom = y >= r.Bottom - edge;
      bool nearLeft = x < r.Left + corner, nearRight = x >= r.Right - corner;
      bool nearTop = y < r.Top + corner, nearBottom = y >= r.Bottom - corner;
      if ((left && nearTop) || (top && nearLeft)) return 13; // HTTOPLEFT
      if ((right && nearTop) || (top && nearRight)) return 14; // HTTOPRIGHT
      if ((left && nearBottom) || (bottom && nearLeft)) return 16; // HTBOTTOMLEFT
      if ((right && nearBottom) || (bottom && nearRight)) return 17; // HTBOTTOMRIGHT
      return left ? 10 : right ? 11 : top ? 12 : bottom ? 15 : 1; // HTCLIENT
    }
    sealed class ResizeFrame {
      readonly Window Window;
      readonly double Gutter;
      readonly Action OnResize;
      HwndSource Source;
      readonly HwndSourceHook Hook;
      internal ResizeFrame(Window window, double gutter, Action onResize) {
        Window = window; Gutter = gutter; OnResize = onResize; Hook = WndProc;
        // Alpha-zero pixels of a layered HWND are click-through before NCHITTEST.
        // An imperceptible alpha keeps the shadow gutter reachable by the mouse.
        if (window.AllowsTransparency) window.Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
        window.SourceInitialized += SourceInitialized;
        window.Closed += Closed;
        Source = PresentationSource.FromVisual(window) as HwndSource;
        if (Source != null) Source.AddHook(Hook);
      }
      void SourceInitialized(object sender, EventArgs e) {
        if (Source != null) return;
        Source = HwndSource.FromHwnd(Handle(Window));
        if (Source != null) Source.AddHook(Hook);
      }
      void Closed(object sender, EventArgs e) {
        if (Source != null) Source.RemoveHook(Hook);
        Source = null;
        Window.SourceInitialized -= SourceInitialized; Window.Closed -= Closed;
      }
      bool CanResize { get { return Window.WindowState == WindowState.Normal && (Window.ResizeMode == ResizeMode.CanResize || Window.ResizeMode == ResizeMode.CanResizeWithGrip); } }
      IntPtr WndProc(IntPtr hwnd, int message, IntPtr wp, IntPtr lp, ref bool handled) {
        if (message == 0x0084) { // WM_NCHITTEST; coordinates are signed screen pixels.
          Rect bounds;
          if (!GetWindowRect(hwnd, out bounds)) return IntPtr.Zero;
          long packed = lp.ToInt64();
          int x = unchecked((short)(packed & 0xffff)), y = unchecked((short)((packed >> 16) & 0xffff));
          int hit = ResizeHitTest(bounds, x, y, Scale(Window), Gutter);
          handled = true;
          return new IntPtr(CanResize ? hit : (hit == 0 ? 0 : 1));
        }
        if (message == 0x0020 && CanResize) { // WM_SETCURSOR; explicitly keep resize cursors over WPF children.
          int hit = (int)(lp.ToInt64() & 0xffff);
          int cursor = hit == 10 || hit == 11 ? 32644 : hit == 12 || hit == 15 ? 32645 : hit == 13 || hit == 17 ? 32642 : hit == 14 || hit == 16 ? 32643 : 0;
          if (cursor != 0) { SetCursor(LoadCursor(IntPtr.Zero, new IntPtr(cursor))); handled = true; return new IntPtr(1); }
        }
        if (message == 0x0024 && lp != IntPtr.Zero) { // WM_GETMINMAXINFO; WPF limits are DIP, Windows needs pixels.
          var info = (MinMaxInfo)Marshal.PtrToStructure(lp, typeof(MinMaxInfo));
          double scale = Scale(Window);
          info.MinTrackSize.X = Math.Max(1, (int)Math.Ceiling(Window.MinWidth * scale));
          info.MinTrackSize.Y = Math.Max(1, (int)Math.Ceiling(Window.MinHeight * scale));
          if (!Double.IsInfinity(Window.MaxWidth)) info.MaxTrackSize.X = Math.Max(info.MinTrackSize.X, (int)Math.Floor(Window.MaxWidth * scale));
          if (!Double.IsInfinity(Window.MaxHeight)) info.MaxTrackSize.Y = Math.Max(info.MinTrackSize.Y, (int)Math.Floor(Window.MaxHeight * scale));
          Marshal.StructureToPtr(info, lp, false); handled = true;
        }
        if (message == 0x0214 && CanResize && OnResize != null) OnResize(); // WM_SIZING, including keyboard resize.
        return IntPtr.Zero;
      }
    }
    // Call only for an explicit open/translation action, after positioning. Show()
    // with ShowActivated=false does not raise a background result window. Use a
    // one-shot native raise without changing WPF's persistent pin preference.
    // The paired calls are synchronous: an unpinned window finishes in the normal
    // window band, above other normal windows, without taking keyboard focus.
    public static void Present(Window w, bool activate = false) {
      if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
      if (!w.IsVisible) w.Show();
      IntPtr h = Handle(w);
      bool pinned = w.Topmost;
      const uint flags = 0x0213; // NOSIZE | NOMOVE | NOACTIVATE | NOOWNERZORDER
      SetWindowPos(h, new IntPtr(-1), 0, 0, 0, 0, flags); // HWND_TOPMOST
      if (!pinned) SetWindowPos(h, new IntPtr(-2), 0, 0, 0, 0, flags); // HWND_NOTOPMOST
      if (activate) w.Activate();
    }
    // All arguments and work areas are physical pixels. Never change z-order
    // here: the user's pin state and the toolbar's Topmost are owned by WPF.
    public static void Place(Window w, int x, int y, bool centered = false) {
      IntPtr h = Handle(w);
      w.WindowStartupLocation = WindowStartupLocation.Manual;
      SetWindowPos(h, IntPtr.Zero, x, y, 0, 0, 0x0015); // NOSIZE | NOZORDER | NOACTIVATE
      w.UpdateLayout();
      var area = Forms.Screen.FromPoint(new System.Drawing.Point(x, y)).WorkingArea;
      FitToWorkArea(w, area);
      Rect actual; GetWindowRect(h, out actual);
      int width = actual.Right - actual.Left, height = actual.Bottom - actual.Top;
      int gap = Math.Max(8, (int)Math.Round(8 * Scale(w)));
      int nx = centered ? area.Left + (area.Width - width) / 2 : x + gap;
      int ny = centered ? area.Top + (area.Height - height) / 2 : y + gap + 4;
      if (!centered && ny + height > area.Bottom - gap) ny = y - height - gap;
      nx = Math.Max(area.Left + gap, Math.Min(nx, area.Right - width - gap));
      ny = Math.Max(area.Top + gap, Math.Min(ny, area.Bottom - height - gap));
      SetWindowPos(h, IntPtr.Zero, nx, ny, 0, 0, 0x0015);
    }
    public static void KeepOnScreen(Window w) {
      Rect r; GetWindowRect(Handle(w), out r);
      var area = Forms.Screen.FromRectangle(new System.Drawing.Rectangle(r.Left, r.Top, r.Right-r.Left, r.Bottom-r.Top)).WorkingArea;
      FitToWorkArea(w, area); GetWindowRect(Handle(w), out r);
      int x = Math.Max(area.Left, Math.Min(r.Left, area.Right - (r.Right-r.Left)));
      int y = Math.Max(area.Top, Math.Min(r.Top, area.Bottom - (r.Bottom-r.Top)));
      SetWindowPos(Handle(w), IntPtr.Zero, x, y, 0, 0, 0x0015);
    }
    static void FitToWorkArea(Window w, System.Drawing.Rectangle area) {
      double scale = Scale(w);
      w.Width = Math.Min(w.Width, Math.Max(w.MinWidth, area.Width / scale - 16));
      w.Height = Math.Min(w.Height, Math.Max(w.MinHeight, area.Height / scale - 16));
      w.UpdateLayout();
    }
  }
}
