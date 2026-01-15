using System.Net;
using System.Net.Sockets;

namespace ClipboardWatcherWin;

internal sealed class LocalStaticFileServer : IDisposable
{
    private readonly string _root;
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private string? _baseUrl;

    public LocalStaticFileServer(string root)
    {
        _root = root;
    }

    public Task<string> StartAsync()
    {
        if (_listener is not null && _baseUrl is not null)
        {
            return Task.FromResult(_baseUrl);
        }

        var port = GetAvailablePort();
        var baseUrl = $"http://127.0.0.1:{port}/";

        _listener = new HttpListener();
        _listener.Prefixes.Add(baseUrl);
        _listener.Start();

        _baseUrl = baseUrl;
        _cts = new CancellationTokenSource();
        _ = Task.Run(() => ListenLoopAsync(_cts.Token));

        return Task.FromResult(baseUrl);
    }

    public void Dispose()
    {
        try
        {
            _cts?.Cancel();
            _listener?.Stop();
            _listener?.Close();
        }
        catch
        {
        }
    }

    private async Task ListenLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            HttpListenerContext? context = null;
            try
            {
                context = await _listener!.GetContextAsync();
            }
            catch when (token.IsCancellationRequested)
            {
                return;
            }
            catch
            {
                continue;
            }

            _ = Task.Run(() => HandleRequestAsync(context));
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context)
    {
        try
        {
            var relativePath = context.Request.Url?.AbsolutePath ?? "/";
            relativePath = Uri.UnescapeDataString(relativePath.TrimStart('/'));

            if (string.IsNullOrWhiteSpace(relativePath))
            {
                relativePath = "index.html";
            }

            if (relativePath.EndsWith("/", StringComparison.Ordinal))
            {
                relativePath += "index.html";
            }

            var safePath = relativePath.Replace('/', Path.DirectorySeparatorChar);
            var candidatePath = Path.GetFullPath(Path.Combine(_root, safePath));
            if (!candidatePath.StartsWith(_root, StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
                context.Response.Close();
                return;
            }

            var resolved = ResolveFilePath(candidatePath, context.Request.Headers["Accept-Encoding"]);
            if (resolved is null)
            {
                context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                context.Response.Close();
                return;
            }

            var filePath = resolved.Value.path;
            var encoding = resolved.Value.encoding;
            var contentType = GetContentType(filePath);
            if (!string.IsNullOrWhiteSpace(contentType))
            {
                context.Response.ContentType = contentType;
            }

            if (!string.IsNullOrWhiteSpace(encoding))
            {
                context.Response.AddHeader("Content-Encoding", encoding);
            }

            context.Response.AddHeader("Cache-Control", "no-cache");

            await using var fileStream = File.OpenRead(filePath);
            context.Response.ContentLength64 = fileStream.Length;
            await fileStream.CopyToAsync(context.Response.OutputStream);
            context.Response.OutputStream.Close();
        }
        catch
        {
            try
            {
                context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                context.Response.Close();
            }
            catch
            {
            }
        }
    }

    private static (string path, string? encoding)? ResolveFilePath(string candidatePath, string? acceptEncoding)
    {
        if (File.Exists(candidatePath))
        {
            var encodedPath = ChooseEncodedVariant(candidatePath, acceptEncoding);
            if (encodedPath is not null)
            {
                return encodedPath;
            }

            return (candidatePath, null);
        }

        if (acceptEncoding is not null)
        {
            var brotliPath = candidatePath + ".br";
            if (File.Exists(brotliPath))
            {
                return (brotliPath, "br");
            }

            var gzipPath = candidatePath + ".gz";
            if (File.Exists(gzipPath))
            {
                return (gzipPath, "gzip");
            }
        }

        return null;
    }

    private static (string path, string encoding)? ChooseEncodedVariant(string originalPath, string? acceptEncoding)
    {
        if (string.IsNullOrWhiteSpace(acceptEncoding))
        {
            return null;
        }

        if (acceptEncoding.Contains("br", StringComparison.OrdinalIgnoreCase))
        {
            var brotliPath = originalPath + ".br";
            if (File.Exists(brotliPath))
            {
                return (brotliPath, "br");
            }
        }

        if (acceptEncoding.Contains("gzip", StringComparison.OrdinalIgnoreCase))
        {
            var gzipPath = originalPath + ".gz";
            if (File.Exists(gzipPath))
            {
                return (gzipPath, "gzip");
            }
        }

        return null;
    }

    private static int GetAvailablePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static string GetContentType(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        var effectiveExtension = extension;

        if (extension is ".br" or ".gz")
        {
            var withoutEncoding = Path.GetFileNameWithoutExtension(path);
            effectiveExtension = Path.GetExtension(withoutEncoding).ToLowerInvariant();
        }

        return effectiveExtension switch
        {
            ".html" => "text/html",
            ".css" => "text/css",
            ".js" => "application/javascript",
            ".json" => "application/json",
            ".wasm" => "application/wasm",
            ".png" => "image/png",
            ".svg" => "image/svg+xml",
            ".ico" => "image/x-icon",
            ".map" => "application/json",
            ".dll" => "application/octet-stream",
            ".dat" => "application/octet-stream",
            _ => "application/octet-stream"
        };
    }
}
