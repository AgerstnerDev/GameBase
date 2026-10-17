using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Host-authoritative ball movement. The owning client reads input and sends it to the server;
/// only the server applies physics forces. NetworkTransform + NetworkRigidbody replicate the result.
/// </summary>
[RequireComponent(typeof(Rigidbody), typeof(NetworkTransform))]
public class NetworkBallController : NetworkBehaviour
{
    [SerializeField] private float moveForce = 10f;
    [SerializeField] private float jumpImpulse = 5f;
    [SerializeField] private float fallResetHeight = -10f;

    private Rigidbody rb;
    private NetworkTransform networkTransform;

    // Owner side
    private Vector2 lastSentInput;

    // Server side
    private Vector2 serverMoveInput;
    private bool serverJumpQueued;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        networkTransform = GetComponent<NetworkTransform>();
    }

    public override void OnNetworkSpawn()
    {
        if (IsOwner && Camera.main != null && Camera.main.TryGetComponent(out CameraFollow follow))
            follow.SetTarget(transform);
    }

    private void Update()
    {
        if (!IsSpawned || !IsOwner) return;

        var keyboard = Keyboard.current;
        if (keyboard == null) return;

        float x = 0f, y = 0f;
        if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) x -= 1f;
        if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) x += 1f;
        if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) y -= 1f;
        if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) y += 1f;
        var input = new Vector2(x, y);

        // Only send when input changes; the server keeps applying the last value it received.
        if (input != lastSentInput)
        {
            lastSentInput = input;
            SubmitMoveRpc(input);
        }

        if (keyboard.spaceKey.wasPressedThisFrame)
            JumpRpc();
    }

    [Rpc(SendTo.Server)]
    private void SubmitMoveRpc(Vector2 input)
    {
        // Clamp on the server so clients can't send oversized input.
        serverMoveInput = Vector2.ClampMagnitude(input, 1f);
    }

    [Rpc(SendTo.Server)]
    private void JumpRpc()
    {
        serverJumpQueued = true;
    }

    private void FixedUpdate()
    {
        if (!IsSpawned || !IsServer) return;

        rb.AddForce(new Vector3(serverMoveInput.x, 0f, serverMoveInput.y) * moveForce);

        if (serverJumpQueued)
        {
            if (IsGrounded())
                rb.AddForce(Vector3.up * jumpImpulse, ForceMode.Impulse);
            serverJumpQueued = false;
        }

        if (transform.position.y < fallResetHeight)
            ResetToSpawn();
    }

    private bool IsGrounded()
    {
        return Physics.Raycast(transform.position, Vector3.down, transform.localScale.y * 0.5f + 0.1f);
    }

    private void ResetToSpawn()
    {
        var spawner = PlayerSpawner.Instance;
        if (spawner == null || !TryGetComponent(out PlayerSlot slot)) return;

        var point = spawner.GetSpawnPoint(slot.Slot.Value);
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        networkTransform.Teleport(point.position, point.rotation, transform.localScale);
    }
}
