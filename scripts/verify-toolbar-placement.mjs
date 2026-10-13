import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { mkdir, writeFile, copyFile } from 'node:fs/promises';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { helperEnvironment } from '../src/environment.js';

// Exercise production placement geometry and this fixture's own invisible HWND.
// No desktop input, selection hooks, clipboard or model access is involved.
if (process.platform !== 'win32') throw new Error('Native toolbar placement verification requires Windows');
const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const output = join(root, '.local', `toolbar-placement-${Date.now()}`);
await mkdir(output, { recursive: true });
const source = join(output, 'ToolbarPlacementVerification.cs');
const executable = join(output, 'ToolbarPlacementVerification.exe');
await writeFile(source, String.raw`
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using DshSelectionAssistant;
using Forms = System.Windows.Forms;

internal static class ToolbarPlacementVerification {
  [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
  static int Checks;
  static void Check(bool valid, string label) { Checks++; if (!valid) throw new Exception(label); }
  static void Equal(int actual, int expected, string label) {
    Check(actual == expected, label + ": expected " + expected + ", got " + actual);
  }
  static int Pixels(double dip, double scale) { return (int)Math.Round(dip * scale); }
  static void Within(Native.Point position, int width, int height, System.Drawing.Rectangle area, int gap, string label) {
    Check(position.X >= area.Left + gap && position.X + width <= area.Right - gap,
      label + " must keep the full toolbar inside horizontal work-area margins");
    Check(position.Y >= area.Top + gap && position.Y + height <= area.Bottom - gap,
      label + " must keep the full toolbar inside vertical work-area margins");
  }
  static void VerifyGeometry(double scale) {
    // A monitor to the upper left of the primary display catches unsigned or DIP/px mistakes.
    var area = new System.Drawing.Rectangle(-2560, -1440, 2560, 1440);
    int width = Pixels(184, scale), height = Pixels(48, scale), gap = Pixels(8, scale), shadow = Pixels(6, scale);
    int x = -1000, y = -700;
    Native.Point regular = Native.ToolbarPosition(area, width, height, x, y, scale);
    Equal(regular.X + width - shadow, x, "visible right edge follows release point at " + scale);
    Equal(regular.Y + shadow, y + gap, "visible top has an 8 DIP gap at " + scale);
    Check(regular.X < x - width / 2, "toolbar extends toward selected text at " + scale);
    Within(regular, width, height, area, gap, "regular placement");

    Native.Point left = Native.ToolbarPosition(area, width, height, area.Left + 1, y, scale);
    Equal(left.X, area.Left + gap, "left edge clamp");
    Native.Point right = Native.ToolbarPosition(area, width, height, area.Right - 1, y, scale);
    Equal(right.X + width, area.Right - gap, "right edge clamp");
    int bottomAnchor = area.Bottom - gap;
    Native.Point above = Native.ToolbarPosition(area, width, height, x, bottomAnchor, scale);
    Equal(above.Y + height - shadow, bottomAnchor - gap, "bottom overflow flips visible face above the pointer");
    Check(above.Y < bottomAnchor, "bottom placement must flip upward");
    foreach (int edgeX in new int[] { area.Left + 1, area.Left + area.Width / 2, area.Right - 1 }) {
      foreach (int edgeY in new int[] { area.Top + 1, area.Top + area.Height / 2, area.Bottom - 1 }) {
        var position = Native.ToolbarPosition(area, width, height, edgeX, edgeY, scale);
        Within(position, width, height, area, gap, "edge/corner placement at " + scale);
      }
    }
  }
  static void Drain(Window window) { window.Dispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(delegate {})); }
  [STAThread] static int Main() {
    Window window = null;
    try {
      foreach (double scale in new double[] { 1, 1.25, 1.5, 2 }) VerifyGeometry(scale);
      IntPtr foreground = GetForegroundWindow();
      var reports = new List<object>();
      var face = new Border { Background = Brushes.White, Margin = new Thickness(6) };
      window = new Window {
        Title = "Toolbar placement fixture", WindowStyle = WindowStyle.None, AllowsTransparency = true,
        Background = Brushes.Transparent, ResizeMode = ResizeMode.NoResize, Width = 184, Height = 48,
        ShowActivated = false, ShowInTaskbar = false, Opacity = 0, Topmost = true,
        UseLayoutRounding = true, SnapsToDevicePixels = true,
        WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000,
        Content = face
      };
      window.Show(); Drain(window);
      IntPtr hwnd = Native.Handle(window);
      foreach (Forms.Screen screen in Forms.Screen.AllScreens) {
        var area = screen.WorkingArea;
        int centerX = area.Left + area.Width / 2, centerY = area.Top + area.Height / 2;
        Native.PlaceToolbar(window, centerX, centerY); Drain(window);
        Native.Rect bounds; Native.GetWindowRect(hwnd, out bounds);
        double scale = Native.Scale(window);
        int width = bounds.Right - bounds.Left, height = bounds.Bottom - bounds.Top;
        int gap = Pixels(8, scale), shadow = Pixels(6, scale);
        Equal(width, Pixels(184, scale), "physical toolbar width after monitor DPI change");
        Equal(height, Pixels(48, scale), "physical toolbar height after monitor DPI change");
        Equal(bounds.Right - shadow, centerX, "actual HWND visible right edge follows anchor");
        Equal(bounds.Top + shadow, centerY + gap, "actual HWND visible top has the intended gap");
        System.Windows.Point faceEdge = face.PointToScreen(new System.Windows.Point(face.ActualWidth, 0));
        Check(Math.Abs(faceEdge.X - centerX) <= 1, "rendered face right edge follows anchor within pixel rounding");
        Check(Math.Abs(faceEdge.Y - (centerY + gap)) <= 1, "rendered face top has the intended gap within pixel rounding");
        foreach (int anchorX in new int[] { area.Left + 1, centerX, area.Right - 1 }) {
          foreach (int anchorY in new int[] { area.Top + 1, centerY, area.Bottom - 1 }) {
            Native.PlaceToolbar(window, anchorX, anchorY); Drain(window);
            Native.GetWindowRect(hwnd, out bounds);
            Within(new Native.Point { X = bounds.Left, Y = bounds.Top }, bounds.Right - bounds.Left,
              bounds.Bottom - bounds.Top, area, gap, "actual HWND edge/corner placement");
            Equal(bounds.Right - bounds.Left, width, "placement preserves toolbar width");
            Equal(bounds.Bottom - bounds.Top, height, "placement preserves toolbar height");
            if (anchorY == area.Bottom - 1) Check(bounds.Bottom - shadow < anchorY, "actual HWND bottom edge flips above anchor");
          }
        }
        reports.Add(new { screen = screen.DeviceName, dpi = (int)Math.Round(scale * 96), width = width, height = height, anchors = 9 });
      }
      Check(window.Topmost && (Native.GetWindowLong(hwnd, -20) & 8) != 0, "placement preserves WPF and native topmost state");
      Check(GetForegroundWindow() == foreground, "placement does not take keyboard focus");
      Console.WriteLine(new JavaScriptSerializer().Serialize(new { checks = Checks, scales = new double[] { 1, 1.25, 1.5, 2 }, screens = reports, result = "passed" }));
      return 0;
    } catch (Exception error) { Console.Error.WriteLine(error.ToString()); return 1; }
    finally { if (window != null) window.Close(); }
  }
}
`, 'utf8');

const framework = join(process.env.WINDIR || 'C:\\Windows', 'Microsoft.NET', 'Framework64', 'v4.0.30319');
const references = ['System.dll', 'System.Core.dll', 'System.Xaml.dll', 'System.Web.Extensions.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll', 'WPF/WindowsBase.dll', 'WPF/PresentationCore.dll', 'WPF/PresentationFramework.dll'];
const run = (file, args) => new Promise((accept, reject) => {
  const child = spawn(file, args, { windowsHide: true, env: helperEnvironment(), stdio: ['ignore', 'pipe', 'pipe'] });
  let stdout = '', stderr = '';
  child.stdout.on('data', chunk => stdout += chunk);
  child.stderr.on('data', chunk => stderr += chunk);
  const timer = setTimeout(() => { child.kill(); reject(new Error('Native toolbar placement verification timed out')); }, 20000);
  child.once('error', error => { clearTimeout(timer); reject(error); });
  child.once('exit', code => { clearTimeout(timer); code === 0 ? accept(stdout) : reject(new Error(stderr || stdout || `Exited ${code}`)); });
});
await run(join(framework, 'csc.exe'), ['/nologo', '/target:exe', '/utf8output', `/out:${executable}`, `/win32manifest:${join(root, 'native', 'app.manifest')}`, ...references.map(path => `/reference:${join(framework, path)}`), join(root, 'native', 'Native.cs'), source]);
await copyFile(join(root, 'native', 'app.config'), executable + '.config');
const result = JSON.parse((await run(executable, [])).trim());
assert.equal(result.result, 'passed');
await writeFile(join(output, 'result.json'), JSON.stringify(result, null, 2) + '\n');
console.log(JSON.stringify({ ...result, report: join(output, 'result.json') }, null, 2));
