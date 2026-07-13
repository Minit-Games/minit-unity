using UnityEditor;
using UnityEngine;

namespace MinitGames.Editor
{
    /// <summary>
    /// Small utility window bundling the Minit editor actions.
    /// Accessible via <b>Minit → Minit</b> in the Unity menu bar.
    /// </summary>
    public class MinitWindow : EditorWindow
    {
        private const string MenuPath = "Minit/Minit";
        private const string CreatorDashboardUrl = "https://console.minit.games";

        [MenuItem(MenuPath)]
        public static void ShowWindow()
        {
            var window = GetWindow<MinitWindow>();
            window.titleContent = new GUIContent("Minit");
            window.Show();
        }

        private void OnGUI()
        {
            // MinitBuild.BuildForMinit() already wraps its work in try/catch and logs
            // errors to the Console, so calling it directly here is sufficient.
            if (GUILayout.Button("Build Zip"))
                MinitBuild.BuildForMinit();

            if (GUILayout.Button("Creator Dashboard"))
                Application.OpenURL(CreatorDashboardUrl);
        }
    }
}
