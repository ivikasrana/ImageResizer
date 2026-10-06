using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace ImageResizer;

/// <summary>Lossy WebP encoding through libwebp (BSD-3-Clause), embedded in the exe and unpacked on first use.</summary>
static class WebpWriter
{
    [DllImport("libwebp", CallingConvention = CallingConvention.Cdecl)]
    static extern nuint WebPEncodeBGRA(byte[] bgra, int width, int height, int stride, float quality, out IntPtr output);

    [DllImport("libwebp", CallingConvention = CallingConvention.Cdecl)]
    static extern void WebPFree(IntPtr ptr);

    static IntPtr _handle;

    static WebpWriter() =>
        NativeLibrary.SetDllImportResolver(typeof(WebpWriter).Assembly, (name, _, _) => name == "libwebp" ? Load() : IntPtr.Zero);

    static IntPtr Load()
    {
        if (_handle != IntPtr.Zero) return _handle;
        var asm = Assembly.GetExecutingAssembly();
        var dir = Path.Combine(Path.GetTempPath(), "ImageResizer", asm.GetName().Version?.ToString() ?? "0");
        Directory.CreateDirectory(dir);
        foreach (var dll in new[] { "libsharpyuv.dll", "libwebp.dll" })
        {
            var target = Path.Combine(dir, dll);
            using var res = asm.GetManifestResourceStream(dll)!;
            if (File.Exists(target) && new FileInfo(target).Length == res.Length) continue;
            try { using var fs = File.Create(target); res.CopyTo(fs); }
            catch (IOException) { /* another instance is extracting/using it */ }
        }
        return _handle = NativeLibrary.Load(Path.Combine(dir, "libwebp.dll"));
    }

    public static bool IsAvailable()
    {
        try { return Load() != IntPtr.Zero; } catch { return false; }
    }

    /// <param name="bgra">Non-premultiplied BGRA32 pixels.</param>
    /// <param name="quality">1-100</param>
    public static void Save(string path, byte[] bgra, int width, int height, int quality)
    {
        var size = WebPEncodeBGRA(bgra, width, height, width * 4, quality, out var ptr);
        if (size == 0) throw new InvalidOperationException("WebP encoding failed.");
        try
        {
            var data = new byte[(int)size];
            Marshal.Copy(ptr, data, 0, data.Length);
            File.WriteAllBytes(path, data);
        }
        finally { WebPFree(ptr); }
    }
}
