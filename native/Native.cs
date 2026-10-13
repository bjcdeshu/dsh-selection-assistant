using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
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
      try { return Math.Max(1, GetDpiForWindow(Handle(w))) / 96.0; }
      catch (EntryPointNotFoundException) { return 1; }
    }
    // All arguments and work areas are physical pixels. Never change z-order
    // here: the user's pin state and the toolbar's Topmost are owned by WPF.
    public static void Place(Window w, int x, int y, bool centered = false) {
      IntPtr h = Handle(w);
      w.WindowStartupLocation = WindowStartupLocation.Manual;
      SetWindowPos(h, IntPtr.Zero, x, y, 0, 0, 0x0015); // NOSIZE | NOZORDER | NOACTIVATE
      w.UpdateLayout();
      Rect actual; GetWindowRect(h, out actual);
      int width = actual.Right - actual.Left, height = actual.Bottom - actual.Top;
      var area = Forms.Screen.FromPoint(new System.Drawing.Point(x, y)).WorkingArea;
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
      int x = Math.Max(area.Left, Math.Min(r.Left, area.Right - (r.Right-r.Left)));
      int y = Math.Max(area.Top, Math.Min(r.Top, area.Bottom - (r.Bottom-r.Top)));
      SetWindowPos(Handle(w), IntPtr.Zero, x, y, 0, 0, 0x0015);
    }
  }
}
