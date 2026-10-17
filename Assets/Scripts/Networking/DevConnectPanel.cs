using System;
using System.Collections.Generic;
using Unity.Netcode;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// TEMPORARY: IMGUI test panel until the real lobby UI (step 7) exists.
/// Top: online sessions through SessionManager. Bottom: direct local Host/Join over 127.0.0.1.
/// </summary>
public class DevConnectPanel : MonoBehaviour
{
    [SerializeField] private string gameSceneName = "BallScene";

    private string sessionName = "Ball Game";
    private string password = "";
    private int maxPlayers = 4;
    private string joinPassword = "";
    private string status = "";
    private IList<ISessionInfo> sessions = new List<ISessionInfo>();
    private Vector2 scroll;

    private static DevConnectPanel instance;

    // Persist so the Leave button stays available in the game scene.
    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        if (SessionManager.Instance != null)
            SessionManager.Instance.SessionLeft += HandleSessionLeft;
    }

    private void OnDestroy()
    {
        if (instance == this && SessionManager.Instance != null)
            SessionManager.Instance.SessionLeft -= HandleSessionLeft;
    }

    private void HandleSessionLeft(string reason) => status = reason ?? "";

    private void OnGUI()
    {
        var sm = SessionManager.Instance;
        var nm = NetworkManager.Singleton;

        if (sm != null && sm.CurrentSession != null)
        {
            DrawInSession(sm);
            return;
        }
        if (nm != null && nm.IsListening) return;

        GUILayout.BeginArea(new Rect(20, 20, 340, 560), GUI.skin.box);
        GUI.enabled = sm != null && !sm.IsBusy && ServicesBootstrap.IsReady;

        GUILayout.Label(ServicesBootstrap.IsReady ? "Dev: online sessions" : "Dev: signing in...");
        GUILayout.Label("Session name");
        sessionName = GUILayout.TextField(sessionName, SessionManager.MaxNameLength);
        GUILayout.Label($"Password (empty = open, {SessionManager.MinPasswordLength}+ chars)");
        password = GUILayout.PasswordField(password, '*', SessionManager.MaxPasswordLength);
        GUILayout.Label($"Max players: {maxPlayers}");
        maxPlayers = Mathf.RoundToInt(GUILayout.HorizontalSlider(maxPlayers, SessionManager.MinPlayers, SessionManager.MaxPlayersLimit));
        GUILayout.Space(6);
        if (GUILayout.Button("Host session", GUILayout.Height(28)))
            Run(sm.HostAsync(sessionName, maxPlayers, password), "Creating session...");

        GUILayout.Space(8);
        GUILayout.BeginHorizontal();
        GUILayout.Label("Join password");
        joinPassword = GUILayout.PasswordField(joinPassword, '*', SessionManager.MaxPasswordLength, GUILayout.Width(160));
        GUILayout.EndHorizontal();
        if (GUILayout.Button("Refresh list"))
            Refresh(sm);

        scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(150));
        foreach (var info in sessions)
        {
            GUILayout.BeginHorizontal();
            int players = info.MaxPlayers - info.AvailableSlots;
            GUILayout.Label($"{(info.HasPassword ? "[locked] " : "")}{info.Name}  {players}/{info.MaxPlayers}");
            bool wasEnabled = GUI.enabled;
            GUI.enabled = wasEnabled && info.AvailableSlots > 0 && !info.IsLocked;
            if (GUILayout.Button("Join", GUILayout.Width(50)))
                Run(sm.JoinAsync(info.Id, joinPassword), $"Joining {info.Name}...");
            GUI.enabled = wasEnabled;
            GUILayout.EndHorizontal();
        }
        if (sessions.Count == 0) GUILayout.Label("(no sessions; press Refresh)");
        GUILayout.EndScrollView();

        GUILayout.Space(8);
        GUILayout.Label("Local only (same PC, no services)");
        GUI.enabled = nm != null;
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Host (local)") && nm.StartHost())
            nm.SceneManager.LoadScene(gameSceneName, LoadSceneMode.Single);
        if (GUILayout.Button("Join (127.0.0.1)"))
            nm.StartClient();
        GUILayout.EndHorizontal();

        GUI.enabled = true;
        if (!string.IsNullOrEmpty(status)) GUILayout.Label(status);
        GUILayout.EndArea();
    }

    private void DrawInSession(SessionManager sm)
    {
        var s = sm.CurrentSession;
        GUILayout.BeginArea(new Rect(20, 20, 260, 80), GUI.skin.box);
        GUILayout.Label($"{s.Name}  {s.PlayerCount}/{s.MaxPlayers}{(s.IsHost ? "  (host)" : "")}");
        if (GUILayout.Button("Leave"))
            Run(sm.LeaveAsync(), "Leaving...");
        GUILayout.EndArea();
    }

    private async void Refresh(SessionManager sm)
    {
        try
        {
            status = "Refreshing...";
            sessions = await sm.QueryAsync();
            status = $"{sessions.Count} session(s) found.";
        }
        catch (Exception e)
        {
            status = SessionManager.Describe(e);
        }
    }

    private async void Run(System.Threading.Tasks.Task task, string busyText)
    {
        status = busyText;
        try
        {
            await task;
            status = "";
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            status = SessionManager.Describe(e);
        }
    }
}
