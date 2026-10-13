import { spawn } from 'node:child_process';
import { mkdir, writeFile, copyFile } from 'node:fs/promises';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { helperEnvironment } from '../src/environment.js';

// Actual HWND regression for Native.Present. Only creates its own off-screen
// fixture windows; no browser, selection hook, clipboard, hotkey or model access.
// --activate also tests explicit activation and restores the prior foreground.
const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const output = join(root, '.local', 'stacking-' + Date.now());
await mkdir(output, { recursive: true });
const source = join(output, 'StackingFixture.cs');
const executable = join(output, 'StackingFixture.exe');
await writeFile(source, String.raw`
using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using DshSelectionAssistant;

internal class StackingFixture {
  [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr h, uint cmd);
  [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
  static int assertions;
  static void Check(bool ok, string message) {
    if (!ok) throw new Exception(message);
    assertions++;
  }
  static void Drain() {
    var frame = new DispatcherFrame();
    Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate { frame.Continue = false; }));
    Dispatcher.PushFrame(frame);
  }
  static bool Above(Window upper, Window lower) {
    IntPtr target = Native.Handle(upper);
    for (IntPtr h = GetWindow(Native.Handle(lower), 3); h != IntPtr.Zero; h = GetWindow(h, 3)) if (h == target) return true;
    return false;
  }
  static bool NativeTopmost(Window w) { return (Native.GetWindowLong(Native.Handle(w), -20) & 8) != 0; }
  static Window Fixture(string title) {
    var w = new Window { Title = title, Width = 120, Height = 80, Left = -30000, Top = -30000,
      WindowStartupLocation = WindowStartupLocation.Manual, ShowActivated = false, ShowInTaskbar = false };
    w.Show(); return w;
  }
  [STAThread] static int Main(string[] args) {
    var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
    IntPtr foreground = GetForegroundWindow();
    Window cover = null, result = null;
    bool activation = Array.IndexOf(args, "--activate") >= 0;
    try {
      cover = Fixture("Stacking fixture: source application");
      result = Fixture("Stacking fixture: translation result");
      Native.ToolWindow(result, false); Drain();
      Native.SetWindowPos(Native.Handle(result), new IntPtr(1), 0, 0, 0, 0, 0x0213);
      Check(Above(cover, result), "Fixture must reproduce an obscured result first");
      Native.Rect before; Native.GetWindowRect(Native.Handle(result), out before);
      Native.Present(result); Drain();
      Check(Above(result, cover), "Explicit presentation must raise above a normal application HWND");
      Check(!NativeTopmost(result) && !result.Topmost, "Unpinned presentation must finish non-topmost in Win32 and WPF");
      Check(GetForegroundWindow() == foreground, "Nonactivating presentation must preserve keyboard focus");
      Native.Rect after; Native.GetWindowRect(Native.Handle(result), out after);
      Check(before.Left == after.Left && before.Top == after.Top && before.Right == after.Right && before.Bottom == after.Bottom,
        "Presentation must not change position or size");
      Native.SetWindowPos(Native.Handle(cover), IntPtr.Zero, 0, 0, 0, 0, 0x0213); Drain();
      Check(Above(cover, result), "Unpinned result must allow the source application to move above it later");
      result.Hide(); Native.Present(result); Drain();
      Check(result.IsVisible && Above(result, cover), "Reopening a hidden result must raise it again");
      result.Topmost = true; Native.Present(result); Drain();
      Check(result.Topmost && NativeTopmost(result), "Presentation must preserve a pinned result");
      Native.SetWindowPos(Native.Handle(cover), IntPtr.Zero, 0, 0, 0, 0, 0x0213); Drain();
      Check(Above(result, cover), "Pinned result must remain above a normal application after it is raised");
      result.Topmost = false; Native.Present(result); Drain();
      Check(!result.Topmost && !NativeTopmost(result) && Above(result, cover), "Unpin followed by presentation must restore normal-window semantics");
      if (activation) {
        cover.Activate(); Drain();
        Check(GetForegroundWindow() == Native.Handle(cover), "Activation fixture needs Windows foreground permission");
        Native.Present(result, true); Drain();
        Check(GetForegroundWindow() == Native.Handle(result) && result.IsActive, "Explicit activating presentation must focus the result");
        Check(!result.Topmost && !NativeTopmost(result), "Activating an unpinned result must not permanently pin it");
        result.Topmost = true; cover.Activate(); Native.Present(result, true); Drain();
        Check(result.Topmost && NativeTopmost(result) && GetForegroundWindow() == Native.Handle(result), "Activating presentation must preserve pin state");
      }
      Console.WriteLine("Actual HWND stacking passed: " + assertions + " assertions; explicit activation=" + activation);
      return 0;
    } catch (Exception error) {
      Console.Error.WriteLine(error.Message); return 1;
    } finally {
      if (result != null) result.Close();
      if (cover != null) cover.Close();
      if (activation && foreground != IntPtr.Zero) SetForegroundWindow(foreground);
      app.Shutdown();
    }
  }
}
`, 'utf8');
const framework = join(process.env.WINDIR || 'C:\\Windows', 'Microsoft.NET', 'Framework64', 'v4.0.30319');
const refs = ['System.dll', 'System.Core.dll', 'System.Xaml.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll', 'WPF/WindowsBase.dll', 'WPF/PresentationCore.dll', 'WPF/PresentationFramework.dll'];
const run = (command, args) => new Promise((accept, reject) => {
  const child = spawn(command, args, { cwd: root, windowsHide: true, stdio: 'inherit', env: helperEnvironment() });
  const timeout = setTimeout(() => { child.kill(); reject(new Error('Stacking regression timed out')); }, 30000);
  child.once('error', error => { clearTimeout(timeout); reject(error); });
  child.once('exit', code => { clearTimeout(timeout); code === 0 ? accept() : reject(new Error('Stacking regression exited ' + code)); });
});
await run(join(framework, 'csc.exe'), ['/nologo', '/target:exe', '/platform:anycpu', '/optimize+', '/utf8output', '/out:' + executable,
  '/win32manifest:' + join(root, 'native', 'app.manifest'), ...refs.map(name => '/reference:' + join(framework, name)), join(root, 'native', 'Native.cs'), source]);
await copyFile(join(root, 'native', 'app.config'), executable + '.config');
await run(executable, process.argv.includes('--activate') ? ['--activate'] : []);
