# Minit Sample Game

A minimal "tap to score" game that demonstrates the Minit Games Unity SDK. The player taps as many times as possible during a 10-second countdown; the final count is submitted as the score.

---

## Importing the sample

1. Open **Window → Package Manager**.
2. Select **Minit Games SDK** from the list.
3. Click the **Samples** tab, find **Minit Sample**, and click **Import**.
4. Unity copies the sample into `Assets/Samples/Minit Games SDK/<version>/MinitSample/`.

---

## Setting up a scene

1. Create a new scene (**File → New Scene → Basic (Built-in)** or any empty template).
2. Add an empty GameObject (right-click in the Hierarchy → **Create Empty**). Name it `Game`.
3. Drag **SampleGame** onto the `Game` object (or use **Add Component → MinitGames.Sample → Sample Game**).
4. On the same GameObject, add the **MinitReady** component (search "MinitReady" in Add Component).
   - `MinitReady` calls `Minit.LoadingDone()` on the first Update frame, telling the platform the game is ready to be revealed.
5. Save the scene.

---

## Building

1. Open **File → Build Settings**.
2. Click **Add Open Scenes** (or drag the sample scene into the list) and ensure it is enabled.
3. Run **Minit → Build for Minit** from the menu bar.
   - The menu item applies all required WebGL Player Settings automatically and writes a `Build/<ProductName>_minit.zip` at your project root.
4. Upload the ZIP to the [Minit creator console](https://console.minit.games).

---

## How it works

| File | Role |
|---|---|
| `SampleGame.cs` | Counts taps for 10 seconds using legacy `Input`, then calls `Minit.ReportResult()`. |
| `MinitReady` component | Ships with the SDK. Calls `Minit.LoadingDone()` on the first Update frame. |

The `SampleGame` component reads `_gameDuration` from the Inspector (default: 10 s). Change it to adjust the round length without modifying code.
