// Copyright (c) 2026 VSCN-Studio
// SPDX-License-Identifier: MIT

using System.Reflection;
using System.Runtime.InteropServices;

namespace VSRmlUi;

internal static class Native
{
    private const string Library = "vsrmlui_native";
    private static nint libraryHandle;
    static Native() => NativeLibrary.SetDllImportResolver(typeof(Native).Assembly, Resolve);
    private static nint Resolve(string name, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (name != Library) return 0;
        if (libraryHandle != 0) return libraryHandle;
        var (rid, file) = PlatformLibrary.GetCurrent();
        string root = Path.GetDirectoryName(assembly.Location)!;
        string path = Path.Combine(root, "native", rid, file);
        if (!File.Exists(path)) throw new DllNotFoundException($"RmlUi native library is not packaged for {rid}: {path}");
        // Keep loaded for the process lifetime: the runtime itself is explicitly shut down per world.
        try { return libraryHandle = NativeLibrary.Load(path); }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException)
        {
            throw new DllNotFoundException($"Cannot load RmlUi for {rid} from {path}. Install the package matching the game process architecture and check native dependencies. {ex.Message}", ex);
        }
    }
    internal static string Utf8(nint ptr) => Marshal.PtrToStringUTF8(ptr) ?? "";
    internal static void Check() { string error = Utf8(vr_error()); if (error.Length != 0) throw new RmlUiException(error); }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int Read(int kind, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, out nint data, out int length, out int width, out int height);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void Free(nint data);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void Log(int level, [MarshalAs(UnmanagedType.LPUTF8Str)] string message);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void Write(int kind, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);
    [StructLayout(LayoutKind.Sequential)] internal struct Callbacks { public Read Read; public Free Free; public Log Log; public Write Write; }
    [StructLayout(LayoutKind.Sequential)] internal struct Event { public ulong Subscription, Document, Target; }
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int vr_abi();
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern nint vr_error();
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int vr_init(in Callbacks callbacks, int headless);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int vr_shutdown();
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern ulong vr_load([MarshalAs(UnmanagedType.LPUTF8Str)] string path, [MarshalAs(UnmanagedType.LPUTF8Str)] string? markup, int width, int height, float scale);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern long vr_doc(ulong document, int operation, int a, int b, float value, [MarshalAs(UnmanagedType.LPUTF8Str)] string? text);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern ulong vr_element(ulong document, ulong element, int operation, [MarshalAs(UnmanagedType.LPUTF8Str)] string? name, [MarshalAs(UnmanagedType.LPUTF8Str)] string? value);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern nint vr_get(ulong document, ulong element, int operation, [MarshalAs(UnmanagedType.LPUTF8Str)] string? name);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern unsafe int vr_query_all(ulong document, ulong element, [MarshalAs(UnmanagedType.LPUTF8Str)] string selector, ulong* output, int capacity);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int vr_font([MarshalAs(UnmanagedType.LPUTF8Str)] string path, [MarshalAs(UnmanagedType.LPUTF8Str)] string family, int weight, int italic, int fallback);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern ulong vr_listen(ulong document, ulong element, [MarshalAs(UnmanagedType.LPUTF8Str)] string type, int capture);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int vr_unlisten(ulong subscription);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int vr_poll(out Event result);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern nint vr_event_value(int field);
}

internal static class PlatformLibrary
{
    internal static (string Rid, string File) GetCurrent() => Get(
        OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsLinux() ? "linux" : OperatingSystem.IsMacOS() ? "osx" : "unknown",
        RuntimeInformation.ProcessArchitecture);

    // ProcessArchitecture intentionally handles an x64 game running under Rosetta.
    internal static (string Rid, string File) Get(string os, Architecture architecture)
    {
        string arch = architecture switch { Architecture.X64 => "x64", Architecture.Arm64 => "arm64", _ => throw new PlatformNotSupportedException($"RmlUi requires an x64 or arm64 game process; got {architecture}.") };
        string file = os switch { "win" => "vsrmlui_native.dll", "linux" => "libvsrmlui_native.so", "osx" => "libvsrmlui_native.dylib", _ => throw new PlatformNotSupportedException($"RmlUi does not support OS {os}.") };
        if (os == "win" && architecture != Architecture.X64) throw new PlatformNotSupportedException("RmlUi Windows builds currently require an x64 game process.");
        return ($"{os}-{arch}", file);
    }
}
