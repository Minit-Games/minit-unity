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

> **Important — select the Minit WebGL template before building.**
> After installation, go to **Project Settings → Player → WebGL → Resolution and Presentation → WebGL Template** and select **Minit** (shown as `PROJECT:Minit`).
> Without this step Unity uses its Default template, which adds a progress bar, a fullscreen button, and a fixed-size canvas — none of which are compatible with the Minit platform.
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

### `Minit.ReportResult(score, flavorText, delay)`

```csharp
public static void ReportResult(double score, string flavorText = null, int delay = 0)
```

Submits the final result and triggers the Minit result screen. Call exactly once when the game ends — the host ignores repeat calls.

| Parameter | Type | Description |
|---|---|---|
| `score` | `double` | The player's result. Higher = better by default. |
| `flavorText` | `string?` | Optional short text shown on the result screen (e.g. `"Nice run!"`, `"3 mistakes"`). |
| `delay` | `int` | Milliseconds to wait before showing the result screen — use this to hold the result screen back during an end-of-game animation. |

In the editor and in non-WebGL builds, this logs to the Console instead of posting to the host.

---

### `Minit.GetConfigValue(key, defaultValue)`

```csharp
public static string GetConfigValue(string key, string defaultValue = "")
```

Reads a config value by key. Config values are delivered as URL query parameters on the game's URL — the same mechanism the JavaScript SDK uses — so the behaviour is identical regardless of which SDK a game uses. Values are always strings — coerce to your target type yourself.

The key `"userData"` is reserved by the platform and always returns `defaultValue`.

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

### Coming next

**DROP-2022 — "Build for Minit" editor menu** will auto-select this template, configure WebGL Player Settings (portrait lock, strip engine code, etc.) and package the build output into a Minit-compliant ZIP with a single menu click. Until then, select the template manually in Player Settings and build via **File → Build Settings → WebGL → Build**.

---

## Minit game rules

Before uploading, make sure your WebGL build meets the Minit platform constraints:

| Rule | Requirement |
|---|---|
| **Orientation** | Portrait only |
| **Input** | Touch / tap only — no keyboard or mouse expected |
| **Storage** | No `localStorage`, `sessionStorage`, `IndexedDB`, or cross-origin `fetch` |
| **ZIP structure** | `index.html` at the ZIP root (not inside a subfolder) |
| **Size** | ≤ 50 MB uncompressed |
| **Loading** | No in-game loading screen — use `Minit.LoadingDone()` to signal readiness; the platform handles the reveal transition |
| **Multithreading** | **Disabled** — leave WebGL multithreading OFF in Player Settings. Threaded builds require `Cross-Origin-Opener-Policy: same-origin` and `Cross-Origin-Embedder-Policy: require-corp` headers (`SharedArrayBuffer`); the Minit platform does not serve those headers, so threaded builds will crash in WKWebView. |

The `loadingDone` signal is the loading gate. Until it fires, the platform keeps Unity's boot splash hidden. Do not show your own loading bar inside the Unity scene.

---

## Coming next

The following features are planned and will land in upcoming releases:

- **"Build for Minit" editor menu** (DROP-2022) — a one-click Unity editor menu item that builds WebGL with the correct settings and packages the output into a Minit-compliant ZIP, ready to upload to the Minit creator console.
