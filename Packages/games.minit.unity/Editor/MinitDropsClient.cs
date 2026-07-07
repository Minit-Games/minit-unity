using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace MinitGames.Editor
{
    // ── Result types ────────────────────────────────────────────────────────────────

    /// <summary>Outcome of <see cref="MinitDropsClient.CreateDraftAsync"/>.</summary>
    public class MinitCreateDropResult
    {
        public bool Success;
        public string Error;
        public string DropId;
        public string Status;

        public static MinitCreateDropResult Ok(string dropId, string status) =>
            new MinitCreateDropResult { Success = true, DropId = dropId, Status = status };

        public static MinitCreateDropResult Failed(string error) =>
            new MinitCreateDropResult { Success = false, Error = error };
    }

    /// <summary>Outcome of <see cref="MinitDropsClient.CreateUploadUrlAsync"/>.</summary>
    public class MinitUploadUrlResult
    {
        public bool Success;
        public string Error;
        public string ResourceId;
        public string ArtifactId;
        public string Url;
        public Dictionary<string, string> Fields;

        public static MinitUploadUrlResult Ok(string resourceId, string artifactId, string url, Dictionary<string, string> fields) =>
            new MinitUploadUrlResult { Success = true, ResourceId = resourceId, ArtifactId = artifactId, Url = url, Fields = fields };

        public static MinitUploadUrlResult Failed(string error) =>
            new MinitUploadUrlResult { Success = false, Error = error };
    }

    /// <summary>Outcome of <see cref="MinitDropsClient.UploadZipToS3Async"/>.</summary>
    public class MinitS3UploadResult
    {
        public bool Success;
        public string Error;
        public int StatusCode;

        public static MinitS3UploadResult Ok(int statusCode) =>
            new MinitS3UploadResult { Success = true, StatusCode = statusCode };

        public static MinitS3UploadResult Failed(string error, int statusCode = 0) =>
            new MinitS3UploadResult { Success = false, Error = error, StatusCode = statusCode };
    }

    /// <summary>
    /// The processing pipeline stage for a drop's uploaded ZIP, derived from field presence on
    /// the drop (there is no server-side "processingState" field — see
    /// <see cref="MinitDropsClient.PollProcessingAsync"/>).
    /// </summary>
    public enum MinitProcessingState
    {
        /// <summary>No <c>streamingUrl</c> yet — the backend has not ingested the ZIP.</summary>
        UploadPending,

        /// <summary><c>streamingUrl</c> is set but <c>screenshotUrl</c> is not — analysis in progress.</summary>
        Analyzing,

        /// <summary>Both <c>streamingUrl</c> and <c>screenshotUrl</c> are set — processing is complete.</summary>
        Analyzed
    }

    /// <summary>Outcome of <see cref="MinitDropsClient.PollProcessingAsync"/>.</summary>
    public class MinitProcessingResult
    {
        public bool Success;
        public string Error;
        public bool TimedOut;
        public MinitProcessingState State;
        public string StreamingUrl;
        public string ScreenshotUrl;

        public static MinitProcessingResult Ok(MinitProcessingState state, string streamingUrl, string screenshotUrl) =>
            new MinitProcessingResult { Success = true, State = state, StreamingUrl = streamingUrl, ScreenshotUrl = screenshotUrl };

        public static MinitProcessingResult Failed(string error) =>
            new MinitProcessingResult { Success = false, Error = error };

        public static MinitProcessingResult Timeout() => new MinitProcessingResult
        {
            Success = false,
            TimedOut = true,
            State = MinitProcessingState.Analyzing,
            Error = "Processing did not finish within 60 seconds. The drop may still be analyzing " +
                    "— check back in a bit (poll again, or view the drop in the creator console)."
        };
    }

    /// <summary>
    /// REST client for the drops/upload flow used by the "Login &amp; Upload" editor window
    /// (DROP-3224): create a draft drop, request a presigned S3 upload URL, upload the built
    /// ZIP directly to S3, then poll the drop until the backend's analysis pipeline finishes.
    ///
    /// <para>
    /// Every authenticated method calls <see cref="MinitAuth.EnsureAccessTokenAsync"/> itself —
    /// callers never need to read <see cref="MinitSession.AccessToken"/> directly. The one
    /// exception is <see cref="UploadZipToS3Async"/>, which talks to S3 directly and must NOT
    /// send the backend bearer token or an api key.
    /// </para>
    /// </summary>
    public static class MinitDropsClient
    {
        // Mirrors MinitBuild.SizeCapBytes and the backend's platform-wide upload cap.
        private const long SizeCapBytes = 52_428_800L; // 50 MiB

        private const int MaxPollAttempts = 30;
        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

        // ── Create draft drop ───────────────────────────────────────────────────────

        /// <summary>
        /// Creates a new draft drop via <c>POST /drops</c>. Requires a creator-tier account
        /// (tier1/tier2/tier3) or admin — a base "user" role gets 403, except on dev where
        /// <c>FF_ALLOW_NON_CREATOR_CONSOLE</c> relaxes this to any signed-in user.
        /// </summary>
        public static async Task<MinitCreateDropResult> CreateDraftAsync(
            MinitSession session,
            string title,
            string description,
            string resultType,
            bool resultReverseSorting,
            CancellationToken ct = default)
        {
            if (session == null)
                throw new ArgumentNullException(nameof(session));
            if (string.IsNullOrEmpty(title))
                throw new ArgumentException("title must not be null or empty.", nameof(title));
            if (string.IsNullOrEmpty(resultType))
                throw new ArgumentException("resultType must not be null or empty.", nameof(resultType));

            string accessToken;
            try
            {
                accessToken = await MinitAuth.EnsureAccessTokenAsync(session, ct);
            }
            catch (Exception ex)
            {
                return MinitCreateDropResult.Failed($"Could not authenticate: {ex.Message}");
            }

            string backendBaseUrl = MinitEnvironments.BackendBaseUrl(session.Environment);
            string requestJson = JsonUtility.ToJson(new CreateDropRequestDto
            {
                title = title,
                description = description ?? string.Empty,
                resultType = resultType,
                resultReverseSorting = resultReverseSorting
            });

            MinitHttpResponse response = await MinitHttp.PostJsonAsync($"{backendBaseUrl}/drops", requestJson, accessToken, ct);
            if (!response.IsSuccess)
                return MinitCreateDropResult.Failed(DescribeCreateDropError(response));

            CreateDropResponseDto dto;
            try
            {
                dto = JsonUtility.FromJson<CreateDropResponseDto>(response.Body);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Minit] Failed to parse POST /drops response:\n{ex}");
                return MinitCreateDropResult.Failed("Could not parse the server's create-drop response.");
            }

            if (dto?.drop == null || string.IsNullOrEmpty(dto.drop.id))
                return MinitCreateDropResult.Failed("The server did not return a valid drop.");

            Debug.Log($"[Minit] Created draft drop {dto.drop.id} ({title}).");
            return MinitCreateDropResult.Ok(dto.drop.id, dto.drop.status);
        }

        private static string DescribeCreateDropError(MinitHttpResponse response)
        {
            GenericErrorDto error = TryParseGenericError(response.Body);

            if (response.StatusCode == 403)
            {
                return "Creating a drop requires a creator-tier account (tier1/tier2/tier3) or admin. " +
                       "On dev, FF_ALLOW_NON_CREATOR_CONSOLE relaxes this to any signed-in user — prod does not. " +
                       $"Backend message: {error?.message ?? "forbidden"}";
            }

            if (response.StatusCode == 400 || response.StatusCode == 422)
                return $"Invalid drop details (HTTP {response.StatusCode}): {error?.message ?? response.Body}";

            return $"Backend error creating the drop (HTTP {response.StatusCode}): {error?.message ?? response.Body}";
        }

        // ── Create upload URL ───────────────────────────────────────────────────────

        /// <summary>
        /// Requests a presigned S3 upload URL for the drop's ZIP via
        /// <c>POST /drops/{dropId}/create-upload-url</c>. Pre-flight rejects oversized ZIPs
        /// locally without calling the backend (mirrors MinitBuild's and uploadGameBundle.ts's
        /// 50 MB cap).
        /// </summary>
        public static async Task<MinitUploadUrlResult> CreateUploadUrlAsync(
            MinitSession session,
            string dropId,
            long sizeBytes,
            CancellationToken ct = default)
        {
            if (session == null)
                throw new ArgumentNullException(nameof(session));
            if (string.IsNullOrEmpty(dropId))
                throw new ArgumentException("dropId must not be null or empty.", nameof(dropId));

            if (sizeBytes > SizeCapBytes)
            {
                double sizeMb = sizeBytes / (1024.0 * 1024.0);
                return MinitUploadUrlResult.Failed(
                    $"ZIP is {sizeMb:F2} MB, which exceeds the Minit 50 MB upload cap. " +
                    "Reduce the build size (asset compression, managed stripping) before uploading.");
            }

            string accessToken;
            try
            {
                accessToken = await MinitAuth.EnsureAccessTokenAsync(session, ct);
            }
            catch (Exception ex)
            {
                return MinitUploadUrlResult.Failed($"Could not authenticate: {ex.Message}");
            }

            string backendBaseUrl = MinitEnvironments.BackendBaseUrl(session.Environment);
            string requestJson = JsonUtility.ToJson(new CreateUploadUrlRequestDto { size = sizeBytes });

            MinitHttpResponse response = await MinitHttp.PostJsonAsync(
                $"{backendBaseUrl}/drops/{dropId}/create-upload-url", requestJson, accessToken, ct);
            if (!response.IsSuccess)
                return MinitUploadUrlResult.Failed(DescribeUploadUrlError(response));

            CreateUploadUrlResponseDto dto;
            try
            {
                // JsonUtility parses resourceId/artifactId/url fine — it silently ignores the
                // "fields" property below since CreateUploadUrlResponseDto has no matching field.
                dto = JsonUtility.FromJson<CreateUploadUrlResponseDto>(response.Body);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Minit] Failed to parse create-upload-url response:\n{ex}");
                return MinitUploadUrlResult.Failed("Could not parse the server's upload-URL response.");
            }

            if (dto == null || string.IsNullOrEmpty(dto.url))
                return MinitUploadUrlResult.Failed("The server's response did not include an upload URL.");

            // "fields" is a flat JSON object with arbitrary string keys (key, policy, x-amz-*,
            // Content-Type, ...) — JsonUtility cannot deserialize that shape, so parse it by hand.
            Dictionary<string, string> fields = ExtractStringMapProperty(response.Body, "fields");

            return MinitUploadUrlResult.Ok(dto.resourceId, dto.artifactId, dto.url, fields);
        }

        private static string DescribeUploadUrlError(MinitHttpResponse response)
        {
            GenericErrorDto error = TryParseGenericError(response.Body);

            switch (response.StatusCode)
            {
                case 403:
                    return "Cannot create an upload URL for this drop: it must be in draft status — " +
                           "published drops cannot be re-uploaded directly. Create a new draft drop instead. " +
                           $"Backend message: {error?.message ?? "forbidden"}";
                case 404:
                    return $"Drop not found: {error?.message ?? "no drop with this ID exists."}";
                case 413:
                    return $"Upload size rejected by the backend (413): {error?.message ?? "the ZIP is too large."}";
                default:
                    return $"Backend error creating the upload URL (HTTP {response.StatusCode}): {error?.message ?? response.Body}";
            }
        }

        // ── Upload ZIP to S3 ────────────────────────────────────────────────────────

        /// <summary>
        /// Uploads <paramref name="zipFilePath"/> directly to S3 via the presigned POST policy
        /// returned by <see cref="CreateUploadUrlAsync"/>. This talks to S3, not the Minit
        /// backend — no bearer token, no api key. All <paramref name="fields"/> entries must be
        /// added to the multipart body BEFORE the file field (S3 POST policy requirement).
        /// </summary>
        public static async Task<MinitS3UploadResult> UploadZipToS3Async(
            string presignedUrl,
            Dictionary<string, string> fields,
            string zipFilePath,
            IProgress<float> progress = null,
            CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(presignedUrl))
                throw new ArgumentException("presignedUrl must not be null or empty.", nameof(presignedUrl));
            if (fields == null)
                throw new ArgumentNullException(nameof(fields));
            if (string.IsNullOrEmpty(zipFilePath) || !File.Exists(zipFilePath))
                return MinitS3UploadResult.Failed($"ZIP file not found: {zipFilePath}");

            byte[] fileBytes;
            try
            {
                fileBytes = File.ReadAllBytes(zipFilePath);
            }
            catch (Exception ex)
            {
                return MinitS3UploadResult.Failed($"Could not read the ZIP file: {ex.Message}");
            }

            progress?.Report(0f);

            using var httpClient = new HttpClient();
            using var form = new MultipartFormDataContent();

            // Policy fields first, in order — the file field must come last.
            foreach (KeyValuePair<string, string> field in fields)
                form.Add(new StringContent(field.Value ?? string.Empty), field.Key);

            var fileContent = new ByteArrayContent(fileBytes);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
            form.Add(fileContent, "file", Path.GetFileName(zipFilePath));

            HttpResponseMessage response;
            try
            {
                response = await httpClient.PostAsync(presignedUrl, form, ct);
            }
            catch (Exception ex)
            {
                return MinitS3UploadResult.Failed($"Upload to S3 failed: {ex.Message}");
            }

            string body = await response.Content.ReadAsStringAsync();
            progress?.Report(1f);

            if (!response.IsSuccessStatusCode)
            {
                // S3 returns an XML error body (e.g. <Error><Code>...</Code><Message>...</Message></Error>)
                // rather than JSON — surface it verbatim, there's no shared error DTO to parse it into.
                return MinitS3UploadResult.Failed(
                    $"S3 rejected the upload (HTTP {(int)response.StatusCode}): {body}", (int)response.StatusCode);
            }

            Debug.Log("[Minit] ZIP uploaded to S3 successfully.");
            return MinitS3UploadResult.Ok((int)response.StatusCode);
        }

        // ── Poll processing ─────────────────────────────────────────────────────────

        /// <summary>
        /// Polls <c>GET /drops/{dropId}</c> every ~2s (up to ~60s total) until the drop's
        /// processing pipeline finishes. The drop stays <c>status: "draft"</c> throughout — there
        /// is no server-side "processingState" field, so this derives one from field presence:
        /// no <c>streamingUrl</c> = upload_pending; <c>streamingUrl</c> without
        /// <c>screenshotUrl</c> = analyzing; both present = analyzed.
        /// </summary>
        public static async Task<MinitProcessingResult> PollProcessingAsync(
            MinitSession session,
            string dropId,
            IProgress<string> statusProgress = null,
            CancellationToken ct = default)
        {
            if (session == null)
                throw new ArgumentNullException(nameof(session));
            if (string.IsNullOrEmpty(dropId))
                throw new ArgumentException("dropId must not be null or empty.", nameof(dropId));

            string backendBaseUrl = MinitEnvironments.BackendBaseUrl(session.Environment);

            for (int attempt = 1; attempt <= MaxPollAttempts; attempt++)
            {
                ct.ThrowIfCancellationRequested();

                string accessToken;
                try
                {
                    accessToken = await MinitAuth.EnsureAccessTokenAsync(session, ct);
                }
                catch (Exception ex)
                {
                    return MinitProcessingResult.Failed($"Could not authenticate: {ex.Message}");
                }

                MinitHttpResponse response = await MinitHttp.GetAsync($"{backendBaseUrl}/drops/{dropId}", accessToken, ct);
                if (!response.IsSuccess)
                {
                    if (response.StatusCode == 404)
                        return MinitProcessingResult.Failed($"Drop not found: {dropId}");

                    return MinitProcessingResult.Failed(
                        $"Backend error while checking processing status (HTTP {response.StatusCode}): {response.Body}");
                }

                GetDropResponseDto dto;
                try
                {
                    dto = JsonUtility.FromJson<GetDropResponseDto>(response.Body);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[Minit] Failed to parse GET /drops/{{dropId}} response:\n{ex}");
                    return MinitProcessingResult.Failed("Could not parse the server's drop response.");
                }

                if (dto?.drop == null)
                    return MinitProcessingResult.Failed("The server did not return a valid drop.");

                bool hasStreaming = !string.IsNullOrEmpty(dto.drop.streamingUrl);
                bool hasScreenshot = !string.IsNullOrEmpty(dto.drop.screenshotUrl);

                MinitProcessingState state =
                    !hasStreaming ? MinitProcessingState.UploadPending :
                    !hasScreenshot ? MinitProcessingState.Analyzing :
                    MinitProcessingState.Analyzed;

                statusProgress?.Report(state.ToString());

                if (state == MinitProcessingState.Analyzed)
                {
                    Debug.Log($"[Minit] Drop {dropId} finished processing.");
                    return MinitProcessingResult.Ok(state, dto.drop.streamingUrl, dto.drop.screenshotUrl);
                }

                if (attempt < MaxPollAttempts)
                    await Task.Delay(PollInterval, ct);
            }

            return MinitProcessingResult.Timeout();
        }

        /// <summary>Builds the creator-console URL for viewing a drop (e.g. a "View in console" link once processing finishes).</summary>
        public static string BuildConsoleDropUrl(MinitSession session, string dropId) =>
            $"{MinitEnvironments.ConsoleBaseUrl(session.Environment)}/minits/{dropId}";

        // ── Shared error parsing ────────────────────────────────────────────────────

        private static GenericErrorDto TryParseGenericError(string body)
        {
            if (string.IsNullOrEmpty(body))
                return null;

            try
            {
                return JsonUtility.FromJson<GenericErrorDto>(body);
            }
            catch
            {
                return null;
            }
        }

        // ── Hand-rolled flat string-map JSON extraction ─────────────────────────────
        // JsonUtility cannot deserialize a JSON object with arbitrary/unknown keys (it requires
        // matching C# fields declared up front), so the "fields" map in the create-upload-url
        // response needs a small manual parser instead of a DTO. ASSUMPTION: every value under
        // the target property is a JSON string — true for S3 POST policy fields (key, policy,
        // signature, x-amz-*, Content-Type are always strings). Nested objects/arrays/numbers/
        // booleans as values are not handled.

        /// <summary>
        /// Extracts the flat string-keyed, string-valued JSON object found at
        /// <paramref name="propertyName"/> within <paramref name="json"/>. Returns an empty
        /// dictionary if the property is missing or malformed.
        /// </summary>
        private static Dictionary<string, string> ExtractStringMapProperty(string json, string propertyName)
        {
            var result = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(json))
                return result;

            string marker = "\"" + propertyName + "\"";
            int markerIndex = json.IndexOf(marker, StringComparison.Ordinal);
            if (markerIndex < 0)
                return result;

            int colonIndex = json.IndexOf(':', markerIndex + marker.Length);
            if (colonIndex < 0)
                return result;

            int braceStart = json.IndexOf('{', colonIndex);
            if (braceStart < 0)
                return result;

            int braceEnd = FindMatchingBrace(json, braceStart);
            if (braceEnd < 0)
                return result;

            string body = json.Substring(braceStart + 1, braceEnd - braceStart - 1);

            int idx = 0;
            while (idx < body.Length)
            {
                int keyStart = body.IndexOf('"', idx);
                if (keyStart < 0)
                    break;

                int keyEnd = FindStringEnd(body, keyStart + 1);
                if (keyEnd < 0)
                    break;

                string key = UnescapeJsonString(body.Substring(keyStart + 1, keyEnd - keyStart - 1));

                int colon = body.IndexOf(':', keyEnd + 1);
                if (colon < 0)
                    break;

                int valueStart = body.IndexOf('"', colon + 1);
                if (valueStart < 0)
                    break;

                int valueEnd = FindStringEnd(body, valueStart + 1);
                if (valueEnd < 0)
                    break;

                string value = UnescapeJsonString(body.Substring(valueStart + 1, valueEnd - valueStart - 1));
                result[key] = value;

                idx = valueEnd + 1;
            }

            return result;
        }

        /// <summary>Given the index of an opening '{', returns the index of its matching '}', respecting quoted strings.</summary>
        private static int FindMatchingBrace(string s, int openBraceIndex)
        {
            int depth = 0;
            bool inString = false;
            bool escaped = false;

            for (int i = openBraceIndex; i < s.Length; i++)
            {
                char c = s[i];
                if (inString)
                {
                    if (escaped) escaped = false;
                    else if (c == '\\') escaped = true;
                    else if (c == '"') inString = false;
                    continue;
                }

                if (c == '"') { inString = true; continue; }
                if (c == '{') depth++;
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0)
                        return i;
                }
            }

            return -1;
        }

        /// <summary>Given the index just past an opening '"', returns the index of the closing (unescaped) '"'.</summary>
        private static int FindStringEnd(string s, int start)
        {
            bool escaped = false;
            for (int i = start; i < s.Length; i++)
            {
                char c = s[i];
                if (escaped) { escaped = false; continue; }
                if (c == '\\') { escaped = true; continue; }
                if (c == '"') return i;
            }

            return -1;
        }

        private static string UnescapeJsonString(string s)
        {
            if (string.IsNullOrEmpty(s) || s.IndexOf('\\') < 0)
                return s;

            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\\' && i + 1 < s.Length)
                {
                    char next = s[i + 1];
                    switch (next)
                    {
                        case '"': sb.Append('"'); i++; break;
                        case '\\': sb.Append('\\'); i++; break;
                        case '/': sb.Append('/'); i++; break;
                        case 'n': sb.Append('\n'); i++; break;
                        case 't': sb.Append('\t'); i++; break;
                        case 'r': sb.Append('\r'); i++; break;
                        case 'b': sb.Append('\b'); i++; break;
                        case 'f': sb.Append('\f'); i++; break;
                        case 'u' when i + 5 < s.Length &&
                                      int.TryParse(s.Substring(i + 2, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code):
                            sb.Append((char)code);
                            i += 5;
                            break;
                        default:
                            sb.Append(c);
                            break;
                    }
                }
                else
                {
                    sb.Append(c);
                }
            }

            return sb.ToString();
        }

        // ── DTOs (JsonUtility-compatible — no Dictionary, no polymorphism) ──────────

        [Serializable]
        private class CreateDropRequestDto
        {
            public string title;
            public string description;
            public string resultType;
            public bool resultReverseSorting;
        }

        [Serializable]
        private class CreateDropResponseDto
        {
            public DropSummaryDto drop;
        }

        [Serializable]
        private class DropSummaryDto
        {
            public string id;
            public string status;
        }

        [Serializable]
        private class CreateUploadUrlRequestDto
        {
            public long size;
        }

        [Serializable]
        private class CreateUploadUrlResponseDto
        {
            public string resourceId;
            public string artifactId;
            public string url;
            // "fields" (Dictionary<string,string>) is deliberately omitted — JsonUtility can't
            // deserialize it. See ExtractStringMapProperty above.
        }

        [Serializable]
        private class GetDropResponseDto
        {
            public DropProcessingDto drop;
        }

        [Serializable]
        private class DropProcessingDto
        {
            public string id;
            public string status;
            public string streamingUrl;
            public string screenshotUrl;
        }

        [Serializable]
        private class GenericErrorDto
        {
            public string error;
            public string message;
            public GenericErrorDetailsDto details;
        }

        [Serializable]
        private class GenericErrorDetailsDto
        {
            public string code;
        }
    }
}
