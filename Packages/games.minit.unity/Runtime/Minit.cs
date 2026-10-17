using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace MinitGames
{
    public static class Minit
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern void MinitReportResult(double score, int hasFlavor, string flavorText, int delay, int hasUserData, string userData);
        [DllImport("__Internal")] private static extern void MinitReportResultString(string result, int hasFlavor, string flavorText, int delay, int hasUserData, string userData);
        [DllImport("__Internal")] private static extern void MinitLoadingDone();
        [DllImport("__Internal")] private static extern IntPtr MinitGetConfigValue(string key, string defaultValue);
        [DllImport("__Internal")] private static extern IntPtr MinitGetUserData(string defaultValue);
        [DllImport("__Internal")] private static extern IntPtr MinitTryGetUserData();
        [DllImport("__Internal")] private static extern void MinitFreeBuffer(IntPtr ptr);
        [DllImport("__Internal")] private static extern int MinitShouldShowTutorial();
        [DllImport("__Internal")] private static extern int MinitIsViewable();
        [DllImport("__Internal")] private static extern void MinitOnViewableChange(Action<int> callback);

        private static readonly List<Action<bool>> ViewableSubscribers = new List<Action<bool>>();
        private static bool _viewableBridgeRegistered;

        [AOT.MonoPInvokeCallback(typeof(Action<int>))]
        private static void DispatchViewableChange(int viewable)
        {
            foreach (Action<bool> subscriber in ViewableSubscribers.ToArray())
            {
                try
                {
                    subscriber(viewable != 0);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }
#endif

        /// <summary>Submit the final result. Call exactly once when the game ends. Higher score = better by default.
        /// For time-based games (meta.json resultSorting fastestTime/slowestTime) <paramref name="score"/> is SECONDS,
        /// not milliseconds — fractions allowed (e.g. 42.5).</summary>
        public static void ReportResult(double score, string flavorText = null, int delay = 0, string userData = null)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            MinitReportResult(score, flavorText != null ? 1 : 0, flavorText, delay, userData != null ? 1 : 0, userData);
#else
            Debug.Log($"[Minit] ReportResult({score}, \"{flavorText}\", {delay}, \"{userData}\")");
#endif
        }

        /// <summary>Submit a non-numeric final result (e.g. "win", "lose", or a choice id). Call exactly once when the game ends.</summary>
        public static void ReportResult(string result, string flavorText = null, int delay = 0, string userData = null)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            MinitReportResultString(result, flavorText != null ? 1 : 0, flavorText, delay, userData != null ? 1 : 0, userData);
#else
            Debug.Log($"[Minit] ReportResult(\"{result}\", \"{flavorText}\", {delay}, \"{userData}\")");
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

        /// <summary>Read the host-injected single-slot userData. Falls back to the `?userData=` URL param for local dev.</summary>
        public static string GetUserData(string defaultValue = "")
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            IntPtr ptr = MinitGetUserData(defaultValue);
            string result = Marshal.PtrToStringUTF8(ptr);
            MinitFreeBuffer(ptr);
            return result ?? defaultValue;
#else
            return defaultValue;
#endif
        }

        /// <summary>Read the single-slot userData, distinguishing "nothing stored" (returns false, <paramref name="value"/> null)
        /// from a stored empty string (returns true, <paramref name="value"/> ""). Same sources as <see cref="GetUserData"/>.
        /// Always false in the editor.</summary>
        public static bool TryGetUserData(out string value)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            IntPtr ptr = MinitTryGetUserData();
            if (ptr == IntPtr.Zero)
            {
                value = null;
                return false;
            }
            value = Marshal.PtrToStringUTF8(ptr) ?? "";
            MinitFreeBuffer(ptr);
            return true;
#else
            value = null;
            return false;
#endif
        }

        /// <summary>Whether to run the first-play tutorial. Config key `tutorial` = "1"/"true" forces it on, "0"/"false" forces it off;
        /// otherwise it is hidden once the host reports the player has already played this Game. Always true in the editor.</summary>
        public static bool ShouldShowTutorial()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return MinitShouldShowTutorial() != 0;
#else
            return true;
#endif
        }

        /// <summary>Whether the game is currently viewable: feed item active and focused, app in the foreground.
        /// Falls back to the page's visibility when the host doesn't report viewability. Always true in the editor.</summary>
        public static bool IsViewable()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return MinitIsViewable() != 0;
#else
            return true;
#endif
        }

        /// <summary>Subscribe to viewability changes; <paramref name="handler"/> fires only when the value actually changes.
        /// Subscribing the same handler twice is a no-op. Returns an Action that unsubscribes. No-op in the editor.</summary>
        public static Action OnViewableChange(Action<bool> handler)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (!ViewableSubscribers.Contains(handler)) ViewableSubscribers.Add(handler);
            if (!_viewableBridgeRegistered)
            {
                _viewableBridgeRegistered = true;
                MinitOnViewableChange(DispatchViewableChange);
            }
            return () => ViewableSubscribers.Remove(handler);
#else
            return () => { };
#endif
        }
    }
}
