using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace MinitGames
{
    public static class Minit
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern void MinitReportResult(double score, string flavorText, int delay);
        [DllImport("__Internal")] private static extern void MinitLoadingDone();
        [DllImport("__Internal")] private static extern IntPtr MinitGetConfigValue(string key, string defaultValue);
        [DllImport("__Internal")] private static extern void MinitFreeBuffer(IntPtr ptr);
#endif

        /// <summary>Submit the final result. Call exactly once when the game ends. Higher score = better by default.</summary>
        public static void ReportResult(double score, string flavorText = null, int delay = 0)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            MinitReportResult(score, flavorText ?? "", delay);
#else
            Debug.Log($"[Minit] ReportResult({score}, \"{flavorText}\", {delay})");
#endif
        }

        /// <summary>Signal the host the game is booted and ready to be revealed.</summary>
        public static void LoadingDone()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            MinitLoadingDone();
#else
            Debug.Log("[Minit] LoadingDone()");
#endif
        }

        /// <summary>Read a game config value from URL query parameters. Returns defaultValue when absent or when key is reserved.</summary>
        public static string GetConfigValue(string key, string defaultValue = "")
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            IntPtr ptr = MinitGetConfigValue(key, defaultValue);
            string result = Marshal.PtrToStringUTF8(ptr);
            MinitFreeBuffer(ptr);
            return result ?? defaultValue;
#else
            return defaultValue;
#endif
        }
    }
}
