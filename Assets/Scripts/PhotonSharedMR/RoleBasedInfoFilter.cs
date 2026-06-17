using System;
using UnityEngine;

[DisallowMultipleComponent]
public class RoleBasedInfoFilter : MonoBehaviour
{
    [Header("Role Source")]
    public NetworkUserAvatar localAvatar;
    public bool useManualRoleOverride;
    public SharedUserRole manualRole = SharedUserRole.ManipulatorOperator;
    public bool allowKeyboardRoleSwitchingInEditor = true;

    [Header("ManipulatorOperator View")]
    public GameObject[] manipulatorBottleObjects;
    public GameObject[] manipulatorEndEffectorObjects;
    public GameObject[] manipulatorGraspAreaObjects;
    public GameObject[] manipulatorPredictedTrajectoryObjects;

    [Header("Scout View")]
    public GameObject[] scoutLayoutObjects;
    public GameObject[] scoutObjectCandidateObjects;
    public GameObject[] scoutOtherUserObjects;

    [Header("Supervisor View")]
    public GameObject[] supervisorAllUserStateObjects;
    public GameObject[] supervisorTaskProgressObjects;
    public GameObject[] supervisorRiskObjects;

    [Header("HMD Overhead Cursors")]
    public bool manipulatorShowsHmdOverheadCursors = true;
    public bool scoutShowsHmdOverheadCursors = true;
    public bool supervisorShowsHmdOverheadCursors = true;
    public GameObject[] hmdOverheadCursorObjects;

    [Header("Always Visible")]
    public GameObject[] alwaysVisibleObjects;

    public SharedUserRole ActiveRole { get; private set; }
    public bool CurrentHmdOverheadCursorsVisible { get; private set; } = true;

    private void OnEnable()
    {
        ApplyNow();
    }

    private void Update()
    {
        if (allowKeyboardRoleSwitchingInEditor)
        {
            HandleKeyboardRoleSwitch();
        }

        SharedUserRole role = ResolveRole();
        if (role != ActiveRole)
        {
            ApplyRole(role);
        }
    }

    public void SetManipulatorOperatorRole()
    {
        SetManualRole(SharedUserRole.ManipulatorOperator);
    }

    public void SetScoutRole()
    {
        SetManualRole(SharedUserRole.Scout);
    }

    public void SetSupervisorRole()
    {
        SetManualRole(SharedUserRole.Supervisor);
    }

    public void SetManualRole(SharedUserRole role)
    {
        manualRole = role;
        useManualRoleOverride = true;
        if (NetworkUserAvatar.Local != null)
        {
            NetworkUserAvatar.Local.SetRole(role);
        }

        ApplyRole(role);
    }

    public void ApplyNow()
    {
        ApplyRole(ResolveRole());
    }

    private SharedUserRole ResolveRole()
    {
        if (useManualRoleOverride)
        {
            return manualRole;
        }

        if (localAvatar == null)
        {
            localAvatar = NetworkUserAvatar.Local;
        }

        return localAvatar != null ? localAvatar.CurrentRole : manualRole;
    }

    private void ApplyRole(SharedUserRole role)
    {
        ActiveRole = role;

        SetActive(alwaysVisibleObjects, true);

        bool isManipulator = role == SharedUserRole.ManipulatorOperator;
        bool isScout = role == SharedUserRole.Scout;
        bool isSupervisor = role == SharedUserRole.Supervisor;

        SetActive(manipulatorBottleObjects, isManipulator);
        SetActive(manipulatorEndEffectorObjects, isManipulator);
        SetActive(manipulatorGraspAreaObjects, isManipulator);
        SetActive(manipulatorPredictedTrajectoryObjects, isManipulator);

        SetActive(scoutLayoutObjects, isScout);
        SetActive(scoutObjectCandidateObjects, isScout);
        SetActive(scoutOtherUserObjects, isScout);

        SetActive(supervisorAllUserStateObjects, isSupervisor);
        SetActive(supervisorTaskProgressObjects, isSupervisor);
        SetActive(supervisorRiskObjects, isSupervisor);

        CurrentHmdOverheadCursorsVisible =
            (isManipulator && manipulatorShowsHmdOverheadCursors)
            || (isScout && scoutShowsHmdOverheadCursors)
            || (isSupervisor && supervisorShowsHmdOverheadCursors);
        ApplyHmdOverheadCursorVisibility(CurrentHmdOverheadCursorsVisible);
    }

    private static void SetActive(GameObject[] targets, bool active)
    {
        if (targets == null)
        {
            return;
        }

        for (int i = 0; i < targets.Length; i++)
        {
            if (targets[i] != null)
            {
                targets[i].SetActive(active);
            }
        }
    }

    private void ApplyHmdOverheadCursorVisibility(bool visible)
    {
        SetActive(hmdOverheadCursorObjects, visible);

        HmdOverheadCursor[] cursors = FindObjectsOfType<HmdOverheadCursor>(true);
        for (int i = 0; i < cursors.Length; i++)
        {
            if (cursors[i] != null)
            {
                cursors[i].SetRoleFilterVisible(visible);
            }
        }
    }

    private void HandleKeyboardRoleSwitch()
    {
        if (Input.GetKeyDown(KeyCode.Alpha1))
        {
            SetManualRole(SharedUserRole.ManipulatorOperator);
        }
        else if (Input.GetKeyDown(KeyCode.Alpha2))
        {
            SetManualRole(SharedUserRole.Scout);
        }
        else if (Input.GetKeyDown(KeyCode.Alpha3))
        {
            SetManualRole(SharedUserRole.Supervisor);
        }
    }
}
