using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace MinitGames.Test
{
    /// <summary>
    /// TEST-ONLY drop-in overlay for visually verifying the Minit bridge on a real device or in
    /// the app WebView. It confirms (a) rendering works, (b) touch/taps are registered, and
    /// (c) the MinitGames.Minit bridge calls fire correctly (LoadingDone, ReportResult,
    /// GetConfigValue). Auto-spawns via RuntimeInitializeOnLoadMethod — no scene or prefab
    /// wiring required. DO NOT ship this component in a real game.
    /// </summary>
    public class MinitDebugOverlay : MonoBehaviour
    {
        // -----------------------------------------------------------------------------------------
        // Auto-spawn — attaches to every scene without any scene setup
        // -----------------------------------------------------------------------------------------

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            var go = new GameObject("MinitDebugOverlay");
            DontDestroyOnLoad(go);
            go.AddComponent<MinitDebugOverlay>();
        }

        // -----------------------------------------------------------------------------------------
        // State
        // -----------------------------------------------------------------------------------------

        private int    _taps;
        private float  _elapsed;
        private bool   _loadingDoneSent;
        private string _lastResult = "(none)";
        private string _seedValue  = "(not read yet)";

        // Cached IMGUI styles — allocated once, reused every frame
        private GUIStyle _panelStyle;
        private GUIStyle _titleStyle;
        private GUIStyle _labelStyle;
        private GUIStyle _buttonStyle;
        private bool     _stylesReady;

        // -----------------------------------------------------------------------------------------
        // Unity lifecycle
        // -----------------------------------------------------------------------------------------

        private void Start()
        {
            Minit.LoadingDone();
            _loadingDoneSent = true;
        }

        private void Update()
        {
            _elapsed += Time.deltaTime;

            // Tap detection is routed through TapBegan() so the same code works regardless of
            // whether the project uses the legacy Input Manager, the Input System package, or Both.
            if (TapBegan())
                _taps++;
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

        private void OnGUI()
        {
            EnsureStyles();

            // Panel geometry — takes up most of the screen so it can't be missed
            float pw = Screen.width  * 0.92f;
            float ph = Screen.height * 0.88f;
            float px = (Screen.width  - pw) * 0.5f;
            float py = (Screen.height - ph) * 0.5f;
            var   panel = new Rect(px, py, pw, ph);

            GUI.Box(panel, GUIContent.none, _panelStyle);

            // Layout inside the panel
            float pad  = Screen.height * 0.025f;
            float lineH = Screen.height * 0.055f;
            float btnH  = Screen.height / 12f;
            float x    = px + pad;
            float w    = pw - pad * 2f;
            float y    = py + pad;

            // Title
            GUI.Label(new Rect(x, y, w, lineH * 1.4f), "MINIT DEBUG", _titleStyle);
            y += lineH * 1.5f;

            // Live status lines
            GUI.Label(new Rect(x, y, w, lineH), $"Platform: {Application.platform}", _labelStyle);
            y += lineH;
            GUI.Label(new Rect(x, y, w, lineH), $"Taps: {_taps}", _labelStyle);
            y += lineH;
            GUI.Label(new Rect(x, y, w, lineH), $"Elapsed: {_elapsed:F1}s", _labelStyle);
            y += lineH;
            GUI.Label(new Rect(x, y, w, lineH), $"LoadingDone sent: {(_loadingDoneSent ? "yes" : "no")}", _labelStyle);
            y += lineH;
            GUI.Label(new Rect(x, y, w, lineH), $"Last result: {_lastResult}", _labelStyle);
            y += lineH;
            GUI.Label(new Rect(x, y, w, lineH), $"cfg \"seed\": {Minit.GetConfigValue("seed", "(unset)")}", _labelStyle);
            y += lineH * 1.4f;

            // Buttons
            if (GUI.Button(new Rect(x, y, w, btnH), "Report Result  (score = taps)", _buttonStyle))
            {
                Minit.ReportResult(_taps, $"Tapped {_taps} times!");
                _lastResult = $"sent {_taps}";
            }
            y += btnH + pad;

            if (GUI.Button(new Rect(x, y, w, btnH), "Signal LoadingDone", _buttonStyle))
            {
                Minit.LoadingDone();
                _loadingDoneSent = true;
            }
            y += btnH + pad;

            if (GUI.Button(new Rect(x, y, w, btnH), "Read config 'seed'", _buttonStyle))
            {
                _seedValue = Minit.GetConfigValue("seed", "(unset)");
            }
            y += btnH + pad;

            GUI.Label(new Rect(x, y, w, lineH), $"seed result: {_seedValue}", _labelStyle);
        }

        // -----------------------------------------------------------------------------------------
        // Style helpers
        // -----------------------------------------------------------------------------------------

        private void EnsureStyles()
        {
            if (_stylesReady) return;

            // Semi-opaque dark background panel
            var bgTex = new Texture2D(1, 1);
            bgTex.SetPixel(0, 0, new Color(0.05f, 0.05f, 0.1f, 0.88f));
            bgTex.Apply();

            _panelStyle = new GUIStyle(GUI.skin.box)
            {
                normal = { background = bgTex }
            };

            // Button highlight texture (slightly lighter)
            var btnTex = new Texture2D(1, 1);
            btnTex.SetPixel(0, 0, new Color(0.18f, 0.36f, 0.72f, 1f));
            btnTex.Apply();

            var btnHoverTex = new Texture2D(1, 1);
            btnHoverTex.SetPixel(0, 0, new Color(0.25f, 0.50f, 0.90f, 1f));
            btnHoverTex.Apply();

            int fontSize     = Mathf.Max(14, Screen.height / 30);
            int titleSize    = Mathf.Max(20, Screen.height / 18);
            int buttonFSize  = Mathf.Max(16, Screen.height / 26);

            _titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize  = titleSize,
                fontStyle = FontStyle.Bold,
                normal    = { textColor = new Color(0.9f, 0.85f, 0.2f) }
            };

            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = fontSize,
                normal   = { textColor = Color.white }
            };

            _buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize  = buttonFSize,
                fontStyle = FontStyle.Bold,
                normal    = { background = btnTex,      textColor = Color.white },
                hover     = { background = btnHoverTex, textColor = Color.white },
                active    = { background = btnHoverTex, textColor = Color.yellow }
            };

            _stylesReady = true;
        }
    }
}
