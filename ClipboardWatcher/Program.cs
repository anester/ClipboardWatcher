using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace ClipboardWatcher;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        var port = PortResolver.Resolve(args);
        var dbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ClipboardWatcher",
            "clipboard.db");

        var store = new ClipboardStore(dbPath);
        using var trayContext = new TrayApplicationContext(store, port);
        Application.Run(trayContext);
    }
}

internal static class PortResolver
{
    private const int DefaultPort = 5055;

    public static int Resolve(string[] args)
    {
        var envValue = Environment.GetEnvironmentVariable("CLIPBOARDWATCHER_PORT");
        if (int.TryParse(envValue, out var envPort) && IsValid(envPort))
        {
            return envPort;
        }

        var argPort = args
            .Select(ParseArg)
            .FirstOrDefault(port => port.HasValue && IsValid(port.Value));

        return argPort ?? DefaultPort;
    }

    private static int? ParseArg(string arg)
    {
        const string prefix = "--port=";
        if (arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && int.TryParse(arg[prefix.Length..], out var value))
        {
            return value;
        }

        return null;
    }

    private static bool IsValid(int port) => port is >= 1024 and <= 65535;
}
