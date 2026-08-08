namespace Matcher;

public static class Globals {
    public enum OS {
        Windows,
        Linux,
        MacOS
    };
    public static OS OperatingSystem = Environment.OSVersion.Platform switch {
		PlatformID.Win32NT or PlatformID.Win32S or PlatformID.Win32Windows or PlatformID.WinCE => OS.Windows,
		PlatformID.MacOSX => OS.MacOS,
		_ => OS.Linux
	};
}
