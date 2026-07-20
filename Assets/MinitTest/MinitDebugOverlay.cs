using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace MinitGames.Test
{
    /// <summary>
    /// TEST-ONLY drop-in overlay for visually verifying the Minit bridge on a real device or in
    /// the app WebView. It confirms (a) rendering works, (b) touch/taps are registered, and
    /// (c) the MinitGames.Minit bridge calls fire correctly (LoadingDone, ReportResult (including
    /// its userData write), GetConfigValue, GetUserData). Auto-spawns via
    /// RuntimeInitializeOnLoadMethod — no scene or prefab wiring required. DO NOT ship this
    /// component in a real game.
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
        private string _userDataValue = "(not read yet)";

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

            // Base all sizing on the short screen edge so both portrait and landscape look right.
            int shortEdge = Mathf.Min(Screen.width, Screen.height);

            // Panel geometry — centred, 92 % wide, up to 90 % tall
            float pw = Screen.width  * 0.92f;
            float ph = Screen.height * 0.90f;
            float px = (Screen.width  - pw) * 0.5f;
            float py = (Screen.height - ph) * 0.5f;
            var   panel = new Rect(px, py, pw, ph);

            GUI.Box(panel, GUIContent.none, _panelStyle);

            // GUILayout area confined to the panel interior
            float pad = shortEdge * 0.03f;
            var   innerRect = new Rect(px + pad, py + pad, pw - pad * 2f, ph - pad * 2f);

            GUILayout.BeginArea(innerRect);

            // Title
            GUILayout.Label("MINIT DEBUG", _titleStyle);
            GUILayout.Space(shortEdge * 0.015f);

            // Live status lines
            GUILayout.Label($"Platform: {Application.platform}", _labelStyle);
            GUILayout.Label($"Taps: {_taps}", _labelStyle);
            GUILayout.Label($"Elapsed: {_elapsed:F1}s", _labelStyle);
            GUILayout.Label($"LoadingDone sent: {(_loadingDoneSent ? "yes" : "no")}", _labelStyle);
            GUILayout.Label($"Last result: {_lastResult}", _labelStyle);
            GUILayout.Label($"cfg \"seed\": {Minit.GetConfigValue("seed", "(unset)")}", _labelStyle);
            GUILayout.Space(shortEdge * 0.03f);

            // Buttons
            float btnH = shortEdge / 9f;
            if (GUILayout.Button("Report Result  (score = taps)", _buttonStyle, GUILayout.Height(btnH)))
            {
                Minit.ReportResult(_taps, $"Tapped {_taps} times!", userData: $"taps={_taps}");
                _lastResult = $"sent {_taps} (userData: taps={_taps})";
            }
            GUILayout.Space(shortEdge * 0.015f);

            if (GUILayout.Button("Read config 'seed'", _buttonStyle, GUILayout.Height(btnH)))
            {
                _seedValue = Minit.GetConfigValue("seed", "(unset)");
            }
            GUILayout.Space(shortEdge * 0.015f);

            GUILayout.Label($"seed result: {_seedValue}", _labelStyle);
            GUILayout.Space(shortEdge * 0.015f);

            if (GUILayout.Button("Read userData", _buttonStyle, GUILayout.Height(btnH)))
            {
                _userDataValue = Minit.GetUserData("(none)");
            }
            GUILayout.Space(shortEdge * 0.015f);

            GUILayout.Label($"userData: {_userDataValue}", _labelStyle);

            GUILayout.EndArea();
        }

        // -----------------------------------------------------------------------------------------
        // Style helpers
        // -----------------------------------------------------------------------------------------

        private void EnsureStyles()
        {
            if (_stylesReady) return;

            int shortEdge = Mathf.Min(Screen.width, Screen.height);

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

            int titleSize   = Mathf.Max(20, shortEdge / 18);
            int fontSize    = Mathf.Max(14, shortEdge / 28);
            int buttonFSize = Mathf.Max(16, shortEdge / 24);

            _titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize  = titleSize,
                fontStyle = FontStyle.Bold,
                wordWrap  = true,
                normal    = { textColor = new Color(0.9f, 0.85f, 0.2f) }
            };

            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = fontSize,
                wordWrap = true,
                normal   = { textColor = Color.white }
            };

            _buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize  = buttonFSize,
                fontStyle = FontStyle.Bold,
                wordWrap  = true,
                normal    = { background = btnTex,      textColor = Color.white },
                hover     = { background = btnHoverTex, textColor = Color.white },
                active    = { background = btnHoverTex, textColor = Color.yellow }
            };

            _stylesReady = true;
        }
    }
}
