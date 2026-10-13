using System.Collections.Generic;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Media;

namespace DshSelectionAssistant {
  // Bound brushes remain mutable when a WPF Style/Template is sealed.
  internal sealed class ThemeColor : INotifyPropertyChanged {
    public Color Value { get; private set; }
    public event PropertyChangedEventHandler PropertyChanged;
    public void Set(string hex) { Value = (Color)ColorConverter.ConvertFromString(hex); if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs("Value")); }
  }
  internal static class Theme {
    static readonly Dictionary<string, ThemeColor> Colors = new Dictionary<string, ThemeColor>();
    internal static SolidColorBrush Brush(string key, string value) {
      var color = new ThemeColor(); color.Set(value); Colors[key] = color;
      var brush = new SolidColorBrush(); BindingOperations.SetBinding(brush, SolidColorBrush.ColorProperty, new Binding("Value") { Source = color }); return brush;
    }
    internal static void Set(string key, string value) { Colors[key].Set(value); }
  }
}
