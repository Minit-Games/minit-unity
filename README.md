# Minit Games Unity SDK

A UPM package that lets Unity creators export an existing **WebGL** game to a Minit-compliant ZIP. The package provides a C# bridge to the host-injected `window.minit` runtime so games can call `reportResult` / `loadingDone` / read config — with no JavaScript required.

GitHub repo: `Minit-Games/minit-unity`

---

## Requirements

| Requirement | Minimum |
|---|---|
| Unity | **6000.0 (Unity 6)** |
| Build target | **WebGL only** |

**Why Unity 6?** Official mobile-browser WebGL support (WKWebView on iOS, Chrome on Android) is only reliable from Unity 6 onwards. Earlier versions produced broken or degraded builds in the in-app WebViews the Minit platform uses to host games.

---

## Installation

Install via UPM Git URL:

1. Open **Window → Package Manager** in the Unity Editor.
2. Click **+ → Add package from git URL…**
3. Paste:
   ```
   https://github.com/Minit-Games/minit-unity.git?path=/Packages/games.minit.unity
   ```
4. Click **Add**.

Unity will download the package and add it to your project's `Packages/` manifest.

> **Note — WebGL template selection.**
> **Minit → Build for Minit** (see the [Build for Minit](#build-for-minit) section) selects the Minit template automatically — no manual step needed.
> If you build via Unity's own **File → Build Settings** instead, go to **Project Settings → Player → WebGL → Resolution and Presentation → WebGL Template** and select **Minit** (`PROJECT:Minit`) before building.
> Without the Minit template Unity uses its Default template, which adds a progress bar, a fullscreen button, and a fixed-size canvas — none of which are compatible with the Minit platform.
> See the [WebGL template](#webgl-template) section below for full details.

---

## Quick start

```csharp
using UnityEngine;
using MinitGames;

public class GameController : MonoBehaviour
{
    private double _score = 0;

    private void Start()
    {
        // Tell the Minit platform the game has loaded and is ready to be shown.
        // Call this after assets are loaded and the first real frame is ready — not
        // necessarily in Start() (which fires before the first rendered frame).
        // Until this call, the platform hides Unity's boot splash behind the feed reveal.
        Minit.LoadingDone();
    }

    private void Update()
    {
        // ... game logic, accumulate score ...
    }

    // Call once when the game ends.
    public void OnGameOver()
    {
        Minit.ReportResult(_score, flavorText: "Nice run!");
    }
}
```

If you don't need to control the exact moment the game is ready, attach the `MinitReady` component to any GameObject in your scene — it calls `LoadingDone()` on the first update frame automatically.

---

## API reference

### `Minit.LoadingDone()`

```csharp
public static void LoadingDone()
```

Signals the host that the game has finished booting and is ready to be revealed to the player. The Minit feed hides Unity's boot splash until this fires — call it after assets are loaded and the first real frame is ready (not necessarily in `Start()`, which fires before the first rendered frame). Call exactly once per session.

If your game doesn't have a precise readiness point, use the `MinitReady` component instead — it calls `LoadingDone()` on the first Update frame automatically.

In the editor and in non-WebGL builds, this logs `[Minit] LoadingDone()` to the Console instead.

---

### `Minit.ReportResult(score, flavorText, delay, userData)`

```csharp
public static void ReportResult(double score, string flavorText = null, int delay = 0, string userData = null)
```

Submits the final result and triggers the Minit result screen. Call exactly once when the game ends — the host ignores repeat calls.

| Parameter | Type | Description |
|---|---|---|
| `score` | `double` | The player's result. Higher = better by default. |
| `flavorText` | `string?` | Optional short text shown on the result screen (e.g. `"Nice run!"`, `"3 mistakes"`). |
| `delay` | `int` | Milliseconds to wait before showing the result screen — use this to hold the result screen back during an end-of-game animation. |
| `userData` | `string?` | Optional player save state to persist for this creator's games. Pass a plain string (serialize it yourself, e.g. JSON). Omit (or pass `null`) to leave the stored value unchanged. Written only at result-report time — there is no standalone save call. |

In the editor and in non-WebGL builds, this logs to the Console instead of posting to the host.

---

### `Minit.GetConfigValue(key, defaultValue)`

```csharp
public static string GetConfigValue(string key, string defaultValue = "")
```

Reads a config value by key. Config values are delivered as URL query parameters on the game's URL — the same mechanism the JavaScript SDK uses — so the behaviour is identical regardless of which SDK a game uses. Values are always strings — coerce to your target type yourself.

The key `"userData"` is reserved by the platform and always returns `defaultValue` — use [`Minit.GetUserData`](#minitgetuserdatadefaultvalue) to read player save state.

| Parameter | Type | Description |
|---|---|---|
| `key` | `string` | The config key to look up. |
| `defaultValue` | `string` | Returned when the key is absent, reserved, or the game is running outside the Minit host. |

```csharp
using MinitGames;

// Example — read a difficulty setting, default "normal"
string difficulty = Minit.GetConfigValue("difficulty", "normal");

// Example — read a numeric config value
float speed = float.TryParse(Minit.GetConfigValue("speed", "1.0"), out float v) ? v : 1f;
```

---

### `Minit.GetUserData(defaultValue)`

```csharp
public static string GetUserData(string defaultValue = "")
```

Reads the host-injected single-slot userData string for this player — shared across all of this creator's games. Reads `window.minit.userData` directly. Falls back to the `?userData=<value>` URL param for local dev (a host-injected string, including `""`, wins over the URL param). Returns `defaultValue` when nothing is stored and no URL param is present, or when running outside the Minit host (editor / non-WebGL).

userData is written via [`Minit.ReportResult`](#minitreportresultscore-flavortext-delay-userdata) — there is no standalone save call. It is single-slot: one value per player per creator, not per game.

| Parameter | Type | Description |
|---|---|---|
| `defaultValue` | `string` | Returned when no userData is stored, no URL param is present, or the game is running outside the Minit host. |

```csharp
using MinitGames;

// Example — read a JSON save blob, default to an empty object
string saveJson = Minit.GetUserData("{}");
var save = JsonUtility.FromJson<SaveData>(saveJson);

// ...later, on game over, write it back
Minit.ReportResult(_score, userData: JsonUtility.ToJson(save));
```

---

### `MinitReady` component

A drop-in `MonoBehaviour` that calls `Minit.LoadingDone()` on the first Update frame after the scene loads. Attach it to any GameObject in your scene. Add `using MinitGames;` if you reference it from code.

Use this when you don't have a precise readiness signal. If your game has its own loading gate (assets downloaded, intro animation finished, countdown started), call `Minit.LoadingDone()` directly from that point instead and leave this component off.

---

## WebGL template

The package ships a custom Unity WebGL template named **Minit**. Select it in your project via:

**Project Settings → Player → WebGL → Resolution and Presentation → WebGL Template → Minit**

(The identifier shown in Player Settings is `PROJECT:Minit`.)

### What it emits

When Unity builds for WebGL with the Minit template selected, it produces a root `index.html` that:

- **Fills the viewport** at any portrait aspect ratio (iPhone ~9:19.5 through Android ~9:21) — the canvas is `100% × 100%` with no fixed pixel dimensions, no letter-boxing, and no aspect-ratio constraint.
- Sets `touch-action: none` on the canvas so iOS WKWebView and Android Chrome do not intercept touch events before Unity sees them.
- Uses `viewport-fit=cover` and `user-scalable=no` so the game extends into the safe-area notch region without bounce-scrolling.
- **Has no loader chrome** — no progress bar, no spinner, no fullscreen button, no Unity logo. The Minit platform hides Unity's boot splash behind the feed reveal; the game signals readiness by calling `Minit.LoadingDone()` (or by attaching the `MinitReady` component), at which point the platform transitions to the game.
- **Contains no `<script>` tag for the Minit SDK.** The host app injects `window.minit` at WebView startup — the template does not need to load it. Adding a second injection here would cause conflicts.

### Build for Minit

Use the **Minit → Build for Minit** menu item (see the [Build for Minit](#build-for-minit) section below) to apply all Player Settings and produce a compliant ZIP in one click — you no longer need to configure the template manually.

---

## Minit game rules

Before uploading, make sure your WebGL build meets the Minit platform constraints:

| Rule | Requirement |
|---|---|
| **Orientation** | Portrait only |
| **Input** | Touch / tap only — no keyboard or mouse expected |
| **Storage** | No `localStorage`, `sessionStorage`, `IndexedDB`, or cross-origin `fetch` |
| **ZIP structure** | `index.html` at the ZIP root (not inside a subfolder) |
| **Size** | ≤ 50 MB — the uploaded ZIP (`Build/<Product>_minit.zip`) must not exceed 50 MB (server hard limit: 52,428,800 bytes) |
| **Loading** | No in-game loading screen — use `Minit.LoadingDone()` to signal readiness; the platform handles the reveal transition |
| **Multithreading** | **Disabled** — leave WebGL multithreading OFF in Player Settings. Threaded builds require `Cross-Origin-Opener-Policy: same-origin` and `Cross-Origin-Embedder-Policy: require-corp` headers (`SharedArrayBuffer`); the Minit platform does not serve those headers, so threaded builds will crash in WKWebView. |

The `loadingDone` signal is the loading gate. Until it fires, the platform keeps Unity's boot splash hidden. Do not show your own loading bar inside the Unity scene.

---

## Build for Minit

> **Unity 6 (6000.0+) required.** The `Minit/Build for Minit` menu item is only available in Unity 6.

### Usage

Open **Minit → Build for Minit** from the Unity menu bar. The command:

1. Checks that the Minit WebGL template and at least one enabled scene are present.
2. Applies all compliant Player Settings automatically (see table below).
3. Scans `Assets/` for `PlayerPrefs` usage and prints a warning if found.
4. Builds WebGL to `Build/MinitWebGL/` inside your project root.
5. Zips the build output so `index.html` is at the archive root → `Build/<ProductName>_minit.zip`.
6. Opens Finder/Explorer at the ZIP location.

Upload the resulting ZIP directly to the [Minit creator console](https://console.minit.games).

### Player Settings applied

| Setting | Value | Why |
|---|---|---|
| **WebGL template** | `PROJECT:Minit` | The Minit template produces a viewport-filling, chrome-free canvas required by the platform. |
| **Compression format** | Brotli | Smallest transfer size; supported natively by all modern mobile browsers (Chrome, Safari 17+, Firefox). |
| **Decompression fallback** | Off | The Minit platform now serves pre-compressed bundle files with the correct `Content-Encoding` header (`br`/`gzip`) on all serving paths (in-app iOS/Android WebView URL-scheme handlers and CDN-served S3 objects), so the browser decompresses Brotli `*.br` files natively (DROP-2092). No bundled JS decompressor is needed, giving a smaller build and faster cold boot. |
| **Exception support** | None | Removes substantial generated code; games should not throw managed exceptions in release. |
| **Data caching (IndexedDB)** | Off | The Minit platform forbids persistent client-side storage (localStorage, sessionStorage, IndexedDB). |
| **Linker target** | Wasm | Single-threaded Wasm is the only output that runs inside WKWebView without COOP/COEP headers. |
| **Scripting backend** | IL2CPP | Better runtime performance and smaller binary than Mono for WebGL. |
| **Managed stripping level** | High | Removes unused managed code, significantly reducing output size. |
| **Strip engine code** | On | Removes unused Unity engine modules for a smaller binary. |
| **Run in background** | On | Keeps the game loop alive when the browser tab loses focus — essential for `ReportResult` not to stall when the WebView is backgrounded. |

### Multithreading must stay OFF

Do **not** enable WebGL multithreading in Player Settings. Threaded builds require `Cross-Origin-Opener-Policy: same-origin` and `Cross-Origin-Embedder-Policy: require-corp` headers (`SharedArrayBuffer`). The Minit platform does not serve those headers and threaded builds will crash inside WKWebView.

### PlayerPrefs warning

Before building, the tool scans all `.cs` files under `Assets/` for the string `PlayerPrefs`. If found, it logs a **non-blocking** warning listing the affected files. `PlayerPrefs` maps to `localStorage`, which is forbidden on the Minit platform — replace any usage with in-memory state or `Minit.GetConfigValue()`.

### Output location

```
<project-root>/
  Build/
    MinitWebGL/          ← raw WebGL build (auto-cleaned on each run)
    <ProductName>_minit.zip  ← upload this to the creator console
```

`<ProductName>` is taken from **Project Settings → Player → Product Name**, with spaces and special characters replaced by underscores.

### Size limit

If the ZIP exceeds **50 MB**, the tool logs a warning. The file is still written but the creator console will reject the upload. Reduce asset sizes or enable additional stripping to bring the build under the cap.

---

## Sample game

The package ships a minimal **Minit Sample** to help you verify the full workflow end-to-end.

**Import:**

1. **Window → Package Manager → Minit Games SDK → Samples → Minit Sample → Import**
2. Unity copies the sample to `Assets/Samples/Minit Games SDK/<version>/MinitSample/`.

**Set up a scene:**

1. Create a new scene.
2. Add an empty GameObject named `Game`.
3. Attach **MinitGames.Sample.SampleGame** and **MinitGames.MinitReady** to it.
4. Save the scene and add it to Build Settings.

**Build:**

Run **Minit → Build for Minit**. The resulting ZIP is a valid Minit game you can upload to the creator console.

See `Samples~/MinitSample/README.md` inside the package for full details.
