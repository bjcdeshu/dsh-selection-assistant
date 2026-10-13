using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace DshSelectionAssistant {
  // UI preferences live beside settings.json, never in a build's native cache.
  // Dimensions are WPF DIPs. The window applies current monitor/minimum bounds.
  internal sealed class WindowPreferences : IDisposable {
    internal sealed class State {
      internal readonly double Width, Height, Opacity;
      internal State(double width, double height, double opacity) {
        Width = width; Height = height; Opacity = opacity;
      }
    }

    readonly object Gate = new object();
    readonly Dictionary<string, State> States = new Dictionary<string, State>();
    readonly Timer SaveTimer;
    readonly string StateFile;
    bool Dirty, Disposed;
    internal string LastError { get; private set; }

    internal WindowPreferences(string stateFile) {
      // A missing path disables persistence for tools that only preview the UI.
      if (!String.IsNullOrWhiteSpace(stateFile)) {
        try {
          if (!Path.IsPathRooted(stateFile)) throw new ArgumentException("Window preference path must be absolute.");
          StateFile = Path.GetFullPath(stateFile);
          Load();
        } catch (Exception error) { LastError = error.Message; }
      }
      SaveTimer = new Timer(delegate(object state) { Flush(); }, null, Timeout.Infinite, Timeout.Infinite);
    }

    internal State Get(string surface) {
      lock (Gate) {
        State state;
        return surface != null && States.TryGetValue(surface, out state) ? state : null;
      }
    }

    internal bool Update(string surface, double width, double height, double opacity) {
      if (!KnownSurface(surface) || !ValidSize(width) || !ValidSize(height) || !ValidOpacity(opacity)) return false;
      lock (Gate) {
        if (Disposed) return false;
        State previous;
        if (States.TryGetValue(surface, out previous) && previous.Width == width && previous.Height == height && previous.Opacity == opacity) return true;
        States[surface] = new State(width, height, opacity);
        Dirty = true;
        SaveTimer.Change(350, Timeout.Infinite);
        return true;
      }
    }

    internal bool Flush() {
      lock (Gate) { return FlushLocked(); }
    }

    bool FlushLocked() {
      if (!Dirty || StateFile == null) return true;
      string temporary = null;
      try {
        Directory.CreateDirectory(Path.GetDirectoryName(StateFile));
        var document = new Dictionary<string, object> { { "version", 1 } };
        foreach (var item in States) {
          document[item.Key] = new Dictionary<string, double> {
            { "width", item.Value.Width }, { "height", item.Value.Height }, { "opacity", item.Value.Opacity }
          };
        }
        string json = new JavaScriptSerializer().Serialize(document) + Environment.NewLine;
        temporary = StateFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
        using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
          using (var writer = new StreamWriter(stream, new UTF8Encoding(false))) {
            writer.Write(json); writer.Flush(); stream.Flush(true);
          }
        }
        if (File.Exists(StateFile)) File.Replace(temporary, StateFile, null);
        else File.Move(temporary, StateFile);
        temporary = null;
        Dirty = false; LastError = null;
        return true;
      } catch (Exception error) {
        // Keep the last valid file and retry pending values on a later Flush.
        LastError = error.Message;
        return false;
      } finally {
        if (temporary != null) { try { File.Delete(temporary); } catch {} }
      }
    }

    void Load() {
      if (!File.Exists(StateFile)) return;
      // The complete file contains at most two triples of numbers.
      var info = new FileInfo(StateFile);
      if (info.Length > 16384) throw new InvalidDataException("Window preference file is too large.");
      var document = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(StateFile, Encoding.UTF8)) as Dictionary<string, object>;
      object version; double number;
      if (document == null || !document.TryGetValue("version", out version) || !Number(version, out number) || number != 1) return;
      foreach (string surface in new string[] { "selection", "quick" }) {
        object value;
        if (!document.TryGetValue(surface, out value)) continue;
        var record = value as Dictionary<string, object>;
        double width, height, opacity;
        if (record != null && Field(record, "width", out width) && Field(record, "height", out height) && Field(record, "opacity", out opacity)
          && ValidSize(width) && ValidSize(height) && ValidOpacity(opacity)) States[surface] = new State(width, height, opacity);
      }
    }

    static bool KnownSurface(string value) { return value == "selection" || value == "quick"; }
    static bool Finite(double value) { return !Double.IsNaN(value) && !Double.IsInfinity(value); }
    // Reject corrupt but finite magnitudes before WPF sees them. Monitor-aware
    // bounds still belong to the window, including normal smaller displays.
    static bool ValidSize(double value) { return Finite(value) && value > 0 && value <= 32768; }
    static bool ValidOpacity(double value) { return Finite(value) && value >= 20 && value <= 100; }
    static bool Field(Dictionary<string, object> record, string key, out double value) {
      object raw; value = 0;
      return record.TryGetValue(key, out raw) && Number(raw, out value);
    }
    static bool Number(object raw, out double value) {
      value = 0;
      if (!(raw is int || raw is long || raw is decimal || raw is double || raw is float)) return false;
      value = Convert.ToDouble(raw, CultureInfo.InvariantCulture);
      return Finite(value);
    }

    public void Dispose() {
      lock (Gate) {
        if (Disposed) return;
        Disposed = true; SaveTimer.Dispose(); FlushLocked();
      }
    }
  }
}
