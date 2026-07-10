# CLAUDE.md — minit-unity

Unity project hosting the Minit Games Unity SDK UPM package (`games.minit.unity`, GitHub repo `Minit-Games/minit-unity`, private), letting Unity creators export an existing WebGL game to a Minit-compliant ZIP via a C# bridge to the host-injected `window.minit` runtime. Full maintainer reference — the `window.minit` contract, shared engine-facade pattern, distribution/release philosophy, and per-engine gotchas — lives in the consolidated SDK-maintenance doc: https://github.com/Minit-Games/minit-root/blob/develop/docs/sdk-maintenance.md

---

## New Editor files (DROP-3224)

`Packages/games.minit.unity/Editor/` gained six files supporting the **Minit → Login & Upload** editor window:

- `MinitEnvironment.cs` — `MinitEnvironment` (Dev/Prod) enum + `MinitEnvironments.BackendBaseUrl()` / `ConsoleBaseUrl()` helpers.
- `MinitSession.cs` — environment (`EditorPrefs`) + access token/identity storage (`SessionState`, in-memory only).
- `MinitHttp.cs` — dependency-free GET/POST-JSON HTTP client wrapper shared by `MinitAuth` and `MinitDropsClient`.
- `MinitAuth.cs` — loopback browser-handoff login (`LoginAsync`), `EnsureAccessTokenAsync`, `Logout`.
- `MinitDropsClient.cs` — Drops/upload REST client: `CreateDraftAsync`, `CreateUploadUrlAsync`, `UploadZipToS3Async`, `PollProcessingAsync`, `BuildConsoleDropUrl`.
- `MinitLoginUploadWindow.cs` — the editor window itself (**Minit → Login & Upload** menu); see below.

---

## Login & Upload editor window (DROP-3224)

**Minit → Login & Upload** opens an in-editor window that lets a creator authenticate and ship a drop ZIP without leaving Unity. Implementation: `Packages/games.minit.unity/Editor/MinitLoginUploadWindow.cs`, built on `MinitEnvironment.cs` / `MinitSession.cs` / `MinitHttp.cs` / `MinitAuth.cs` / `MinitDropsClient.cs`.

**Environment.** A Dev/Prod popup bound to `MinitSession.Environment` (persisted via `EditorPrefs`, so it survives Editor restarts). Dev and Prod are different backends *and* different consoles — switching while logged in clears the session, since the stored token belongs to the old backend.

**Auth flow — loopback browser handoff, no api key.** Logging in opens the system browser at `console[-dev].minit.games/unity/authorize`, which (relying on the browser's existing Cognito session) redirects back to a local loopback listener with a Cognito token; that token is exchanged for backend tokens via the public `POST /auth/console` (authorizer: None). There is no api-key-based "refresh" path and `POST /users/refresh` is never called — when the ~15-minute access token expires, `MinitAuth.EnsureAccessTokenAsync` silently re-runs the exact same browser handoff. As long as the browser's Cognito session is still alive this is invisible to the creator (no prompt); if it has lapsed, the browser prompts for sign-in again. This removes the need for any long-lived secret inside the Unity tooling.

**Token storage.** The access token and user identity live in `SessionState` — in-memory for the life of the Editor process, never written to disk, cleared on logout or Editor restart. Storage is unencrypted; that's an accepted trade-off for a developer-facing editor tool holding a short-lived (~15 min) token, not a production credential store.

**Upload flow.** Once logged in:
1. **ZIP source** — either **Build for Minit now** (`MinitBuild.BuildAndZip()`, the same build+zip logic as the standalone `Minit → Build for Minit` menu, refactored in DROP-3224 to return the ZIP path instead of only revealing it in Finder/Explorer) or **Select ZIP…** to point at an existing archive.
2. **Target draft** — either create a new draft (title, description, result type, reverse-sorting) via `POST /drops`, or paste the id of an existing draft. **There is no list-drafts client method as of this cut** — pasting an id is the simplest "pick an existing draft" path; a picker is a natural follow-up once a list-drops client method exists.
3. **Upload** — `CreateUploadUrlAsync` (enforces the 50 MB cap pre-flight and mirrors it server-side) → `UploadZipToS3Async` (direct presigned POST to S3, no bearer token) → `PollProcessingAsync` (polls `GET /upload-history/drops/{dropId}?limit=1` — the same authoritative signal minit-web's UploadTracker polls — every ~2s/~60s cap until the newest upload record's `status` reaches `completed` or `failed`; an earlier version derived "done" from `streamingUrl`/`screenshotUrl` presence on `GET /drops/{dropId}`, which was wrong — the backend always populates both fields at create time, so that check reported success on the first poll and could never surface a ZIP-processing failure) → a **View in console** link built from `MinitDropsClient.BuildConsoleDropUrl`.

**`resultType` values.** `"score" | "time" | "group"` — confirmed against `minit-root/docs/api.md` (`POST /drops`). The window exposes them as a popup, defaulting to `"score"`.

---

## Validation (DROP-3224)

There is no automated C# test suite. **Minit → Login & Upload** validation:

1. Open **Minit → Login & Upload**. Confirm the Environment popup defaults to **Dev** and the window shows the login view.
2. Click **Log in with Minit** — a browser window opens at `console-dev.minit.games/unity/authorize`; the button shows "Waiting for browser sign-in…" and is disabled while the flow is in progress. After signing in, Unity should regain focus and show the logged-in header (name/email) without further interaction.
3. Toggle the Environment popup while logged in — confirm it logs out immediately and returns to the login view.
4. Log back in, then click **Build for Minit now** — confirm it builds and shows the resulting ZIP path + size, and that the window's ZIP source, unlike the standalone **Minit → Build for Minit** menu, does NOT reveal the file in Finder/Explorer.
5. Alternatively click **Select ZIP…** and pick an existing `Build/<Product>_minit.zip` — confirm the same path/size display appears.
6. Fill in **Create new draft** (Title required) and click **Upload to Minit** — confirm the progress bar/status text advances through "Creating draft…" → "Requesting upload URL…" → "Uploading…" → "Processing… (...)" → a success message with a working **Open in console** button that opens the drop in the creator console.
7. Repeat using **Use existing draft ID** with a previously-created draft's id — confirm it skips straight to the upload-URL step.
8. Force a failure (e.g. log out mid-upload by editing `SessionState` externally, or point at a bogus draft id) and confirm the failure surfaces inline instead of the Unity Editor showing an unhandled exception.
9. Close the window while an upload is in flight — confirm the Console shows no errors from a continuation writing to a destroyed window.
