using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Server-side: gives each connected client a free slot and spawns their ball at that slot's spawn point.
/// Lives in the game scene as a scene-placed NetworkObject, so it only runs once the scene is network-loaded.
/// </summary>
public class PlayerSpawner : NetworkBehaviour
{
    public static PlayerSpawner Instance { get; private set; }

    [SerializeField] private NetworkObject playerPrefab;
    [SerializeField] private Transform[] spawnPoints;

    private readonly Dictionary<ulong, int> slotByClient = new Dictionary<ulong, int>();

    public int Capacity => spawnPoints.Length;

    public Transform GetSpawnPoint(int slot) => spawnPoints[Mathf.Clamp(slot, 0, spawnPoints.Length - 1)];

    private void Awake()
    {
        Instance = this;
    }

    public override void OnDestroy()
    {
        if (Instance == this) Instance = null;
        base.OnDestroy();
    }

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;

        NetworkManager.OnClientConnectedCallback += HandleClientConnected;
        NetworkManager.OnClientDisconnectCallback += HandleClientDisconnected;

        // Everyone already connected when the scene loaded (always includes the host).
        foreach (var clientId in NetworkManager.ConnectedClientsIds)
            SpawnPlayer(clientId);
    }

    public override void OnNetworkDespawn()
    {
        if (!IsServer || NetworkManager == null) return;

        NetworkManager.OnClientConnectedCallback -= HandleClientConnected;
        NetworkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
        slotByClient.Clear();
    }

    private void HandleClientConnected(ulong clientId) => SpawnPlayer(clientId);

    // NGO destroys the player object itself; we only need to free the slot.
    private void HandleClientDisconnected(ulong clientId) => slotByClient.Remove(clientId);

    private void SpawnPlayer(ulong clientId)
    {
        if (slotByClient.ContainsKey(clientId)) return;

        int slot = FindFreeSlot();
        if (slot < 0)
        {
            // The session's max-player cap should prevent this; kick as a fallback.
            Debug.LogWarning($"[Spawner] No free slot for client {clientId}, disconnecting.");
            NetworkManager.DisconnectClient(clientId, "Session is full.");
            return;
        }

        slotByClient[clientId] = slot;
        var point = spawnPoints[slot];

        var instance = Instantiate(playerPrefab, point.position, point.rotation);
        instance.SpawnAsPlayerObject(clientId, destroyWithScene: true);
        // Written right after spawn; it goes out in the same network tick and PlayerSlot reacts via OnValueChanged.
        instance.GetComponent<PlayerSlot>().Slot.Value = slot;
    }

    private int FindFreeSlot()
    {
        for (int i = 0; i < spawnPoints.Length; i++)
        {
            if (!slotByClient.ContainsValue(i))
                return i;
        }
        return -1;
    }
}
