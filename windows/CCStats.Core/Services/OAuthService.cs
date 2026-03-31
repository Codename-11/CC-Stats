using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace CCStats.Core.Services;

public sealed class OAuthService : IDisposable
{
    private const string AuthorizationEndpoint = "https://claude.ai/oauth/authorize";
    private const string TokenEndpoint = "https://console.anthropic.com/v1/oauth/token";
    /// Anthropic's public OAuth client ID (used by Claude Code and OpenCode)
    private const string ClientId = "9d1c250a-e61b-44d9-88ed-5944d1962f5e";

    private static readonly TimeSpan AuthTimeout = TimeSpan.FromMinutes(5);

    private HttpListener? _listener;
    private CancellationTokenSource? _timeoutCts;
    private string? _currentState;
    private string? _currentVerifier;

    public event EventHandler? AuthStarted;
    public event EventHandler<AuthCompletedEventArgs>? AuthCompleted;
    public event EventHandler<AuthFailedEventArgs>? AuthFailed;

    public static (string CodeVerifier, string CodeChallenge) GeneratePkce()
    {
        var verifierBytes = RandomNumberGenerator.GetBytes(32);
        var codeVerifier = Base64UrlEncode(verifierBytes);

        var challengeBytes = SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier));
        var codeChallenge = Base64UrlEncode(challengeBytes);

        return (codeVerifier, codeChallenge);
    }

    public Task<string> StartAuthFlowAsync()
    {
        StopListening();
        _timeoutCts?.Dispose();

        var (verifier, challenge) = GeneratePkce();
        _currentVerifier = verifier;
        _currentState = Guid.NewGuid().ToString("N");

        HttpListener? listener = null;
        int port = 0;

        for (int attempt = 0; attempt < 3; attempt++)
        {
            port = GetAvailablePort();
            listener = new HttpListener();
            listener.Prefixes.Add($"http://localhost:{port}/");
            try
            {
                listener.Start();
                break;
            }
            catch (HttpListenerException) when (attempt < 2)
            {
                try { listener.Close(); } catch { /* best-effort cleanup */ }
                listener = null;
            }
            catch
            {
                try { listener.Close(); } catch { /* best-effort cleanup */ }
                listener = null;
                throw;
            }
        }

        _listener = listener ?? throw new InvalidOperationException("Failed to bind HTTP listener after 3 attempts");
        var redirectUri = $"http://localhost:{port}/callback";

        _timeoutCts = new CancellationTokenSource(AuthTimeout);

        AuthStarted?.Invoke(this, EventArgs.Empty);

        // Start listening for callback in background
        _ = ListenForCallbackAsync(redirectUri, _timeoutCts.Token);

        var authUrl = BuildAuthorizationUrl(challenge, _currentState, redirectUri);
        return Task.FromResult(authUrl);
    }

    public void Cancel()
    {
        _timeoutCts?.Cancel();
        StopListening();
    }

    public void Dispose()
    {
        StopListening();
        _timeoutCts?.Dispose();
    }

    private async Task ListenForCallbackAsync(string redirectUri, CancellationToken cancellationToken)
    {
        try
        {
            var listener = _listener;
            if (listener is null)
            {
                AuthFailed?.Invoke(this, new AuthFailedEventArgs("Listener was disposed before callback"));
                return;
            }
            var context = await listener.GetContextAsync().WaitAsync(cancellationToken);
            var request = context.Request;
            var query = request.QueryString;

            var code = query["code"];
            var state = query["state"];
            var error = query["error"];

            // Send styled response to browser
            var response = context.Response;
            var responseBody = error is not null
                ? BuildCallbackHtml(false, error)
                : BuildCallbackHtml(true, null);
            var buffer = Encoding.UTF8.GetBytes(responseBody);
            response.ContentLength64 = buffer.Length;
            response.ContentType = "text/html; charset=utf-8";
            await response.OutputStream.WriteAsync(buffer, cancellationToken);
            response.Close();

            if (error is not null)
            {
                AuthFailed?.Invoke(this, new AuthFailedEventArgs(error));
                return;
            }

            if (_currentState is null || code is null || state != _currentState)
            {
                AuthFailed?.Invoke(this, new AuthFailedEventArgs("Invalid callback: missing code or state mismatch"));
                return;
            }

            var validatedState = _currentState;

            await HandleCallbackAsync(code, validatedState!, redirectUri);
        }
        catch (OperationCanceledException)
        {
            AuthFailed?.Invoke(this, new AuthFailedEventArgs("Authentication timed out"));
        }
        catch (Exception ex)
        {
            AuthFailed?.Invoke(this, new AuthFailedEventArgs(ex.Message));
        }
        finally
        {
            StopListening();
        }
    }

    private async Task HandleCallbackAsync(string authCode, string state, string redirectUri)
    {
        try
        {
            var verifier = _currentVerifier
                ?? throw new InvalidOperationException("Code verifier is not set");

            using var client = new HttpClient();
            // NOTE: Do NOT send anthropic-beta on the token endpoint — upstream doesn't,
            // and the endpoint rejects it as "Invalid request format"

            var body = new Dictionary<string, string>
            {
                ["code"] = authCode,
                ["state"] = state,
                ["grant_type"] = "authorization_code",
                ["client_id"] = ClientId,
                ["redirect_uri"] = redirectUri,
                ["code_verifier"] = verifier,
            };

            Core.Services.AppLogger.Log("Auth",
                $"Token exchange: endpoint={TokenEndpoint}, redirect={redirectUri}, code={authCode[..Math.Min(8, authCode.Length)]}...");

            var content = new StringContent(
                System.Text.Json.JsonSerializer.Serialize(body),
                Encoding.UTF8,
                "application/json");

            var response = await client.PostAsync(TokenEndpoint, content);
            var json = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                // Log the actual error body for debugging
                Core.Services.AppLogger.Error("Auth",
                    $"Token exchange failed: {response.StatusCode} — {json}");
                AuthFailed?.Invoke(this, new AuthFailedEventArgs(
                    $"Token exchange failed: {response.StatusCode}"));
                return;
            }

            _currentState = null; // Clear state only after successful exchange
            AuthCompleted?.Invoke(this, new AuthCompletedEventArgs(json));
        }
        catch (Exception ex)
        {
            AuthFailed?.Invoke(this, new AuthFailedEventArgs(ex.Message));
        }
        finally
        {
            _currentVerifier = null;
        }
    }

    private void StopListening()
    {
        try
        {
            _listener?.Stop();
            _listener?.Close();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[OAuthService] Listener cleanup error: {ex.Message}");
        }

        _listener = null;
    }

    private static string BuildAuthorizationUrl(string codeChallenge, string state, string redirectUri)
    {
        var parameters = new Dictionary<string, string>
        {
            ["response_type"] = "code",
            ["client_id"] = ClientId,
            ["redirect_uri"] = redirectUri,
            ["state"] = state,
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256",
            ["scope"] = "org:create_api_key user:profile user:inference",
        };

        var query = string.Join("&", parameters.Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"));
        return $"{AuthorizationEndpoint}?{query}";
    }

    private static int GetAvailablePort()
    {
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static string Base64UrlEncode(byte[] data) =>
        Convert.ToBase64String(data)
            .Replace("+", "-")
            .Replace("/", "_")
            .TrimEnd('=');

    private static string BuildCallbackHtml(bool success, string? errorMessage)
    {
        var title = success ? "Authentication Successful" : "Authentication Failed";
        var icon = success
            ? "<svg width=\"48\" height=\"48\" viewBox=\"0 0 48 48\" fill=\"none\"><circle cx=\"24\" cy=\"24\" r=\"24\" fill=\"#D97757\"/><path d=\"M15.5 23.5C15.5 19.4 18.8 16 23 16h2c4.2 0 7.5 3.4 7.5 7.5v1c0 4.1-3.3 7.5-7.5 7.5h-2c-4.2 0-7.5-3.4-7.5-7.5v-1z\" fill=\"#1A1300\"/><circle cx=\"21\" cy=\"22\" r=\"1.5\" fill=\"#D97757\"/><circle cx=\"27\" cy=\"22\" r=\"1.5\" fill=\"#D97757\"/></svg>"
            : "<svg width=\"48\" height=\"48\" viewBox=\"0 0 48 48\" fill=\"none\"><circle cx=\"24\" cy=\"24\" r=\"24\" fill=\"#F0645B\"/><path d=\"M16 16l16 16M32 16L16 32\" stroke=\"white\" stroke-width=\"3\" stroke-linecap=\"round\"/></svg>";
        var message = success
            ? "You're signed in. Return to <strong>CC-Stats</strong> &mdash; this tab will close automatically."
            : $"Something went wrong: {System.Net.WebUtility.HtmlEncode(errorMessage ?? "Unknown error")}.<br>Please try again from CC-Stats.";
        var autoClose = success ? "<script>setTimeout(()=>window.close(),3000)</script>" : "";
        var statusLine = success
            ? "<p class=\"status\"><span class=\"dot\"></span> Connecting to Claude API...</p>"
            : "";

        return $@"<!DOCTYPE html>
<html lang=""en"">
<head>
<meta charset=""utf-8"">
<meta name=""viewport"" content=""width=device-width,initial-scale=1"">
<title>{title} — CC-Stats</title>
<style>
  *{{margin:0;padding:0;box-sizing:border-box}}
  body{{font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,sans-serif;
       background:#0F1115;color:#F5F7FB;display:flex;align-items:center;
       justify-content:center;min-height:100vh}}
  .card{{text-align:center;max-width:380px;padding:48px 40px;
        background:#1A1E26;border:1px solid #202A38;border-radius:16px}}
  .icon{{margin-bottom:20px}}
  h1{{font-size:20px;font-weight:600;margin-bottom:12px}}
  p{{font-size:14px;color:#8FA0B8;line-height:1.6}}
  .brand{{display:flex;align-items:center;justify-content:center;gap:8px;
         margin-bottom:24px;font-size:13px;color:#6B7A8D}}
  .brand strong{{color:#8FA0B8}}
  .dot{{width:6px;height:6px;border-radius:50%;background:#66B866;
       display:inline-block;animation:pulse 2s infinite}}
  @keyframes pulse{{0%,100%{{opacity:1}}50%{{opacity:.3}}}}
  .status{{margin-top:16px;font-size:12px;color:#6B7A8D}}
</style>
</head>
<body>
<div class=""card"">
  <div class=""brand""><strong>CC-Stats</strong> &middot; Claude Headroom Monitor</div>
  <div class=""icon"">{icon}</div>
  <h1>{title}</h1>
  <p>{message}</p>
  {statusLine}
</div>
{autoClose}
</body>
</html>";
    }
}

public sealed class AuthCompletedEventArgs : EventArgs
{
    public string TokenResponseJson { get; }

    public AuthCompletedEventArgs(string tokenResponseJson)
    {
        TokenResponseJson = tokenResponseJson;
    }
}

public sealed class AuthFailedEventArgs : EventArgs
{
    public string Error { get; }

    public AuthFailedEventArgs(string error)
    {
        Error = error;
    }
}
