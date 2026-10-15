import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { copyFile, mkdir, readdir, writeFile } from 'node:fs/promises';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { helperEnvironment } from '../src/environment.js';

// Route keys through real production controls in an isolated off-screen window.
// No desktop input, hooks, clipboard, user preferences or model requests.
if (process.platform !== 'win32') throw new Error('Popup keyboard verification requires Windows');
const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const output = join(root, '.local', `popup-keyboard-${Date.now()}`);
await mkdir(output, { recursive: true });
const source = join(output, 'PopupKeyboardVerification.cs');
const executable = join(output, 'PopupKeyboardVerification.exe');
await writeFile(source, String.raw`
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using DshSelectionAssistant;
using P = DshSelectionAssistant.Program;

internal static class PopupKeyboardVerification {
  static int Checks;
  static void Check(bool value, string label) { Checks++; if (!value) throw new Exception(label); }
  static T Field<T>(object owner, string name) { return (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(owner); }
  static void Drain() { Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(delegate {})); }
  static void Open(Button button) { button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Drain(); }
  static KeyEventArgs Key(FrameworkElement target, Key key) {
    var e = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(target), 0, key) { RoutedEvent = UIElement.PreviewKeyDownEvent };
    target.RaiseEvent(e); Drain(); return e;
  }
  [STAThread] static int Main() {
    TranslationWindow panel = null;
    try {
      var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
      typeof(P).GetField("App", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, app);
      P.Config = new Dictionary<string, object> { { "theme", "light" }, { "targetLanguage", "auto" } };
      typeof(P).GetMethod("SetupStyles", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
      Visuals.AnimationsEnabled = false;
      panel = new TranslationWindow("quick");
      panel.Window.Left = -10000; panel.Window.Top = -10000; panel.Window.ShowInTaskbar = false;
      panel.Window.Show(); panel.Window.UpdateLayout(); Drain();
      var opacity = Field<OpacityControl>(panel, "Transparency");
      var flyout = Field<Popup>(opacity, "Flyout");
      var slider = Field<Slider>(opacity, "Slider");
      var input = Field<TextBox>(panel, "Input");
      opacity.SetPercent(73);
      // Test popup-child routing and the fallback when focus has not yet moved
      // from the owner/trigger. Neither route may cancel an in-flight translation.
      foreach (FrameworkElement target in new FrameworkElement[] { slider, opacity.Button, input }) {
        panel.Busy = true; Open(opacity.Button); Check(flyout.IsOpen, "opacity popup opens");
        var key = Key(target, System.Windows.Input.Key.Escape);
        Check(!flyout.IsOpen && key.Handled, "Escape closes only opacity popup");
        Check(panel.Window.IsVisible && panel.Busy, "popup Escape keeps window and active translation");
        Check(opacity.Percent == 73, "popup Escape preserves selected opacity");
      }
      panel.Busy = false;
      var language = Field<LanguagePicker>(panel, "Language");
      var search = Field<TextBox>(language, "Search");
      foreach (FrameworkElement target in new FrameworkElement[] { search, language, input }) {
        Open(language); Check(language.IsPopupOpen, "language popup opens");
        var key = Key(target, System.Windows.Input.Key.Escape);
        Check(!language.IsPopupOpen && key.Handled, "Escape closes only language popup");
        Check(panel.Window.IsVisible && language.Value == "auto", "language Escape preserves window and target language");
      }
      panel.Busy = true;
      Key(input, System.Windows.Input.Key.Escape);
      Check(!panel.Busy && panel.Window.IsVisible, "Escape without popup still stops translation first");
      Key(input, System.Windows.Input.Key.Escape);
      Check(!panel.Window.IsVisible, "next Escape closes idle window");
      Console.WriteLine(new JavaScriptSerializer().Serialize(new { checks = Checks, result = "passed" }));
      return 0;
    } catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    finally { P.Closing = true; if (panel != null) panel.Window.Close(); }
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
  const timer = setTimeout(() => { child.kill(); reject(new Error('Popup keyboard verification timed out')); }, 20000);
  child.once('error', error => { clearTimeout(timer); reject(error); });
  child.once('exit', code => { clearTimeout(timer); code === 0 ? accept(stdout) : reject(new Error(stderr || stdout || `Exited ${code}`)); });
});
const sources = (await readdir(join(root, 'native'))).filter(name => name.endsWith('.cs')).sort().map(name => join(root, 'native', name));
await run(join(framework, 'csc.exe'), ['/nologo', '/target:exe', '/main:PopupKeyboardVerification', '/utf8output', `/out:${executable}`, `/win32manifest:${join(root, 'native', 'app.manifest')}`, ...references.map(path => `/reference:${join(framework, path)}`), ...sources, source]);
await copyFile(join(root, 'native', 'app.config'), executable + '.config');
const result = JSON.parse((await run(executable, [])).trim());
assert.equal(result.result, 'passed');
await writeFile(join(output, 'result.json'), JSON.stringify(result, null, 2) + '\n');
console.log(JSON.stringify({ ...result, report: join(output, 'result.json') }, null, 2));
