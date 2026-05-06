using System.Net;
using System.Text;

namespace DevSprint.UI.Auth;

/// <summary>
/// A one-shot HTTP listener bound to <c>http://127.0.0.1:&lt;port&gt;/&lt;path&gt;/</c>
/// used as the OAuth 2.0 redirect target for desktop apps (RFC 8252 §7.3).
/// Awaits exactly one incoming request, returns the parsed query parameters,
/// and shuts down. Sends a friendly HTML response so the user sees confirmation
/// in the browser before closing the tab.
/// </summary>
/// <remarks>
/// Loopback (<c>127.0.0.1</c>) bindings on Windows do NOT require a URL ACL,
/// so this works for non-elevated users.
/// </remarks>
public sealed class LoopbackHttpListener : IDisposable
{
    private readonly HttpListener _listener;
    private readonly string _normalisedPath;

    /// <summary>The full callback URL to register with the OAuth provider (e.g., <c>http://127.0.0.1:7890/callback</c>).</summary>
    public string CallbackUrl { get; }

    public LoopbackHttpListener(int port, string path = "callback")
    {
        _normalisedPath = path.Trim('/');
        _listener = new HttpListener();
        // Trailing slash is required by HttpListener prefix syntax.
        _listener.Prefixes.Add($"http://127.0.0.1:{port}/{_normalisedPath}/");
        CallbackUrl = $"http://127.0.0.1:{port}/{_normalisedPath}";
    }

    /// <summary>
    /// Starts listening (if not already) and awaits exactly one request, returning
    /// its query string as a dictionary. Cancellation stops the listener.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, string>> AwaitCallbackAsync(CancellationToken cancellationToken)
    {
        if (!_listener.IsListening) _listener.Start();

        // Stopping the listener releases GetContextAsync with an exception we
        // translate into OperationCanceledException for the caller.
        using var registration = cancellationToken.Register(() =>
        {
            try { if (_listener.IsListening) _listener.Stop(); } catch { /* best-effort */ }
        });

        HttpListenerContext context;
        try
        {
            context = await _listener.GetContextAsync().ConfigureAwait(false);
        }
        catch (HttpListenerException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }

        // Parse query parameters before responding (response close ends the request).
        var query = context.Request.QueryString;
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var key in query.AllKeys)
        {
            if (key is null) continue;
            result[key] = query[key] ?? string.Empty;
        }

        await WriteFriendlyResponseAsync(context, cancellationToken);
        return result;
    }

    private static async Task WriteFriendlyResponseAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        const string html = """
            <!DOCTYPE html>
            <html><head><meta charset="utf-8"><title>DevSprint sign-in</title>
            <style>
              body { font-family: -apple-system, Segoe UI, sans-serif; background: #f3f3f3; padding: 48px; color: #1a1a1a; }
              .card { max-width: 460px; margin: 0 auto; background: #fff; border: 1px solid #e5e5e5; border-radius: 8px; padding: 28px 32px; }
              h1 { margin: 0 0 8px; font-size: 20px; font-weight: 600; }
              p { margin: 0; color: #616161; font-size: 14px; }
            </style></head><body>
            <div class="card"><h1>Sign-in complete</h1><p>You can close this tab and return to DevSprint.</p></div>
            </body></html>
            """;
        var bytes = Encoding.UTF8.GetBytes(html);
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.ContentLength64 = bytes.Length;
        context.Response.StatusCode = 200;
        await context.Response.OutputStream.WriteAsync(bytes, cancellationToken);
        context.Response.Close();
    }

    public void Dispose()
    {
        try { if (_listener.IsListening) _listener.Stop(); } catch { /* best-effort */ }
        _listener.Close();
    }
}
