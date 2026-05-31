using UnityEngine;

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

            // Detect taps: legacy Input works for both mouse clicks (desktop/editor)
            // and touch events on mobile without any extra package.
            if (Input.GetMouseButtonDown(0))
                _taps++;

            // Also count additional touch points so multi-touch counts each finger.
            for (int i = 0; i < Input.touchCount; i++)
            {
                if (Input.GetTouch(i).phase == TouchPhase.Began)
                    _taps++;
            }

            if (_timeLeft <= 0f)
                EndGame();
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
