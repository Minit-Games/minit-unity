using System;
using UnityEditor;

namespace MinitGames.Editor
{
    /// <summary>
    /// The authenticated user identity returned by <c>GET /users/self</c>.
    /// Mirrors the subset of the backend's <c>SelfUser</c> shape the Unity tooling needs.
    /// </summary>
    [Serializable]
    public class MinitUser
    {
        public string id;
        public string name;
        public string email;
    }

    /// <summary>
    /// Holds the state shared by the whole Login &amp; Upload window: which environment
    /// (dev/prod) is selected, and the current login session (access token + identity).
    ///
    /// <para>
    /// The selected <see cref="Environment"/> is persisted in <see cref="EditorPrefs"/> so it
    /// survives Editor restarts. The access token and identity are session-scoped
    /// (<see cref="SessionState"/>) — access tokens are short-lived (~15 min), so persisting
    /// them across Editor restarts would be pointless; re-authenticating via the loopback
    /// browser handoff on next use is cheap and silent as long as the browser's Cognito
    /// session is still alive.
    /// </para>
    ///
    /// <para>
    /// SECURITY NOTE: the access token is stored unencrypted in <see cref="SessionState"/>
    /// (in-memory for the life of the Editor process, not written to disk). This is
    /// acceptable for a developer-facing editor tool talking to a short-lived token, but the
    /// token should never be logged or otherwise persisted beyond this session.
    /// </para>
    /// </summary>
    public class MinitSession
    {
        private const string EnvironmentPrefKey = "Minit.Environment";
        private const string AccessTokenStateKey = "Minit.AccessToken";
        private const string AccessTokenAcquiredAtStateKey = "Minit.AccessTokenAcquiredAtUtcTicks";
        private const string UserIdStateKey = "Minit.User.Id";
        private const string UserNameStateKey = "Minit.User.Name";
        private const string UserEmailStateKey = "Minit.User.Email";

        // Access tokens are minted with a ~15-minute lifetime. Treat them as expired a minute
        // early so we never hand out a token that expires mid-request.
        private static readonly TimeSpan AccessTokenAssumedLifetime = TimeSpan.FromMinutes(14);

        /// <summary>
        /// The environment (dev/prod) currently selected in the Login &amp; Upload window.
        /// Backed by <see cref="EditorPrefs"/>, so it persists across Editor restarts.
        /// Defaults to <see cref="MinitEnvironment.Dev"/>.
        /// </summary>
        public MinitEnvironment Environment
        {
            get
            {
                int stored = EditorPrefs.GetInt(EnvironmentPrefKey, (int)MinitEnvironment.Dev);
                return Enum.IsDefined(typeof(MinitEnvironment), stored)
                    ? (MinitEnvironment)stored
                    : MinitEnvironment.Dev;
            }
            set => EditorPrefs.SetInt(EnvironmentPrefKey, (int)value);
        }

        /// <summary>The currently stored backend access token, or <c>null</c> if never set.</summary>
        public string AccessToken
        {
            get
            {
                string token = SessionState.GetString(AccessTokenStateKey, string.Empty);
                return string.IsNullOrEmpty(token) ? null : token;
            }
        }

        /// <summary>
        /// True iff a backend access token is stored and is not yet assumed-expired
        /// (see <see cref="AccessTokenAssumedLifetime"/>).
        /// </summary>
        public bool IsLoggedIn
        {
            get
            {
                if (string.IsNullOrEmpty(AccessToken))
                    return false;

                long acquiredAtTicks = SessionState.GetString(AccessTokenAcquiredAtStateKey, string.Empty) is { Length: > 0 } raw
                    ? long.Parse(raw)
                    : 0L;

                if (acquiredAtTicks == 0L)
                    return false;

                var acquiredAt = new DateTime(acquiredAtTicks, DateTimeKind.Utc);
                return DateTime.UtcNow - acquiredAt < AccessTokenAssumedLifetime;
            }
        }

        /// <summary>The identity of the currently logged-in user, or <c>null</c> if not logged in.</summary>
        public MinitUser User
        {
            get
            {
                string id = SessionState.GetString(UserIdStateKey, string.Empty);
                if (string.IsNullOrEmpty(id))
                    return null;

                return new MinitUser
                {
                    id = id,
                    name = SessionState.GetString(UserNameStateKey, string.Empty),
                    email = SessionState.GetString(UserEmailStateKey, string.Empty) is { Length: > 0 } email ? email : null
                };
            }
        }

        /// <summary>
        /// Stores a freshly-acquired access token + identity, stamping the acquisition time
        /// used by <see cref="IsLoggedIn"/> to determine expiry.
        /// </summary>
        public void SetSession(string accessToken, MinitUser user)
        {
            if (string.IsNullOrEmpty(accessToken))
                throw new ArgumentException("accessToken must not be null or empty.", nameof(accessToken));
            if (user == null)
                throw new ArgumentNullException(nameof(user));

            SessionState.SetString(AccessTokenStateKey, accessToken);
            SessionState.SetString(AccessTokenAcquiredAtStateKey, DateTime.UtcNow.Ticks.ToString());
            SessionState.SetString(UserIdStateKey, user.id ?? string.Empty);
            SessionState.SetString(UserNameStateKey, user.name ?? string.Empty);
            SessionState.SetString(UserEmailStateKey, user.email ?? string.Empty);
        }

        /// <summary>Logs out — clears the stored access token and identity.</summary>
        public void Clear()
        {
            SessionState.EraseString(AccessTokenStateKey);
            SessionState.EraseString(AccessTokenAcquiredAtStateKey);
            SessionState.EraseString(UserIdStateKey);
            SessionState.EraseString(UserNameStateKey);
            SessionState.EraseString(UserEmailStateKey);
        }
    }
}
