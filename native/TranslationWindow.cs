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
    readonly TextBlock Status, Title, Count, Route, EmptyText;
    readonly LanguagePicker Language;
    internal string TargetLanguage { get { return Language.Value; } }
    readonly Button Stop, Translate, Retry, Copy, Pin;
    readonly Border SourceBox;
    readonly FrameworkElement LanguageRow, EmptyResult, Placeholder;
    readonly ContentControl ActionIcon;
    readonly ProgressBar Progress;
    readonly OpacityControl Transparency;
    internal double OpacityPercent { get { return Transparency.Percent; } }
    readonly DispatcherTimer CopyTimer;
    readonly DispatcherTimer RenderTimer;
    Run StreamingRun;
    bool Pinned, Moved, Resized, HasPosition, Awaiting;
    string PendingId, LastDefaultLanguage;
    int AnchorX, AnchorY;
    bool SourceExpanded;

    internal TranslationWindow(string surface) {
      Surface = surface;
      bool quick = surface == "quick";
      Window = P.WindowBase(quick ? 780 : 520, quick ? 530 : 420, false, true);
      Window.Title = quick ? "DSH 快捷翻译" : "DSH 划词结果";
      Window.ShowActivated = false; Window.Topmost = false;
      Window.MinWidth = quick ? 640 : 380; Window.MinHeight = quick ? 400 : 280;
      Window.ResizeMode = ResizeMode.CanResize;
      var layout = new DockPanel { LastChildFill = true };

      // A small, separate title bar keeps window controls out of the reading area.
      var header = new DockPanel { Height = 34, Margin = new Thickness(8, 0, 6, 0), LastChildFill = true };
      var controls = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
      Pin = Visuals.IconButton("pin", TogglePin, "固定在其他窗口上方", 26);
      controls.Children.Add(Pin);
      Transparency = new OpacityControl(Window); controls.Children.Add(Transparency.Button);
      controls.Children.Add(Visuals.IconButton("settings", P.ShowSettings, "设置", 26));
      controls.Children.Add(Visuals.IconButton("minus", delegate { Window.WindowState = WindowState.Minimized; }, "最小化", 26));
      controls.Children.Add(Visuals.IconButton("close", Close, "关闭 · Esc", 26));
      DockPanel.SetDock(controls, Dock.Right); header.Children.Add(controls);
      var caption = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
      ActionIcon = new ContentControl { Content = Visuals.Icon("translate", 14), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 7, 0) };
      caption.Children.Add(ActionIcon);
      Title = P.Text(quick ? "快捷翻译" : "翻译", 13); Title.Margin = new Thickness(0); Title.VerticalAlignment = VerticalAlignment.Center;
      caption.Children.Add(Title); header.Children.Add(caption);
      var titlebar = new Border { Background = P.Soft, BorderBrush = P.Line, BorderThickness = new Thickness(0, 0, 0, 1), CornerRadius = new CornerRadius(8, 8, 0, 0), Child = header };
      titlebar.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e) {
        if (e.ChangedButton == MouseButton.Left) { Moved = true; Window.DragMove(); }
      };
      DockPanel.SetDock(titlebar, Dock.Top); layout.Children.Add(titlebar);
      Progress = new ProgressBar { Height = 2, IsIndeterminate = false, Foreground = P.Accent, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Opacity = 0 };
      DockPanel.SetDock(Progress, Dock.Top); layout.Children.Add(Progress);

      var footer = new DockPanel { LastChildFill = true, Margin = new Thickness(12, 5, 12, 5), Height = 28 };
      var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
      Copy = Visuals.IconButton("copy", CopyAnswer, "复制译文", 26);
      Retry = Visuals.IconButton("refresh", Submit, "重新生成", 26);
      Stop = Visuals.IconButton("stop", StopRun, "停止生成", 26);
      actions.Children.Add(Retry); actions.Children.Add(Stop); actions.Children.Add(Copy);
      DockPanel.SetDock(actions, Dock.Right); footer.Children.Add(actions);
      Route = P.Text("", 11, P.Muted); Route.Margin = new Thickness(0); Route.MaxWidth = quick ? 250 : 175; Route.TextTrimming = TextTrimming.CharacterEllipsis; Route.TextWrapping = TextWrapping.NoWrap;
      var routeButton = P.Button("", P.ShowSettings, "选择模型 / 设置");
      var routeLabel = new StackPanel { Orientation = Orientation.Horizontal };
      routeLabel.Children.Add(Visuals.Icon("explain", 13, P.Muted)); Route.Margin = new Thickness(6, 0, 2, 0); routeLabel.Children.Add(Route);
      routeButton.Content = routeLabel; routeButton.Padding = new Thickness(4); routeButton.Margin = new Thickness(0); DockPanel.SetDock(routeButton, Dock.Left); footer.Children.Add(routeButton);
      Status = P.Text(quick ? "等待输入" : "正在连接…", 11, P.Muted); Status.Margin = new Thickness(8, 0, 8, 0); Status.TextWrapping = TextWrapping.NoWrap; Status.TextTrimming = TextTrimming.CharacterEllipsis;
      Status.VerticalAlignment = VerticalAlignment.Center; Status.HorizontalAlignment = HorizontalAlignment.Right;
      footer.Children.Add(Status);
      var footerBorder = new Border { BorderBrush = P.Line, BorderThickness = new Thickness(0, 1, 0, 0), Child = footer };
      DockPanel.SetDock(footerBorder, Dock.Bottom); layout.Children.Add(footerBorder);

      Input = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, BorderThickness = new Thickness(0), FontSize = P.Num(P.Config, "fontSize", 15), Background = Brushes.Transparent, Foreground = P.Ink, IsReadOnly = !quick, Padding = new Thickness(0), IsUndoEnabled = quick };
      Input.SetValue(System.Windows.Automation.AutomationProperties.NameProperty, quick ? "待翻译文字" : "原文");
      Result = new RichTextBox { IsReadOnly = true, IsDocumentEnabled = false, IsUndoEnabled = false, BorderThickness = new Thickness(0), Background = Brushes.Transparent, Foreground = P.Ink, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0) };
      Result.SetValue(System.Windows.Automation.AutomationProperties.NameProperty, "译文或结果");
      Count = P.Text("0 / " + P.Num(P.Config, "maxChars", 12000) + " 字", 11, P.Muted); Count.Margin = new Thickness(0);
      Language = new LanguagePicker(); Language.Width = 142; Language.MinHeight = 28; Language.Margin = new Thickness(0); Language.FontSize = 12;
      Language.SetValue(System.Windows.Automation.AutomationProperties.NameProperty, "目标语言");
      Translate = P.Button("", Submit, "翻译 · Ctrl+Enter"); Translate.Content = Visuals.Label("translate", "翻译", 14); Translate.Background = P.Hover; Translate.Foreground = P.Accent; Translate.Padding = new Thickness(10, 5, 10, 5); Translate.Margin = new Thickness(8, 0, 0, 0);

      var empty = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, IsHitTestVisible = false };
      var emptyIcon = Visuals.Icon("translate", 26, P.Muted); emptyIcon.Opacity = 0.35; empty.Children.Add(emptyIcon);
      EmptyText = P.Text(quick ? "译文会显示在这里" : "正在翻译…", 12, P.Muted); EmptyText.Margin = new Thickness(0, 12, 0, 0); empty.Children.Add(EmptyText); EmptyResult = empty;
      var resultHost = new Grid(); resultHost.Children.Add(Result); resultHost.Children.Add(EmptyResult);
      var placeholder = P.Text("输入或粘贴要翻译的文字…", 15, P.Muted); placeholder.Margin = new Thickness(0, 1, 0, 0); placeholder.IsHitTestVisible = false; Placeholder = placeholder;
      var inputHost = new Grid(); if (quick) { inputHost.Children.Add(Input); inputHost.Children.Add(Placeholder); }

      if (quick) {
        var languagebar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 10, 16, 10) };
        var label = P.Text("译为", 12, P.Muted); label.VerticalAlignment = VerticalAlignment.Center; label.Margin = new Thickness(0, 0, 10, 0); languagebar.Children.Add(label);
        languagebar.Children.Add(Language); languagebar.Children.Add(Translate); LanguageRow = languagebar;
        var languageBorder = new Border { BorderBrush = P.Line, BorderThickness = new Thickness(0, 0, 0, 1), Child = languagebar };
        DockPanel.SetDock(languageBorder, Dock.Top); layout.Children.Add(languageBorder);
        var columns = new Grid(); columns.ColumnDefinitions.Add(new ColumnDefinition()); columns.ColumnDefinitions.Add(new ColumnDefinition());
        var source = new DockPanel { Margin = new Thickness(16, 10, 16, 12), LastChildFill = true };
        var sourceHead = new DockPanel { Height = 30, LastChildFill = true };
        var tools = new StackPanel { Orientation = Orientation.Horizontal };
        tools.Children.Add(Visuals.IconButton("paste", Paste, "粘贴文字", 26)); tools.Children.Add(Visuals.IconButton("clear", Clear, "清空原文与译文", 26));
        DockPanel.SetDock(tools, Dock.Right); sourceHead.Children.Add(tools);
        var sourceLabel = P.Text("原文", 11, P.Muted); sourceLabel.VerticalAlignment = VerticalAlignment.Center; sourceLabel.Margin = new Thickness(0); sourceHead.Children.Add(sourceLabel);
        DockPanel.SetDock(sourceHead, Dock.Top); source.Children.Add(sourceHead);
        var sourceFoot = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        var shortcut = P.Text("Ctrl + Enter  翻译", 11, P.Muted); shortcut.Margin = new Thickness(0); DockPanel.SetDock(shortcut, Dock.Right); sourceFoot.Children.Add(shortcut); sourceFoot.Children.Add(Count);
        DockPanel.SetDock(sourceFoot, Dock.Bottom); source.Children.Add(sourceFoot);
        inputHost.Margin = new Thickness(0, 10, 0, 0); source.Children.Add(inputHost);
        var sourceBorder = new Border { BorderBrush = P.Line, BorderThickness = new Thickness(0, 0, 1, 0), Child = source }; columns.Children.Add(sourceBorder);
        var translated = new DockPanel { Margin = new Thickness(16, 10, 16, 12) };
        var resultLabel = P.Text("译文", 11, P.Muted); resultLabel.Margin = new Thickness(0); resultLabel.Height = 30; resultLabel.VerticalAlignment = VerticalAlignment.Center; DockPanel.SetDock(resultLabel, Dock.Top); translated.Children.Add(resultLabel);
        resultHost.Margin = new Thickness(0, 10, 0, 0); translated.Children.Add(resultHost); Grid.SetColumn(translated, 1); columns.Children.Add(translated); layout.Children.Add(columns);
        Input.TextChanged += delegate { Count.Text = Input.Text.Length + " / " + P.Num(P.Config, "maxChars", 12000) + " 字"; Placeholder.Visibility = Input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; UpdateButtons(); };
      } else {
        Placeholder.Visibility = Visibility.Collapsed;
        var body = new DockPanel { Margin = new Thickness(16, 10, 16, 16), LastChildFill = true };
        var languagebar = new DockPanel { Margin = new Thickness(0, 0, 0, 12), LastChildFill = false };
        var toggle = P.Button("", delegate { SourceExpanded = !SourceExpanded; SourceBox.Visibility = SourceExpanded ? Visibility.Visible : Visibility.Collapsed; }, "显示 / 隐藏原文"); toggle.Content = Visuals.Label("chevron", "原文", 12); toggle.FontSize = 12; toggle.Padding = new Thickness(5); toggle.Margin = new Thickness(0); DockPanel.SetDock(toggle, Dock.Right); languagebar.Children.Add(toggle);
        var languagePair = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        languagePair.Children.Add(new Border { Background = P.Soft, CornerRadius = new CornerRadius(5), Padding = new Thickness(8, 5, 8, 5), Child = P.Text("自动识别", 11, P.Muted) });
        var arrow = Visuals.Icon("arrowRight", 15, P.Muted); ((FrameworkElement)arrow).Margin = new Thickness(8, 0, 8, 0); languagePair.Children.Add(arrow); languagePair.Children.Add(Language);
        languagebar.Children.Add(languagePair); LanguageRow = languagebar; DockPanel.SetDock(languagebar, Dock.Top); body.Children.Add(languagebar);
        Input.FontSize = 12; Input.MaxHeight = 90;
        SourceBox = new Border { Background = P.Soft, CornerRadius = new CornerRadius(6), Padding = new Thickness(10), Margin = new Thickness(0, 0, 0, 12), Child = Input, Visibility = Visibility.Collapsed };
        DockPanel.SetDock(SourceBox, Dock.Top); body.Children.Add(SourceBox); body.Children.Add(resultHost); layout.Children.Add(body);
      }
      Window.Content = Visuals.Shell(layout);
      var saved = P.Preferences == null ? null : P.Preferences.Get(Surface);
      if (saved != null) {
        Window.Width = Math.Max(Window.MinWidth, saved.Width); Window.Height = Math.Max(Window.MinHeight, saved.Height);
        Transparency.SetPercent(saved.Opacity); Resized = true;
      }
      Transparency.Changed += delegate { SaveWindowPreferences(); };
      Window.SizeChanged += delegate { if (Resized && Window.IsVisible) SaveWindowPreferences(); };
      Window.PreviewKeyDown += delegate(object sender, KeyEventArgs e) {
        if (Language.IsPopupOpen) return;
        if (e.Key == Key.Escape) { if (Busy || Awaiting) StopRun(); else Close(); e.Handled = true; }
        else if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) != 0) { Submit(); e.Handled = true; }
      };
      Native.EnableResize(Window, 10, delegate { Resized = true; Window.Dispatcher.BeginInvoke(new System.Action(SaveWindowPreferences)); });
      Window.Closing += delegate(object sender, System.ComponentModel.CancelEventArgs e) { if (!P.Closing) { e.Cancel = true; Close(); } };
      RenderTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
      RenderTimer.Tick += delegate { RenderTimer.Stop(); Paint(false); };
      CopyTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
      CopyTimer.Tick += delegate { CopyTimer.Stop(); Copy.Content = Visuals.Icon("copy", 16); };
      RefreshSettings(); ResetDocument(); UpdateButtons();
    }
    void SaveWindowPreferences() {
      if (P.Preferences == null || P.Closing || Window.WindowState != WindowState.Normal) return;
      P.Preferences.Update(Surface, Window.Width, Window.Height, Transparency.Percent);
    }
    void TogglePin() {
      Pinned = !Pinned; Window.Topmost = Pinned;
      Pin.Content = Visuals.Icon(Pinned ? "pinOff" : "pin", 16);
      if (Pinned) Pin.Foreground = P.Accent; else Pin.ClearValue(Control.ForegroundProperty); Pin.Background = Pinned ? P.Hover : Brushes.Transparent;
      Pin.ToolTip = Pinned ? "取消固定" : "固定在其他窗口上方";
      Pin.SetValue(System.Windows.Automation.AutomationProperties.NameProperty, Pinned ? "取消固定" : "固定在其他窗口上方");
    }
    internal void RefreshSettings() {
      Route.Text = P.Str(P.Config, "model") == "" ? "DSH 默认模型" : P.Str(P.Config, "model");
      Input.FontSize = Surface == "quick" ? P.Num(P.Config, "fontSize", 15) : 12;
      if (Language != null && !Busy && LastDefaultLanguage != P.Str(P.Config, "targetLanguage", "auto")) {
        string language = P.Str(P.Config, "targetLanguage", "auto");
        LastDefaultLanguage = language;
        Language.SetValue(language);
      }
      UpdateButtons();
    }
    internal void Show() {
      if (Window.WindowState == WindowState.Minimized) Window.WindowState = WindowState.Normal;
      Visuals.Show(Window); Window.UpdateLayout();
      if (!HasPosition) { Native.Point p; Native.GetCursorPos(out p); Native.Place(Window, p.X, p.Y, true); HasPosition = true; }
      else Native.KeepOnScreen(Window);
      Native.Present(Window, true); if (Surface == "quick") Input.Focus();
    }
    internal void Begin(Dictionary<string, object> message) {
      if (P.Str(message, "source") == "manual" && P.Str(message, "id") != PendingId) { P.Send("cancel", "id", P.Str(message, "id")); return; }
      PendingId = null;
      if (Window.WindowState == WindowState.Minimized) Window.WindowState = WindowState.Normal;
      bool wasVisible = Window.IsVisible && (Surface == "quick" || HasPosition);
      RenderTimer.Stop(); RunId = P.Str(message, "id"); Original = P.Str(message, "text"); Action = P.Str(message, "action", "translate");
      Awaiting = false; Busy = true; Buffer.Clear(); ResetDocument();
      if (Surface == "selection") { Input.Text = Original; Title.Text = Action == "translate" ? "翻译" : Action == "explain" ? "解释" : "总结"; ActionIcon.Content = Visuals.Icon(Action, 14); }
      // A fresh selection uses the Host's configured language. Manual retries
      // instead carry the language picked in this window with their request.
      if (P.Str(message, "source") != "manual") Language.SetValue(P.Str(P.Config, "targetLanguage", "auto"));
      Language.AutoLabel = Action == "translate" ? "自动中英互译" : "默认语言";
      Status.Text = "正在连接模型…"; UpdateButtons();
      if (Surface == "quick") { if (!wasVisible) Show(); return; }
      Visuals.Show(Window); Window.UpdateLayout();
      if (P.Str(message, "source") != "manual" && (!wasVisible || (!Pinned && !Moved))) {
        AnchorX = P.Num(message, "x"); AnchorY = P.Num(message, "y");
        Native.Place(Window, AnchorX, AnchorY); HasPosition = true;
      }
      else Native.KeepOnScreen(Window);
      Native.Present(Window, true);
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
      string target = Language.Value;
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
    void CopyAnswer() { if (Buffer.Length == 0) return; try { Clipboard.SetText(Answer); Status.Text = "已复制"; Copy.Content = Visuals.Icon("check", 16); CopyTimer.Stop(); CopyTimer.Start(); } catch { Status.Text = "剪贴板暂时忙，请重试"; } }
    internal void Clear() { StopRun(false); Input.Clear(); Original = ""; Buffer.Clear(); ResetDocument(); Status.Text = "输入或粘贴文字"; UpdateButtons(); }
    internal void Close() {
      SaveWindowPreferences(); if (P.Preferences != null) P.Preferences.Flush();
      Language.ClosePopup(); Transparency.Close(); StopRun(false); Visuals.Hide(Window);
      // Quick input and output live only in this process until Clear or exit.
      if (Surface == "selection") { Input.Clear(); Original = ""; Buffer.Clear(); ResetDocument(); Moved = false; HasPosition = false; }
    }
    void UpdateButtons() {
      bool hasInput = !string.IsNullOrWhiteSpace(Surface == "quick" ? Input.Text : Original);
      Translate.IsEnabled = !Awaiting && !Busy && hasInput;
      Retry.Visibility = Busy || Awaiting || !hasInput || Buffer.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
      Stop.Visibility = Busy || Awaiting ? Visibility.Visible : Visibility.Collapsed;
      Language.IsEnabled = !Busy && !Awaiting;
      Progress.IsIndeterminate = Busy || Awaiting; Progress.Opacity = Busy || Awaiting ? 1 : 0;
      EmptyResult.Visibility = Buffer.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
      EmptyText.Text = Busy || Awaiting ? "正在翻译…" : Surface == "quick" && Original.Length == 0 ? "译文会显示在这里" : Status.Text;
      Status.ToolTip = Status.Text;
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
      if (atBottom) Result.ScrollToEnd(); UpdateButtons();
    }
    internal void DemoOpacity(double value) { Transparency.SetPercent(value); }
    internal void DemoLanguage(string value) { Language.SetValue(value); }
    internal void DemoResize(double width, double height) {
      Resized = true; Window.Width = Math.Max(Window.MinWidth, width); Window.Height = Math.Max(Window.MinHeight, height);
      Window.UpdateLayout(); SaveWindowPreferences();
    }
    internal void DemoInput(string text) { Input.Text = text; }
    internal void DemoPin() { Pin.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
  }
}
