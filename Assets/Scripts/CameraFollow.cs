using UnityEngine;

/// <summary>
/// Smoothly follows a target at a fixed offset. The local player's ball assigns itself on spawn.
/// </summary>
public class CameraFollow : MonoBehaviour
{
    [SerializeField] private Vector3 offset = new Vector3(0f, 12f, -14f);
    [SerializeField] private float smoothTime = 0.15f;

    private Transform target;
    private Vector3 velocity;

    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
        if (target != null)
            transform.position = target.position + offset;
    }

    private void LateUpdate()
    {
        if (target == null) return;

        transform.position = Vector3.SmoothDamp(transform.position, target.position + offset, ref velocity, smoothTime);
        transform.LookAt(target.position);
    }
}
