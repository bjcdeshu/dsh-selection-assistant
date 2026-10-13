import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { mkdir, writeFile, copyFile } from 'node:fs/promises';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { helperEnvironment } from '../src/environment.js';

// Exercise the production extractor and actual WPF HWNDs owned by this fixture.
// The host executable is read only for resources; it is never started.
if (process.platform !== 'win32') throw new Error('Host icon verification requires Windows');
const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const host = process.argv[2] || join(process.env.LOCALAPPDATA, 'Programs', 'DeepSeek Harness', 'DeepSeek Harness.exe');
const output = join(root, '.local', `host-icon-verification-${Date.now()}`);
await mkdir(output, { recursive: true });
const source = join(output, 'HostIconVerification.cs');
const executable = join(output, 'HostIconVerification.exe');
await writeFile(source, String.raw`
using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Drawing = System.Drawing;
using DshSelectionAssistant;

internal static class HostIconVerification {
  static int Checks;
  static void Check(bool value, string label) { Checks++; if (!value) throw new Exception(label); }
  [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);
  [DllImport("shell32.dll", CharSet=CharSet.Unicode)] static extern uint ExtractIconEx(string file, int index, IntPtr[] large, IntPtr[] small, uint count);
  [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
  delegate bool EnumResourceName(IntPtr module, IntPtr type, IntPtr name, IntPtr parameter);
  [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);
  [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] static extern bool EnumResourceNames(IntPtr module, IntPtr type, EnumResourceName callback, IntPtr parameter);
  [DllImport("kernel32.dll")] static extern bool FreeLibrary(IntPtr module);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern IntPtr LoadImage(IntPtr module, IntPtr name, uint type, int width, int height, uint flags);

  static double CompareWithHost(string path, IntPtr actual) {
    using (var icon = Drawing.Icon.FromHandle(actual)) {
      IntPtr module = LoadLibraryEx(path, IntPtr.Zero, 0x22), expected = IntPtr.Zero;
      try {
        Check(module != IntPtr.Zero, "independent host resource mapping");
        EnumResourceName callback = delegate(IntPtr handle, IntPtr type, IntPtr name, IntPtr parameter) {
          expected = LoadImage(handle, name, 1, icon.Width, icon.Height, 0); return false;
        };
        EnumResourceNames(module, new IntPtr(14), callback, IntPtr.Zero); GC.KeepAlive(callback);
        Check(expected != IntPtr.Zero, "independent host image at window icon resolution");
        return Difference(actual, expected);
      } finally { if (expected != IntPtr.Zero) DestroyIcon(expected); if (module != IntPtr.Zero) FreeLibrary(module); }
    }
  }

  static double Difference(IntPtr left, IntPtr right) {
    using (var a = Drawing.Icon.FromHandle(left)) using (var b = Drawing.Icon.FromHandle(right))
    using (var first = a.ToBitmap()) using (var second = b.ToBitmap()) {
      Check(first.Size == second.Size, "native host and window icon dimensions match: " + first.Size + " / " + second.Size);
      double difference = 0;
      for (int y = 0; y < first.Height; y++) for (int x = 0; x < first.Width; x++) {
        Drawing.Color p = first.GetPixel(x, y), q = second.GetPixel(x, y);
        difference += Math.Abs(p.A - q.A) + Math.Abs(p.R * p.A / 255 - q.R * q.A / 255)
          + Math.Abs(p.G * p.A / 255 - q.G * q.A / 255) + Math.Abs(p.B * p.A / 255 - q.B * q.A / 255);
      }
      return difference / (first.Width * first.Height * 4);
    }
  }

  [STAThread] static int Main(string[] args) {
    Window first = null, second = null;
    var large = new IntPtr[1]; var small = new IntPtr[1];
    try {
      using (var missing = HostIcon.Load(null)) {
        Check(!missing.IsHostIcon && missing.WindowImage == null && missing.TrayIcon != null, "demo fallback");
      }
      using (var missing = HostIcon.Load(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".exe"))) {
        Check(!missing.IsHostIcon && missing.TrayIcon != null, "missing host fallback");
      }
      using (var relative = HostIcon.Load("DeepSeek Harness.exe")) {
        Check(!relative.IsHostIcon, "relative executable path is not loaded");
      }
      using (var icons = HostIcon.Load(args[0])) {
        Check(icons.IsHostIcon && icons.WindowImage != null, "host resources loaded");
        Check(icons.WindowImage.IsFrozen, "decoded window image is immutable and detached from source file");
        var frame = icons.WindowImage as BitmapFrame;
        int[] sizes = frame.Decoder.Frames.Select(value => value.PixelWidth).Distinct().OrderBy(value => value).ToArray();
        Check(sizes.Length > 1 && sizes.Contains(16) && sizes.Contains(32), "small and large host resources retained");
        Check(sizes.Max() >= 128, "high resolution host resource retained");
        using (var saved = File.Create(args[1])) {
          var encoder = new PngBitmapEncoder(); encoder.Frames.Add(frame.Decoder.Frames.OrderByDescending(value => value.PixelWidth).First()); encoder.Save(saved);
        }
        Check(icons.TrayIcon.Handle != IntPtr.Zero, "tray icon owns a valid handle");
        GC.Collect(); GC.WaitForPendingFinalizers();
        first = new Window { Icon = icons.WindowImage, ShowInTaskbar = false, ShowActivated = false,
          WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000, Width = 300, Height = 200 };
        second = new Window { Icon = icons.WindowImage, ShowInTaskbar = false, ShowActivated = false,
          WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000, Width = 300, Height = 200 };
        first.Show(); second.Show(); first.UpdateLayout(); second.UpdateLayout();
        first.Dispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(delegate {}));
        IntPtr firstHwnd = new WindowInteropHelper(first).Handle, secondHwnd = new WindowInteropHelper(second).Handle;
        IntPtr big = SendMessage(firstHwnd, 0x007F, new IntPtr(1), IntPtr.Zero);
        IntPtr little = SendMessage(firstHwnd, 0x007F, IntPtr.Zero, IntPtr.Zero);
        Check(big != IntPtr.Zero && little != IntPtr.Zero, "window publishes large and small native icons");
        Check(SendMessage(secondHwnd, 0x007F, new IntPtr(1), IntPtr.Zero) != IntPtr.Zero, "shared image survives use in another window");
        Check(ExtractIconEx(args[0], 0, large, small, 1) > 0 && large[0] != IntPtr.Zero && small[0] != IntPtr.Zero, "independent Windows host extraction");
        double largeDifference = CompareWithHost(args[0], big);
        double smallDifference = CompareWithHost(args[0], little);
        Check(largeDifference <= 2, "taskbar/AltTab icon pixels match the host executable");
        Check(smallDifference <= 2, "small window icon pixels match the host executable");
        Console.WriteLine(new JavaScriptSerializer().Serialize(new {
          result = "passed", checks = Checks, sizes = sizes, largePixelDifference = largeDifference, smallPixelDifference = smallDifference
        }));
        first.Close(); first = null; second.Close(); second = null;
      }
      return 0;
    } catch (Exception error) { Console.Error.WriteLine(error.ToString()); return 1; }
    finally {
      if (first != null) first.Close(); if (second != null) second.Close();
      if (large[0] != IntPtr.Zero) DestroyIcon(large[0]); if (small[0] != IntPtr.Zero) DestroyIcon(small[0]);
    }
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
  const timer = setTimeout(() => { child.kill(); reject(new Error('Host icon verification timed out')); }, 20000);
  child.once('error', error => { clearTimeout(timer); reject(error); });
  child.once('exit', code => { clearTimeout(timer); code === 0 ? accept(stdout) : reject(new Error(stderr || stdout || `Exited ${code}`)); });
});
await run(join(framework, 'csc.exe'), ['/nologo', '/target:exe', '/utf8output', `/out:${executable}`, `/win32manifest:${join(root, 'native', 'app.manifest')}`, ...references.map(path => `/reference:${join(framework, path)}`), join(root, 'native', 'HostIcon.cs'), source]);
await copyFile(join(root, 'native', 'app.config'), executable + '.config');
const result = JSON.parse((await run(executable, [host, join(output, 'host-icon.png')])).trim());
assert.equal(result.result, 'passed');
await writeFile(join(output, 'result.json'), JSON.stringify(result, null, 2) + '\n');
console.log(JSON.stringify({ ...result, report: join(output, 'result.json') }, null, 2));
