using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

// UI-only process. No model credentials, networking, UI Automation or key logging.
namespace DshSelectionAssistant {
  internal class Program {
    static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = 262144 };
    static readonly object OutputLock = new object();
    static Application App;
    static Window Toolbar, Settings;
    static TranslationWindow SelectionPanel, QuickPanel;
    static Forms.NotifyIcon Tray;
    static HwndSource HotkeyWindow;
    static DispatcherTimer ToolbarTimer;
    internal static Dictionary<string, object> Config;
    static object[] Models = new object[0];
    static string SelectionId;
    internal static bool Closing;
    static bool HotkeyRegistered;
    static string RegisteredQuickHotkey;
    static bool Demo;
    static string PreviewDirectory;
    static TextBlock SettingsStatus;
    static Button SaveButton;
    static Forms.ToolStripMenuItem ToggleItem;
    static CheckBox EnabledBox, ClipboardBox;
    static ComboBox TriggerBox, LanguageBox, ModelBox, ThemeBox, QuickHotkeyBox;
    static TextBox ProviderBox, ModelIdBox, BlockedBox;
    static Dictionary<string, TextBox> PromptBoxes = new Dictionary<string, TextBox>();
    internal static SolidColorBrush Ink = Theme.Brush("Ink", "#1A1A1A"), Muted = Theme.Brush("Muted", "#707070"), Accent = Theme.Brush("Accent", "#159B68"), Line = Theme.Brush("Line", "#E6E6E6"), Surface = Theme.Brush("Surface", "#FFFFFF"), Soft = Theme.Brush("Soft", "#F5F5F5"), Hover = Theme.Brush("Hover", "#EFEFEF");

    static SolidColorBrush Hex(string value) { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(value)); }
    internal static string Str(Dictionary<string, object> d, string k, string fallback = "") { object v; return d != null && d.TryGetValue(k, out v) && v != null ? Convert.ToString(v) : fallback; }
    internal static bool Bool(Dictionary<string, object> d, string k) { object v; return d != null && d.TryGetValue(k, out v) && v is bool && (bool)v; }
    internal static int Num(Dictionary<string, object> d, string k, int fallback = 0) { int value; return int.TryParse(Str(d, k), out value) ? value : fallback; }
    internal static Dictionary<string, object> Obj(Dictionary<string, object> d, string k) { object v; return d != null && d.TryGetValue(k, out v) ? v as Dictionary<string, object> : null; }
    internal static void Send(string type, params object[] pairs) {
      if (Closing && type != "shutdown") return;
      var message = new Dictionary<string, object> { { "type", type } };
      for (int i = 0; i + 1 < pairs.Length; i += 2) message[(string)pairs[i]] = pairs[i + 1];
      try { lock (OutputLock) { Console.Out.WriteLine(Json.Serialize(message)); Console.Out.Flush(); } } catch { Shutdown(); }
    }

    [STAThread] public static void Main(string[] args) {
      Demo = args.Length > 0 && args[0] == "--demo";
      Visuals.AnimationsEnabled = !Demo;
      if (Demo && args.Length > 1) PreviewDirectory = Path.GetFullPath(args[1]);
      // A WinExe has no console code page. Open inherited pipes explicitly;
      // Console.InputEncoding/OutputEncoding would call SetConsoleCP and fail.
      Console.SetIn(new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false)));
      Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true });
      App = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
      App.DispatcherUnhandledException += delegate(object sender, DispatcherUnhandledExceptionEventArgs e) { e.Handled = true; if (Demo) Send("demo-error", "message", e.Exception.ToString()); Health("ui-error", e.Exception.GetType().Name); Notice("界面操作未完成，请关闭小窗后重试。"); };
      App.Startup += delegate {
        SetupStyles(); SetupTray(); SetupHotkey();
        ToolbarTimer = new DispatcherTimer(); ToolbarTimer.Tick += delegate { HideToolbar(); };
        var reader = new Thread(ReadInput) { IsBackground = true }; reader.Start();
        Send("ready");
      };
      App.Run();
    }

    static void ReadInput() {
      try {
        string line;
        while ((line = Console.In.ReadLine()) != null) {
          if (line.Length > 262144) break;
          var data = Json.DeserializeObject(line) as Dictionary<string, object>;
          if (data == null) break;
          App.Dispatcher.BeginInvoke(new System.Action(delegate { Handle(data); }));
        }
      } catch {}
      App.Dispatcher.BeginInvoke(new System.Action(Shutdown));
    }

    static void SetupStyles() {
      App.Resources.Add(typeof(Button), Visuals.ButtonStyle());
      var textBox = new Style(typeof(TextBox));
      textBox.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8)));
      textBox.Setters.Add(new Setter(Control.BorderBrushProperty, Line));
      textBox.Setters.Add(new Setter(Control.FontSizeProperty, 13.0));
      textBox.Setters.Add(new Setter(Control.ForegroundProperty, Ink));
      textBox.Setters.Add(new Setter(Control.BackgroundProperty, Surface));
      textBox.Setters.Add(new Setter(TextBox.CaretBrushProperty, Ink));
      App.Resources.Add(typeof(TextBox), textBox);
      App.Resources["Ink"] = Ink; App.Resources["Muted"] = Muted; App.Resources["Surface"] = Surface; App.Resources["Soft"] = Soft; App.Resources["Line"] = Line; App.Resources["Hover"] = Hover; App.Resources["Accent"] = Accent;
      var styles = (ResourceDictionary)System.Windows.Markup.XamlReader.Parse(ControlStyles.Markup);
      App.Resources.MergedDictionaries.Add(styles);
    }

    static void ApplyTheme() {
      bool dark = Str(Config, "theme") == "dark";
      if (Str(Config, "theme", "system") == "system") {
        try { dark = Convert.ToInt32(Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1)) == 0; } catch {}
      }
      Theme.Set("Ink", dark ? "#E8E8E8" : "#1A1A1A");
      Theme.Set("Muted", dark ? "#A3A3A3" : "#707070");
      Theme.Set("Surface", dark ? "#1F1F1F" : "#FFFFFF");
      Theme.Set("Soft", dark ? "#292929" : "#F5F5F5");
      Theme.Set("Line", dark ? "#3A3A3A" : "#E6E6E6");
      Theme.Set("Hover", dark ? "#363636" : "#EFEFEF");
      Theme.Set("Accent", dark ? "#32B580" : "#159B68");
    }

    internal static Window WindowBase(double width, double height, bool noActivate, bool taskbar = false) {
      var w = new Window { Width = width, Height = height, WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent, ShowInTaskbar = taskbar, Topmost = true, ShowActivated = !noActivate, WindowStartupLocation = WindowStartupLocation.Manual, ResizeMode = ResizeMode.NoResize, FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI"), Foreground = Ink };
      w.UseLayoutRounding = true; w.SnapsToDevicePixels = true;
      TextOptions.SetTextFormattingMode(w, TextFormattingMode.Display);
      TextOptions.SetTextRenderingMode(w, TextRenderingMode.Auto);
      w.SourceInitialized += delegate { if (!taskbar) Native.ToolWindow(w, noActivate); };
      return w;
    }
    internal static Border Card(UIElement child, int padding = 8) { return new Border { Background = Surface, BorderBrush = Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(padding), Child = child }; }
    internal static Button Button(string text, System.Action click, string help = null) { var b = new Button { Content = text, ToolTip = help ?? text }; b.SetValue(System.Windows.Automation.AutomationProperties.NameProperty, help ?? text); b.Click += delegate { click(); }; return b; }
    internal static TextBlock Text(string text, double size = 13, Brush color = null) { return new TextBlock { Text = text, FontSize = size, Foreground = color ?? Ink, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 4, 2, 4) }; }

    static void SetupTray() {
      Tray = new Forms.NotifyIcon { Icon = System.Drawing.SystemIcons.Information, Text = "DSH 划词助手 · 等待启用", Visible = true };
      var menu = new Forms.ContextMenuStrip();
      ToggleItem = new Forms.ToolStripMenuItem("启用全局划词");
      ToggleItem.Click += delegate { if (Config == null) return; if (!Bool(Config, "enabled")) ShowSettings(); else Send("settings-save", "settings", new Dictionary<string, object> { { "enabled", false } }); };
      menu.Items.Add("快捷翻译", null, delegate { ShowQuick(); });
      menu.Items.Add(new Forms.ToolStripSeparator());
      menu.Items.Add(ToggleItem);
      menu.Items.Add("设置 / 提示词", null, delegate { ShowSettings(); });
      menu.Items.Add("显示当前结果", null, delegate { if (SelectionPanel != null) SelectionPanel.Show(); });
      menu.Items.Add(new Forms.ToolStripSeparator());
      menu.Items.Add("退出划词助手", null, delegate { Send("shutdown"); Shutdown(); });
      Tray.ContextMenuStrip = menu; Tray.DoubleClick += delegate { ShowQuick(); };
    }
    static void SetupHotkey() {
      var p = new HwndSourceParameters("DSH Selection Hotkey") { ParentWindow = new IntPtr(-3), WindowStyle = 0 };
      HotkeyWindow = new HwndSource(p);
      HotkeyWindow.AddHook(delegate(IntPtr h, int msg, IntPtr wp, IntPtr lp, ref bool handled) {
        if (msg == 0x0312 && wp.ToInt32() == 1) { Native.Point cursor; Native.GetCursorPos(out cursor); Send("hotkey", "x", cursor.X, "y", cursor.Y); handled = true; }
        if (msg == 0x0312 && wp.ToInt32() == 2) { ShowQuick(); handled = true; }
        if (msg == 0x001A && Config != null) ApplyTheme();
        return IntPtr.Zero;
      });
    }
    static void UpdateHotkey() {
      if (Demo) return;
      if (HotkeyRegistered) { Native.UnregisterHotKey(HotkeyWindow.Handle, 1); HotkeyRegistered = false; }
      if (Bool(Config, "enabled")) {
        HotkeyRegistered = Native.RegisterHotKey(HotkeyWindow.Handle, 1, 0x0001 | 0x0002 | 0x4000, 0x44);
        if (!HotkeyRegistered) Notice("Ctrl+Alt+D 已被其他应用占用；自动划词仍可使用。");
      }
      string shortcut = Str(Config, "quickHotkey", "Ctrl+Alt+T");
      if (RegisteredQuickHotkey != shortcut) {
        Native.UnregisterHotKey(HotkeyWindow.Handle, 2);
        uint modifiers = shortcut == "Ctrl+Shift+T" ? 0x0002u | 0x0004u : shortcut == "Alt+Shift+T" ? 0x0001u | 0x0004u : 0x0001u | 0x0002u;
        if (shortcut == "none" || Native.RegisterHotKey(HotkeyWindow.Handle, 2, modifiers | 0x4000, 0x54)) RegisteredQuickHotkey = shortcut;
        else { RegisteredQuickHotkey = null; Notice(shortcut + " 已被占用；可从托盘打开快捷翻译，或在设置中换一个快捷键。"); }
      }
      Health("ready");
    }
    static void Health(string state, string errorType = "") {
      if (Demo) return;
      // Last status only, in this build's cache. No source text, model output or credentials.
      try { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ui-status.json"), new JavaScriptSerializer().Serialize(new { state = state, errorType = errorType, processId = System.Diagnostics.Process.GetCurrentProcess().Id, selectionShortcut = HotkeyRegistered, quickShortcut = RegisteredQuickHotkey, at = DateTime.UtcNow.ToString("o") })); } catch {}
    }

    static TranslationWindow GetPanel(string surface) {
      if (surface == "quick") { if (QuickPanel == null) QuickPanel = new TranslationWindow("quick"); return QuickPanel; }
      if (SelectionPanel == null) SelectionPanel = new TranslationWindow("selection"); return SelectionPanel;
    }
    static void ShowQuick() { if (Config == null) return; HideToolbar(); GetPanel("quick").Show(); }
    static void Handle(Dictionary<string, object> message) {
      string type = Str(message, "type"), surface = Str(message, "surface", "selection");
      if (type == "shutdown") { Shutdown(); return; }
      if (Demo && type == "capture") {
        string which = Str(message, "surface");
        Window target = which == "toolbar" ? Toolbar : which == "settings" ? Settings : GetPanel(which).Window;
        if (target != null && target.IsVisible && PreviewDirectory != null) {
          target.UpdateLayout();
          double scale = Native.Scale(target);
          var bitmap = new RenderTargetBitmap((int)Math.Ceiling(target.ActualWidth * scale), (int)Math.Ceiling(target.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
          bitmap.Render(target); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
          Directory.CreateDirectory(PreviewDirectory);
          string captureName = Regex.Replace(Str(message, "name", which), "[^a-zA-Z0-9_-]", "");
          using (var output = new FileStream(Path.Combine(PreviewDirectory, captureName + ".png"), FileMode.CreateNew)) encoder.Save(output);
          Send("captured", "surface", which, "name", captureName, "width", bitmap.PixelWidth, "height", bitmap.PixelHeight);
        }
        return;
      }
      if (Demo && type == "show-settings") { ShowSettings(); return; }
      if (Demo && type == "show-quick") { ShowQuick(); return; }
      if (Demo && type == "demo-input") { GetPanel(surface).DemoInput(Str(message, "text")); return; }
      if (Demo && type == "demo-pin") { GetPanel(surface).DemoPin(); return; }
      if (Demo && type == "demo-monitors") { Send("demo-monitors", "screens", Forms.Screen.AllScreens.Select(s => new { x = s.WorkingArea.Left, y = s.WorkingArea.Top, width = s.WorkingArea.Width, height = s.WorkingArea.Height }).ToArray()); return; }
      if (Demo && type == "demo-submit") { GetPanel(surface).Submit(); return; }
      if (Demo && type == "demo-opacity") { GetPanel(surface).DemoOpacity(Num(message, "percent", 100)); return; }
      if (Demo && type == "demo-minimize") { GetPanel(surface).Window.WindowState = WindowState.Minimized; return; }
      if (Demo && type == "demo-stop") { GetPanel(surface).StopRun(); return; }
      if (Demo && type == "demo-close") { GetPanel(surface).Window.Close(); return; }
      if (Demo && type == "demo-state") {
        TranslationWindow panel = surface == "toolbar" ? null : GetPanel(surface);
        Window window = panel == null ? Toolbar : panel.Window;
        Native.Rect rect = new Native.Rect(); if (window != null) { window.UpdateLayout(); Native.GetWindowRect(Native.Handle(window), out rect); }
        Send("demo-state", "nonce", Str(message, "nonce"), "runId", panel == null ? null : panel.RunId, "busy", panel != null && panel.Busy, "visible", window != null && window.IsVisible, "answer", panel == null ? "" : panel.Answer,
          "x", rect.Left, "y", rect.Top, "width", rect.Right-rect.Left, "height", rect.Bottom-rect.Top, "dpi", window == null ? 96 : Native.Scale(window)*96, "topmost", window != null && window.Topmost, "minimized", window != null && window.WindowState == WindowState.Minimized, "opacityPercent", panel == null ? 100 : panel.OpacityPercent, "contentOpacity", window != null && window.Content is UIElement ? ((UIElement)window.Content).Opacity : 1, "input", panel == null ? "" : panel.InputText);
        return;
      }
      if (type == "settings") {
        Config = Obj(message, "settings"); ApplyTheme();
        object models; if (message.TryGetValue("models", out models) && models is object[]) { Models = (object[])models; PopulateModels(); }
        ToggleItem.Text = Bool(Config, "enabled") ? "暂停全局划词" : "启用全局划词";
        Tray.Text = "DSH 划词助手 · " + (Bool(Config, "enabled") ? "已启用" : "已暂停");
        UpdateHotkey();
        if (SelectionPanel != null) SelectionPanel.RefreshSettings(); if (QuickPanel != null) QuickPanel.RefreshSettings();
        if (SettingsStatus != null) SettingsStatus.Text = "设置已保存";
        if (SaveButton != null) SaveButton.IsEnabled = true;
        return;
      }
      if (type == "selection") { ShowToolbar(message); return; }
      if (type == "hide-toolbar") { HideToolbar(); return; }
      if (type == "pointer") {
        int x = Num(message, "x"), y = Num(message, "y");
        if (!Native.Contains(Toolbar, x, y)) { HideToolbar(); if (Str(message, "pendingId") != "") Send("dismiss", "id", Str(message, "pendingId")); }
        return;
      }
      if (type == "notice") { Notice(Str(message, "message")); return; }
      if (type == "begin") { HideToolbar(); GetPanel(surface).Begin(message); return; }
      var recipient = surface == "quick" ? QuickPanel : SelectionPanel;
      if (recipient != null) recipient.Receive(message);
    }

    static void ShowToolbar(Dictionary<string, object> message) {
      if (Config == null || !Bool(Config, "enabled")) return;
      HideToolbar(); SelectionId = Str(message, "id");
      if (Toolbar == null) {
        Toolbar = WindowBase(184, 48, true); Toolbar.Title = "DSH Selection Toolbar";
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        var grip = new Border { Width = 22, Height = 30, Child = Visuals.Icon("grip", 14, Muted), Cursor = Cursors.SizeAll, ToolTip = "拖动浮条" };
        grip.MouseLeftButtonDown += delegate { Toolbar.DragMove(); };
        row.Children.Add(grip);
        row.Children.Add(Visuals.IconButton("translate", delegate { Choose("translate"); }, "翻译", 32));
        row.Children.Add(Visuals.IconButton("explain", delegate { Choose("explain"); }, "解释", 32));
        row.Children.Add(Visuals.IconButton("summarize", delegate { Choose("summarize"); }, "总结", 32));
        row.Children.Add(new Border { Width = 1, Height = 16, Background = Line, Margin = new Thickness(4, 0, 4, 0) });
        row.Children.Add(Visuals.IconButton("settings", ShowSettings, "设置 / 暂停", 28));
        var shell = Visuals.Shell(row, 0, 10, 6, 8);
        Toolbar.Content = shell;
      }
      Visuals.Show(Toolbar); Toolbar.UpdateLayout(); Native.Place(Toolbar, Num(message, "x"), Num(message, "y")); Native.Present(Toolbar);
      string placedId = SelectionId;
      Toolbar.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new System.Action(delegate { if (Toolbar.IsVisible && SelectionId == placedId) Native.Place(Toolbar, Num(message, "x"), Num(message, "y")); }));
      ToolbarTimer.Interval = TimeSpan.FromMilliseconds(Num(message, "expiresMs", 5000)); ToolbarTimer.Start();
    }
    static void Choose(string action) {
      string id = SelectionId;
      // Submit before dismissing; otherwise the companion forgets the snapshot.
      Send("action", "id", id, "action", action); HideToolbar(false);
    }
    static void HideToolbar(bool notify = true) {
      if (ToolbarTimer != null) ToolbarTimer.Stop();
      if (Toolbar != null) Visuals.Hide(Toolbar, false);
      string dismissed = SelectionId; SelectionId = null;
      if (notify && dismissed != null) Send("dismiss", "id", dismissed);
    }

    static void Notice(string message) {
      if (SettingsStatus != null) SettingsStatus.Text = message;
      if (Tray != null) { Tray.BalloonTipTitle = "DSH 划词助手"; Tray.BalloonTipText = message; Tray.ShowBalloonTip(3500); }
      if (SaveButton != null) SaveButton.IsEnabled = true;
    }
    static void AddField(StackPanel stack, string label, UIElement control) { stack.Children.Add(Text(label, 12, Muted)); stack.Children.Add(control); }
    internal static ComboBox Combo(params string[] items) { var box = new ComboBox { MinHeight = 30, Margin = new Thickness(0, 2, 0, 7) }; foreach (var i in items) box.Items.Add(i); box.SelectedIndex = 0; return box; }
    static TextBox Edit(string value, bool multiline = false, double height = double.NaN) { return new TextBox { Text = value, AcceptsReturn = multiline, TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap, Height = height, VerticalScrollBarVisibility = multiline ? ScrollBarVisibility.Auto : ScrollBarVisibility.Hidden, Margin = new Thickness(0, 2, 0, 7) }; }

    internal static void ShowSettings() {
      HideToolbar();
      if (Config == null) return;
      if (Settings != null) { Settings.Show(); Settings.Activate(); Send("settings-get"); return; }
      Settings = new Window { Title = "DSH 划词助手 · 设置", Width = 560, Height = 620, MinWidth = 440, MinHeight = 480, WindowStartupLocation = WindowStartupLocation.CenterScreen, Background = Surface, FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI"), Foreground = Ink, Topmost = true };
      Settings.Closed += delegate { Settings = null; SettingsStatus = null; SaveButton = null; ModelBox = null; PromptBoxes.Clear(); };
      var dock = new DockPanel { Margin = new Thickness(18) };
      var title = Text("划词与翻译", 21); title.FontWeight = FontWeights.SemiBold; DockPanel.SetDock(title, Dock.Top); dock.Children.Add(title);
      var subtitle = Text("翻译 · 解释 · 总结 / 由你现有的 DSH 模型处理", 12, Muted); DockPanel.SetDock(subtitle, Dock.Top); dock.Children.Add(subtitle);
      var bottom = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
      SettingsStatus = Text("快捷翻译可在暂停划词时使用。", 11, Muted); bottom.Children.Add(SettingsStatus);
      var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
      buttons.Children.Add(Button("取消", delegate { Settings.Close(); }));
      SaveButton = Button("保存设置", SaveSettings); SaveButton.Background = Hover; buttons.Children.Add(SaveButton); bottom.Children.Add(buttons);
      DockPanel.SetDock(bottom, Dock.Bottom); dock.Children.Add(bottom);
      var tabs = new TabControl { Margin = new Thickness(0, 12, 0, 0), BorderBrush = Line };
      var general = new StackPanel { Margin = new Thickness(12) };
      EnabledBox = new CheckBox { Content = "启用全局划词", IsChecked = Bool(Config, "enabled"), Margin = new Thickness(0, 5, 0, 12) }; general.Children.Add(EnabledBox);
      TriggerBox = Combo("选中文字自动出现工具条", "仅使用 Ctrl+Alt+D 呼出工具条"); TriggerBox.SelectedIndex = Str(Config, "trigger") == "hotkey" ? 1 : 0; AddField(general, "触发方式（自动模式也支持快捷键）", TriggerBox);
      LanguageBox = Combo("自动中英互译 / 中文解释与总结", "简体中文", "English", "日本語", "한국어", "繁體中文");
      string target = Str(Config, "targetLanguage", "auto"); if (target != "auto") { LanguageBox.SelectedItem = target; if (LanguageBox.SelectedIndex < 0) { LanguageBox.Items.Add(target); LanguageBox.SelectedItem = target; } }
      AddField(general, "输出语言", LanguageBox);
      ThemeBox = Combo("跟随系统", "浅色", "深色"); ThemeBox.SelectedIndex = Str(Config, "theme") == "dark" ? 2 : Str(Config, "theme") == "light" ? 1 : 0; AddField(general, "外观", ThemeBox);
      QuickHotkeyBox = Combo("Ctrl+Alt+T", "Ctrl+Shift+T", "Alt+Shift+T", "关闭快捷键"); QuickHotkeyBox.SelectedIndex = Str(Config, "quickHotkey", "Ctrl+Alt+T") == "none" ? 3 : Math.Max(0, QuickHotkeyBox.Items.IndexOf(Str(Config, "quickHotkey", "Ctrl+Alt+T"))); AddField(general, "快捷翻译（也可从托盘打开）", QuickHotkeyBox);
      ModelBox = Combo("跟随 DSH 默认模型"); AddField(general, "模型目录（不复制 API Key）", ModelBox);
      ProviderBox = Edit(Str(Config, "provider")); ModelIdBox = Edit(Str(Config, "model"));
      AddField(general, "提供方 route ID（留空跟随默认）", ProviderBox); AddField(general, "模型 ID（与提供方成对填写）", ModelIdBox);
      ModelBox.SelectionChanged += delegate { var item = ModelBox.SelectedItem as ComboBoxItem; if (item == null || ProviderBox == null) return; var model = item.Tag as Dictionary<string, object>; ProviderBox.Text = Str(model, "provider"); ModelIdBox.Text = Str(model, "model"); };
      PopulateModels();
      tabs.Items.Add(new TabItem { Header = " 常规 ", Content = new ScrollViewer { Content = general, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });
      var prompts = new StackPanel { Margin = new Thickness(12) };
      prompts.Children.Add(Text("可使用 {{targetLanguage}}；留空恢复默认。选区会单独作为资料提交，不拼进系统指令。", 12, Muted));
      var values = Obj(Config, "prompts");
      foreach (var pair in new[] { new[] { "translate", "翻译" }, new[] { "explain", "解释" }, new[] { "summarize", "总结" } }) { var box = Edit(Str(values, pair[0]), true, 150); PromptBoxes[pair[0]] = box; AddField(prompts, pair[1], box); }
      tabs.Items.Add(new TabItem { Header = " 提示词 ", Content = new ScrollViewer { Content = prompts, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });
      var privacy = new StackPanel { Margin = new Thickness(12) };
      privacy.Children.Add(Text("选中时仅在本机暂存文字；点击翻译 / 解释 / 总结后，才发给 DSH 中配置的模型服务。插件不创建聊天会话、不保存原文或译文，不读取周围段落。模型服务及 DSH 的诊断策略另行适用。", 13));
      privacy.Children.Add(Text("暂停会停止取词钩子。黑名单用于排除密码管理器、终端等应用，但不能保证识别所有敏感输入。请勿在敏感内容上调用 AI。", 13));
      ClipboardBox = new CheckBox { Content = "允许剪贴板兜底（可能模拟 Ctrl+C 并触碰剪贴板）", IsChecked = Bool(Config, "clipboardFallback"), Margin = new Thickness(0, 12, 0, 6) }; privacy.Children.Add(ClipboardBox);
      privacy.Children.Add(Text("默认关闭。PDF 或特殊控件取不到时可尝试开启；不需要管理员权限，也不尝试跨越提权窗口边界。", 12, Muted));
      object blocked; string list = Config.TryGetValue("blockedApps", out blocked) && blocked is object[] ? string.Join("\n", ((object[])blocked).Select(Convert.ToString).ToArray()) : "";
      BlockedBox = Edit(list, true, 160); AddField(privacy, "应用黑名单（每行一个进程文件名）", BlockedBox);
      tabs.Items.Add(new TabItem { Header = " 隐私与取词 ", Content = new ScrollViewer { Content = privacy, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });
      dock.Children.Add(tabs); Settings.Content = dock; Settings.Show(); Send("settings-get");
    }
    static void PopulateModels() {
      if (ModelBox == null) return;
      ModelBox.Items.Clear();
      ModelBox.Items.Add(new ComboBoxItem { Content = "跟随 DSH 默认模型", Tag = new Dictionary<string, object>() });
      int chosen = 0;
      foreach (var raw in Models) {
        var model = raw as Dictionary<string, object>; if (model == null) continue;
        ModelBox.Items.Add(new ComboBoxItem { Content = Str(model, "provider") + " / " + Str(model, "name"), Tag = model });
        if (Str(model, "provider") == Str(Config, "provider") && Str(model, "model") == Str(Config, "model")) chosen = ModelBox.Items.Count - 1;
      }
      // Do not modify provider edits while asynchronously refreshing the catalog.
      var p = ProviderBox == null ? "" : ProviderBox.Text; var m = ModelIdBox == null ? "" : ModelIdBox.Text;
      ModelBox.SelectedIndex = chosen;
      if (ProviderBox != null) { ProviderBox.Text = p; ModelIdBox.Text = m; }
    }
    static void SaveSettings() {
      var prompts = new Dictionary<string, object>(); foreach (var pair in PromptBoxes) prompts[pair.Key] = pair.Value.Text;
      var patch = new Dictionary<string, object> {
        { "theme", ThemeBox.SelectedIndex == 2 ? "dark" : ThemeBox.SelectedIndex == 1 ? "light" : "system" },
        { "quickHotkey", QuickHotkeyBox.SelectedIndex == 3 ? "none" : Convert.ToString(QuickHotkeyBox.SelectedItem) },
        { "enabled", EnabledBox.IsChecked == true }, { "clipboardFallback", ClipboardBox.IsChecked == true },
        { "trigger", TriggerBox.SelectedIndex == 1 ? "hotkey" : "auto" },
        { "targetLanguage", LanguageBox.SelectedIndex == 0 ? "auto" : Convert.ToString(LanguageBox.SelectedItem) },
        { "provider", ProviderBox.Text.Trim() }, { "model", ModelIdBox.Text.Trim() },
        { "blockedApps", BlockedBox.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(s => s.Length > 0).ToArray() },
        { "prompts", prompts }
      };
      SettingsStatus.Text = "正在保存…"; SaveButton.IsEnabled = false; Send("settings-save", "settings", patch);
    }
    static void Shutdown() {
      if (Closing) return; Closing = true;
      if (HotkeyWindow != null) { Native.UnregisterHotKey(HotkeyWindow.Handle, 1); Native.UnregisterHotKey(HotkeyWindow.Handle, 2); HotkeyWindow.Dispose(); }
      if (Tray != null) { Tray.Visible = false; Tray.Dispose(); }
      if (ToolbarTimer != null) ToolbarTimer.Stop();
      App.Shutdown();
    }
  }
}
