using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace MinitGames.Editor
{
    /// <summary>
    /// Editor window that lets a creator log in with their Minit account and build/upload a drop
    /// ZIP without leaving Unity. Accessible via <b>Minit → Login &amp; Upload</b> in the Unity
    /// menu bar (DROP-3224).
    ///
    /// <para>
    /// ASYNC/REPAINT PATTERN: IMGUI's <see cref="OnGUI"/> cannot await a <see cref="Task"/>
    /// directly, and Unity Editor async continuations are not guaranteed to resume on the main
    /// thread (there is no guaranteed main-thread <see cref="SynchronizationContext"/> here).
    /// So every async operation below only ever writes to plain fields (bools/strings/floats) —
    /// it never touches a Unity API (no <c>Repaint()</c>, no <c>EditorUtility.*</c>) from inside
    /// an awaited continuation. Instead, <see cref="OnEnable"/> subscribes
    /// <c>EditorApplication.update += Repaint</c>, so the window keeps repainting itself on the
    /// main thread for as long as it's open, and <see cref="OnGUI"/> simply reads whatever the
    /// latest field values are on every one of those repaints. A <c>_closed</c> flag (set from
    /// <see cref="OnDisable"/>, which — like all Editor window callbacks — always runs on the
    /// main thread) guards continuations against writing to a window that's already been closed.
    /// </para>
    /// </summary>
    public class MinitLoginUploadWindow : EditorWindow
    {
        // Confirmed against minit-root/docs/api.md (POST /drops resultType): "score" | "time" | "group".
        private static readonly string[] ResultTypeOptions = { "score", "time", "group" };

        // Mirrors MinitBuild.SizeCapBytes / MinitDropsClient.SizeCapBytes — UI-only warning here;
        // MinitDropsClient.CreateUploadUrlAsync is the actual enforcement point.
        private const long SizeCapBytes = 52_428_800L; // 50 MiB

        private enum DraftTargetMode
        {
            CreateNew,
            ExistingId
        }

        private MinitSession _session;
        private CancellationTokenSource _cts;
        private bool _closed;

        // ── Login state ──────────────────────────────────────────────────────────
        private bool _loginInFlight;
        private string _loginError;

        // ── ZIP source state ─────────────────────────────────────────────────────
        private string _zipPath;
        private long _zipSizeBytes;
        private string _zipError;

        // ── Draft target state ───────────────────────────────────────────────────
        private DraftTargetMode _draftTargetMode = DraftTargetMode.CreateNew;
        private string _draftTitle = "";
        private string _draftDescription = "";
        private int _resultTypeIndex;
        private bool _resultReverseSorting;
        private string _existingDropId = "";

        // ── Upload flow state (written from awaited continuations — plain fields only,
        //    see the class remarks above) ─────────────────────────────────────────────
        private bool _uploadInFlight;
        private string _uploadStatus;
        private float _uploadProgress = -1f; // -1 = indeterminate / not started
        private string _uploadError;
        private bool _uploadSucceeded;
        private string _uploadedDropId;

        [MenuItem("Minit/Login & Upload")]
        public static void ShowWindow()
        {
            GetWindow<MinitLoginUploadWindow>("Minit");
        }

        private void OnEnable()
        {
            _session = new MinitSession();
            _cts = new CancellationTokenSource();
            _closed = false;

            // Keeps the window painting the latest field values for the lifetime of any
            // in-flight async operation (and beyond — see the class remarks). This is the only
            // place Repaint() is ever called; async continuations never call it directly.
            EditorApplication.update += Repaint;
        }

        private void OnDisable()
        {
            EditorApplication.update -= Repaint;
            _closed = true;
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }

        private void OnGUI()
        {
            DrawEnvironmentSelector();
            EditorGUILayout.Space();

            if (_session == null || !_session.IsLoggedIn)
            {
                DrawLoginView();
                return;
            }

            DrawLoggedInHeader();
            EditorGUILayout.Space();
            DrawUploadView();
        }

        // ── Environment ──────────────────────────────────────────────────────────

        private void DrawEnvironmentSelector()
        {
            EditorGUI.BeginChangeCheck();
            var newEnv = (MinitEnvironment)EditorGUILayout.EnumPopup("Environment", _session.Environment);
            if (EditorGUI.EndChangeCheck() && newEnv != _session.Environment)
            {
                // Dev and Prod are different backends/consoles entirely — the current session's
                // token belongs to the old one, so drop it rather than silently reusing it.
                if (_session.IsLoggedIn)
                {
                    MinitAuth.Logout(_session);
                    Debug.Log("[Minit] Environment changed — logged out; log in again to continue.");
                }

                _session.Environment = newEnv;
                ResetUploadState();
            }
        }

        // ── Login view ───────────────────────────────────────────────────────────

        private void DrawLoginView()
        {
            EditorGUILayout.HelpBox(
                "Log in with your Minit creator account to build and upload drops without leaving the editor.",
                MessageType.Info);
            EditorGUILayout.Space();

            using (new EditorGUI.DisabledScope(_loginInFlight))
            {
                if (GUILayout.Button(_loginInFlight ? "Waiting for browser sign-in…" : "Log in with Minit"))
                    _ = LoginAsync();
            }

            if (!string.IsNullOrEmpty(_loginError))
                EditorGUILayout.HelpBox(_loginError, MessageType.Error);
        }

        private async Task LoginAsync()
        {
            _loginInFlight = true;
            _loginError = null;

            CancellationToken ct = _cts?.Token ?? CancellationToken.None;
            MinitAuthResult result;
            try
            {
                result = await MinitAuth.LoginAsync(_session, ct);
            }
            catch (Exception ex)
            {
                result = MinitAuthResult.Failed($"Login failed: {ex.Message}");
            }

            if (_closed)
                return;

            _loginInFlight = false;
            _loginError = result.Success ? null : (result.Error ?? "Login failed.");
        }

        // ── Logged-in header ─────────────────────────────────────────────────────

        private void DrawLoggedInHeader()
        {
            MinitUser user = _session.User;
            string identity = user == null
                ? "Signed in"
                : string.IsNullOrEmpty(user.email) ? user.name : $"{user.name} ({user.email})";

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(identity, EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Log out", GUILayout.Width(80)))
            {
                MinitAuth.Logout(_session);
                ResetUploadState();
            }
            EditorGUILayout.EndHorizontal();
        }

        // ── Upload view ──────────────────────────────────────────────────────────

        private void DrawUploadView()
        {
            EditorGUILayout.LabelField("ZIP source", EditorStyles.boldLabel);
            DrawZipSourceSection();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Target draft", EditorStyles.boldLabel);
            DrawDraftTargetSection();

            EditorGUILayout.Space();
            DrawUploadSection();
        }

        private void DrawZipSourceSection()
        {
            using (new EditorGUI.DisabledScope(_uploadInFlight))
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Build for Minit now"))
                    BuildZipNow();
                if (GUILayout.Button("Select ZIP…"))
                    SelectZip();
                EditorGUILayout.EndHorizontal();
            }

            if (!string.IsNullOrEmpty(_zipError))
                EditorGUILayout.HelpBox(_zipError, MessageType.Error);

            if (!string.IsNullOrEmpty(_zipPath))
            {
                double zipMb = _zipSizeBytes / (1024.0 * 1024.0);
                EditorGUILayout.LabelField("ZIP", _zipPath);
                EditorGUILayout.LabelField("Size", $"{zipMb:F2} MB");

                if (_zipSizeBytes > SizeCapBytes)
                    EditorGUILayout.HelpBox(
                        $"This ZIP is {zipMb:F2} MB — over the Minit 50 MB upload cap. The backend will reject it.",
                        MessageType.Warning);
            }
            else
            {
                EditorGUILayout.HelpBox("No ZIP selected yet.", MessageType.None);
            }
        }

        private void BuildZipNow()
        {
            _zipError = null;

            string zipPath;
            try
            {
                zipPath = MinitBuild.BuildAndZip();
            }
            catch (Exception ex)
            {
                _zipError = $"Build failed: {ex.Message}";
                Debug.LogError($"[Minit] Build for Minit (from Login & Upload window) failed:\n{ex}");
                return;
            }

            if (string.IsNullOrEmpty(zipPath))
            {
                _zipError = "Build failed — see the Console above for details.";
                return;
            }

            SetZipPath(zipPath);
        }

        private void SelectZip()
        {
            string path = EditorUtility.OpenFilePanel("Select Minit ZIP", "", "zip");
            if (string.IsNullOrEmpty(path))
                return;

            _zipError = null;
            SetZipPath(path);
        }

        private void SetZipPath(string path)
        {
            try
            {
                _zipSizeBytes = new FileInfo(path).Length;
                _zipPath = path;
            }
            catch (Exception ex)
            {
                _zipError = $"Could not read the ZIP file: {ex.Message}";
                _zipPath = null;
                _zipSizeBytes = 0;
            }
        }

        private void DrawDraftTargetSection()
        {
            using (new EditorGUI.DisabledScope(_uploadInFlight))
            {
                _draftTargetMode = (DraftTargetMode)GUILayout.Toolbar(
                    (int)_draftTargetMode, new[] { "Create new draft", "Use existing draft ID" });

                EditorGUILayout.Space();

                if (_draftTargetMode == DraftTargetMode.CreateNew)
                {
                    _draftTitle = EditorGUILayout.TextField("Title", _draftTitle);

                    EditorGUILayout.LabelField("Description");
                    _draftDescription = EditorGUILayout.TextArea(_draftDescription, GUILayout.Height(60));

                    // Allowed values confirmed against minit-root/docs/api.md (POST /drops).
                    _resultTypeIndex = EditorGUILayout.Popup("Result Type", _resultTypeIndex, ResultTypeOptions);
                    _resultReverseSorting = EditorGUILayout.Toggle("Reverse sorting", _resultReverseSorting);
                }
                else
                {
                    // No list-drafts client method exists (Step 3 scope) — pasting an id is the
                    // simplest "pick an existing draft" path for this cut.
                    _existingDropId = EditorGUILayout.TextField("Draft ID", _existingDropId);
                }
            }
        }

        private bool HasValidTarget =>
            _draftTargetMode == DraftTargetMode.CreateNew
                ? !string.IsNullOrEmpty(_draftTitle)
                : !string.IsNullOrEmpty(_existingDropId);

        private void DrawUploadSection()
        {
            bool canUpload = !_uploadInFlight && !string.IsNullOrEmpty(_zipPath) && HasValidTarget;

            using (new EditorGUI.DisabledScope(!canUpload))
            {
                if (GUILayout.Button(_uploadInFlight ? "Uploading…" : "Upload to Minit"))
                    _ = UploadAsync();
            }

            if (_uploadInFlight)
            {
                Rect rect = GUILayoutUtility.GetRect(18, 18);
                if (_uploadProgress >= 0f)
                    EditorGUI.ProgressBar(rect, _uploadProgress, _uploadStatus ?? "Working…");
                else
                    EditorGUI.ProgressBar(rect, 1f, _uploadStatus ?? "Working…");
            }

            if (!string.IsNullOrEmpty(_uploadError))
                EditorGUILayout.HelpBox(_uploadError, MessageType.Error);

            if (_uploadSucceeded)
            {
                EditorGUILayout.HelpBox("Upload complete — the drop is ready in draft.", MessageType.Info);
                if (GUILayout.Button("Open in console"))
                    Application.OpenURL(MinitDropsClient.BuildConsoleDropUrl(_session, _uploadedDropId));
            }
        }

        private async Task UploadAsync()
        {
            _uploadInFlight = true;
            _uploadError = null;
            _uploadSucceeded = false;
            _uploadedDropId = null;
            _uploadStatus = "Starting…";
            _uploadProgress = -1f;

            CancellationToken ct = _cts?.Token ?? CancellationToken.None;
            string zipPath = _zipPath;

            try
            {
                string dropId;
                if (_draftTargetMode == DraftTargetMode.CreateNew)
                {
                    _uploadStatus = "Creating draft…";
                    MinitCreateDropResult createResult = await MinitDropsClient.CreateDraftAsync(
                        _session, _draftTitle, _draftDescription, ResultTypeOptions[_resultTypeIndex],
                        _resultReverseSorting, ct);
                    if (_closed) return;

                    if (!createResult.Success)
                    {
                        FailUpload(createResult.Error ?? "Could not create the draft drop.");
                        return;
                    }

                    dropId = createResult.DropId;
                }
                else
                {
                    dropId = _existingDropId.Trim();
                }

                _uploadStatus = "Requesting upload URL…";
                long sizeBytes = new FileInfo(zipPath).Length;
                MinitUploadUrlResult uploadUrlResult = await MinitDropsClient.CreateUploadUrlAsync(_session, dropId, sizeBytes, ct);
                if (_closed) return;

                if (!uploadUrlResult.Success)
                {
                    FailUpload(uploadUrlResult.Error ?? "Could not create an upload URL.");
                    return;
                }

                _uploadStatus = "Uploading…";
                _uploadProgress = 0f;
                var uploadProgress = new FieldProgress<float>(v => _uploadProgress = v);
                MinitS3UploadResult s3Result = await MinitDropsClient.UploadZipToS3Async(
                    uploadUrlResult.Url, uploadUrlResult.Fields, zipPath, uploadProgress, ct);
                if (_closed) return;

                if (!s3Result.Success)
                {
                    FailUpload(s3Result.Error ?? "Upload to S3 failed.");
                    return;
                }

                _uploadStatus = "Processing…";
                _uploadProgress = -1f;
                var statusProgress = new FieldProgress<string>(s => _uploadStatus = $"Processing… ({s})");
                MinitProcessingResult processingResult = await MinitDropsClient.PollProcessingAsync(_session, dropId, statusProgress, ct);
                if (_closed) return;

                if (!processingResult.Success)
                {
                    FailUpload(processingResult.Error ?? "Processing failed.");
                    return;
                }

                _uploadedDropId = dropId;
                _uploadSucceeded = true;
                _uploadStatus = "Done.";
                Debug.Log($"[Minit] Drop {dropId} uploaded and processed successfully.");
            }
            catch (OperationCanceledException)
            {
                // Window closed (or otherwise cancelled) mid-flight — nothing to report.
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Minit] Upload failed with an unexpected exception:\n{ex}");
                if (!_closed)
                    FailUpload($"Unexpected error: {ex.Message}");
            }
            finally
            {
                _uploadInFlight = false;
            }
        }

        private void FailUpload(string error)
        {
            _uploadError = error;
            Debug.LogError($"[Minit] Upload failed: {error}");
        }

        private void ResetUploadState()
        {
            _uploadInFlight = false;
            _uploadStatus = null;
            _uploadProgress = -1f;
            _uploadError = null;
            _uploadSucceeded = false;
            _uploadedDropId = null;
        }

        /// <summary>
        /// Minimal <see cref="IProgress{T}"/> that writes straight to a plain field via a
        /// captured setter. Deliberately NOT <c>System.Progress&lt;T&gt;</c>, which posts through
        /// <see cref="SynchronizationContext"/>.Current — the Unity Editor does not guarantee a
        /// main-thread sync context for async continuations, so this stays a dumb field write;
        /// <see cref="OnGUI"/> reads it on the next repaint (see the class remarks) rather than
        /// anything here touching a Unity API.
        /// </summary>
        private class FieldProgress<T> : IProgress<T>
        {
            private readonly Action<T> _setter;
            public FieldProgress(Action<T> setter) => _setter = setter;
            public void Report(T value) => _setter(value);
        }
    }
}
