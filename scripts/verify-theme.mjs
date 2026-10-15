import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { mkdir, writeFile, copyFile, readdir } from 'node:fs/promises';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { helperEnvironment } from '../src/environment.js';

// Real WPF controls and the production SystemEvents listener, with an injected
// system-theme reader. Messages target only this fixture's own hidden window;
// the Windows theme, registry, desktop input and user's preferences are untouched.
if (process.platform !== 'win32') throw new Error('Theme verification requires Windows');
const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const output = join(root, '.local', `theme-verification-${Date.now()}`);
await mkdir(output, { recursive: true });
const source = join(output, 'ThemeVerification.cs');
const executable = join(output, 'ThemeVerification.exe');
await writeFile(source, String.raw`
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using DshSelectionAssistant;
using P = DshSelectionAssistant.Program;

internal static class ThemeVerification {
  static int Checks, NativeNotifications;
  static readonly BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;
  delegate bool EnumWindowsCallback(IntPtr hwnd, IntPtr parameter);
  [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd, StringBuilder name, int length);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);
  static void Check(bool value, string label) { Checks++; if (!value) throw new Exception(label); }
  static T Field<T>(object owner, string name) { return (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(owner); }
  static void SetProgram(string name, object value) { typeof(P).GetField(name, PrivateStatic).SetValue(null, value); }
  static void Pump(int milliseconds) {
    var frame = new DispatcherFrame();
    var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
    timer.Tick += delegate { timer.Stop(); frame.Continue = false; };
    timer.Start(); Dispatcher.PushFrame(frame);
  }
  static void WaitFor(Func<bool> condition, int timeout, string label) {
    var clock = Stopwatch.StartNew();
    while (!condition() && clock.ElapsedMilliseconds < timeout) Pump(20);
    Check(condition(), label);
  }
  static IntPtr BroadcastWindow() {
    IntPtr result = IntPtr.Zero; uint ownProcess = (uint)Process.GetCurrentProcess().Id;
    EnumWindowsCallback callback = delegate(IntPtr hwnd, IntPtr parameter) {
      uint process; GetWindowThreadProcessId(hwnd, out process);
      if (process != ownProcess) return true;
      var name = new StringBuilder(256); GetClassName(hwnd, name, name.Capacity);
      if (name.ToString().StartsWith(".NET-BroadcastEventWindow", StringComparison.Ordinal)) { result = hwnd; return false; }
      return true;
    };
    EnumWindows(callback, IntPtr.Zero); GC.KeepAlive(callback);
    Check(result != IntPtr.Zero, "production SystemEvents created a broadcast-capable window in this fixture process");
    return result;
  }
  static void Notify(IntPtr hwnd, IntPtr themeName) {
    uint process; GetWindowThreadProcessId(hwnd, out process);
    Check(process == Process.GetCurrentProcess().Id, "theme notification target belongs only to this fixture");
    int before = NativeNotifications;
    // WM_SETTINGCHANGE carries a string pointer, so Windows requires synchronous
    // delivery. Never broadcast it: the only recipient is our own SystemEvents HWND.
    IntPtr result;
    Check(SendMessageTimeout(hwnd, 0x001A, IntPtr.Zero, themeName, 2, 1000, out result) != IntPtr.Zero, "send WM_SETTINGCHANGE to owned broadcast window");
    WaitFor(delegate { return NativeNotifications > before; }, 1500, "Windows notification reached SystemEvents");
  }
  static void Palette(TranslationWindow selection, TranslationWindow quick, Button button, Border chrome, bool dark) {
    var surface = (Color)ColorConverter.ConvertFromString(dark ? "#1F1F1F" : "#FFFFFF");
    var ink = (Color)ColorConverter.ConvertFromString(dark ? "#E8E8E8" : "#1A1A1A");
    var hover = (Color)ColorConverter.ConvertFromString(dark ? "#363636" : "#EFEFEF");
    Check(P.Surface.Color == surface && P.Ink.Color == ink, "production palette changed");
    foreach (var panel in new TranslationWindow[] { selection, quick }) {
      Check(panel.Window.IsVisible, panel.Surface + " remains visible");
      var shell = (Visuals.WindowShell)panel.Window.Content;
      Check(((SolidColorBrush)shell.Face.Background).Color == surface, panel.Surface + " existing surface reflects theme");
      Check(((SolidColorBrush)Field<TextBox>(panel, "Input").Foreground).Color == ink, panel.Surface + " existing text reflects theme");
      Check(((SolidColorBrush)Field<RichTextBox>(panel, "Result").Foreground).Color == ink, panel.Surface + " existing result reflects theme");
    }
    Check(button.Style.IsSealed && button.Template.IsSealed, "existing production button style and template are sealed");
    Check(((SolidColorBrush)chrome.Background).Color == hover, "sealed button template keeps live theme brush");
    Check(!P.Surface.IsFrozen && !P.Ink.IsFrozen, "shared bound theme brushes remain mutable");
  }
  [STAThread] static int Main() {
    ThemeWatcher watcher = null, fallback = null;
    TranslationWindow selection = null, quick = null;
    IntPtr themeName = IntPtr.Zero;
    Microsoft.Win32.UserPreferenceChangedEventHandler observed = delegate { Interlocked.Increment(ref NativeNotifications); };
    try {
      var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
      SetProgram("App", app);
      P.Config = new Dictionary<string, object> { { "theme", "system" } };
      typeof(P).GetMethod("SetupStyles", PrivateStatic).Invoke(null, null);
      var applyTheme = typeof(P).GetMethod("ApplyTheme", PrivateStatic);
      int applied = 0, reads = 0, uiThread = Thread.CurrentThread.ManagedThreadId;
      bool wrongThread = false; bool? systemDark = true;
      Action<bool> apply = delegate(bool dark) {
        applied++; wrongThread |= Thread.CurrentThread.ManagedThreadId != uiThread;
        applyTheme.Invoke(null, new object[] { dark });
      };
      watcher = new ThemeWatcher(app.Dispatcher, delegate { return P.Str(P.Config, "theme", "system"); }, apply,
        delegate { reads++; return systemDark; }, TimeSpan.FromSeconds(10));
      SetProgram("Appearance", watcher);
      Check(applied == 1 && P.Surface.Color == (Color)ColorConverter.ConvertFromString("#1F1F1F"), "initial system theme applied without opening settings");
      selection = new TranslationWindow("selection"); quick = new TranslationWindow("quick");
      foreach (var panel in new TranslationWindow[] { selection, quick }) {
        panel.Window.Left = -10000; panel.Window.Top = -10000; panel.Window.ShowInTaskbar = false;
        panel.Window.ShowActivated = false; panel.Window.Show(); panel.Window.UpdateLayout();
      }
      quick.DemoInput("Synthetic text retained through theme changes");
      quick.DemoOpacity(73); selection.DemoOpacity(81);
      var originalSurface = P.Surface; var originalInk = P.Ink;
      IntPtr selectionHwnd = new WindowInteropHelper(selection.Window).Handle, quickHwnd = new WindowInteropHelper(quick.Window).Handle;
      double originalWidth = quick.Window.Width, originalHeight = quick.Window.Height;
      var button = Field<Button>(quick, "Translate");
      var chrome = (Border)button.Template.FindName("chrome", button);
      Check(chrome != null, "actual translation button has production template");
      Palette(selection, quick, button, chrome, true);
      Microsoft.Win32.SystemEvents.UserPreferenceChanged += observed;
      IntPtr broadcast = BroadcastWindow(); themeName = Marshal.StringToHGlobalUni("ImmersiveColorSet");

      systemDark = false; Notify(broadcast, themeName);
      WaitFor(delegate { return applied == 2; }, 1500, "night to day follows actual Windows notification");
      Palette(selection, quick, button, chrome, false);
      systemDark = true; Notify(broadcast, themeName);
      WaitFor(delegate { return applied == 3; }, 1500, "day to night follows actual Windows notification");
      Palette(selection, quick, button, chrome, true);
      int before = applied, readBefore = reads;
      Notify(broadcast, themeName); WaitFor(delegate { return reads > readBefore; }, 1500, "unchanged theme notification rechecks system state");
      Check(applied == before, "unchanged theme does not repaint controls");

      P.Config["theme"] = "light"; P.RefreshTheme();
      Check(P.Surface.Color == Colors.White, "explicit light preference applies immediately");
      before = applied; readBefore = reads; Notify(broadcast, themeName); Pump(300);
      Check(applied == before && reads == readBefore && P.Surface.Color == Colors.White, "system dark cannot override fixed light");
      P.Config["theme"] = "dark"; P.RefreshTheme(); systemDark = false;
      before = applied; readBefore = reads; Notify(broadcast, themeName); Pump(300);
      Check(applied == before && reads == readBefore && P.Surface.Color != Colors.White, "system light cannot override fixed dark");
      P.Config["theme"] = "system"; systemDark = null; P.RefreshTheme();
      Check(applied == before, "temporary unavailable system state preserves current appearance");
      systemDark = false;
      var worker = new Thread(delegate() { watcher.Refresh(); }); worker.Start(); worker.Join();
      Check(applied == before, "background refresh does not mutate WPF controls directly");
      WaitFor(delegate { return applied == before + 1; }, 1500, "background refresh reaches UI dispatcher");
      Palette(selection, quick, button, chrome, false);
      Check(!wrongThread, "every palette update ran on the WPF UI thread");
      Check(Object.ReferenceEquals(originalSurface, P.Surface) && Object.ReferenceEquals(originalInk, P.Ink), "existing shared brushes retain identity");
      Check(new WindowInteropHelper(selection.Window).Handle == selectionHwnd && new WindowInteropHelper(quick.Window).Handle == quickHwnd, "theme refresh does not recreate either window");
      Check(quick.InputText == "Synthetic text retained through theme changes", "theme refresh preserves typed text");
      Check(quick.OpacityPercent == 73 && selection.OpacityPercent == 81, "theme refresh preserves both opacity preferences");
      Check(quick.Window.Width == originalWidth && quick.Window.Height == originalHeight, "theme refresh preserves window size");
      Check(typeof(P).GetField("Settings", PrivateStatic).GetValue(null) == null, "entire update worked without constructing settings");

      systemDark = true;
      worker = new Thread(delegate() { watcher.Refresh(); }); worker.Start(); worker.Join();
      before = applied; readBefore = reads; watcher.Dispose(); watcher.Dispose();
      Notify(broadcast, themeName); Pump(350);
      Check(applied == before && reads == readBefore, "disposal cancels queued refresh and unsubscribes notifications");
      SetProgram("Appearance", null);

      int fallbackReads = 0;
      fallback = new ThemeWatcher(app.Dispatcher, delegate { return "system"; }, apply,
        delegate { fallbackReads++; return systemDark; }, TimeSpan.FromMilliseconds(150));
      before = applied; systemDark = false;
      WaitFor(delegate { return applied == before + 1; }, 1000, "missed notification is repaired by fallback timer");
      Palette(selection, quick, button, chrome, false);
      Check(fallbackReads >= 2, "fallback rereads current system theme");
      before = applied; readBefore = fallbackReads; fallback.Dispose(); systemDark = true; Pump(450);
      Check(applied == before && fallbackReads == readBefore, "disposed watcher no longer polls or repaints");
      bool? initialUnavailable = null;
      using (var unavailable = new ThemeWatcher(app.Dispatcher, delegate { return "system"; }, delegate(bool dark) { initialUnavailable = dark; },
        delegate { return null; }, TimeSpan.FromSeconds(10))) {
        Check(initialUnavailable == false, "unavailable initial system theme has a light default");
      }
      Console.WriteLine(new JavaScriptSerializer().Serialize(new {
        result = "passed", checks = Checks, nativeNotifications = NativeNotifications,
        surfaces = new string[] { "selection", "quick" }, systemThemeChanged = false
      }));
      return 0;
    } catch (Exception error) { Console.Error.WriteLine(error.ToString()); return 1; }
    finally {
      if (watcher != null) watcher.Dispose(); if (fallback != null) fallback.Dispose();
      Microsoft.Win32.SystemEvents.UserPreferenceChanged -= observed;
      if (themeName != IntPtr.Zero) Marshal.FreeHGlobal(themeName);
      P.Closing = true;
      if (selection != null) selection.Window.Close(); if (quick != null) quick.Window.Close();
    }
  }
}
`, 'utf8');

const framework = join(process.env.WINDIR || 'C:\\Windows', 'Microsoft.NET', 'Framework64', 'v4.0.30319');
const references = ['System.dll', 'System.Core.dll', 'System.Xaml.dll', 'System.Web.Extensions.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll', 'WPF/WindowsBase.dll', 'WPF/PresentationCore.dll', 'WPF/PresentationFramework.dll', 'WPF/UIAutomationClient.dll', 'WPF/UIAutomationTypes.dll'];
const run = (file, args) => new Promise((accept, reject) => {
  const child = spawn(file, args, { windowsHide: true, env: helperEnvironment(), stdio: ['ignore', 'pipe', 'pipe'] });
  let stdout = '', stderr = '';
  child.stdout.on('data', chunk => stdout += chunk);
  child.stderr.on('data', chunk => stderr += chunk);
  const timer = setTimeout(() => { child.kill(); reject(new Error('Theme verification timed out')); }, 30000);
  child.once('error', error => { clearTimeout(timer); reject(error); });
  child.once('exit', code => { clearTimeout(timer); code === 0 ? accept(stdout) : reject(new Error(stderr || stdout || `Exited ${code}`)); });
});
const sources = (await readdir(join(root, 'native'))).filter(name => name.endsWith('.cs')).map(name => join(root, 'native', name));
await run(join(framework, 'csc.exe'), ['/nologo', '/target:exe', '/main:ThemeVerification', '/utf8output', `/out:${executable}`, `/win32manifest:${join(root, 'native', 'app.manifest')}`, ...references.map(path => `/reference:${join(framework, path)}`), ...sources, source]);
await copyFile(join(root, 'native', 'app.config'), executable + '.config');
const result = JSON.parse((await run(executable, [])).trim());
assert.equal(result.result, 'passed');
await writeFile(join(output, 'result.json'), JSON.stringify(result, null, 2) + '\n');
console.log(JSON.stringify({ ...result, report: join(output, 'result.json') }, null, 2));
