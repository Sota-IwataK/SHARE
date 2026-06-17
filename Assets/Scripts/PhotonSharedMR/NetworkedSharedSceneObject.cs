using UnityEngine;

#if FUSION_WEAVER && FUSION2
using Fusion;
#endif

[DisallowMultipleComponent]
public class NetworkedSharedSceneObject :
#if FUSION_WEAVER && FUSION2
    NetworkBehaviour
#else
    MonoBehaviour
#endif
{
    [Header("Shared Object")]
    public SharedNetworkObjectKind objectKind = SharedNetworkObjectKind.Bottle;
    public bool allowStateAuthorityGrab = true;

    [Header("PC Editor Test Grab")]
    public bool allowMouseEditorGrab = true;
    public float mouseDragDistanceMeters = 0.7f;
    public float mouseFollowSpeed = 18f;

    private bool localDragActive;
    private bool pendingAuthorityRequest;
    private Vector3 localDragTarget;

#if FUSION_WEAVER && FUSION2
    [Networked] public Vector3 NetworkPosition { get; set; }
    [Networked] public Quaternion NetworkRotation { get; set; }
    [Networked] public NetworkBool IsGrabbed { get; set; }
    [Networked] public PlayerRef LockOwner { get; set; }

    public bool IsLockedByOther
    {
        get
        {
            if (!IsGrabbed)
            {
                return false;
            }

            return Runner != null && LockOwner != Runner.LocalPlayer;
        }
    }

    public override void Spawned()
    {
        if (HasStateAuthority)
        {
            NetworkPosition = transform.position;
            NetworkRotation = transform.rotation;
        }

        Debug.Log("[NetworkedSharedSceneObject] Spawned " + name
            + " kind=" + objectKind
            + " hasStateAuthority=" + HasStateAuthority
            + " localPlayer=" + (Runner != null ? Runner.LocalPlayer.ToString() : "none"));
    }

    public override void FixedUpdateNetwork()
    {
        if (pendingAuthorityRequest && HasStateAuthority)
        {
            BeginOwnedGrab();
        }

        if (HasStateAuthority)
        {
            if (localDragActive)
            {
                transform.position = Vector3.Lerp(transform.position, localDragTarget, Runner.DeltaTime * mouseFollowSpeed);
            }

            NetworkPosition = transform.position;
            NetworkRotation = transform.rotation;
        }
    }

    public override void Render()
    {
        if (!HasStateAuthority)
        {
            transform.SetPositionAndRotation(NetworkPosition, NetworkRotation);
        }
    }

    public bool TryBeginLocalGrab()
    {
        if (!allowStateAuthorityGrab)
        {
            return false;
        }

        if (Object == null || Runner == null || !Runner.IsRunning)
        {
            Debug.LogWarning("[NetworkedSharedSceneObject] Object is not spawned by Fusion yet: " + name);
            return false;
        }

        localDragTarget = transform.position;

        if (IsLockedByOther)
        {
            Debug.Log("[NetworkedSharedSceneObject] Grab rejected because another user owns " + name
                + " owner=" + LockOwner
                + " localPlayer=" + Runner.LocalPlayer);
            return false;
        }

        if (!HasStateAuthority)
        {
            pendingAuthorityRequest = true;
            Object.RequestStateAuthority();
            Debug.Log("[NetworkedSharedSceneObject] StateAuthority requested for " + name
                + " localPlayer=" + Runner.LocalPlayer);
            return true;
        }

        BeginOwnedGrab();
        return true;
    }

    public void EndLocalGrab()
    {
        localDragActive = false;
        pendingAuthorityRequest = false;

        if (HasStateAuthority && Runner != null && LockOwner == Runner.LocalPlayer)
        {
            Debug.Log("[NetworkedSharedSceneObject] Grab lock released " + name
                + " owner=" + LockOwner);
            IsGrabbed = false;
            LockOwner = PlayerRef.None;
        }
    }

    private void BeginOwnedGrab()
    {
        pendingAuthorityRequest = false;
        localDragActive = true;
        IsGrabbed = true;
        LockOwner = Runner.LocalPlayer;
        Debug.Log("[NetworkedSharedSceneObject] Grab lock acquired " + name
            + " owner=" + LockOwner);
    }
#else
    public bool IsLockedByOther => false;

    public bool TryBeginLocalGrab()
    {
        localDragActive = true;
        return true;
    }

    public void EndLocalGrab()
    {
        localDragActive = false;
    }
#endif

#if UNITY_EDITOR || UNITY_STANDALONE
    private void OnMouseDown()
    {
        if (!allowMouseEditorGrab)
        {
            return;
        }

        if (TryBeginLocalGrab())
        {
            UpdateMouseDragTarget();
        }
    }

    private void OnMouseDrag()
    {
        if (!allowMouseEditorGrab || !localDragActive)
        {
            return;
        }

        UpdateMouseDragTarget();
#if !FUSION_WEAVER || !FUSION2
        transform.position = Vector3.Lerp(transform.position, localDragTarget, Time.deltaTime * mouseFollowSpeed);
#endif
    }

    private void OnMouseUp()
    {
        EndLocalGrab();
    }

    private void UpdateMouseDragTarget()
    {
        Camera camera = Camera.main;
        if (camera == null)
        {
            return;
        }

        Ray ray = camera.ScreenPointToRay(Input.mousePosition);
        localDragTarget = ray.origin + ray.direction.normalized * mouseDragDistanceMeters;
    }
#endif
}
