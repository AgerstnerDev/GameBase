using Unity.Netcode;
using UnityEngine;

/// <summary>
/// The player's slot (0-9), assigned by the server. The slot picks both the spawn point and the ball color.
/// </summary>
public class PlayerSlot : NetworkBehaviour
{
    public static readonly Color[] Palette =
    {
        new Color(0.20f, 0.55f, 1.00f), // blue
        new Color(1.00f, 0.85f, 0.15f), // yellow
        new Color(0.25f, 0.85f, 0.35f), // green
        new Color(1.00f, 0.50f, 0.10f), // orange
        new Color(0.70f, 0.35f, 1.00f), // purple
        new Color(0.15f, 0.90f, 0.90f), // cyan
        new Color(1.00f, 0.45f, 0.75f), // pink
        new Color(0.95f, 0.95f, 0.95f), // white
        new Color(0.15f, 0.15f, 0.18f), // black
        new Color(0.60f, 0.40f, 0.20f), // brown
    };

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    public NetworkVariable<int> Slot = new NetworkVariable<int>(-1);

    [SerializeField] private Renderer targetRenderer;
    private MaterialPropertyBlock propertyBlock;

    private void Awake()
    {
        if (targetRenderer == null)
            targetRenderer = GetComponent<Renderer>();
        propertyBlock = new MaterialPropertyBlock();
    }

    public override void OnNetworkSpawn()
    {
        Slot.OnValueChanged += HandleSlotChanged;
        ApplyColor(Slot.Value);
    }

    public override void OnNetworkDespawn()
    {
        Slot.OnValueChanged -= HandleSlotChanged;
    }

    private void HandleSlotChanged(int previous, int current) => ApplyColor(current);

    private void ApplyColor(int slot)
    {
        if (slot < 0 || targetRenderer == null) return;

        targetRenderer.GetPropertyBlock(propertyBlock);
        propertyBlock.SetColor(BaseColorId, Palette[slot % Palette.Length]);
        targetRenderer.SetPropertyBlock(propertyBlock);
    }
}
