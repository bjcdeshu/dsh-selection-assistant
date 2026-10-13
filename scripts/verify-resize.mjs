import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { mkdir, writeFile, copyFile } from 'node:fs/promises';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { helperEnvironment } from '../src/environment.js';

// Compile a small WPF harness against the production Native.cs. It only sends
// messages to its own off-screen HWND: no desktop input, hooks, clipboard or API.
if (process.platform !== 'win32') throw new Error('Native resize verification requires Windows');
const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const output = join(root, '.local', `resize-verification-${Date.now()}`);
await mkdir(output, { recursive: true });
const source = join(output, 'ResizeVerification.cs');
const executable = join(output, 'ResizeVerification.exe');
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

internal static class ResizeVerification {
  static int Checks;
  static void Equal(int actual, int expected, string label) {
    Checks++; if (actual != expected) throw new Exception(label + ": expected " + expected + ", got " + actual);
  }
  static IntPtr Position(int x, int y) { return new IntPtr(unchecked((int)((uint)(ushort)x | ((uint)(ushort)y << 16)))); }
  static int Hit(IntPtr hwnd, int x, int y) { return Native.SendMessage(hwnd, 0x0084, IntPtr.Zero, Position(x, y)).ToInt32(); }
  static void VerifyDirections(Native.Rect r, double dpiScale, Func<int, int, int> hit) {
    int x = r.Left + (int)Math.Round(10 * dpiScale), right = r.Right - (int)Math.Round(10 * dpiScale) - 1;
    int y = r.Top + (int)Math.Round(10 * dpiScale), bottom = r.Bottom - (int)Math.Round(10 * dpiScale) - 1;
    int centerX = (r.Left + r.Right) / 2, centerY = (r.Top + r.Bottom) / 2;
    Equal(hit(x, y), 13, "top-left"); Equal(hit(centerX, y), 12, "top"); Equal(hit(right, y), 14, "top-right");
    Equal(hit(x, centerY), 10, "left"); Equal(hit(right, centerY), 11, "right");
    Equal(hit(x, bottom), 16, "bottom-left"); Equal(hit(centerX, bottom), 15, "bottom"); Equal(hit(right, bottom), 17, "bottom-right");
    Equal(hit(centerX, centerY), 1, "center client"); Equal(hit(r.Left - 1, centerY), 0, "outside");
    Equal(hit(r.Left + 1, centerY), 10, "outer shadow gutter");
    Equal(hit(r.Left + (int)Math.Ceiling(15 * dpiScale), centerY), 1, "content outside resize edge");
  }
  [STAThread] static int Main() {
    Window window = null;
    try {
      // Pure geometry also covers negative monitor coordinates and another scale.
      foreach (double scale in new double[] { 1, 1.25, 1.5, 2 }) {
        Native.Rect r = new Native.Rect { Left = -2000, Top = -1200, Right = -1000, Bottom = -400 };
        VerifyDirections(r, scale, delegate(int x, int y) { return Native.ResizeHitTest(r, x, y, scale, 10); });
      }
      window = new Window {
        WindowStyle = WindowStyle.None, AllowsTransparency = true, ResizeMode = ResizeMode.CanResize,
        Width = 520, Height = 420, MinWidth = 380, MinHeight = 280, MaxWidth = 900, MaxHeight = 750,
        ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual,
        Left = -10000, Top = -10000, Content = new Border { Background = Brushes.White, Margin = new Thickness(10) }
      };
      int resized = 0;
      Native.EnableResize(window, 10, delegate { resized++; });
      window.Show(); window.UpdateLayout();
      window.Dispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(delegate {}));
      IntPtr hwnd = Native.Handle(window);
      double nativeScale = Native.Scale(window);
      Native.Rect bounds; Native.GetWindowRect(hwnd, out bounds);
      VerifyDirections(bounds, nativeScale, delegate(int x, int y) { return Hit(hwnd, x, y); });
      Equal((Native.GetWindowLong(hwnd, -16) & 0x00040000) != 0 ? 1 : 0, 1, "WS_THICKFRAME retained");
      Equal(((SolidColorBrush)window.Background).Color.A, 1, "shadow gutter participates in mouse hit testing");
      IntPtr infoPointer = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(Native.MinMaxInfo)));
      try {
        Marshal.StructureToPtr(new Native.MinMaxInfo(), infoPointer, false);
        Native.SendMessage(hwnd, 0x0024, IntPtr.Zero, infoPointer);
        var info = (Native.MinMaxInfo)Marshal.PtrToStructure(infoPointer, typeof(Native.MinMaxInfo));
        Equal(info.MinTrackSize.X, (int)Math.Ceiling(380 * nativeScale), "minimum width");
        Equal(info.MinTrackSize.Y, (int)Math.Ceiling(280 * nativeScale), "minimum height");
        Equal(info.MaxTrackSize.X, (int)Math.Floor(900 * nativeScale), "maximum width");
        Equal(info.MaxTrackSize.Y, (int)Math.Floor(750 * nativeScale), "maximum height");
      } finally { Marshal.FreeHGlobal(infoPointer); }
      IntPtr rectPointer = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(Native.Rect)));
      try {
        Marshal.StructureToPtr(bounds, rectPointer, false);
        Native.SendMessage(hwnd, 0x0214, new IntPtr(8), rectPointer);
        Equal(resized, 1, "user-size callback");
      } finally { Marshal.FreeHGlobal(rectPointer); }
      window.ResizeMode = ResizeMode.NoResize;
      Equal(Hit(hwnd, bounds.Left + 1, bounds.Top + 1), 1, "disabled resize");
      window.ResizeMode = ResizeMode.CanResize;
      window.WindowState = WindowState.Minimized;
      Native.GetWindowRect(hwnd, out bounds);
      Equal(Hit(hwnd, bounds.Left + 1, bounds.Top + 1), 1, "minimized has no resize border");
      Console.WriteLine(new JavaScriptSerializer().Serialize(new { checks = Checks, dpi = (int)Math.Round(nativeScale * 96), scales = new double[] { 1, 1.25, 1.5, 2 }, directions = 8, result = "passed" }));
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
  const timer = setTimeout(() => { child.kill(); reject(new Error('Native resize verification timed out')); }, 20000);
  child.once('error', error => { clearTimeout(timer); reject(error); });
  child.once('exit', code => { clearTimeout(timer); code === 0 ? accept(stdout) : reject(new Error(stderr || stdout || `Exited ${code}`)); });
});
await run(join(framework, 'csc.exe'), ['/nologo', '/target:exe', '/utf8output', `/out:${executable}`, `/win32manifest:${join(root, 'native', 'app.manifest')}`, ...references.map(path => `/reference:${join(framework, path)}`), join(root, 'native', 'Native.cs'), source]);
await copyFile(join(root, 'native', 'app.config'), executable + '.config');
const result = JSON.parse((await run(executable, [])).trim());
assert.equal(result.result, 'passed');
await writeFile(join(output, 'result.json'), JSON.stringify(result, null, 2) + '\n');
console.log(JSON.stringify({ ...result, report: join(output, 'result.json') }, null, 2));
