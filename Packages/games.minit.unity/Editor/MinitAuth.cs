using System;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace MinitGames.Editor
{
    /// <summary>Outcome of <see cref="MinitAuth.LoginAsync"/>.</summary>
    public class MinitAuthResult
    {
        public bool Success;
        public string Error;
        public MinitUser User;

        public static MinitAuthResult Ok(MinitUser user) => new MinitAuthResult { Success = true, User = user };
        public static MinitAuthResult Failed(string error) => new MinitAuthResult { Success = false, Error = error };
    }

    /// <summary>
    /// Login orchestrator for the Minit "Login &amp; Upload" editor window.
    ///
    /// <para>
    /// Auth design (DROP-3224): no api key is embedded in Unity and <c>POST /users/refresh</c>
    /// is never called. Instead, whenever a fresh backend access token is needed, we re-run the
    /// loopback browser handoff (mirrors the MCP native-app / RFC 8252 loopback flow): the
    /// system browser silently mints a fresh Cognito token from its long-lived Cognito session
    /// (no user interaction unless that session has lapsed) and hands it back to a local
    /// <see cref="HttpListener"/>, which we exchange via the PUBLIC <c>POST /auth/console</c>
    /// (authorizer: None — no api key needed). This removes the need for any secret in Unity.
    /// </para>
    /// </summary>
    public static class MinitAuth
    {
        private static readonly TimeSpan LoginTimeout = TimeSpan.FromMinutes(3);

        /// <summary>
        /// Runs the full loopback handoff: opens the system browser, waits for the callback,
        /// exchanges the Cognito token for backend tokens via <c>/auth/console</c>, fetches the
        /// user's identity via <c>/users/self</c>, and stores the result in <paramref name="session"/>.
        /// </summary>
        public static async Task<MinitAuthResult> LoginAsync(MinitSession session, CancellationToken ct = default)
        {
            if (session == null)
                throw new ArgumentNullException(nameof(session));

            HttpListener listener = null;
            try
            {
                int port = GetFreeTcpPort();
                string redirectUri = $"http://127.0.0.1:{port}/callback";

                // Bind the listener to the bare root of the port rather than the exact
                // "/callback" path: HttpListener prefix matching requires a trailing slash on
                // registered prefixes, and a request to "/callback" (no trailing slash, just a
                // query string) would not match a "/callback/" prefix. Binding the root avoids
                // that mismatch; the callback's query string is validated below regardless.
                listener = new HttpListener();
                listener.Prefixes.Add($"http://127.0.0.1:{port}/");
                listener.Start();

                string state = GenerateStateNonce();
                string authorizeUrl =
                    $"{MinitEnvironments.ConsoleBaseUrl(session.Environment)}/unity/authorize" +
                    $"?redirect_uri={Uri.EscapeDataString(redirectUri)}&state={Uri.EscapeDataString(state)}";

                Debug.Log("[Minit] Opening browser for sign-in — return to Unity once you've signed in.");
                Application.OpenURL(authorizeUrl);

                (string cognitoToken, string waitError) = await WaitForCallbackAsync(listener, state, ct);
                if (waitError != null)
                    return MinitAuthResult.Failed(waitError);

                return await ExchangeAndFetchIdentityAsync(session, cognitoToken, ct);
            }
            catch (OperationCanceledException)
            {
                return MinitAuthResult.Failed("Login cancelled.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Minit] Login failed with an unexpected exception:\n{ex}");
                return MinitAuthResult.Failed($"Login failed: {ex.Message}");
            }
            finally
            {
                try
                {
                    listener?.Stop();
                    listener?.Close();
                }
                catch
                {
                    // Best-effort cleanup — listener may already be stopped/disposed.
                }
            }
        }

        /// <summary>
        /// Returns a valid access token, silently re-running the loopback handoff (via
        /// <see cref="LoginAsync"/>) if the currently stored token is missing or expired.
        /// Step 3 (drops/upload REST client) and Step 4 (window UI) should call this before
        /// every authenticated request rather than reading <see cref="MinitSession.AccessToken"/>
        /// directly.
        /// </summary>
        public static async Task<string> EnsureAccessTokenAsync(MinitSession session, CancellationToken ct = default)
        {
            if (session == null)
                throw new ArgumentNullException(nameof(session));

            if (session.IsLoggedIn)
                return session.AccessToken;

            MinitAuthResult result = await LoginAsync(session, ct);
            if (!result.Success)
                throw new InvalidOperationException(result.Error ?? "Failed to acquire a Minit access token.");

            return session.AccessToken;
        }

        /// <summary>
        /// Logs out — clears the stored access token and identity. Server-side session
        /// revocation (<c>POST /users/{userId}/revoke-sessions</c>) is out of scope here.
        /// </summary>
        public static void Logout(MinitSession session)
        {
            if (session == null)
                throw new ArgumentNullException(nameof(session));

            session.Clear();
            Debug.Log("[Minit] Logged out.");
        }

        // ── Loopback listener ───────────────────────────────────────────────────────

        private static int GetFreeTcpPort()
        {
            // HttpListener has no "bind to port 0" support, so find a free port via a
            // throwaway TcpListener first, then start HttpListener on that exact port.
            // Small TOCTOU race (another process could grab the port in between) is an
            // accepted, low-probability risk for a local dev tool.
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            int port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
        }

        private static string GenerateStateNonce()
        {
            byte[] bytes = new byte[32];
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
                rng.GetBytes(bytes);

            // base64url, no padding — safe to embed directly in a query string.
            return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        }

        private static async Task<(string cognitoToken, string error)> WaitForCallbackAsync(
            HttpListener listener, string expectedState, CancellationToken ct)
        {
            using var timeoutCts = new CancellationTokenSource(LoginTimeout);
            using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

            // HttpListener.GetContextAsync has no CancellationToken overload — stopping the
            // listener is what aborts a pending accept, so register that as our cancellation.
            using (combinedCts.Token.Register(() =>
            {
                try { listener.Stop(); }
                catch { /* already stopped/disposed */ }
            }))
            {
                // Loop rather than treating the first received request as THE callback: a stray
                // request (browser favicon/preflight probe, another local process hitting this
                // port) must not consume the one real callback and be mistaken for it. Only a
                // request to the "/callback" path is treated as the OAuth handoff; anything else
                // gets a 404 and we keep waiting. The overall timeout + cancellation (via
                // listener.Stop() on the linked CTS above) still bounds how long this can run.
                while (true)
                {
                    HttpListenerContext context;
                    try
                    {
                        context = await listener.GetContextAsync();
                    }
                    catch (Exception) when (combinedCts.IsCancellationRequested)
                    {
                        if (timeoutCts.IsCancellationRequested)
                            return (null, "Login timed out waiting for the browser sign-in to complete.");

                        ct.ThrowIfCancellationRequested();
                        return (null, "Login cancelled.");
                    }

                    HttpListenerRequest request = context.Request;

                    if (request.Url == null || request.Url.AbsolutePath != "/callback")
                    {
                        await WriteNotFoundResponseAsync(context.Response);
                        continue;
                    }

                    string state = request.QueryString["state"];
                    string cognitoToken = request.QueryString["cognito_token"];

                    bool stateOk = !string.IsNullOrEmpty(state) && state == expectedState;
                    bool tokenOk = !string.IsNullOrEmpty(cognitoToken);

                    await WriteCallbackResponseAsync(context.Response, success: stateOk && tokenOk);

                    if (!stateOk)
                        return (null, "Login failed: the sign-in callback's state did not match (possible CSRF) — please try again.");
                    if (!tokenOk)
                        return (null, "Login failed: the browser did not return a sign-in token.");

                    return (cognitoToken, null);
                }
            }
        }

        private static async Task WriteNotFoundResponseAsync(HttpListenerResponse response)
        {
            byte[] buffer = Encoding.UTF8.GetBytes("Not found");
            response.StatusCode = 404;
            response.ContentType = "text/plain; charset=utf-8";
            response.ContentLength64 = buffer.Length;

            try
            {
                await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            }
            finally
            {
                response.OutputStream.Close();
            }
        }

        private static async Task WriteCallbackResponseAsync(HttpListenerResponse response, bool success)
        {
            string html = success
                ? "<!doctype html><html><head><meta charset=\"utf-8\"><title>Minit</title>" +
                  "<style>body{background:#0b0b12;color:#f5f5f7;font-family:-apple-system,Segoe UI,sans-serif;" +
                  "display:flex;align-items:center;justify-content:center;height:100vh;margin:0}" +
                  ".card{text-align:center}h1{font-size:20px;margin:0 0 8px}p{opacity:.7;margin:0}</style></head>" +
                  "<body><div class=\"card\"><h1>You're signed in ✓</h1><p>Return to Unity to continue.</p></div>" +
                  "<script>try{window.close()}catch(e){}</script></body></html>"
                : "<!doctype html><html><head><meta charset=\"utf-8\"><title>Minit</title>" +
                  "<style>body{background:#0b0b12;color:#f5f5f7;font-family:-apple-system,Segoe UI,sans-serif;" +
                  "display:flex;align-items:center;justify-content:center;height:100vh;margin:0}" +
                  ".card{text-align:center}h1{font-size:20px;margin:0 0 8px}p{opacity:.7;margin:0}</style></head>" +
                  "<body><div class=\"card\"><h1>Sign-in failed</h1><p>Close this tab and try again in Unity.</p></div>" +
                  "</body></html>";

            byte[] buffer = Encoding.UTF8.GetBytes(html);
            response.ContentType = "text/html; charset=utf-8";
            response.ContentLength64 = buffer.Length;

            try
            {
                await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            }
            finally
            {
                response.OutputStream.Close();
            }
        }

        // ── /auth/console exchange + /users/self ────────────────────────────────────

        private static async Task<MinitAuthResult> ExchangeAndFetchIdentityAsync(MinitSession session, string cognitoToken, CancellationToken ct = default)
        {
            string backendBaseUrl = MinitEnvironments.BackendBaseUrl(session.Environment);

            string requestJson = JsonUtility.ToJson(new ConsoleAuthRequestDto { cognitoToken = cognitoToken });

            // No x-api-key / Authorization header — /auth/console is public (authorizer: None).
            MinitHttpResponse exchangeResponse = await MinitHttp.PostJsonAsync($"{backendBaseUrl}/auth/console", requestJson, ct: ct);
            if (!exchangeResponse.IsSuccess)
                return MinitAuthResult.Failed(DescribeConsoleAuthError(exchangeResponse));

            ConsoleAuthResponseDto exchange;
            try
            {
                exchange = JsonUtility.FromJson<ConsoleAuthResponseDto>(exchangeResponse.Body);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Minit] Failed to parse /auth/console response:\n{ex}");
                return MinitAuthResult.Failed("Login failed: could not parse the server's response.");
            }

            string accessToken = exchange?.tokens?.accessToken;
            if (string.IsNullOrEmpty(accessToken))
                return MinitAuthResult.Failed("Login failed: the server's response did not include an access token.");

            MinitHttpResponse selfResponse = await MinitHttp.GetAsync($"{backendBaseUrl}/users/self", accessToken, ct);
            if (!selfResponse.IsSuccess)
                return MinitAuthResult.Failed($"Login failed: could not fetch your profile (HTTP {selfResponse.StatusCode}).");

            SelfUserResponseDto selfDto;
            try
            {
                selfDto = JsonUtility.FromJson<SelfUserResponseDto>(selfResponse.Body);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Minit] Failed to parse /users/self response:\n{ex}");
                return MinitAuthResult.Failed("Login failed: could not parse your profile.");
            }

            if (selfDto?.user == null || string.IsNullOrEmpty(selfDto.user.id))
                return MinitAuthResult.Failed("Login failed: the server did not return a valid user profile.");

            var user = new MinitUser
            {
                id = selfDto.user.id,
                name = selfDto.user.name,
                email = string.IsNullOrEmpty(selfDto.user.email) ? null : selfDto.user.email
            };

            session.SetSession(accessToken, user);
            Debug.Log($"[Minit] Logged in as {(string.IsNullOrEmpty(user.name) ? user.id : user.name)}.");

            return MinitAuthResult.Ok(user);
        }

        /// <summary>
        /// Turns an /auth/console error response into a user-facing message, special-casing the
        /// CREATOR_TIER_REQUIRED code (403, account exists but lacks creator tier).
        /// </summary>
        private static string DescribeConsoleAuthError(MinitHttpResponse response)
        {
            ConsoleAuthErrorDto error = null;
            try { error = JsonUtility.FromJson<ConsoleAuthErrorDto>(response.Body); }
            catch { /* best-effort — fall back to the status code below */ }

            if (error?.details?.code == "CREATOR_TIER_REQUIRED")
                return "Login failed: this account does not have creator access yet. Upgrade to a creator tier on the Minit console, then try again.";

            switch (response.StatusCode)
            {
                case 401:
                    return "Login failed: the browser sign-in session is invalid or expired. Please try again.";
                case 403:
                    return $"Login failed: access denied ({error?.message ?? "forbidden"}).";
                case 404:
                    return "Login failed: no Minit account was found for this sign-in.";
                default:
                    return $"Login failed: unexpected server response (HTTP {response.StatusCode}).";
            }
        }

        // ── DTOs (JsonUtility-compatible — no Dictionary, no polymorphism) ──────────

        [Serializable]
        private class ConsoleAuthRequestDto
        {
            public string cognitoToken;
        }

        [Serializable]
        private class ConsoleAuthResponseDto
        {
            public ConsoleSessionDto session;
            public ConsoleUserDto user;
            public ConsoleTokensDto tokens;
        }

        [Serializable]
        private class ConsoleSessionDto
        {
            public string id;
            public double start;
        }

        [Serializable]
        private class ConsoleUserDto
        {
            public string id;
        }

        [Serializable]
        private class ConsoleTokensDto
        {
            public string accessToken;
            public string refreshToken;
        }

        [Serializable]
        private class ConsoleAuthErrorDto
        {
            public string error;
            public string message;
            public ConsoleAuthErrorDetailsDto details;
        }

        [Serializable]
        private class ConsoleAuthErrorDetailsDto
        {
            public string code;
        }

        [Serializable]
        private class SelfUserResponseDto
        {
            public SelfUserDto user;
        }

        [Serializable]
        private class SelfUserDto
        {
            public string id;
            public string name;
            public string email;
            public string tag;
            public string role;
        }
    }
}
