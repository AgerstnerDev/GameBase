using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// TEMPORARY: direct local Host/Join buttons for testing before the session lobby UI exists.
/// Connects over the transport's default address (127.0.0.1:7777). Remove once the lobby UI lands.
/// </summary>
public class DevConnectPanel : MonoBehaviour
{
    [SerializeField] private string gameSceneName = "BallScene";

    private void OnGUI()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || nm.IsListening) return;

        GUILayout.BeginArea(new Rect(20, 20, 220, 120), GUI.skin.box);
        GUILayout.Label("Dev: local test (no sessions)");

        if (GUILayout.Button("Host (local)", GUILayout.Height(30)) && nm.StartHost())
            nm.SceneManager.LoadScene(gameSceneName, LoadSceneMode.Single);

        if (GUILayout.Button("Join (127.0.0.1)", GUILayout.Height(30)))
            nm.StartClient();

        GUILayout.EndArea();
    }
}
