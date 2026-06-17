using UnityEngine;

#if FUSION_WEAVER && FUSION2
using Fusion;
#endif

[DisallowMultipleComponent]
public class NetworkedVirtualAmirState :
#if FUSION_WEAVER && FUSION2
    NetworkBehaviour
#else
    MonoBehaviour
#endif
{
    public VirtualAmirStateDisplay display;
    public bool simulateWhenAuthoritative = true;
    public float simulatedCycleSeconds = 8f;

#if FUSION_WEAVER && FUSION2
    [Networked] public int StatusValue { get; set; }
    [Networked] public float TaskProgress { get; set; }
    [Networked] public float RiskLevel { get; set; }
    [Networked] public Vector3 EndEffectorTarget { get; set; }

    public override void Spawned()
    {
        ResolveDisplay();
        if (HasStateAuthority)
        {
            StatusValue = (int)VirtualAmirStatus.Idle;
            TaskProgress = 0.15f;
            RiskLevel = 0.05f;
            EndEffectorTarget = transform.TransformPoint(new Vector3(0.35f, 0.55f, 0.45f));
        }
    }

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority || !simulateWhenAuthoritative)
        {
            return;
        }

        float t = Mathf.Repeat((float)Runner.SimulationTime, simulatedCycleSeconds) / simulatedCycleSeconds;
        TaskProgress = t;
        RiskLevel = Mathf.Clamp01(Mathf.Abs(Mathf.Sin(t * Mathf.PI * 2f)) * 0.55f);
        StatusValue = t < 0.25f ? (int)VirtualAmirStatus.Planning :
            t < 0.75f ? (int)VirtualAmirStatus.Moving :
            RiskLevel > 0.45f ? (int)VirtualAmirStatus.Blocked :
            (int)VirtualAmirStatus.Idle;
        EndEffectorTarget = transform.TransformPoint(new Vector3(
            Mathf.Lerp(0.2f, 0.55f, t),
            0.45f + Mathf.Sin(t * Mathf.PI) * 0.18f,
            0.4f));
    }

    public override void Render()
    {
        ResolveDisplay();
        if (display != null)
        {
            display.ApplyState((VirtualAmirStatus)StatusValue, TaskProgress, RiskLevel, EndEffectorTarget);
        }
    }
#else
    private void Update()
    {
        ResolveDisplay();
        if (display == null || !simulateWhenAuthoritative)
        {
            return;
        }

        float t = Mathf.Repeat(Time.time, simulatedCycleSeconds) / simulatedCycleSeconds;
        VirtualAmirStatus state = t < 0.25f ? VirtualAmirStatus.Planning :
            t < 0.75f ? VirtualAmirStatus.Moving :
            VirtualAmirStatus.Idle;
        float risk = Mathf.Clamp01(Mathf.Abs(Mathf.Sin(t * Mathf.PI * 2f)) * 0.55f);
        Vector3 target = transform.TransformPoint(new Vector3(
            Mathf.Lerp(0.2f, 0.55f, t),
            0.45f + Mathf.Sin(t * Mathf.PI) * 0.18f,
            0.4f));

        display.ApplyState(state, t, risk, target);
    }
#endif

    private void ResolveDisplay()
    {
        if (display == null)
        {
            display = GetComponent<VirtualAmirStateDisplay>();
        }
    }
}
