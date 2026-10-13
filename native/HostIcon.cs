using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace DshSelectionAssistant {
  // Read the running host's own icon without executing or copying its executable.
  // Keep the complete icon group so WPF can choose the right image for each DPI.
  internal sealed class HostIcon : IDisposable {
    internal ImageSource WindowImage { get; private set; }
    internal Drawing.Icon TrayIcon { get; private set; }
    internal bool IsHostIcon { get; private set; }
    MemoryStream TrayStream;

    HostIcon() { TrayIcon = (Drawing.Icon)Drawing.SystemIcons.Application.Clone(); }

    internal static HostIcon Load(string source) {
      var result = new HostIcon();
      try {
        if (String.IsNullOrWhiteSpace(source) || !Path.IsPathRooted(source)) return result;
        string path = Path.GetFullPath(source);
        if (!File.Exists(path)) return result;
        byte[] bytes = ReadIconGroup(path);
        if (bytes == null) return result;
        BitmapFrame image;
        using (var stream = new MemoryStream(bytes, false)) {
          image = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
          image.Freeze();
        }
        var trayStream = new MemoryStream(bytes, false);
        Drawing.Icon trayIcon;
        try { trayIcon = new Drawing.Icon(trayStream, Forms.SystemInformation.SmallIconSize); }
        catch { trayStream.Dispose(); throw; }
        result.TrayIcon.Dispose();
        result.TrayIcon = trayIcon;
        result.TrayStream = trayStream;
        result.WindowImage = image;
        result.IsHostIcon = true;
      } catch {
        // Missing, replaced or unsupported host resources must not prevent the UI
        // from starting. Demos without a host retain the ordinary application icon.
      }
      return result;
    }

    static byte[] ReadIconGroup(string path) {
      // AS_DATAFILE | AS_IMAGE_RESOURCE maps resources only; no DllMain runs.
      IntPtr module = LoadLibraryEx(path, IntPtr.Zero, 0x00000002 | 0x00000020);
      if (module == IntPtr.Zero) return null;
      try {
        byte[] icon = null;
        EnumResourceName callback = delegate(IntPtr handle, IntPtr type, IntPtr name, IntPtr parameter) {
          try { icon = BuildIcon(handle, ReadResource(handle, name, new IntPtr(14))); }
          catch { icon = null; }
          return icon == null;
        };
        EnumResourceNames(module, new IntPtr(14), callback, IntPtr.Zero);
        GC.KeepAlive(callback);
        return icon;
      } finally { FreeLibrary(module); }
    }

    static byte[] BuildIcon(IntPtr module, byte[] group) {
      if (group == null || group.Length < 6 || BitConverter.ToUInt16(group, 0) != 0 || BitConverter.ToUInt16(group, 2) != 1) return null;
      int count = BitConverter.ToUInt16(group, 4);
      if (count < 1 || count > 256 || group.Length < 6 + count * 14) return null;
      var images = new List<byte[]>();
      for (int i = 0; i < count; i++) {
        int entry = 6 + i * 14;
        byte[] image = ReadResource(module, new IntPtr(BitConverter.ToUInt16(group, entry + 12)), new IntPtr(3));
        if (image == null || image.Length != BitConverter.ToUInt32(group, entry + 8)) return null;
        images.Add(image);
      }
      using (var output = new MemoryStream()) {
        using (var writer = new BinaryWriter(output)) {
          writer.Write(group, 0, 6);
          int offset = 6 + count * 16;
          for (int i = 0; i < count; i++) {
            writer.Write(group, 6 + i * 14, 12);
            writer.Write(offset);
            offset = checked(offset + images[i].Length);
          }
          foreach (byte[] image in images) writer.Write(image);
          return output.ToArray();
        }
      }
    }

    static byte[] ReadResource(IntPtr module, IntPtr name, IntPtr type) {
      IntPtr resource = FindResource(module, name, type);
      if (resource == IntPtr.Zero) return null;
      uint size = SizeofResource(module, resource);
      if (size == 0 || size > 16 * 1024 * 1024) return null;
      IntPtr data = LockResource(LoadResource(module, resource));
      if (data == IntPtr.Zero) return null;
      var bytes = new byte[(int)size];
      Marshal.Copy(data, bytes, 0, bytes.Length);
      return bytes;
    }

    public void Dispose() {
      if (TrayIcon != null) { TrayIcon.Dispose(); TrayIcon = null; }
      if (TrayStream != null) { TrayStream.Dispose(); TrayStream = null; }
    }

    delegate bool EnumResourceName(IntPtr module, IntPtr type, IntPtr name, IntPtr parameter);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool EnumResourceNames(IntPtr module, IntPtr type, EnumResourceName callback, IntPtr parameter);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr FindResource(IntPtr module, IntPtr name, IntPtr type);
    [DllImport("kernel32.dll")] static extern IntPtr LoadResource(IntPtr module, IntPtr resource);
    [DllImport("kernel32.dll")] static extern IntPtr LockResource(IntPtr resource);
    [DllImport("kernel32.dll")] static extern uint SizeofResource(IntPtr module, IntPtr resource);
    [DllImport("kernel32.dll")] static extern bool FreeLibrary(IntPtr module);
  }
}
