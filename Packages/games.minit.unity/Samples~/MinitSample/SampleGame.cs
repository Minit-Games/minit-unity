using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace MinitGames.Sample
{
    /// <summary>
    /// Minimal "tap to score" sample game for the Minit Games Unity SDK.
    ///
    /// Setup:
    ///   1. Create a new scene.
    ///   2. Add an empty GameObject and attach this component.
    ///   3. Add a <see cref="MinitGames.MinitReady"/> component on the same (or any other)
    ///      GameObject — it will call <c>Minit.LoadingDone()</c> on the first Update frame,
    ///      signalling the platform that the game is ready to be revealed.
    ///   4. Add the scene to Build Settings and run <b>Minit → Build for Minit</b>.
    ///
    /// Play:
    ///   - Tap (or click) as many times as you can during the 10-second countdown.
    ///   - When time expires the final score is submitted to the platform via
    ///     <c>Minit.ReportResult()</c>.
    /// </summary>
    public class SampleGame : MonoBehaviour
    {
        [Header("Game settings")]
        [SerializeField] private float _gameDuration = 10f;

        private int   _taps;
        private float _timeLeft;
        private bool  _gameOver;

        private void Start()
        {
            _timeLeft = _gameDuration;
            _gameOver = false;
            _taps     = 0;

            // LoadingDone() is handled by the MinitReady component attached to the scene.
            // If you prefer to call it manually, remove MinitReady and uncomment:
            //   Minit.LoadingDone();
        }

        private void Update()
        {
            if (_gameOver) return;

            // Count the time remaining.
            _timeLeft -= Time.deltaTime;

            // Detect taps via TapBegan(): on WebGL this fires once per tap for both
            // mouse clicks (desktop/editor) and single-finger touch (mobile WebGL).
            // Do NOT also loop Input.GetTouch — on WebGL the first touch is exposed as
            // BOTH a pointer press and touch[0], so combining both would double-count.
            if (TapBegan())
                _taps++;

            if (_timeLeft <= 0f)
                EndGame();
        }

        // Returns true on the frame a primary tap/click begins.
        // Works with legacy Input Manager, Input System package, or Both.
        private static bool TapBegan()
        {
#if ENABLE_INPUT_SYSTEM
            return Pointer.current != null && Pointer.current.press.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetMouseButtonDown(0);
#else
            return false;
#endif
        }

        private void EndGame()
        {
            _gameOver = true;

            string flavor = _taps == 1
                ? "Tapped 1 time!"
                : $"Tapped {_taps} times!";

            Debug.Log($"[MinitSample] Game over — {flavor}");
            Minit.ReportResult(_taps, flavor);
        }
    }
}
