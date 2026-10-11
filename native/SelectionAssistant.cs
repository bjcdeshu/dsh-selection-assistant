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
  internal static class Native {
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int height, uint flags);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out Rect r);
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
    public static void Place(Window w, int x, int y) {
      IntPtr h = Handle(w);
      // Move to the target monitor first so GetDpiForWindow is not the old DPI.
      SetWindowPos(h, new IntPtr(-1), x, y, 0, 0, 0x0011);
      double dpi = 1.0;
      try { dpi = GetDpiForWindow(h) / 96.0; } catch (EntryPointNotFoundException) {}
      if (dpi < 0.5) dpi = 1;
      var area = Forms.Screen.FromPoint(new System.Drawing.Point(x, y)).WorkingArea;
      int width = (int)Math.Ceiling(w.Width * dpi), height = (int)Math.Ceiling(w.Height * dpi);
      int nx = Math.Max(area.Left + 8, Math.Min(x + 8, area.Right - width - 8));
      int ny = y + 12;
      if (ny + height > area.Bottom - 8) ny = y - height - 12;
      ny = Math.Max(area.Top + 8, Math.Min(ny, area.Bottom - height - 8));
      SetWindowPos(h, new IntPtr(-1), nx, ny, width, height, 0x0010);
    }
  }

  internal class Program {
    static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = 262144 };
    static readonly object OutputLock = new object();
    static Application App;
    static Window Toolbar, Panel, Settings;
    static Forms.NotifyIcon Tray;
    static HwndSource HotkeyWindow;
    static DispatcherTimer ToolbarTimer, RenderTimer;
    static Dictionary<string, object> Config;
    static object[] Models = new object[0];
    static string SelectionId, RunId, Answer = "", Original = "", Action = "translate";
    static bool Closing, Busy, Initial = true, HotkeyRegistered;
    static bool Pinned = true, Demo;
    static string PreviewDirectory;
    static int AnchorX, AnchorY;
    static RichTextBox Result;
    static TextBlock Status, PanelTitle, SettingsStatus;
    static TextBox SourceText;
    static Button StopButton, PinButton, SaveButton;
    static Forms.ToolStripMenuItem ToggleItem;
    static CheckBox EnabledBox, ClipboardBox;
    static ComboBox TriggerBox, LanguageBox, ModelBox;
    static TextBox ProviderBox, ModelIdBox, BlockedBox;
    static Dictionary<string, TextBox> PromptBoxes = new Dictionary<string, TextBox>();
    static Brush Ink = Hex("#1D2935"), Muted = Hex("#687785"), Accent = Hex("#167D74"), Line = Hex("#DEE7E7"), Surface = Hex("#FFFFFF");

    static Brush Hex(string value) { return (Brush)new BrushConverter().ConvertFromString(value); }
    static string Str(Dictionary<string, object> d, string k, string fallback = "") { object v; return d != null && d.TryGetValue(k, out v) && v != null ? Convert.ToString(v) : fallback; }
    static bool Bool(Dictionary<string, object> d, string k) { object v; return d != null && d.TryGetValue(k, out v) && v is bool && (bool)v; }
    static int Num(Dictionary<string, object> d, string k, int fallback = 0) { int value; return int.TryParse(Str(d, k), out value) ? value : fallback; }
    static Dictionary<string, object> Obj(Dictionary<string, object> d, string k) { object v; return d != null && d.TryGetValue(k, out v) ? v as Dictionary<string, object> : null; }
    static void Send(string type, params object[] pairs) {
      if (Closing && type != "shutdown") return;
      var message = new Dictionary<string, object> { { "type", type } };
      for (int i = 0; i + 1 < pairs.Length; i += 2) message[(string)pairs[i]] = pairs[i + 1];
      try { lock (OutputLock) { Console.Out.WriteLine(Json.Serialize(message)); Console.Out.Flush(); } } catch { Shutdown(); }
    }

    [STAThread] public static void Main(string[] args) {
      Demo = args.Length > 0 && args[0] == "--demo";
      if (Demo && args.Length > 1) PreviewDirectory = Path.GetFullPath(args[1]);
      // A WinExe has no console code page. Open inherited pipes explicitly;
      // Console.InputEncoding/OutputEncoding would call SetConsoleCP and fail.
      Console.SetIn(new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false)));
      Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true });
      App = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
      App.DispatcherUnhandledException += delegate(object sender, DispatcherUnhandledExceptionEventArgs e) { e.Handled = true; Notice("界面操作未完成，请关闭小窗后重试。"); };
      App.Startup += delegate {
        SetupStyles(); SetupTray(); SetupHotkey();
        ToolbarTimer = new DispatcherTimer(); ToolbarTimer.Tick += delegate { HideToolbar(); };
        RenderTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
        RenderTimer.Tick += delegate { RenderTimer.Stop(); PaintAnswer(false); };
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
      var button = new Style(typeof(Button));
      button.Setters.Add(new Setter(Control.FontFamilyProperty, new FontFamily("Segoe UI, Microsoft YaHei UI")));
      button.Setters.Add(new Setter(Control.FontSizeProperty, 13.0));
      button.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 6, 10, 6)));
      button.Setters.Add(new Setter(Control.MarginProperty, new Thickness(2)));
      button.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
      button.Setters.Add(new Setter(Control.ForegroundProperty, Ink));
      button.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
      button.Setters.Add(new Setter(Control.CursorProperty, Cursors.Hand));
      var template = new ControlTemplate(typeof(Button));
      var border = new FrameworkElementFactory(typeof(Border)); border.Name = "chrome";
      border.SetValue(Border.CornerRadiusProperty, new CornerRadius(7));
      border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
      var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
      presenter.SetValue(FrameworkElement.MarginProperty, new Thickness(9, 6, 9, 6));
      presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
      border.AppendChild(presenter); template.VisualTree = border;
      var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
      hover.Setters.Add(new Setter(Border.BackgroundProperty, Hex("#E8F3F0"), "chrome")); template.Triggers.Add(hover);
      button.Setters.Add(new Setter(Control.TemplateProperty, template)); App.Resources.Add(typeof(Button), button);
      var textBox = new Style(typeof(TextBox));
      textBox.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8)));
      textBox.Setters.Add(new Setter(Control.BorderBrushProperty, Line));
      textBox.Setters.Add(new Setter(Control.FontSizeProperty, 13.0));
      App.Resources.Add(typeof(TextBox), textBox);
    }

    static Window WindowBase(double width, double height, bool noActivate) {
      var w = new Window { Width = width, Height = height, WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent, ShowInTaskbar = false, Topmost = true, ShowActivated = !noActivate, ResizeMode = ResizeMode.NoResize, FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI"), Foreground = Ink };
      w.SourceInitialized += delegate { Native.ToolWindow(w, noActivate); };
      return w;
    }
    static Border Card(UIElement child, int padding = 8) { return new Border { Background = Surface, BorderBrush = Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(padding), Child = child }; }
    static Button Button(string text, System.Action click, string help = null) { var b = new Button { Content = text, ToolTip = help ?? text }; b.Click += delegate { click(); }; return b; }
    static TextBlock Text(string text, double size = 13, Brush color = null) { return new TextBlock { Text = text, FontSize = size, Foreground = color ?? Ink, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 4, 2, 4) }; }

    static void SetupTray() {
      Tray = new Forms.NotifyIcon { Icon = System.Drawing.SystemIcons.Information, Text = "DSH 划词助手 · 等待启用", Visible = true };
      var menu = new Forms.ContextMenuStrip();
      ToggleItem = new Forms.ToolStripMenuItem("启用全局划词");
      ToggleItem.Click += delegate { if (Config == null) return; if (!Bool(Config, "enabled")) ShowSettings(); else Send("settings-save", "settings", new Dictionary<string, object> { { "enabled", false } }); };
      menu.Items.Add(ToggleItem);
      menu.Items.Add("设置 / 提示词", null, delegate { ShowSettings(); });
      menu.Items.Add("显示当前结果", null, delegate { if (Panel != null) { Panel.Show(); Panel.Activate(); } });
      menu.Items.Add(new Forms.ToolStripSeparator());
      menu.Items.Add("退出划词助手", null, delegate { Send("shutdown"); Shutdown(); });
      Tray.ContextMenuStrip = menu; Tray.DoubleClick += delegate { ShowSettings(); };
    }
    static void SetupHotkey() {
      var p = new HwndSourceParameters("DSH Selection Hotkey") { ParentWindow = new IntPtr(-3), WindowStyle = 0 };
      HotkeyWindow = new HwndSource(p);
      HotkeyWindow.AddHook(delegate(IntPtr h, int msg, IntPtr wp, IntPtr lp, ref bool handled) {
        if (msg == 0x0312 && wp.ToInt32() == 1) { Send("hotkey"); handled = true; }
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
    }

    static void Handle(Dictionary<string, object> message) {
      string type = Str(message, "type");
      if (type == "shutdown") { Shutdown(); return; }
      if (Demo && type == "capture") {
        string which = Str(message, "surface");
        Window target = which == "toolbar" ? Toolbar : which == "settings" ? Settings : Panel;
        if (target != null && target.IsVisible && PreviewDirectory != null) {
          target.UpdateLayout();
          var content = (FrameworkElement)target;
          var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
          bitmap.Render(content); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
          Directory.CreateDirectory(PreviewDirectory);
          using (var output = new FileStream(Path.Combine(PreviewDirectory, which + ".png"), FileMode.CreateNew)) encoder.Save(output);
          Send("captured", "surface", which, "width", bitmap.PixelWidth, "height", bitmap.PixelHeight);
        }
        return;
      }
      if (Demo && type == "show-settings") { ShowSettings(); return; }
      if (Demo && type == "demo-stop") { if (StopButton != null) StopButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)); return; }
      if (Demo && type == "demo-close") { if (Panel != null) Panel.Close(); return; }
      if (Demo && type == "demo-state") { Send("demo-state", "nonce", Str(message, "nonce"), "runId", RunId, "busy", Busy, "visible", Panel != null && Panel.IsVisible, "answer", Answer); return; }
      if (type == "settings") {
        Config = Obj(message, "settings");
        object models; if (message.TryGetValue("models", out models) && models is object[]) { Models = (object[])models; PopulateModels(); }
        ToggleItem.Text = Bool(Config, "enabled") ? "暂停全局划词" : "启用全局划词";
        Tray.Text = "DSH 划词助手 · " + (Bool(Config, "enabled") ? "已启用" : "已暂停");
        UpdateHotkey();
        if (SettingsStatus != null) SettingsStatus.Text = "设置已同步 · API 密钥仍由 DSH 管理";
        if (SaveButton != null) SaveButton.IsEnabled = true;
        if (Initial) { Initial = false; if (!Bool(Config, "enabled")) ShowSettings(); }
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
      if (type == "begin") {
        HideToolbar(); RunId = Str(message, "id"); Original = Str(message, "text"); Action = Str(message, "action"); Answer = ""; Busy = true;
        AnchorX = Num(message, "x"); AnchorY = Num(message, "y"); ShowPanel(); return;
      }
      if (Str(message, "id") != RunId || Panel == null) return;
      if (type == "started") { var route = Obj(message, "route"); Status.Text = "正在生成 · " + Str(route, "model"); }
      else if (type == "delta") { Answer += Str(message, "text"); if (!RenderTimer.IsEnabled) RenderTimer.Start(); }
      else if (type == "done") { Busy = false; RenderTimer.Stop(); PaintAnswer(true); Status.Text = Bool(message, "truncated") ? "达到输出上限 · 可选择更短的文本" : "已完成 · 仅处理本次选区"; StopButton.Visibility = Visibility.Collapsed; }
      else if (type == "failed") { Busy = false; RenderTimer.Stop(); PaintAnswer(true); Status.Text = Str(message, "message"); StopButton.Visibility = Visibility.Collapsed; }
    }

    static void ShowToolbar(Dictionary<string, object> message) {
      if (Config == null || !Bool(Config, "enabled")) return;
      HideToolbar(); SelectionId = Str(message, "id");
      if (Toolbar == null) {
        Toolbar = WindowBase(302, 52, true); Toolbar.Title = "DSH Selection Toolbar";
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        row.Children.Add(Button("译  翻译", delegate { Choose("translate"); }));
        row.Children.Add(Button("释  解释", delegate { Choose("explain"); }));
        row.Children.Add(Button("摘  总结", delegate { Choose("summarize"); }));
        row.Children.Add(Button("⚙", ShowSettings, "设置 / 暂停"));
        Toolbar.Content = Card(row, 3);
      }
      Toolbar.Show(); Native.Place(Toolbar, Num(message, "x"), Num(message, "y"));
      ToolbarTimer.Interval = TimeSpan.FromMilliseconds(Num(message, "expiresMs", 5000)); ToolbarTimer.Start();
    }
    static void Choose(string action) {
      string id = SelectionId;
      // Submit before dismissing; otherwise the companion forgets the snapshot.
      Send("action", "id", id, "action", action); HideToolbar(false);
    }
    static void HideToolbar(bool notify = true) {
      if (ToolbarTimer != null) ToolbarTimer.Stop();
      if (Toolbar != null) Toolbar.Hide();
      string dismissed = SelectionId; SelectionId = null;
      if (notify && dismissed != null) Send("dismiss", "id", dismissed);
    }

    static void ShowPanel() {
      if (Panel == null) {
        Panel = WindowBase(460, 460, false); Panel.Title = "DSH Selection Result"; Panel.ShowActivated = false; Panel.MinWidth = 340; Panel.MinHeight = 250; Panel.ResizeMode = ResizeMode.CanResizeWithGrip;
        Panel.SourceInitialized += delegate { Native.ToolWindow(Panel, false); };
        var dock = new DockPanel();
        var header = new DockPanel { LastChildFill = true, Margin = new Thickness(3, 0, 3, 6) };
        var close = Button("✕", ClosePanel, "关闭并停止当前请求"); DockPanel.SetDock(close, Dock.Right); header.Children.Add(close);
        var settings = Button("⚙", ShowSettings); DockPanel.SetDock(settings, Dock.Right); header.Children.Add(settings);
        PinButton = Button("已置顶", delegate { Pinned = !Pinned; Panel.Topmost = Pinned; PinButton.Content = Pinned ? "已置顶" : "置顶"; }, "切换置顶；结果保留到手动关闭"); DockPanel.SetDock(PinButton, Dock.Right); header.Children.Add(PinButton);
        PanelTitle = Text("划词助手", 15, Accent); PanelTitle.FontWeight = FontWeights.SemiBold;
        PanelTitle.MouseLeftButtonDown += delegate { if (Mouse.LeftButton == MouseButtonState.Pressed) Panel.DragMove(); };
        header.Children.Add(PanelTitle); DockPanel.SetDock(header, Dock.Top); dock.Children.Add(header);
        SourceText = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 84, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = Hex("#F3F7F6"), BorderThickness = new Thickness(0) };
        var source = new Expander { Header = "原文", Content = SourceText, Margin = new Thickness(3, 0, 3, 8), Foreground = Muted };
        DockPanel.SetDock(source, Dock.Top); dock.Children.Add(source);
        var footer = new DockPanel { Margin = new Thickness(2, 8, 2, 1) };
        var copy = Button("复制", delegate { try { Clipboard.SetText(Answer); Status.Text = "已复制"; } catch { Status.Text = "剪贴板暂时忙，请重试"; } }); DockPanel.SetDock(copy, Dock.Right); footer.Children.Add(copy);
        StopButton = Button("停止", delegate { Send("cancel", "id", RunId); RunId = null; Busy = false; RenderTimer.Stop(); PaintAnswer(true); Status.Text = "已停止"; StopButton.Visibility = Visibility.Collapsed; }); DockPanel.SetDock(StopButton, Dock.Right); footer.Children.Add(StopButton);
        Status = Text("正在连接模型…", 11, Muted); Status.VerticalAlignment = VerticalAlignment.Center; footer.Children.Add(Status); DockPanel.SetDock(footer, Dock.Bottom); dock.Children.Add(footer);
        Result = new RichTextBox { IsReadOnly = true, IsDocumentEnabled = false, BorderThickness = new Thickness(0), Background = Brushes.Transparent, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(3) };
        dock.Children.Add(Result); Panel.Content = Card(dock, 12);
        Panel.PreviewKeyDown += delegate(object s, KeyEventArgs e) { if (e.Key == Key.Escape) { ClosePanel(); e.Handled = true; } };
        Panel.PreviewMouseLeftButtonDown += ResizePanel;
        // Alt+F4 must have the same cancellation/cleanup semantics as our X.
        Panel.Closing += delegate(object s, System.ComponentModel.CancelEventArgs e) { if (!Closing) { e.Cancel = true; ClosePanel(); } };
      }
      PanelTitle.Text = Action == "translate" ? "翻译" : Action == "explain" ? "解释" : "总结";
      SourceText.Text = Original; Status.Text = "正在连接 DSH 模型…"; StopButton.Visibility = Visibility.Visible;
      PaintAnswer(false); Panel.Show(); Native.Place(Panel, AnchorX, AnchorY);
      Native.SetWindowPos(Native.Handle(Panel), new IntPtr(Pinned ? -1 : -2), 0, 0, 0, 0, 0x0013);
    }
    static void ResizePanel(object sender, MouseButtonEventArgs e) {
      var p = e.GetPosition(Panel); double w = Panel.ActualWidth, h = Panel.ActualHeight;
      bool left = p.X < 5, right = p.X > w - 5, top = p.Y < 5, bottom = p.Y > h - 5;
      int hit = top && left ? 13 : top && right ? 14 : bottom && left ? 16 : bottom && right ? 17 : left ? 10 : right ? 11 : top ? 12 : bottom ? 15 : 0;
      if (hit != 0) { Native.ReleaseCapture(); Native.SendMessage(Native.Handle(Panel), 0x00A1, new IntPtr(hit), IntPtr.Zero); e.Handled = true; }
    }
    static void ClosePanel() {
      if (Busy) Send("cancel", "id", RunId);
      Busy = false; RunId = null; Answer = ""; Original = "";
      if (RenderTimer != null) RenderTimer.Stop();
      if (Panel != null) { Result.Document.Blocks.Clear(); SourceText.Clear(); Panel.Hide(); }
    }
    static void PaintAnswer(bool markdown) {
      if (Result == null) return;
      bool atBottom = Result.VerticalOffset + Result.ViewportHeight >= Result.ExtentHeight - 30;
      var doc = new FlowDocument { FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI"), FontSize = Num(Config, "fontSize", 15), Foreground = Ink, PagePadding = new Thickness(0), LineHeight = Num(Config, "fontSize", 15) * 1.65 };
      if (!markdown) doc.Blocks.Add(new Paragraph(new Run(Answer)) { Margin = new Thickness(0) });
      else {
        bool code = false;
        foreach (string raw in Answer.Replace("\r", "").Split('\n')) {
          if (raw.TrimStart().StartsWith("```")) { code = !code; continue; }
          string line = raw;
          var p = new Paragraph { Margin = new Thickness(0, 0, 0, 5) };
          if (code) { p.FontFamily = new FontFamily("Consolas"); p.FontSize = 13; p.Background = Hex("#F3F6F7"); p.Inlines.Add(new Run(line)); }
          else {
            var heading = Regex.Match(line, @"^(#{1,4})\s+(.+)$");
            if (heading.Success) { line = heading.Groups[2].Value; p.FontSize = 20 - heading.Groups[1].Value.Length; p.FontWeight = FontWeights.SemiBold; }
            line = Regex.Replace(line, @"^\s*[-*]\s+", "• ");
            if (line.StartsWith("> ")) { line = line.Substring(2); p.BorderBrush = Line; p.BorderThickness = new Thickness(3, 0, 0, 0); p.Padding = new Thickness(8, 0, 0, 0); p.Foreground = Muted; }
            foreach (string part in Regex.Split(line, @"(\*\*[^*]+\*\*|`[^`]+`)") ) {
              if (part.StartsWith("**") && part.EndsWith("**") && part.Length >= 4) p.Inlines.Add(new Bold(new Run(part.Substring(2, part.Length - 4))));
              else if (part.StartsWith("`") && part.EndsWith("`") && part.Length >= 2) p.Inlines.Add(new Run(part.Substring(1, part.Length - 2)) { FontFamily = new FontFamily("Consolas"), Background = Hex("#EDF3F2") });
              else p.Inlines.Add(new Run(part));
            }
          }
          doc.Blocks.Add(p);
        }
      }
      Result.Document = doc; if (atBottom) Result.ScrollToEnd();
    }

    static void Notice(string message) {
      if (SettingsStatus != null) SettingsStatus.Text = message;
      if (Status != null && Panel != null && Panel.IsVisible) Status.Text = message;
      if (Tray != null) { Tray.BalloonTipTitle = "DSH 划词助手"; Tray.BalloonTipText = message; Tray.ShowBalloonTip(3500); }
      if (SaveButton != null) SaveButton.IsEnabled = true;
    }
    static void AddField(StackPanel stack, string label, UIElement control) { stack.Children.Add(Text(label, 12, Muted)); stack.Children.Add(control); }
    static ComboBox Combo(params string[] items) { var box = new ComboBox { MinHeight = 30, Margin = new Thickness(0, 2, 0, 7) }; foreach (var i in items) box.Items.Add(i); box.SelectedIndex = 0; return box; }
    static TextBox Edit(string value, bool multiline = false, double height = double.NaN) { return new TextBox { Text = value, AcceptsReturn = multiline, TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap, Height = height, VerticalScrollBarVisibility = multiline ? ScrollBarVisibility.Auto : ScrollBarVisibility.Hidden, Margin = new Thickness(0, 2, 0, 7) }; }

    static void ShowSettings() {
      HideToolbar();
      if (Config == null) return;
      if (Settings != null) { Settings.Show(); Settings.Activate(); Send("settings-get"); return; }
      Settings = new Window { Title = "DSH 划词助手 · 设置", Width = 560, Height = 620, MinWidth = 440, MinHeight = 480, WindowStartupLocation = WindowStartupLocation.CenterScreen, Background = Surface, FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI"), Foreground = Ink, Topmost = true };
      Settings.Closed += delegate { Settings = null; SettingsStatus = null; SaveButton = null; ModelBox = null; PromptBoxes.Clear(); };
      var dock = new DockPanel { Margin = new Thickness(18) };
      var title = Text("少一个后台，多一点顺手。", 21, Accent); title.FontWeight = FontWeights.SemiBold; DockPanel.SetDock(title, Dock.Top); dock.Children.Add(title);
      var subtitle = Text("翻译 · 解释 · 总结 / 由你现有的 DSH 模型处理", 12, Muted); DockPanel.SetDock(subtitle, Dock.Top); dock.Children.Add(subtitle);
      var bottom = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
      SettingsStatus = Text("首次使用请阅读隐私说明，并主动勾选启用。", 11, Muted); bottom.Children.Add(SettingsStatus);
      var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
      buttons.Children.Add(Button("取消", delegate { Settings.Close(); }));
      SaveButton = Button("保存设置", SaveSettings); SaveButton.Background = Hex("#E8F3F0"); buttons.Children.Add(SaveButton); bottom.Children.Add(buttons);
      DockPanel.SetDock(bottom, Dock.Bottom); dock.Children.Add(bottom);
      var tabs = new TabControl { Margin = new Thickness(0, 12, 0, 0), BorderBrush = Line };
      var general = new StackPanel { Margin = new Thickness(12) };
      EnabledBox = new CheckBox { Content = "启用全局划词监听（我了解选区处理方式）", IsChecked = Bool(Config, "enabled"), Margin = new Thickness(0, 5, 0, 12) }; general.Children.Add(EnabledBox);
      TriggerBox = Combo("选中文字自动出现工具条", "仅使用 Ctrl+Alt+D 呼出工具条"); TriggerBox.SelectedIndex = Str(Config, "trigger") == "hotkey" ? 1 : 0; AddField(general, "触发方式（自动模式也支持快捷键）", TriggerBox);
      LanguageBox = Combo("自动中英互译 / 中文解释与总结", "简体中文", "English", "日本語", "한국어", "繁體中文");
      string target = Str(Config, "targetLanguage", "auto"); if (target != "auto") { LanguageBox.SelectedItem = target; if (LanguageBox.SelectedIndex < 0) { LanguageBox.Items.Add(target); LanguageBox.SelectedItem = target; } }
      AddField(general, "输出语言", LanguageBox);
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
      if (HotkeyWindow != null) { Native.UnregisterHotKey(HotkeyWindow.Handle, 1); HotkeyWindow.Dispose(); }
      if (Tray != null) { Tray.Visible = false; Tray.Dispose(); }
      if (ToolbarTimer != null) ToolbarTimer.Stop();
      if (RenderTimer != null) RenderTimer.Stop();
      App.Shutdown();
    }
  }
}
