using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnitySceneManager = UnityEngine.SceneManagement.SceneManager;

/// <summary>
/// Wraps the Multiplayer Services Sessions API: host a named, optionally password-protected session
/// over Relay, list open sessions, join one, and leave. Starting/stopping Netcode is handled by the
/// Sessions network handler; this class loads the game scene once hosting starts.
/// </summary>
public class SessionManager : MonoBehaviour
{
    public const int MinPlayers = 2;
    public const int MaxPlayersLimit = 10;
    public const int MaxNameLength = 32;
    public const int MinPasswordLength = 8;   // Lobby service limits
    public const int MaxPasswordLength = 64;

    public static SessionManager Instance { get; private set; }

    [SerializeField] private string gameSceneName = "BallScene";
    [SerializeField] private string menuSceneName = "MainMenu";

    public ISession CurrentSession { get; private set; }
    public bool IsBusy { get; private set; }

    /// <summary>Raised after successfully hosting or joining.</summary>
    public event Action<ISession> SessionJoined;

    /// <summary>Raised after leaving, being removed, or the session ending. Carries a reason for the UI (may be null).</summary>
    public event Action<string> SessionLeft;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    /// <summary>Returns a user-facing error, or null if the settings are valid.</summary>
    public static string ValidateHostSettings(string sessionName, int maxPlayers, string password)
    {
        if (string.IsNullOrWhiteSpace(sessionName))
            return "Enter a session name.";
        if (sessionName.Trim().Length > MaxNameLength)
            return $"Session name must be {MaxNameLength} characters or fewer.";
        if (maxPlayers < MinPlayers || maxPlayers > MaxPlayersLimit)
            return $"Max players must be between {MinPlayers} and {MaxPlayersLimit}.";
        if (!string.IsNullOrEmpty(password) && (password.Length < MinPasswordLength || password.Length > MaxPasswordLength))
            return $"Password must be {MinPasswordLength}-{MaxPasswordLength} characters, or left empty for an open session.";
        return null;
    }

    /// <summary>Creates a session (Relay), starts Netcode as host and loads the game scene.</summary>
    public async Task HostAsync(string sessionName, int maxPlayers, string password)
    {
        var error = ValidateHostSettings(sessionName, maxPlayers, password);
        if (error != null)
            throw new ArgumentException(error);

        await RunExclusive(async () =>
        {
            await ServicesBootstrap.WhenReady();

            var options = new SessionOptions
            {
                Name = sessionName.Trim(),
                MaxPlayers = maxPlayers,
                Password = string.IsNullOrEmpty(password) ? null : password,
                IsPrivate = false,
            }.WithRelayNetwork();

            var session = await MultiplayerService.Instance.CreateSessionAsync(options);
            Track(session);

            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsServer)
            {
                await LeaveInternalAsync(null);
                throw new InvalidOperationException("The session was created but the host failed to start networking.");
            }

            nm.SceneManager.LoadScene(gameSceneName, UnityEngine.SceneManagement.LoadSceneMode.Single);
            Debug.Log($"[Sessions] Hosting '{session.Name}' ({session.Id}), max {session.MaxPlayers}, password: {session.HasPassword}");
        });
    }

    /// <summary>Lists public sessions. Full and locked sessions are included so the UI can show them disabled.</summary>
    public async Task<IList<ISessionInfo>> QueryAsync()
    {
        await ServicesBootstrap.WhenReady();
        var results = await MultiplayerService.Instance.QuerySessionsAsync(new QuerySessionsOptions { Count = 50 });
        return results.Sessions;
    }

    /// <summary>Joins by id. Netcode starts as client and the host's scene is synced automatically.</summary>
    public async Task JoinAsync(string sessionId, string password)
    {
        await RunExclusive(async () =>
        {
            await ServicesBootstrap.WhenReady();

            var options = new JoinSessionOptions
            {
                Password = string.IsNullOrEmpty(password) ? null : password,
            };

            var session = await MultiplayerService.Instance.JoinSessionByIdAsync(sessionId, options);
            Track(session);
            Debug.Log($"[Sessions] Joined '{session.Name}' ({session.Id})");
        });
    }

    /// <summary>Leaves the current session (the host's leaving ends it for everyone) and returns to the menu.</summary>
    public Task LeaveAsync() => LeaveInternalAsync(null);

    /// <summary>Turns service exceptions into short messages for the UI.</summary>
    public static string Describe(Exception e)
    {
        if (e is ArgumentException)
            return e.Message;

        if (e is SessionException se)
        {
            switch (se.Error)
            {
                case SessionError.Forbidden:
                case SessionError.NotAuthorized:
                    return "Could not join: wrong password, or the session is full or locked.";
                case SessionError.SessionNotFound:
                case SessionError.SessionDeleted:
                    return "That session no longer exists.";
                case SessionError.RateLimitExceeded:
                    return "Too many requests. Wait a few seconds and try again.";
                case SessionError.InvalidCreateSessionOptions:
                case SessionError.InvalidParameter:
                    return "Invalid session settings: " + se.Message;
            }
        }

        return "Something went wrong: " + e.Message;
    }

    private async Task RunExclusive(Func<Task> action)
    {
        if (IsBusy)
            throw new InvalidOperationException("Another session operation is already in progress.");
        if (CurrentSession != null)
            throw new InvalidOperationException("Already in a session. Leave it first.");

        IsBusy = true;
        try
        {
            await action();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Track(ISession session)
    {
        CurrentSession = session;
        session.RemovedFromSession += HandleRemoved;
        session.Deleted += HandleDeleted;
        SessionJoined?.Invoke(session);
    }

    private void Untrack()
    {
        if (CurrentSession == null) return;
        CurrentSession.RemovedFromSession -= HandleRemoved;
        CurrentSession.Deleted -= HandleDeleted;
        CurrentSession = null;
    }

    private void HandleRemoved() => _ = LeaveInternalAsync("You were removed from the session.");
    private void HandleDeleted() => _ = LeaveInternalAsync("The host ended the session.");

    private async Task LeaveInternalAsync(string reason)
    {
        var session = CurrentSession;
        if (session == null) return;
        Untrack();

        try
        {
            await session.LeaveAsync();
        }
        catch (Exception e)
        {
            // Already gone (e.g. host deleted it); we're leaving anyway.
            Debug.LogWarning($"[Sessions] Leave failed: {e.Message}");
        }

        // The NetworkManager lives in DontDestroyOnLoad; destroy it so MainMenu's fresh copy is the only one.
        var nm = NetworkManager.Singleton;
        if (nm != null)
        {
            if (nm.IsListening)
                nm.Shutdown();
            Destroy(nm.gameObject);
        }

        if (UnitySceneManager.GetActiveScene().name != menuSceneName)
            UnitySceneManager.LoadScene(menuSceneName);

        SessionLeft?.Invoke(reason);
    }
}
