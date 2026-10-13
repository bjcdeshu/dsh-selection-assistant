using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using P = DshSelectionAssistant.Program;

namespace DshSelectionAssistant {
  internal sealed class OpacityControl {
    internal readonly Button Button;
    readonly Window Owner;
    readonly Popup Flyout;
    readonly Slider Slider;
    readonly TextBlock ValueLabel;
    internal double Percent { get { return Slider.Value; } }

    internal OpacityControl(Window owner) {
      Owner = owner;
      Button = Visuals.IconButton("opacity", Toggle, "透明度", 26);
      var body = new StackPanel { Width = 96 };
      var label = P.Text("不透明度", 11, P.Muted); label.HorizontalAlignment = HorizontalAlignment.Center; body.Children.Add(label);
      ValueLabel = P.Text("100%", 13); ValueLabel.HorizontalAlignment = HorizontalAlignment.Center; body.Children.Add(ValueLabel);
      Slider = new Slider { Minimum = 20, Maximum = 100, Value = 100, Orientation = Orientation.Vertical,
        Width = 28, Height = 120, Margin = new Thickness(0, 6, 0, 10), SmallChange = 1, LargeChange = 10,
        IsSnapToTickEnabled = false, IsMoveToPointEnabled = true, Foreground = P.Accent };
      Slider.SetValue(System.Windows.Automation.AutomationProperties.NameProperty, "窗口不透明度");
      Slider.ValueChanged += delegate {
        var shell = Owner.Content as Visuals.WindowShell;
        if (shell != null) shell.SetContentOpacity(Slider.Value / 100.0);
        ValueLabel.Text = Math.Round(Slider.Value) + "%";
        Button.ToolTip = "透明度 · " + Math.Round(Slider.Value) + "% 不透明";
      };
      body.Children.Add(Slider);
      var restore = P.Button("恢复 100%", delegate { Slider.Value = 100; }, "恢复完全不透明");
      restore.FontSize = 11; restore.Padding = new Thickness(4, 5, 4, 5); body.Children.Add(restore);
      Flyout = new Popup { PlacementTarget = Button, Placement = PlacementMode.Bottom, AllowsTransparency = true,
        StaysOpen = false, PopupAnimation = SystemParameters.ClientAreaAnimation ? PopupAnimation.Fade : PopupAnimation.None,
        Child = Visuals.Shell(body, 8, 8, 8, 10) };
      Flyout.Opened += delegate { Slider.Focus(); };
      owner.IsVisibleChanged += delegate { if (!owner.IsVisible) Close(); };
      owner.LocationChanged += delegate { Close(); };
      owner.SizeChanged += delegate { Close(); };
      owner.Closed += delegate { Close(); };
      body.PreviewKeyDown += delegate(object sender, KeyEventArgs e) {
        if (e.Key == Key.Escape) { Close(); Button.Focus(); e.Handled = true; }
      };
    }
    void Toggle() { Flyout.IsOpen = !Flyout.IsOpen; }
    internal void Close() { Flyout.IsOpen = false; }
    internal void SetPercent(double value) { Slider.Value = Math.Max(20, Math.Min(100, value)); }
  }
}
