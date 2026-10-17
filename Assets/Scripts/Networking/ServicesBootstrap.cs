using System;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using UnityEngine;

/// <summary>
/// Initializes Unity Gaming Services and signs the player in anonymously.
/// Other systems (sessions, lobby UI) should await <see cref="WhenReady"/> before calling any service.
/// </summary>
public class ServicesBootstrap : MonoBehaviour
{
    public static ServicesBootstrap Instance { get; private set; }

    public static bool IsReady => Instance != null && Instance.readyTask != null && Instance.readyTask.IsCompletedSuccessfully;
    public static string PlayerId => AuthenticationService.Instance.PlayerId;

    /// <summary>Raised once sign-in succeeds.</summary>
    public static event Action Ready;

    /// <summary>Raised if initialization or sign-in fails, with a user-facing message.</summary>
    public static event Action<string> Failed;

    private Task readyTask;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        readyTask = InitializeAsync();
    }

    /// <summary>Completes when services are initialized and the player is signed in.</summary>
    public static Task WhenReady()
    {
        if (Instance == null)
            throw new InvalidOperationException("ServicesBootstrap is not in the scene.");
        return Instance.readyTask;
    }

    private async Task InitializeAsync()
    {
        try
        {
            var options = new InitializationOptions();
            var profile = ResolveProfile();
            if (!string.IsNullOrEmpty(profile))
                options.SetProfile(profile);

            await UnityServices.InitializeAsync(options);

            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();

            Debug.Log($"[Services] Signed in as {AuthenticationService.Instance.PlayerId} (profile '{profile ?? "default"}')");
            Ready?.Invoke();
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            Failed?.Invoke("Could not connect to online services. Check your internet connection and that the project is linked in Project Settings > Services.");
            throw;
        }
    }

    /// <summary>
    /// Each local player needs its own auth profile, otherwise multiple instances on one machine
    /// share an anonymous identity and can't join each other's sessions.
    /// Priority: "-profile name" command-line arg, then a per-project-folder name in the editor
    /// (Multiplayer Play Mode clones each run from their own folder), else the default profile.
    /// </summary>
    private static string ResolveProfile()
    {
        var args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "-profile")
                return Sanitize(args[i + 1]);
        }

#if UNITY_EDITOR
        return "editor" + (uint)Application.dataPath.GetHashCode();
#else
        return null;
#endif
    }

    // Profile names allow only ASCII letters, digits, '-' and '_', 1-30 characters.
    private static string Sanitize(string value)
    {
        var chars = value.ToCharArray();
        int len = 0;
        foreach (var c in chars)
        {
            if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-' || c == '_')
                chars[len++] = c;
        }
        return len == 0 ? null : new string(chars, 0, Math.Min(len, 30));
    }
}
