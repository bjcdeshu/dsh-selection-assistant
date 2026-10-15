using System;
using System.Windows.Threading;
using Microsoft.Win32;

namespace DshSelectionAssistant {
  // SystemEvents owns a broadcast-capable window. The message-only hotkey
  // window cannot receive Windows' WM_SETTINGCHANGE broadcasts.
  internal sealed class ThemeWatcher : IDisposable {
    readonly Dispatcher Dispatcher;
    readonly Func<string> Preference;
    readonly Func<bool?> ReadSystem;
    readonly Action<bool> Apply;
    readonly DispatcherTimer Deferred, Fallback;
    bool? CurrentDark;
    bool Disposed;

    internal ThemeWatcher(Dispatcher dispatcher, Func<string> preference, Action<bool> apply)
      : this(dispatcher, preference, apply, ReadSystemTheme, TimeSpan.FromSeconds(30)) {}

    internal ThemeWatcher(Dispatcher dispatcher, Func<string> preference, Action<bool> apply,
      Func<bool?> readSystem, TimeSpan fallbackInterval) {
      Dispatcher = dispatcher; Preference = preference; Apply = apply; ReadSystem = readSystem;
      Deferred = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.FromMilliseconds(200) };
      Deferred.Tick += OnDeferred;
      Fallback = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = fallbackInterval };
      Fallback.Tick += OnFallback;
      SystemEvents.UserPreferenceChanged += OnPreferenceChanged;
      SystemEvents.PowerModeChanged += OnPowerModeChanged;
      SystemEvents.SessionSwitch += OnSessionSwitch;
      Fallback.Start();
      Refresh();
    }

    static bool? ReadSystemTheme() {
      try {
        object value = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1);
        return Convert.ToInt32(value ?? 1) == 0;
      } catch { return null; }
    }

    internal void Refresh() {
      if (Disposed) return;
      if (!Dispatcher.CheckAccess()) { RequestRefresh(); return; }
      string mode = Preference();
      bool dark = mode == "dark";
      if (mode != "dark" && mode != "light") dark = ReadSystem() ?? CurrentDark ?? false;
      if (CurrentDark == dark) return;
      CurrentDark = dark;
      Apply(dark);
    }

    void RequestRefresh() {
      if (Disposed || Dispatcher.HasShutdownStarted) return;
      // Events may arrive on the SystemEvents thread and in a burst. Coalesce
      // them on the UI dispatcher, after Windows finishes updating its values.
      try {
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate {
          if (Disposed) return;
          Deferred.Stop(); Deferred.Start();
        }));
      } catch (InvalidOperationException) { /* Dispatcher is shutting down. */ }
    }
    void OnPreferenceChanged(object sender, UserPreferenceChangedEventArgs e) { RequestRefresh(); }
    void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e) { if (e.Mode == PowerModes.Resume) RequestRefresh(); }
    void OnSessionSwitch(object sender, SessionSwitchEventArgs e) {
      if (e.Reason == SessionSwitchReason.SessionUnlock || e.Reason == SessionSwitchReason.SessionLogon) RequestRefresh();
    }
    void OnDeferred(object sender, EventArgs e) { Deferred.Stop(); Refresh(); }
    void OnFallback(object sender, EventArgs e) { Refresh(); }

    public void Dispose() {
      if (Disposed) return;
      Disposed = true;
      SystemEvents.UserPreferenceChanged -= OnPreferenceChanged;
      SystemEvents.PowerModeChanged -= OnPowerModeChanged;
      SystemEvents.SessionSwitch -= OnSessionSwitch;
      Deferred.Stop(); Fallback.Stop();
      Deferred.Tick -= OnDeferred; Fallback.Tick -= OnFallback;
    }
  }
}
