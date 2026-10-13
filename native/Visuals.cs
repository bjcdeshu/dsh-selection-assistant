using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace DshSelectionAssistant {
  // Small, independently drawn vectors keep utility controls consistent across DPI levels.
  internal static class Visuals {
    internal static bool AnimationsEnabled = true;

    static readonly Dictionary<string, string> Icons = new Dictionary<string, string> {
      { "translate", "M3,5 L13,5 M8,2 L8,5 M5,5 C6,9 9,12 12,13 M11,5 C10,9 7,12 3,14 M13,21 L17,10 L21,21 M14.5,17 L19.5,17" },
      { "explain", "M6,3 L18,3 Q21,3 21,6 L21,15 Q21,18 18,18 L9,18 L4,21 L4,17 Q3,16 3,14 L3,6 Q3,3 6,3 M8,8 L16,8 M8,12 L14,12" },
      { "summarize", "M7,3 L17,3 Q19,3 19,5 L19,19 Q19,21 17,21 L7,21 Q5,21 5,19 L5,5 Q5,3 7,3 M9,8 L15,8 M9,12 L15,12 M9,16 L13,16" },
      { "copy", "M8,7 L8,4 Q8,2 10,2 L19,2 Q21,2 21,4 L21,14 Q21,16 19,16 L16,16 M5,7 L14,7 Q16,7 16,9 L16,19 Q16,21 14,21 L5,21 Q3,21 3,19 L3,9 Q3,7 5,7" },
      { "paste", "M8,5 L5,5 Q3,5 3,7 L3,19 Q3,21 5,21 L19,21 Q21,21 21,19 L21,7 Q21,5 19,5 L16,5 M9,2 L15,2 Q16,2 16,3 L16,6 L8,6 L8,3 Q8,2 9,2 M8,11 L16,11 M8,15 L14,15" },
      { "clear", "M9,20 L3,14 Q2,13 3,12 L12,3 Q13,2 14,3 L21,10 Q22,11 21,12 L13,20 Z M7,8 L16,17 M9,20 L22,20" },
      { "pin", "M8,3 L16,3 L15,9 L19,13 L5,13 L9,9 Z M12,13 L12,21" },
      { "pinOff", "M8,3 L16,3 L15,9 L19,13 L13,13 M8,10 L5,13 L9,13 M12,16 L12,21 M3,3 L21,21" },
      { "close", "M6,6 L18,18 M18,6 L6,18" },
      { "minus", "M5,12 L19,12" },
      { "opacity", "M12,2 C10,6 5,11 5,15 A7,7 0 0 0 19,15 C19,11 14,6 12,2 Z M8,15 A4,4 0 0 0 12,19" },
      { "grip", "M9,5 L9.1,5 M15,5 L15.1,5 M9,12 L9.1,12 M15,12 L15.1,12 M9,19 L9.1,19 M15,19 L15.1,19" },
      { "more", "M5,11 A1,1 0 1 0 5,13 A1,1 0 1 0 5,11 M12,11 A1,1 0 1 0 12,13 A1,1 0 1 0 12,11 M19,11 A1,1 0 1 0 19,13 A1,1 0 1 0 19,11" },
      { "settings", "M9,3 L15,3 L15.6,5.4 L17.7,6.6 L20,6 L23,11 L21.2,12.8 L21.2,15.2 L23,17 L20,22 L17.7,21.4 L15.6,22.6 L15,25 L9,25 L8.4,22.6 L6.3,21.4 L4,22 L1,17 L2.8,15.2 L2.8,12.8 L1,11 L4,6 L6.3,6.6 L8.4,5.4 Z M12,10 A4,4 0 1 0 12,18 A4,4 0 1 0 12,10" },
      { "chevron", "M7,10 L12,15 L17,10" },
      { "arrowRight", "M4,12 L20,12 M14,6 L20,12 L14,18" },
      { "refresh", "M20,8 A8,8 0 0 0 6,5 L3,8 M3,3 L3,8 L8,8 M4,16 A8,8 0 0 0 18,19 L21,16 M16,16 L21,16 L21,21" },
      { "stop", "M6,5 L18,5 Q19,5 19,6 L19,18 Q19,19 18,19 L6,19 Q5,19 5,18 L5,6 Q5,5 6,5" },
      { "check", "M4,12 L9,17 L20,6" },
      { "keyboard", "M4,5 L20,5 Q22,5 22,7 L22,17 Q22,19 20,19 L4,19 Q2,19 2,17 L2,7 Q2,5 4,5 M6,9 L6.1,9 M10,9 L10.1,9 M14,9 L14.1,9 M18,9 L18.1,9 M6,12 L6.1,12 M10,12 L10.1,12 M14,12 L14.1,12 M18,12 L18.1,12 M7,15 L17,15" },
      { "swap", "M3,8 L20,8 M16,4 L20,8 L16,12 M21,16 L4,16 M8,12 L4,16 L8,20" }
    };

    internal static UIElement Icon(string name, double size = 16, Brush stroke = null) {
      string geometry;
      if (!Icons.TryGetValue(name, out geometry)) geometry = Icons["more"];
      var path = new Path {
        Data = Geometry.Parse(geometry), StrokeThickness = 1.7,
        StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
        StrokeLineJoin = PenLineJoin.Round, Fill = Brushes.Transparent,
        SnapsToDevicePixels = false, IsHitTestVisible = false
      };
      if (stroke == null) path.SetBinding(Shape.StrokeProperty, new Binding("Foreground") {
        RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(Control), 1), FallbackValue = Program.Ink
      });
      else path.Stroke = stroke;
      // The gear's construction grid is slightly taller; center it in the common 24 px canvas.
      if (name == "settings") path.RenderTransform = new TransformGroup { Children = new TransformCollection { new ScaleTransform(0.8, 0.8), new TranslateTransform(2.4, 0.8) } };
      var canvas = new Canvas { Width = 24, Height = 24, IsHitTestVisible = false };
      canvas.Children.Add(path);
      return new Viewbox { Width = size, Height = size, Stretch = Stretch.Uniform, Child = canvas, IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center };
    }

    internal static Button IconButton(string name, System.Action click, string help, double size = 28) {
      var button = new Button {
        Content = Icon(name), Width = size, Height = size, Padding = new Thickness(0), Margin = new Thickness(1),
        ToolTip = help, VerticalContentAlignment = VerticalAlignment.Center, HorizontalContentAlignment = HorizontalAlignment.Center
      };
      button.SetValue(AutomationProperties.NameProperty, help);
      button.Click += delegate { if (click != null) click(); };
      return button;
    }

    internal static UIElement Label(string icon, string text, double iconSize = 15) {
      var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
      row.Children.Add(Icon(icon, iconSize));
      row.Children.Add(new TextBlock { Text = text, Margin = new Thickness(7, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
      return row;
    }

    internal sealed class WindowShell : Grid {
      internal Border Face;
      internal void SetContentOpacity(double value) {
        Opacity = Math.Max(0.2, Math.Min(1, value));
        // Subpixel text is appropriate only over an opaque content surface.
        RenderOptions.SetClearTypeHint(Face, Opacity >= 1 ? ClearTypeHint.Enabled : ClearTypeHint.Auto);
      }
    }
    internal static WindowShell Shell(UIElement child, int padding = 0, int radius = 8, int gutter = 10, int blur = 14) {
      var shell = new WindowShell { Margin = new Thickness(gutter), UseLayoutRounding = true, SnapsToDevicePixels = true };
      // Effects must never be ancestors of text: render only this empty silhouette.
      shell.Children.Add(new Border {
        Background = Program.Surface, CornerRadius = new CornerRadius(radius), IsHitTestVisible = false,
        Effect = new DropShadowEffect { BlurRadius = blur, ShadowDepth = 2, Opacity = 0.14, Color = Colors.Black, RenderingBias = RenderingBias.Quality }
      });
      shell.Face = new Border {
        Background = Program.Surface, BorderBrush = Program.Line, BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(radius), Padding = new Thickness(padding), Child = child,
        UseLayoutRounding = true, SnapsToDevicePixels = true
      };
      shell.Children.Add(shell.Face); shell.SetContentOpacity(1); return shell;
    }

    static Binding ParentBinding(string property) {
      return new Binding(property) { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) };
    }

    internal static Style ButtonStyle() {
      var style = new Style(typeof(Button));
      style.Setters.Add(new Setter(Control.FontFamilyProperty, new FontFamily("Segoe UI, Microsoft YaHei UI")));
      style.Setters.Add(new Setter(Control.FontSizeProperty, 12.0));
      style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 6, 10, 6)));
      style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(2)));
      style.Setters.Add(new Setter(Control.ForegroundProperty, Program.Ink));
      style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
      style.Setters.Add(new Setter(Control.BorderBrushProperty, Brushes.Transparent));
      style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
      style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Center));
      style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
      style.Setters.Add(new Setter(FrameworkElement.CursorProperty, Cursors.Hand));
      style.Setters.Add(new Setter(FrameworkElement.FocusVisualStyleProperty, null));

      var template = new ControlTemplate(typeof(Button));
      var chrome = new FrameworkElementFactory(typeof(Border)); chrome.Name = "chrome";
      chrome.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
      chrome.SetBinding(Border.BackgroundProperty, ParentBinding("Background"));
      chrome.SetBinding(Border.BorderBrushProperty, ParentBinding("BorderBrush"));
      chrome.SetBinding(Border.BorderThicknessProperty, ParentBinding("BorderThickness"));
      chrome.SetBinding(Border.PaddingProperty, ParentBinding("Padding"));
      var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
      presenter.SetBinding(ContentPresenter.ContentProperty, ParentBinding("Content"));
      presenter.SetBinding(ContentPresenter.ContentTemplateProperty, ParentBinding("ContentTemplate"));
      presenter.SetBinding(FrameworkElement.HorizontalAlignmentProperty, ParentBinding("HorizontalContentAlignment"));
      presenter.SetBinding(FrameworkElement.VerticalAlignmentProperty, ParentBinding("VerticalContentAlignment"));
      presenter.SetValue(ContentPresenter.RecognizesAccessKeyProperty, true);
      chrome.AppendChild(presenter); template.VisualTree = chrome;

      var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
      hover.Setters.Add(new Setter(Border.BackgroundProperty, Program.Hover, "chrome"));
      hover.Setters.Add(new Setter(Control.ForegroundProperty, Program.Accent)); template.Triggers.Add(hover);
      var primaryHover = new MultiTrigger();
      primaryHover.Conditions.Add(new System.Windows.Condition(UIElement.IsMouseOverProperty, true));
      primaryHover.Conditions.Add(new System.Windows.Condition(FrameworkElement.TagProperty, "primary"));
      primaryHover.Setters.Add(new Setter(Border.BackgroundProperty, Program.Accent, "chrome"));
      primaryHover.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
      primaryHover.Setters.Add(new Setter(UIElement.OpacityProperty, 0.88, "chrome")); template.Triggers.Add(primaryHover);
      var pressed = new Trigger { Property = System.Windows.Controls.Primitives.ButtonBase.IsPressedProperty, Value = true };
      pressed.Setters.Add(new Setter(UIElement.OpacityProperty, 0.90, "chrome")); template.Triggers.Add(pressed);
      var focused = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true };
      focused.Setters.Add(new Setter(Border.BorderBrushProperty, Program.Accent, "chrome")); template.Triggers.Add(focused);
      var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
      disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.38, "chrome")); template.Triggers.Add(disabled);
      style.Setters.Add(new Setter(Control.TemplateProperty, template));
      return style;
    }

    sealed class MotionState {
      internal int Generation;
      internal bool Hiding;
      internal TranslateTransform Translation;
      internal FrameworkElement Content;
    }
    static readonly Dictionary<Window, MotionState> Motions = new Dictionary<Window, MotionState>();

    static MotionState State(Window window) {
      MotionState state;
      if (Motions.TryGetValue(window, out state)) return state;
      state = new MotionState(); Motions[window] = state;
      window.Closed += delegate { Motions.Remove(window); };
      return state;
    }

    static TranslateTransform Translation(Window window, MotionState state) {
      var content = window.Content as FrameworkElement;
      if (content == null) return null;
      if (state.Content == content && state.Translation != null) return state.Translation;
      var group = new TransformGroup();
      if (content.RenderTransform != null && content.RenderTransform != Transform.Identity) group.Children.Add(content.RenderTransform);
      state.Translation = new TranslateTransform(); group.Children.Add(state.Translation);
      content.RenderTransform = group; state.Content = content;
      return state.Translation;
    }

    static bool ShouldAnimate(bool animate) { return animate && AnimationsEnabled && SystemParameters.ClientAreaAnimation; }

    internal static void Show(Window window, bool animate = true) {
      if (window == null) return;
      var state = State(window);
      bool visible = window.IsVisible;
      if (visible && !state.Hiding) {
        if (!ShouldAnimate(animate)) {
          state.Generation++; window.BeginAnimation(UIElement.OpacityProperty, null); window.Opacity = 1;
          if (state.Translation != null) { state.Translation.BeginAnimation(TranslateTransform.YProperty, null); state.Translation.Y = 0; }
        }
        return;
      }
      double opacity = visible ? window.Opacity : 0;
      double offset = visible && state.Translation != null ? state.Translation.Y : 4;
      state.Generation++; state.Hiding = false; window.IsHitTestVisible = true;
      window.BeginAnimation(UIElement.OpacityProperty, null);
      var translation = Translation(window, state);
      if (translation != null) translation.BeginAnimation(TranslateTransform.YProperty, null);
      if (!ShouldAnimate(animate)) {
        window.Opacity = 1; if (translation != null) translation.Y = 0;
        if (!visible) window.Show(); return;
      }
      window.Opacity = opacity; if (translation != null) translation.Y = offset;
      if (!visible) window.Show();
      var duration = new Duration(TimeSpan.FromMilliseconds(150));
      var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
      window.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(opacity, 1, duration) { EasingFunction = easing, FillBehavior = FillBehavior.Stop });
      window.Opacity = 1;
      if (translation != null) {
        translation.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(offset, 0, duration) { EasingFunction = easing, FillBehavior = FillBehavior.Stop });
        translation.Y = 0;
      }
    }

    internal static void Hide(Window window, bool animate = true) {
      if (window == null) return;
      var state = State(window);
      if (state.Hiding && ShouldAnimate(animate)) return;
      double opacity = window.Opacity;
      int generation = ++state.Generation; state.Hiding = true; window.IsHitTestVisible = false;
      window.BeginAnimation(UIElement.OpacityProperty, null);
      if (state.Translation != null) { state.Translation.BeginAnimation(TranslateTransform.YProperty, null); state.Translation.Y = 0; }
      if (!window.IsVisible || !ShouldAnimate(animate)) {
        window.Hide(); window.Opacity = 1; state.Hiding = false; return;
      }
      var fade = new DoubleAnimation(opacity, 0, new Duration(TimeSpan.FromMilliseconds(100))) { FillBehavior = FillBehavior.HoldEnd };
      fade.Completed += delegate {
        if (state.Generation != generation || !state.Hiding) return;
        window.Hide(); window.BeginAnimation(UIElement.OpacityProperty, null); window.Opacity = 1; state.Hiding = false;
      };
      window.BeginAnimation(UIElement.OpacityProperty, fade);
    }
  }
}
