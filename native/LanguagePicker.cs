using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace DshSelectionAssistant {
  internal sealed class LanguageOption {
    internal readonly string Value, Name, NativeName, SearchNames;
    internal LanguageOption(string value, string name, string nativeName, string aliases) {
      Value = value; Name = name; NativeName = nativeName;
      SearchNames = name + " " + value + " " + nativeName + " " + aliases;
    }
    internal bool Matches(string query) {
      foreach (string word in (query ?? "").Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
        if (CultureInfo.InvariantCulture.CompareInfo.IndexOf(SearchNames, word, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) < 0) return false;
      return true;
    }
  }

  // Values are prompt language names, not an allow-list of model capabilities.
  // Keep the original six values stable so existing preferences remain compatible.
  internal static class LanguageCatalog {
    internal static readonly LanguageOption[] All = {
      new LanguageOption("简体中文", "简体中文", "Chinese (Simplified)", "普通话 汉语 Mandarin zh-CN"),
      new LanguageOption("繁體中文", "繁体中文", "Chinese (Traditional)", "繁體 國語 汉语 zh-TW zh-HK"),
      new LanguageOption("English", "英语", "English", "英文 en"),
      new LanguageOption("日本語", "日语", "日本語 · Japanese", "日文 ja"),
      new LanguageOption("한국어", "韩语", "한국어 · Korean", "朝鲜语 조선말 ko"),
      new LanguageOption("French", "法语", "Français", "French fr"),
      new LanguageOption("German", "德语", "Deutsch", "German de"),
      new LanguageOption("Spanish", "西班牙语", "Español", "Spanish es"),
      new LanguageOption("Italian", "意大利语", "Italiano", "Italian it"),
      new LanguageOption("Portuguese", "葡萄牙语", "Português", "Portuguese pt"),
      new LanguageOption("Brazilian Portuguese", "葡萄牙语（巴西）", "Português do Brasil", "Brazilian Portuguese pt-BR"),
      new LanguageOption("Russian", "俄语", "Русский", "Russian ru"),
      new LanguageOption("Arabic", "阿拉伯语", "العربية", "Arabic ar"),
      new LanguageOption("Hindi", "印地语", "हिन्दी", "Hindi hi"),
      new LanguageOption("Bengali", "孟加拉语", "বাংলা", "Bengali Bangla bn"),
      new LanguageOption("Urdu", "乌尔都语", "اردو", "Urdu ur"),
      new LanguageOption("Persian", "波斯语", "فارسی", "Persian Farsi fa"),
      new LanguageOption("Turkish", "土耳其语", "Türkçe", "Turkish tr"),
      new LanguageOption("Vietnamese", "越南语", "Tiếng Việt", "Vietnamese vi"),
      new LanguageOption("Thai", "泰语", "ไทย", "Thai th"),
      new LanguageOption("Indonesian", "印度尼西亚语", "Bahasa Indonesia", "Indonesian 印尼语 id"),
      new LanguageOption("Malay", "马来语", "Bahasa Melayu", "Malay ms"),
      new LanguageOption("Filipino", "菲律宾语", "Filipino / Tagalog", "塔加洛语 菲律宾文 tl fil"),
      new LanguageOption("Cantonese", "粤语", "廣東話 · Cantonese", "广东话 白话 yue"),
      new LanguageOption("Mongolian", "蒙古语", "Монгол", "Mongolian mn"),
      new LanguageOption("Tibetan", "藏语", "བོད་ཡིག", "Tibetan bo"),
      new LanguageOption("Uyghur", "维吾尔语", "ئۇيغۇرچە", "Uyghur Uighur ug"),
      new LanguageOption("Kazakh", "哈萨克语", "Қазақша", "Kazakh kk"),
      new LanguageOption("Uzbek", "乌兹别克语", "Oʻzbekcha", "Uzbek uz"),
      new LanguageOption("Kyrgyz", "吉尔吉斯语", "Кыргызча", "Kyrgyz Kirghiz ky"),
      new LanguageOption("Tajik", "塔吉克语", "Тоҷикӣ", "Tajik tg"),
      new LanguageOption("Turkmen", "土库曼语", "Türkmençe", "Turkmen tk"),
      new LanguageOption("Tamil", "泰米尔语", "தமிழ்", "Tamil ta"),
      new LanguageOption("Telugu", "泰卢固语", "తెలుగు", "Telugu te"),
      new LanguageOption("Marathi", "马拉地语", "मराठी", "Marathi mr"),
      new LanguageOption("Gujarati", "古吉拉特语", "ગુજરાતી", "Gujarati gu"),
      new LanguageOption("Kannada", "卡纳达语", "ಕನ್ನಡ", "Kannada kn"),
      new LanguageOption("Malayalam", "马拉雅拉姆语", "മലയാളം", "Malayalam ml"),
      new LanguageOption("Punjabi", "旁遮普语", "ਪੰਜਾਬੀ / پنجابی", "Punjabi Panjabi pa"),
      new LanguageOption("Nepali", "尼泊尔语", "नेपाली", "Nepali ne"),
      new LanguageOption("Sinhala", "僧伽罗语", "සිංහල", "Sinhala Sinhalese si"),
      new LanguageOption("Khmer", "高棉语", "ខ្មែរ", "Khmer 柬埔寨语 km"),
      new LanguageOption("Lao", "老挝语", "ລາວ", "Lao Laotian lo"),
      new LanguageOption("Burmese", "缅甸语", "မြန်မာ", "Burmese Myanmar my"),
      new LanguageOption("Hebrew", "希伯来语", "עברית", "Hebrew he"),
      new LanguageOption("Greek", "希腊语", "Ελληνικά", "Greek el"),
      new LanguageOption("Dutch", "荷兰语", "Nederlands", "Dutch nl"),
      new LanguageOption("Polish", "波兰语", "Polski", "Polish pl"),
      new LanguageOption("Ukrainian", "乌克兰语", "Українська", "Ukrainian uk"),
      new LanguageOption("Czech", "捷克语", "Čeština", "Czech cs"),
      new LanguageOption("Slovak", "斯洛伐克语", "Slovenčina", "Slovak sk"),
      new LanguageOption("Romanian", "罗马尼亚语", "Română", "Romanian ro"),
      new LanguageOption("Hungarian", "匈牙利语", "Magyar", "Hungarian hu"),
      new LanguageOption("Bulgarian", "保加利亚语", "Български", "Bulgarian bg"),
      new LanguageOption("Serbian", "塞尔维亚语", "Српски / Srpski", "Serbian sr"),
      new LanguageOption("Croatian", "克罗地亚语", "Hrvatski", "Croatian hr"),
      new LanguageOption("Bosnian", "波斯尼亚语", "Bosanski", "Bosnian bs"),
      new LanguageOption("Slovenian", "斯洛文尼亚语", "Slovenščina", "Slovenian Slovene sl"),
      new LanguageOption("Swedish", "瑞典语", "Svenska", "Swedish sv"),
      new LanguageOption("Danish", "丹麦语", "Dansk", "Danish da"),
      new LanguageOption("Norwegian", "挪威语", "Norsk", "Norwegian no nb"),
      new LanguageOption("Finnish", "芬兰语", "Suomi", "Finnish fi"),
      new LanguageOption("Icelandic", "冰岛语", "Íslenska", "Icelandic is"),
      new LanguageOption("Estonian", "爱沙尼亚语", "Eesti", "Estonian et"),
      new LanguageOption("Latvian", "拉脱维亚语", "Latviešu", "Latvian lv"),
      new LanguageOption("Lithuanian", "立陶宛语", "Lietuvių", "Lithuanian lt"),
      new LanguageOption("Irish", "爱尔兰语", "Gaeilge", "Irish ga"),
      new LanguageOption("Welsh", "威尔士语", "Cymraeg", "Welsh cy"),
      new LanguageOption("Scottish Gaelic", "苏格兰盖尔语", "Gàidhlig", "Scottish Gaelic gd"),
      new LanguageOption("Basque", "巴斯克语", "Euskara", "Basque eu"),
      new LanguageOption("Catalan", "加泰罗尼亚语", "Català", "Catalan ca"),
      new LanguageOption("Galician", "加利西亚语", "Galego", "Galician gl"),
      new LanguageOption("Albanian", "阿尔巴尼亚语", "Shqip", "Albanian sq"),
      new LanguageOption("Macedonian", "马其顿语", "Македонски", "Macedonian mk"),
      new LanguageOption("Belarusian", "白俄罗斯语", "Беларуская", "Belarusian be"),
      new LanguageOption("Armenian", "亚美尼亚语", "Հայերեն", "Armenian hy"),
      new LanguageOption("Georgian", "格鲁吉亚语", "ქართული", "Georgian ka"),
      new LanguageOption("Azerbaijani", "阿塞拜疆语", "Azərbaycanca", "Azerbaijani Azeri az"),
      new LanguageOption("Afrikaans", "南非荷兰语", "Afrikaans", "南非语 af"),
      new LanguageOption("Swahili", "斯瓦希里语", "Kiswahili", "Swahili sw"),
      new LanguageOption("Amharic", "阿姆哈拉语", "አማርኛ", "Amharic am"),
      new LanguageOption("Hausa", "豪萨语", "Hausa", "ha"),
      new LanguageOption("Yoruba", "约鲁巴语", "Yorùbá", "Yoruba yo"),
      new LanguageOption("Igbo", "伊博语", "Igbo", "ig"),
      new LanguageOption("Zulu", "祖鲁语", "isiZulu", "Zulu zu"),
      new LanguageOption("Somali", "索马里语", "Soomaali", "Somali so"),
      new LanguageOption("Kinyarwanda", "卢旺达语", "Ikinyarwanda", "Kinyarwanda rw"),
      new LanguageOption("Malagasy", "马尔加什语", "Malagasy", "马达加斯加语 mg"),
      new LanguageOption("Cebuano", "宿务语", "Cebuano / Bisaya", "ceb"),
      new LanguageOption("Javanese", "爪哇语", "Basa Jawa", "Javanese jv"),
      new LanguageOption("Sundanese", "巽他语", "Basa Sunda", "Sundanese su"),
      new LanguageOption("Maori", "毛利语", "Te Reo Māori", "Maori mi"),
      new LanguageOption("Samoan", "萨摩亚语", "Gagana Samoa", "Samoan sm"),
      new LanguageOption("Hawaiian", "夏威夷语", "ʻŌlelo Hawaiʻi", "Hawaiian haw"),
      new LanguageOption("Latin", "拉丁语", "Latina", "Latin la"),
      new LanguageOption("Esperanto", "世界语", "Esperanto", "eo")
    };
    internal static LanguageOption Find(string value) { return All.FirstOrDefault(delegate(LanguageOption option) { return string.Equals(option.Value, value, StringComparison.OrdinalIgnoreCase); }); }
    internal static bool ValidValue(string value) { return !string.IsNullOrWhiteSpace(value) && value.Length <= 160 && !value.Any(char.IsControl); }
  }

  internal sealed class LanguagePicker : Button {
    readonly Popup Flyout;
    readonly TextBox Search;
    readonly ListBox Choices;
    readonly TextBlock Caption, Empty;
    readonly Button Custom;
    string CurrentValue = "auto", CurrentAutoLabel;
    Window Owner;
    internal string Value { get { return CurrentValue; } }
    internal bool IsPopupOpen { get { return Flyout.IsOpen; } }
    internal event Action<string> ValueChanged;
    internal string AutoLabel { get { return CurrentAutoLabel; } set { CurrentAutoLabel = value ?? "自动中英互译"; UpdateCaption(); if (Flyout != null && Flyout.IsOpen) Filter(); } }

    internal LanguagePicker(string autoLabel = "自动中英互译") {
      CurrentAutoLabel = autoLabel;
      // Derived controls do not pick up the application's implicit Button style.
      Style = TryFindResource(typeof(Button)) as Style;
      MinHeight = 30; Padding = new Thickness(10, 4, 9, 4); Margin = new Thickness(0, 2, 0, 7);
      HorizontalContentAlignment = HorizontalAlignment.Stretch; VerticalContentAlignment = VerticalAlignment.Center;
      SetResourceReference(BackgroundProperty, "Soft");
      SetValue(AutomationProperties.NameProperty, "目标语言");
      var trigger = new Grid(); trigger.ColumnDefinitions.Add(new ColumnDefinition()); trigger.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
      Caption = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
      var arrow = new TextBlock { Text = "⌄", Margin = new Thickness(8, 0, 0, 2), VerticalAlignment = VerticalAlignment.Center };
      arrow.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); Grid.SetColumn(arrow, 1); trigger.Children.Add(Caption); trigger.Children.Add(arrow); Content = trigger;

      var body = new StackPanel { Width = 302, UseLayoutRounding = true, SnapsToDevicePixels = true };
      TextOptions.SetTextFormattingMode(body, TextFormattingMode.Display);
      TextElement.SetFontFamily(body, new FontFamily("Segoe UI, Microsoft YaHei UI"));
      var searchRow = new Grid { Margin = new Thickness(0, 0, 0, 7) };
      Search = new TextBox { MaxLength = 160, MinHeight = 34, Padding = new Thickness(9, 6, 9, 6), FontSize = 13 };
      Search.SetValue(AutomationProperties.NameProperty, "搜索语言"); Search.ToolTip = "按中文、英文或本地名称搜索";
      var hint = new TextBlock { Text = "搜索语言名称…", Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false, FontSize = 13 };
      hint.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); searchRow.Children.Add(Search); searchRow.Children.Add(hint); body.Children.Add(searchRow);
      Choices = new ListBox { MaxHeight = 292, BorderThickness = new Thickness(0), Background = Brushes.Transparent, HorizontalContentAlignment = HorizontalAlignment.Stretch };
      Choices.SetValue(AutomationProperties.NameProperty, "语言列表");
      ScrollViewer.SetHorizontalScrollBarVisibility(Choices, ScrollBarVisibility.Disabled);
      ScrollViewer.SetVerticalScrollBarVisibility(Choices, ScrollBarVisibility.Auto);
      Choices.ItemContainerStyle = ChoiceStyle(); body.Children.Add(Choices);
      Empty = new TextBlock { Text = "未找到匹配语言，可使用下方自定义语言。", FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8, 10, 8, 10) };
      Empty.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); body.Children.Add(Empty);
      Custom = new Button { HorizontalContentAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 7, 0, 0), Padding = new Thickness(8, 8, 8, 8), FontSize = 12 };
      Custom.SetValue(AutomationProperties.NameProperty, "使用自定义语言"); Custom.Click += delegate { Commit(Search.Text.Trim()); }; body.Children.Add(Custom);
      var edge = new Border { Padding = new Thickness(8), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Child = body };
      edge.SetResourceReference(Border.BackgroundProperty, "Surface"); edge.SetResourceReference(Border.BorderBrushProperty, "Line");
      Flyout = new Popup { PlacementTarget = this, Placement = PlacementMode.Bottom, VerticalOffset = 4, AllowsTransparency = true, StaysOpen = false, Child = edge };
      Flyout.Opened += delegate {
        Search.Text = ""; Filter();
        Search.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(delegate { if (Flyout.IsOpen) { Search.Focus(); Keyboard.Focus(Search); } }));
      };
      Search.TextChanged += delegate { hint.Visibility = Search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; Filter(); };
      Search.PreviewKeyDown += SearchKeyDown;
      Choices.PreviewKeyDown += delegate(object sender, KeyEventArgs e) { if (e.Key == Key.Enter || e.Key == Key.Space) { CommitSelected(); e.Handled = true; } };
      Choices.PreviewMouseLeftButtonUp += delegate(object sender, MouseButtonEventArgs e) {
        var item = ItemsControl.ContainerFromElement(Choices, e.OriginalSource as DependencyObject) as ListBoxItem;
        if (item != null) { Commit((string)item.Tag); e.Handled = true; }
      };
      body.PreviewKeyDown += delegate(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) { ClosePopup(); Focus(); e.Handled = true; } };
      Click += delegate { Flyout.IsOpen = !Flyout.IsOpen; };
      PreviewKeyDown += delegate(object sender, KeyEventArgs e) { if (e.Key == Key.Down || e.Key == Key.F4) { Flyout.IsOpen = true; e.Handled = true; } };
      IsEnabledChanged += delegate { if (!IsEnabled) ClosePopup(); };
      Loaded += delegate { AttachOwner(); };
      Unloaded += delegate { ClosePopup(); DetachOwner(); };
      UpdateCaption();
    }

    static Style ChoiceStyle() {
      var style = new Style(typeof(ListBoxItem));
      style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(9, 7, 9, 7)));
      style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
      var template = new ControlTemplate(typeof(ListBoxItem));
      var border = new FrameworkElementFactory(typeof(Border)); border.Name = "ChoiceBorder";
      border.SetValue(Border.CornerRadiusProperty, new CornerRadius(5)); border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
      border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
      var presenter = new FrameworkElementFactory(typeof(ContentPresenter)); border.AppendChild(presenter); template.VisualTree = border;
      var hover = new Trigger { Property = IsMouseOverProperty, Value = true }; hover.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension("Hover"), "ChoiceBorder")); template.Triggers.Add(hover);
      var selected = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true }; selected.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension("Hover"), "ChoiceBorder")); template.Triggers.Add(selected);
      style.Setters.Add(new Setter(Control.TemplateProperty, template)); return style;
    }

    void Filter() {
      string query = Search.Text.Trim(); Choices.Items.Clear();
      var automatic = new LanguageOption("auto", CurrentAutoLabel, "", "auto automatic 自动 默认 中英互译");
      if (automatic.Matches(query)) AddChoice(automatic);
      foreach (var option in LanguageCatalog.All) if (option.Matches(query)) AddChoice(option);
      var existing = LanguageCatalog.Find(CurrentValue);
      if (CurrentValue != "auto" && existing == null) {
        var custom = new LanguageOption(CurrentValue, CurrentValue, "自定义语言", "custom");
        if (custom.Matches(query)) AddChoice(custom);
      }
      Empty.Visibility = Choices.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
      Custom.Visibility = LanguageCatalog.ValidValue(query) ? Visibility.Visible : Visibility.Collapsed;
      Custom.Content = new TextBlock { Text = "使用自定义语言：" + query, TextTrimming = TextTrimming.CharacterEllipsis };
      if (Choices.Items.Count > 0) {
        ListBoxItem selected = Choices.Items.Cast<ListBoxItem>().FirstOrDefault(delegate(ListBoxItem item) { return (string)item.Tag == CurrentValue; });
        Choices.SelectedItem = selected ?? Choices.Items[0]; Choices.ScrollIntoView(Choices.SelectedItem);
      }
    }

    void AddChoice(LanguageOption option) {
      var label = new StackPanel();
      var title = new TextBlock { Text = option.Name, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis };
      title.SetResourceReference(TextBlock.ForegroundProperty, option.Value == CurrentValue ? "Accent" : "Ink"); label.Children.Add(title);
      if (option.NativeName.Length > 0) { var subtitle = new TextBlock { Text = option.NativeName, FontSize = 11, Margin = new Thickness(0, 2, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis }; subtitle.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); label.Children.Add(subtitle); }
      var item = new ListBoxItem { Content = label, Tag = option.Value };
      item.SetValue(AutomationProperties.NameProperty, option.Name + (option.NativeName.Length > 0 ? " · " + option.NativeName : "")); Choices.Items.Add(item);
    }

    void SearchKeyDown(object sender, KeyEventArgs e) {
      if (e.Key == Key.Down || e.Key == Key.Up) {
        if (Choices.Items.Count > 0) { int next = Choices.SelectedIndex + (e.Key == Key.Down ? 1 : -1); Choices.SelectedIndex = Math.Max(0, Math.Min(Choices.Items.Count - 1, next)); Choices.ScrollIntoView(Choices.SelectedItem); }
        e.Handled = true;
      } else if (e.Key == Key.Enter) {
        if (Choices.Items.Count > 0) CommitSelected(); else Commit(Search.Text.Trim()); e.Handled = true;
      }
    }
    void CommitSelected() { var item = Choices.SelectedItem as ListBoxItem; if (item != null) Commit((string)item.Tag); }
    void Commit(string value) { if (!LanguageCatalog.ValidValue(value)) return; SetValue(value); ClosePopup(); Focus(); }
    internal void SetValue(string value) {
      string next = LanguageCatalog.ValidValue(value) ? value.Trim() : "auto";
      bool changed = next != CurrentValue; CurrentValue = next; UpdateCaption();
      if (changed && ValueChanged != null) ValueChanged(CurrentValue);
    }
    void UpdateCaption() {
      var option = LanguageCatalog.Find(CurrentValue);
      Caption.Text = CurrentValue == "auto" ? CurrentAutoLabel : option != null ? option.Name : CurrentValue;
      ToolTip = Caption.Text + " · 点击搜索语言";
      SetValue(AutomationProperties.HelpTextProperty, "当前语言：" + Caption.Text);
    }
    internal void ClosePopup() { Flyout.IsOpen = false; }
    void AttachOwner() {
      var owner = Window.GetWindow(this); if (owner == Owner) return; DetachOwner(); Owner = owner;
      if (Owner == null) return;
      Owner.LocationChanged += OwnerMoved; Owner.SizeChanged += OwnerSized; Owner.IsVisibleChanged += OwnerVisible; Owner.Closed += OwnerClosed;
    }
    void DetachOwner() {
      if (Owner == null) return;
      Owner.LocationChanged -= OwnerMoved; Owner.SizeChanged -= OwnerSized; Owner.IsVisibleChanged -= OwnerVisible; Owner.Closed -= OwnerClosed; Owner = null;
    }
    void OwnerMoved(object sender, EventArgs e) { ClosePopup(); }
    void OwnerSized(object sender, SizeChangedEventArgs e) { ClosePopup(); }
    void OwnerVisible(object sender, DependencyPropertyChangedEventArgs e) { if (Owner != null && !Owner.IsVisible) ClosePopup(); }
    void OwnerClosed(object sender, EventArgs e) { ClosePopup(); DetachOwner(); }
  }
}
