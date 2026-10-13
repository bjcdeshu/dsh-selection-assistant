using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using P = DshSelectionAssistant.Program;

namespace DshSelectionAssistant {
  internal sealed class TranslationWindow {
    internal readonly Window Window;
    internal readonly string Surface;
    internal string RunId, Original = "", Action = "translate";
    internal bool Busy;
    internal string Answer { get { return Buffer.ToString(); } }
    internal string InputText { get { return Input.Text; } }
    readonly StringBuilder Buffer = new StringBuilder();
    readonly TextBox Input;
    readonly RichTextBox Result;
    readonly TextBlock Status, Title, Count, Route;
    readonly ComboBox Language;
    readonly Button Stop, Translate, Retry, Copy, Pin;
    readonly DispatcherTimer RenderTimer;
    Run StreamingRun;
    bool Pinned, Moved, Resized, HasPosition, Awaiting;
    string PendingId, LastDefaultLanguage;
    int AnchorX, AnchorY;
    double LastAutoHeight;

    internal TranslationWindow(string surface) {
      Surface = surface;
      bool quick = surface == "quick";
      Window = P.WindowBase(quick ? 740 : 456, quick ? 500 : 280, false, quick);
      Window.Title = quick ? "DSH 快捷翻译" : "DSH 划词结果";
      Window.ShowActivated = quick; Window.Topmost = false;
      Window.MinWidth = quick ? 620 : 360; Window.MinHeight = quick ? 380 : 230;
      Window.ResizeMode = ResizeMode.CanResizeWithGrip;
      var layout = new DockPanel { LastChildFill = true };
      var header = new DockPanel { Margin = new Thickness(0, 0, 0, 10), LastChildFill = true };
      var close = P.Button("×", Close, "关闭 · Esc"); close.FontSize = 19;
      DockPanel.SetDock(close, Dock.Right); header.Children.Add(close);
      var settings = P.Button("⋯", P.ShowSettings, "设置"); DockPanel.SetDock(settings, Dock.Right); header.Children.Add(settings);
      Pin = P.Button("固定", delegate { Pinned = !Pinned; Window.Topmost = Pinned; Pin.Foreground = Pinned ? P.Accent : P.Muted; Pin.Content = Pinned ? "已固定" : "固定"; }, "固定窗口位置并置顶");
      DockPanel.SetDock(Pin, Dock.Right); header.Children.Add(Pin);
      Title = P.Text(quick ? "快捷翻译" : "翻译", 16); Title.FontWeight = FontWeights.SemiBold;
      Title.VerticalAlignment = VerticalAlignment.Center;
      Title.MouseLeftButtonDown += delegate { if (Mouse.LeftButton == MouseButtonState.Pressed) { Moved = true; Window.DragMove(); } };
      header.Children.Add(Title); DockPanel.SetDock(header, Dock.Top); layout.Children.Add(header);

      var footer = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
      var actions = new StackPanel { Orientation = Orientation.Horizontal };
      Translate = P.Button("翻译", Submit, "翻译 · Ctrl+Enter"); Translate.Background = P.Accent; Translate.Foreground = Brushes.White; Translate.Tag = "primary"; Translate.MinWidth = 72;
      Translate.Visibility = quick ? Visibility.Visible : Visibility.Collapsed;
      Retry = P.Button("重试", Submit, "重新处理原文");
      Copy = P.Button("复制", CopyAnswer, "复制译文");
      Stop = P.Button("停止", StopRun, "停止生成"); Stop.Visibility = Visibility.Collapsed;
      actions.Children.Add(Copy); actions.Children.Add(Retry); actions.Children.Add(Stop); actions.Children.Add(Translate);
      DockPanel.SetDock(actions, Dock.Right); footer.Children.Add(actions);
      Status = P.Text(quick ? "输入或粘贴文字" : "正在连接…", 11, P.Muted); Status.MaxWidth = quick ? 320 : 205;
      Status.VerticalAlignment = VerticalAlignment.Center; Status.HorizontalAlignment = HorizontalAlignment.Left; footer.Children.Add(Status);
      DockPanel.SetDock(footer, Dock.Bottom); layout.Children.Add(footer);

      Route = P.Text("", 11, P.Muted); Route.TextTrimming = TextTrimming.CharacterEllipsis; Route.TextWrapping = TextWrapping.NoWrap;
      DockPanel.SetDock(Route, Dock.Bottom); layout.Children.Add(Route);
      Input = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, BorderThickness = new Thickness(0), FontSize = P.Num(P.Config, "fontSize", 15), Background = P.Soft, Foreground = P.Ink, IsReadOnly = !quick, Padding = new Thickness(12), IsUndoEnabled = quick };
      Input.SetValue(System.Windows.Automation.AutomationProperties.NameProperty, quick ? "待翻译文字" : "原文");
      Result = new RichTextBox { IsReadOnly = true, IsDocumentEnabled = false, IsUndoEnabled = false, BorderThickness = new Thickness(0), Background = Brushes.Transparent, Foreground = P.Ink, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(4, 8, 4, 8) };
      Result.SetValue(System.Windows.Automation.AutomationProperties.NameProperty, "译文或结果");
      Count = P.Text("0 字", 11, P.Muted);
      if (quick) {
        var languages = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        var langLabel = P.Text("自动识别  →", 13, P.Muted); langLabel.VerticalAlignment = VerticalAlignment.Center; languages.Children.Add(langLabel);
        Language = P.Combo("自动中英互译", "简体中文", "English", "日本語", "한국어", "繁體中文"); Language.Width = 165; Language.Margin = new Thickness(10, 0, 0, 0); Language.HorizontalAlignment = HorizontalAlignment.Left;
        Language.SetValue(System.Windows.Automation.AutomationProperties.NameProperty, "目标语言");
        languages.Children.Add(Language); DockPanel.SetDock(languages, Dock.Top); layout.Children.Add(languages);
        var columns = new Grid(); columns.ColumnDefinitions.Add(new ColumnDefinition()); columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) }); columns.ColumnDefinitions.Add(new ColumnDefinition());
        var source = new DockPanel();
        var sourceHead = new DockPanel { Margin = new Thickness(2, 0, 2, 5) };
        var clear = P.Button("清空", Clear, "清空原文与译文"); DockPanel.SetDock(clear, Dock.Right); sourceHead.Children.Add(clear);
        var paste = P.Button("粘贴", Paste, "读取剪贴板中的文字"); DockPanel.SetDock(paste, Dock.Right); sourceHead.Children.Add(paste);
        sourceHead.Children.Add(P.Text("原文", 12, P.Muted)); DockPanel.SetDock(sourceHead, Dock.Top); source.Children.Add(sourceHead);
        DockPanel.SetDock(Count, Dock.Bottom); source.Children.Add(Count);
        source.Children.Add(new Border { Background = P.Soft, CornerRadius = new CornerRadius(8), Child = Input });
        columns.Children.Add(source);
        var translated = new DockPanel(); var label = P.Text("译文", 12, P.Muted); label.Height = 35; DockPanel.SetDock(label, Dock.Top); translated.Children.Add(label);
        translated.Children.Add(Result); Grid.SetColumn(translated, 2); columns.Children.Add(translated); layout.Children.Add(columns);
        Input.TextChanged += delegate {
          Count.Text = Input.Text.Length + " / " + P.Num(P.Config, "maxChars", 12000) + " 字";
          UpdateButtons();
        };
      } else {
        Input.MaxHeight = 94;
        var source = new Expander { Header = "原文", Content = Input, Foreground = P.Muted, Margin = new Thickness(2, 0, 2, 8) };
        DockPanel.SetDock(source, Dock.Top); layout.Children.Add(source); layout.Children.Add(Result);
      }
      Window.Content = P.Card(layout, 16);
      Window.PreviewKeyDown += delegate(object sender, KeyEventArgs e) {
        if (e.Key == Key.Escape) { Close(); e.Handled = true; }
        else if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) != 0) { Submit(); e.Handled = true; }
      };
      Window.PreviewMouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e) {
        var p = e.GetPosition(Window); double w = Window.ActualWidth, h = Window.ActualHeight;
        bool left = p.X < 5, right = p.X > w - 5, top = p.Y < 5, bottom = p.Y > h - 5;
        int hit = top && left ? 13 : top && right ? 14 : bottom && left ? 16 : bottom && right ? 17 : left ? 10 : right ? 11 : top ? 12 : bottom ? 15 : 0;
        if (hit != 0) { Resized = true; Native.ReleaseCapture(); Native.SendMessage(Native.Handle(Window), 0x00A1, new IntPtr(hit), IntPtr.Zero); e.Handled = true; }
      };
      Window.Closing += delegate(object sender, System.ComponentModel.CancelEventArgs e) { if (!P.Closing) { e.Cancel = true; Close(); } };
      RenderTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
      RenderTimer.Tick += delegate { RenderTimer.Stop(); Paint(false); };
      RefreshSettings(); ResetDocument(); UpdateButtons();
    }
    internal void RefreshSettings() {
      Route.Text = P.Str(P.Config, "model") == "" ? "DSH 默认模型" : P.Str(P.Config, "model");
      Input.FontSize = P.Num(P.Config, "fontSize", 15);
      if (Language != null && !Busy && LastDefaultLanguage != P.Str(P.Config, "targetLanguage", "auto")) {
        string language = P.Str(P.Config, "targetLanguage", "auto");
        LastDefaultLanguage = language;
        string label = language == "auto" ? "自动中英互译" : language;
        if (!Language.Items.Contains(label)) Language.Items.Add(label);
        Language.SelectedItem = label;
      }
      UpdateButtons();
    }
    internal void Show() {
      Window.Show(); Window.UpdateLayout();
      if (!HasPosition) { Native.Point p; Native.GetCursorPos(out p); Native.Place(Window, p.X, p.Y, true); HasPosition = true; }
      else Native.KeepOnScreen(Window);
      Window.Activate(); if (Surface == "quick") Input.Focus();
    }
    internal void Begin(Dictionary<string, object> message) {
      if (P.Str(message, "source") == "manual" && P.Str(message, "id") != PendingId) { P.Send("cancel", "id", P.Str(message, "id")); return; }
      PendingId = null;
      bool wasVisible = Window.IsVisible;
      RenderTimer.Stop(); RunId = P.Str(message, "id"); Original = P.Str(message, "text"); Action = P.Str(message, "action", "translate");
      Awaiting = false; Busy = true; Buffer.Clear(); ResetDocument();
      if (Surface == "selection") { Input.Text = Original; Title.Text = Action == "translate" ? "翻译" : Action == "explain" ? "解释" : "总结"; }
      Status.Text = "正在连接模型…"; UpdateButtons();
      if (Surface == "quick") { if (!wasVisible) Show(); return; }
      if (!Resized) { Window.Height = 280; LastAutoHeight = 280; }
      Window.Show(); Window.UpdateLayout();
      if (P.Str(message, "source") != "manual" && (!wasVisible || (!Pinned && !Moved))) {
        AnchorX = P.Num(message, "x"); AnchorY = P.Num(message, "y");
        Native.Place(Window, AnchorX, AnchorY); HasPosition = true;
      }
      else Native.KeepOnScreen(Window);
    }
    internal void Receive(Dictionary<string, object> message) {
      string type = P.Str(message, "type");
      if (type == "input-error") { if (P.Str(message, "id") != PendingId) return; Awaiting = false; PendingId = null; Status.Text = P.Str(message, "message"); UpdateButtons(); return; }
      if (RunId == null || P.Str(message, "id") != RunId) return;
      if (type == "started") { Route.Text = P.Str(P.Obj(message, "route"), "model"); Status.Text = "正在生成…"; }
      else if (type == "delta") { Buffer.Append(P.Str(message, "text")); if (!RenderTimer.IsEnabled) RenderTimer.Start(); }
      else if (type == "done" || type == "failed") {
        Busy = false; RunId = null; RenderTimer.Stop(); Paint(true);
        Status.Text = type == "failed" ? P.Str(message, "message") : P.Bool(message, "truncated") ? "已达到输出上限" : "已完成";
        UpdateButtons();
      }
    }
    internal void Submit() {
      if (Awaiting) return;
      string text = Surface == "quick" ? Input.Text : Original;
      if (string.IsNullOrWhiteSpace(text)) { Status.Text = "请先输入或粘贴文字"; return; }
      if (text.Length > P.Num(P.Config, "maxChars", 12000)) { Status.Text = "文字过长，请缩短后重试"; return; }
      StopRun(false); Awaiting = true; PendingId = Guid.NewGuid().ToString("N"); Status.Text = "正在提交…"; UpdateButtons();
      string target = Language == null ? P.Str(P.Config, "targetLanguage", "auto") : Language.SelectedIndex == 0 ? "auto" : Convert.ToString(Language.SelectedItem);
      P.Send("submit", "id", PendingId, "surface", Surface, "text", text, "action", Surface == "quick" ? "translate" : Action, "targetLanguage", target);
    }
    internal void StopRun() { StopRun(true); }
    void StopRun(bool paint) {
      bool wasBusy = Busy;
      if (RunId != null) P.Send("cancel", "id", RunId);
      if (PendingId != null) P.Send("cancel", "id", PendingId);
      PendingId = null; RunId = null; Busy = false; Awaiting = false; RenderTimer.Stop(); if (paint && wasBusy) Paint(true); if (wasBusy) Status.Text = "已停止"; UpdateButtons();
    }
    void Paste() {
      try {
        if (!Clipboard.ContainsText()) { Status.Text = Clipboard.ContainsImage() ? "剪贴板是图片，请先用 OCR 提取文字" : "剪贴板中没有文字"; return; }
        string text = Clipboard.GetText();
        if (string.IsNullOrWhiteSpace(text)) { Status.Text = "剪贴板文字为空"; return; }
        if (text.Length > P.Num(P.Config, "maxChars", 12000)) { Status.Text = "剪贴板文字过长，请分段粘贴"; return; }
        Input.Text = text; Input.CaretIndex = Input.Text.Length; Input.Focus(); Status.Text = "已粘贴，点击翻译开始";
      } catch { Status.Text = "剪贴板暂时忙，请重试"; }
    }
    void CopyAnswer() { if (Buffer.Length == 0) return; try { Clipboard.SetText(Answer); Status.Text = "已复制"; } catch { Status.Text = "剪贴板暂时忙，请重试"; } }
    internal void Clear() { StopRun(false); Input.Clear(); Original = ""; Buffer.Clear(); ResetDocument(); Status.Text = "输入或粘贴文字"; UpdateButtons(); }
    internal void Close() {
      StopRun(false); Window.Hide();
      // Quick input and output live only in this process until Clear or exit.
      if (Surface == "selection") { Input.Clear(); Original = ""; Buffer.Clear(); ResetDocument(); Moved = false; HasPosition = false; }
    }
    void UpdateButtons() {
      bool hasInput = !string.IsNullOrWhiteSpace(Surface == "quick" ? Input.Text : Original);
      Translate.IsEnabled = !Awaiting && !Busy && hasInput;
      Retry.Visibility = Busy || Awaiting || !hasInput || Buffer.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
      Stop.Visibility = Busy ? Visibility.Visible : Visibility.Collapsed;
      Copy.IsEnabled = Buffer.Length > 0;
    }
    FlowDocument Document() { return new FlowDocument { FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI"), FontSize = P.Num(P.Config, "fontSize", 15), Foreground = P.Ink, PagePadding = new Thickness(0), LineHeight = P.Num(P.Config, "fontSize", 15) * 1.7 }; }
    void ResetDocument() {
      var doc = Document(); StreamingRun = new Run(""); doc.Blocks.Add(new Paragraph(StreamingRun) { Margin = new Thickness(0) }); Result.Document = doc;
    }
    void Paint(bool markdown) {
      bool atBottom = Result.VerticalOffset + Result.ViewportHeight >= Result.ExtentHeight - 30;
      string answer = Answer;
      if (!markdown) {
        // Reuse one document/run for the whole stream. Formatting happens once.
        if (StreamingRun == null) ResetDocument();
        StreamingRun.Text = answer;
      } else {
        var doc = Document(); bool code = false;
        foreach (string raw in answer.Replace("\r", "").Split('\n')) {
          if (raw.TrimStart().StartsWith("```")) { code = !code; continue; }
          string line = raw; var p = new Paragraph { Margin = new Thickness(0, 0, 0, 5) };
          if (code) { p.FontFamily = new FontFamily("Consolas"); p.FontSize = 13; p.Background = P.Soft; p.Inlines.Add(new Run(line)); }
          else {
            var heading = Regex.Match(line, @"^(#{1,4})\s+(.+)$");
            if (heading.Success) { line = heading.Groups[2].Value; p.FontSize = 20 - heading.Groups[1].Value.Length; p.FontWeight = FontWeights.SemiBold; }
            line = Regex.Replace(line, @"^\s*[-*]\s+", "• ");
            if (line.StartsWith("> ")) { line = line.Substring(2); p.BorderBrush = P.Line; p.BorderThickness = new Thickness(3, 0, 0, 0); p.Padding = new Thickness(8, 0, 0, 0); p.Foreground = P.Muted; }
            foreach (string part in Regex.Split(line, @"(\*\*[^*]+\*\*|`[^`]+`)") ) {
              if (part.StartsWith("**") && part.EndsWith("**") && part.Length >= 4) p.Inlines.Add(new Bold(new Run(part.Substring(2, part.Length - 4))));
              else if (part.StartsWith("`") && part.EndsWith("`") && part.Length >= 2) p.Inlines.Add(new Run(part.Substring(1, part.Length - 2)) { FontFamily = new FontFamily("Consolas"), Background = P.Soft });
              else p.Inlines.Add(new Run(part));
            }
          }
          doc.Blocks.Add(p);
        }
        Result.Document = doc; StreamingRun = null;
      }
      if (Surface == "selection" && !Resized && !Pinned && !Moved) {
        // Grow short answers gradually, cap long ones; only move for screen edges.
        double desired = Math.Min(510, Math.Max(280, 200 + (answer.Length / 38 + answer.Split('\n').Length) * 25));
        if (Math.Abs(desired - LastAutoHeight) > 20) { Window.Height = desired; LastAutoHeight = desired; Window.UpdateLayout(); Native.KeepOnScreen(Window); }
      }
      if (atBottom) Result.ScrollToEnd(); UpdateButtons();
    }
    internal void DemoInput(string text) { Input.Text = text; }
    internal void DemoPin() { Pin.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
  }
}
