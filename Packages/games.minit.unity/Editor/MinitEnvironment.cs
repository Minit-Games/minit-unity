using System;

namespace MinitGames.Editor
{
    /// <summary>
    /// The two backend/console deployments a creator can target from the Unity editor tools.
    /// </summary>
    public enum MinitEnvironment
    {
        Dev,
        Prod
    }

    /// <summary>
    /// Base URLs for each <see cref="MinitEnvironment"/>. Neither URL has a trailing slash —
    /// callers concatenate paths directly (e.g. <c>BackendBaseUrl(env) + "/auth/console"</c>).
    /// </summary>
    public static class MinitEnvironments
    {
        /// <summary>Base URL of the Minit backend REST API for <paramref name="environment"/>.</summary>
        public static string BackendBaseUrl(MinitEnvironment environment)
        {
            switch (environment)
            {
                case MinitEnvironment.Dev:
                    return "https://api-dev.minit.games";
                case MinitEnvironment.Prod:
                    return "https://api.minit.games";
                default:
                    throw new ArgumentOutOfRangeException(nameof(environment), environment, null);
            }
        }

        /// <summary>Base URL of the Minit creator console for <paramref name="environment"/>.</summary>
        public static string ConsoleBaseUrl(MinitEnvironment environment)
        {
            switch (environment)
            {
                case MinitEnvironment.Dev:
                    return "https://console-dev.minit.games";
                case MinitEnvironment.Prod:
                    return "https://console.minit.games";
                default:
                    throw new ArgumentOutOfRangeException(nameof(environment), environment, null);
            }
        }
    }
}
