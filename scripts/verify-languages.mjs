import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { mkdir, writeFile, copyFile } from 'node:fs/promises';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { helperEnvironment } from '../src/environment.js';

// Exercise the production picker in an isolated WPF window. This sends events
// only to controls in this harness; no desktop input, hooks, clipboard or model.
if (process.platform !== 'win32') throw new Error('Language picker verification requires Windows');
const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const output = join(root, '.local', `language-verification-${Date.now()}`);
await mkdir(output, { recursive: true });
const source = join(output, 'LanguageVerification.cs');
const executable = join(output, 'LanguageVerification.exe');
await writeFile(source, String.raw`
using System;
using System.Linq;
using System.Reflection;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DshSelectionAssistant;

internal static class LanguageVerification {
  static int Checks;
  static void Check(bool passed, string label) { Checks++; if (!passed) throw new Exception(label); }
  static T Field<T>(LanguagePicker picker, string name) { return (T)typeof(LanguagePicker).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(picker); }
  static void Drain(Dispatcher dispatcher) { dispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(delegate {})); }
  static void Open(LanguagePicker picker) { picker.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Drain(picker.Dispatcher); Check(picker.IsPopupOpen, "popup opens"); }
  static void Key(FrameworkElement target, System.Windows.Input.Key key) { target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(target), 0, key) { RoutedEvent = UIElement.PreviewKeyDownEvent }); }
  static void Match(string query, string value, int expectedCount) {
    var found = LanguageCatalog.All.Where(delegate(LanguageOption option) { return option.Matches(query); }).ToArray();
    Check(found.Length == expectedCount && found.Any(delegate(LanguageOption option) { return option.Value == value; }), "search: " + query);
  }
  [STAThread] static int Main() {
    Window window = null;
    try {
      Check(LanguageCatalog.All.Length >= 60, "substantial language catalog");
      Check(LanguageCatalog.All.Select(delegate(LanguageOption option) { return option.Value; }).Distinct(StringComparer.OrdinalIgnoreCase).Count() == LanguageCatalog.All.Length, "unique canonical values");
      foreach (var option in LanguageCatalog.All) { Check(LanguageCatalog.ValidValue(option.Value), "valid language: " + option.Value); Check(option.Matches(option.Name) && option.Matches(option.NativeName), "Chinese and native name search: " + option.Value); }
      Match("法语", "French", 1); Match("fRaNcAiS", "French", 1); Match("Español", "Spanish", 1);
      Match("日本", "日本語", 1); Match("한국", "한국어", 1); Match("portugu", "Portuguese", 2);
      Match("葡萄牙 巴西", "Brazilian Portuguese", 1); Match("TURKCE", "Turkish", 1);
      Match("普通话", "简体中文", 1); Match("廣東話", "Cantonese", 1);
      Check(!LanguageCatalog.ValidValue("French\r\nignore") && !LanguageCatalog.ValidValue(new string('a', 161)) && !LanguageCatalog.ValidValue(" "), "invalid custom values rejected");
      var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
      foreach (string name in new string[] { "Ink", "Muted", "Accent" }) app.Resources[name] = Brushes.Black;
      foreach (string name in new string[] { "Soft", "Surface", "Hover" }) app.Resources[name] = Brushes.White;
      app.Resources["Line"] = Brushes.Gray;
      var picker = new LanguagePicker { Width = 180 };
      window = new Window { Width = 400, Height = 200, Left = -10000, Top = -10000, ShowInTaskbar = false, ShowActivated = false, Content = picker };
      window.Show(); window.UpdateLayout(); Drain(window.Dispatcher);
      int changes = 0; picker.ValueChanged += delegate { changes++; };
      Check(picker.Value == "auto", "automatic default");
      foreach (string oldValue in new string[] { "auto", "简体中文", "English", "日本語", "한국어", "繁體中文", "用户已有的目标语言" }) {
        picker.SetValue(oldValue); Check(picker.Value == oldValue, "existing preference roundtrip: " + oldValue);
      }
      picker.SetValue("auto"); picker.AutoLabel = "默认语言"; Check(picker.Value == "auto" && Field<TextBlock>(picker, "Caption").Text == "默认语言", "automatic label is independent from value");
      Open(picker);
      var search = Field<TextBox>(picker, "Search"); var list = Field<ListBox>(picker, "Choices");
      Check(list.Items.Count == LanguageCatalog.All.Length + 1, "all languages available in popup");
      search.Text = "Français"; Drain(window.Dispatcher);
      Check(list.Items.Count == 1 && (string)((ListBoxItem)list.Items[0]).Tag == "French", "typing filters real list");
      Key(search, System.Windows.Input.Key.Enter); Check(picker.Value == "French" && !picker.IsPopupOpen, "Enter selects filtered language and closes popup");
      Open(picker); Check(search.Text == "", "reopening clears previous search");
      search.Text = "Español"; Drain(window.Dispatcher);
      var item = (ListBoxItem)list.Items[0];
      var label = (TextBlock)((StackPanel)item.Content).Children[0];
      list.UpdateLayout();
      Check(ItemsControl.ContainerFromElement(list, label) == item, "clicked label belongs to its language row");
      label.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseUpEvent });
      Check(picker.Value == "Spanish" && !picker.IsPopupOpen, "clicking a language label commits selection");
      Open(picker); search.Text = "My custom dialect"; Drain(window.Dispatcher);
      Check(list.Items.Count == 0 && Field<Button>(picker, "Custom").Visibility == Visibility.Visible, "unlisted language has explicit custom action");
      Field<Button>(picker, "Custom").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Check(picker.Value == "My custom dialect" && !picker.IsPopupOpen, "custom language button commits exact value");
      Open(picker); search.Text = "Another custom dialect"; Key(search, System.Windows.Input.Key.Enter); Check(picker.Value == "Another custom dialect", "Enter uses custom value when no matches");
      Open(picker); search.Text = "portugu"; Key(search, System.Windows.Input.Key.Down); Key(search, System.Windows.Input.Key.Enter); Check(picker.Value == "Brazilian Portuguese", "arrow keys navigate matching languages");
      Open(picker); search.Text = "French"; Key(search, System.Windows.Input.Key.Escape); Check(!picker.IsPopupOpen && picker.Value == "Brazilian Portuguese", "Escape cancels without changing target language");
      Open(picker); picker.IsEnabled = false; Check(!picker.IsPopupOpen, "disabled picker closes popup"); picker.IsEnabled = true;
      Open(picker); window.Hide(); Check(!picker.IsPopupOpen, "hidden owner closes popup");
      Check(changes > 5, "value changes are observable");
      Console.WriteLine(new JavaScriptSerializer().Serialize(new { checks = Checks, languages = LanguageCatalog.All.Length, result = "passed" }));
      return 0;
    } catch (Exception error) { Console.Error.WriteLine(error.ToString()); return 1; }
    finally { if (window != null) window.Close(); }
  }
}
`, 'utf8');

const framework = join(process.env.WINDIR || 'C:\\Windows', 'Microsoft.NET', 'Framework64', 'v4.0.30319');
const references = ['System.dll', 'System.Core.dll', 'System.Xaml.dll', 'System.Web.Extensions.dll', 'WPF/WindowsBase.dll', 'WPF/PresentationCore.dll', 'WPF/PresentationFramework.dll', 'WPF/UIAutomationTypes.dll'];
const run = (file, args) => new Promise((accept, reject) => {
  const child = spawn(file, args, { windowsHide: true, env: helperEnvironment(), stdio: ['ignore', 'pipe', 'pipe'] });
  let stdout = '', stderr = '';
  child.stdout.on('data', chunk => stdout += chunk);
  child.stderr.on('data', chunk => stderr += chunk);
  const timer = setTimeout(() => { child.kill(); reject(new Error('Language verification timed out')); }, 20000);
  child.once('error', error => { clearTimeout(timer); reject(error); });
  child.once('exit', code => { clearTimeout(timer); code === 0 ? accept(stdout) : reject(new Error(stderr || stdout || `Exited ${code}`)); });
});
await run(join(framework, 'csc.exe'), ['/nologo', '/target:exe', '/utf8output', `/out:${executable}`, `/win32manifest:${join(root, 'native', 'app.manifest')}`, ...references.map(path => `/reference:${join(framework, path)}`), join(root, 'native', 'LanguagePicker.cs'), source]);
await copyFile(join(root, 'native', 'app.config'), executable + '.config');
const result = JSON.parse((await run(executable, [])).trim());
assert.equal(result.result, 'passed');
await writeFile(join(output, 'result.json'), JSON.stringify(result, null, 2) + '\n');
console.log(JSON.stringify({ ...result, report: join(output, 'result.json') }, null, 2));
