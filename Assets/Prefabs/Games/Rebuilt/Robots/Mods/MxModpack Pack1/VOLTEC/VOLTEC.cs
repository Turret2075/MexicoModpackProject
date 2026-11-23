using Games.Rebuilt.Components;
using Games.Rebuilt.Robots;
using MoSimLib;
using RobotFramework.Components;
using RobotFramework.Controllers.PidSystems;
using RobotFramework.Enums;
using UnityEngine;

public class VOLTEC : RebuiltRobotBase
{
    [Header("Joints")]
    [SerializeField] private GenericJoint intakePivot;
    [SerializeField] private GenericJoint hoodPivot;

    [Header("Rollers")]
    [Tooltip("Single roller.")]
    [SerializeField] private RollerGroup shooterRollers;
    [Tooltip("Single roller. Spins opposite to the shooter (set 'flipped' in the prefab).")]
    [SerializeField] private RollerGroup hoodRollers;
    [Tooltip("Group of rollers.")]
    [SerializeField] private RollerGroup floorRollers;
    [Tooltip("Single roller (transfer). Fuel rests on colliders above it.")]
    [SerializeField] private RollerGroup uptakeRollers;
    [Tooltip("Two rollers spinning in the same direction.")]
    [SerializeField] private RollerGroup intakeRollers;

    [Header("PIDS")]
    [SerializeField] private PidConstants intakePidConstants;
    [SerializeField] private PidConstants hoodPidConstants;
    [SerializeField] private PidConstants hoodShootingPidConstants;

    [Header("Alliance Zone Interpolation (same assets as ReBlitz)")]
    [SerializeField] private RebuiltInterpolation allianceZoneFlywheelInterpolation;
    [SerializeField] private RebuiltInterpolation allianceZoneHoodInterpolation;

    [Header("Pass Zone Interpolation (same assets as ReBlitz)")]
    [SerializeField] private RebuiltInterpolation passZoneFlywheelInterpolation;
    [SerializeField] private RebuiltInterpolation passZoneHoodInterpolation;

    [Header("Intake Values")]
    [Tooltip("Intake pivot X angle when deployed (down). Stow = 0.")]
    [SerializeField] private float intakeDeployedAngle = 136f;
    [Tooltip("Intake pivot X angle when retracted (stowed).")]
    [SerializeField] private float intakeStowAngle = 0f;
    [SerializeField] private float intakeRpm = 2000f;
    [Tooltip("Intake roller speed while shooting.")]
    [SerializeField] private float intakeShootingRpm = 1500f;

    [Header("Shooter Values")]
    [SerializeField] private float uptakeRpm = 2000f;
    [SerializeField] private float floorRpm = 2000f;
    [Tooltip("Maximum auto-aim yaw error in degrees before feed rollers are allowed to run.")]
    [SerializeField] private float aimTolerance = 2f;
    [Tooltip("Maximum hood pivot angle error in degrees before feed rollers are allowed to run.")]
    [SerializeField, Min(0f)] private float hoodAngleTolerance = 0.5f;
    [SerializeField, Min(0f)] private float hoodAngleLockTolerance = 0.1f;
    [Tooltip("How long all shot readiness checks must remain true before feeding.")]
    [SerializeField, Min(0f)] private float feedReadyDebounceTime = 0.06f;
    [Tooltip("Reverse speed on the uptake when feeding stops, to back fuel off the flywheel. 0 disables.")]
    [SerializeField, Min(0f)] private float uptakeBackoffRpm = 15f;
    [Tooltip("Reverse speed on the floor when feeding stops. 0 disables.")]
    [SerializeField, Min(0f)] private float floorBackoffRpm = 40f;
    [Tooltip("Hood only travels from 0 to 40 degrees.")]
    [SerializeField] private Vector2 hoodAngleLimits = new(0f, 40f);
    [SerializeField] private float manualShotRpm = 2000f;
    [SerializeField] private float manualHoodAngle = 25f;
    [SerializeField, Min(0.01f)] private float hoodRpmReduction = 1.5f;


    [Header("Trench Avoidance")]
    [SerializeField] private Transform[] shooterCorners;

    [Header("Shoot On The Move")]
    [SerializeField] private SOTMSettings sotmSettings;

    [Header("Debug")]
    [SerializeField] private float aimErrorDebug;
    [SerializeField] private bool alignedToShootDebug;

    private float targetIntakeAngle;
    private float targetHoodAngle;
    private bool intakeDeployed;
    private bool hoodLocked;
    private float feedReadyTimer;
    private bool feedLatched;
    private bool _wantsShooting;
    private RebuiltAutoAimController aim;
    private Rigidbody robotRigidbody;

    protected override void Init()
    {
        intakePivot.SetPid(intakePidConstants);
        hoodPivot.SetPid(hoodPidConstants);

        allianceZoneFlywheelInterpolation.Initialize(this);
        allianceZoneHoodInterpolation.Initialize(this);
        passZoneFlywheelInterpolation.Initialize(this);
        passZoneHoodInterpolation.Initialize(this);

        targetIntakeAngle = intakeStowAngle;
        targetHoodAngle = 0f;
        intakeDeployed = false;
        _wantsShooting = false;

        if (!TryGetComponent(out aim))
        {
            Debug.LogError($"{nameof(VOLTEC)} requires a {nameof(RebuiltAutoAimController)}.", this);
        }

        robotRigidbody = GetComponent<Rigidbody>();

        Scheduler.AddSubsystems(HandleAutoAim, Shooter, Intake);
        Scheduler.AddProtectedSystem(UpdateJoints, UpdateDriveSpeed);
        Scheduler.AddLateRecursive(UpdatePids);
    }

    private void UpdatePids()
    {
        intakePivot.UpdatePid(intakePidConstants);
        hoodPivot.UpdatePid(WantsToShoot() && HoodAtAngle() ? hoodShootingPidConstants : hoodPidConstants);
    }

    // Special 2: outtake (reverses uptake, floor and intake rollers).
    private void Intake()
    {
        if (IntakeAction)
        {
            intakeDeployed = true;
        }

        if (!intakeDeployed)
        {
            targetIntakeAngle = intakeStowAngle;
            intakeRollers.SetRpm(0f);
        }
        else
        {
            targetIntakeAngle = intakeDeployedAngle;
            intakeRollers.SetRpm(WantsToShoot() ? intakeShootingRpm : (IntakeAction ? intakeRpm : intakeRpm / 5f));
        }

        if (RobotSpecial2)
        {
            intakeRollers.SetRpm(-(intakeRpm/2f));
        }
    }

    private void HandleAutoAim()
    {
        if (aim == null)
        {
            return;
        }

        if (OuttakeAction && InAllianceZone())
        {
            aim.SetDefaultMap(DefaultMap.Hub);
            aim.SetCustomTarget(GetTimeOfFlightTarget(aim.GetCurrentTarget()));
            aim.Aim();
        }
        else if (IsPassShot())
        {
            aim.SetPassMapClosest();
            aim.SetCustomTarget(GetTimeOfFlightTarget(aim.GetCurrentTarget()));
            aim.Aim();
        }
        else
        {
            aim.SetDefaultMap(DefaultMap.Hub);
            aim.SetCustomTarget(GetTimeOfFlightTarget(aim.GetCurrentTarget()));
            allianceZoneFlywheelInterpolation.SetDefaultMap(DefaultMap.Hub);
            allianceZoneHoodInterpolation.SetDefaultMap(DefaultMap.Hub);
            ApplyTimeOfFlightToInterpolation(
                allianceZoneFlywheelInterpolation,
                allianceZoneHoodInterpolation);
        }
    }

    private void Shooter()
    {
        if (!WantsToShoot())
        {
            StopShooter();
            targetHoodAngle = 0f;
            return;
        }

        float shooterSpeed;

        if (ManualShoot)
        {
            shooterSpeed = manualShotRpm;
            targetHoodAngle = manualHoodAngle;
        }
        else
        {
            SetInterpolationTarget();
            shooterSpeed = GetActiveFlywheelInterpolation().Interpolate();
            targetHoodAngle = GetActiveHoodInterpolation().Interpolate();
        }

        targetHoodAngle = Mathf.Clamp(targetHoodAngle, hoodAngleLimits.x, hoodAngleLimits.y);

        shooterRollers.SetRpm(shooterSpeed);
        hoodRollers.SetRpm(GetHoodRpm(shooterSpeed));

        SetFeedRollers(FeedReady());
    }

    // Once the shot is ready (aligned, at speed, hood at angle) for the debounce time, the feed latches on
    // and stays on until the shooter is stopped. Without the latch, any brief dip in flywheel speed or hood
    // angle stops the feed and the fuel stutters.
    private bool FeedReady()
    {
        if (feedLatched)
        {
            return true;
        }

        if (!CanRunFeed())
        {
            feedReadyTimer = 0f;
            return false;
        }

        feedReadyTimer += Time.fixedDeltaTime;
        if (feedReadyTimer >= feedReadyDebounceTime)
        {
            feedLatched = true;
        }

        return feedLatched;
    }

    private void SetFeedRollers(bool feeding)
    {
        if (RobotSpecial2)
        {
            uptakeRollers.SetRpm(-uptakeRpm / 2f);
            floorRollers.SetRpm(-floorRpm);
        }
        else if (feeding)
        {
            uptakeRollers.SetRpm(uptakeRpm);
            floorRollers.SetRpm(floorRpm);
        }
        else
        {
            uptakeRollers.SetRpm(-uptakeBackoffRpm);
            floorRollers.SetRpm(-floorBackoffRpm);
        }
    }

    private void StopShooter()
    {
        feedLatched = false;
        feedReadyTimer = 0f;
        shooterRollers.SetRpm(0f);
        hoodRollers.SetRpm(0f);
        SetFeedRollers(false);
    }

    private bool CanRunFeed()
    {
        return ShooterAligned() && shooterRollers.AtSpeed() && hoodRollers.AtSpeed() && HoodAtAngle();
    }

    public bool ShooterAligned()
    {
        if (ManualShoot)
        {
            alignedToShootDebug = true;
            aimErrorDebug = 0f;
            return true;
        }

        if (aim == null)
        {
            alignedToShootDebug = false;
            aimErrorDebug = 0f;
            return false;
        }

        aimErrorDebug = Mathf.Abs(aim.GetRotationError());
        alignedToShootDebug = aimErrorDebug <= aimTolerance;
        return alignedToShootDebug;
    }

    private void SetInterpolationTarget()
    {
        if (IsPassShot())
        {
            passZoneFlywheelInterpolation.SetPassMapClosest();
            passZoneHoodInterpolation.SetPassMapClosest();
            ApplyTimeOfFlightToInterpolation(
                passZoneFlywheelInterpolation,
                passZoneHoodInterpolation);
            return;
        }

        allianceZoneFlywheelInterpolation.SetDefaultMap(DefaultMap.Hub);
        allianceZoneHoodInterpolation.SetDefaultMap(DefaultMap.Hub);
        ApplyTimeOfFlightToInterpolation(
            allianceZoneFlywheelInterpolation,
            allianceZoneHoodInterpolation);
    }

    private RebuiltInterpolation GetActiveFlywheelInterpolation()
    {
        return IsPassShot() ? passZoneFlywheelInterpolation : allianceZoneFlywheelInterpolation;
    }

    private RebuiltInterpolation GetActiveHoodInterpolation()
    {
        return IsPassShot() ? passZoneHoodInterpolation : allianceZoneHoodInterpolation;
    }

    private Vector3 GetTimeOfFlightTarget(Vector3 target)
    {
        if (robotRigidbody == null)
        {
            return target;
        }

        Vector3 fieldVelocity = robotRigidbody.velocity;
        fieldVelocity.y = 0f;
        return target - fieldVelocity * sotmSettings.timeOfFlight;
    }

    private void ApplyTimeOfFlightToInterpolation(
        RebuiltInterpolation flywheelInterpolation,
        RebuiltInterpolation hoodInterpolation)
    {
        Vector3 compensatedTarget = GetTimeOfFlightTarget(flywheelInterpolation.GetCurrentTarget());
        flywheelInterpolation.SetCustomTarget(compensatedTarget);
        hoodInterpolation.SetCustomTarget(compensatedTarget);
    }

    private void UpdateJoints()
    {
        intakePivot.SetTargetAngle(targetIntakeAngle).withAxis(JointAxis.X).useCustomStartingOffset(0);
        hoodPivot.SetTargetAngle(targetHoodAngle).withAxis(JointAxis.X);

        // Lock/free only on transitions. Calling lockAllAxis every frame re-homes the rigidbody each time.
        bool atLock = HoodAtLockAngle();
        if (atLock && !hoodLocked)
        {
            hoodPivot.lockAllAxis();
            hoodLocked = true;
        }
        else if (!atLock && hoodLocked)
        {
            hoodPivot.freeAngularAxis(JointAxis.X);
            hoodLocked = false;
        }
    }

    private void UpdateDriveSpeed()
    {
        if (DriveController == null)
        {
            return;
        }

        DriveController.SetDriveMp(WantsToShoot() ? sotmSettings.shootingDriveMultiplier : 1f);
    }

    private bool WantsToShoot()
    {
        if (!(OuttakeAction || AutoPass || ManualShoot)) _wantsShooting = false;
        else if (!ShooterUnderTrench()) _wantsShooting = true;
        return _wantsShooting && intakeDeployed;
    }

    private bool IsPassShot()
    {
        return AutoPass || (OuttakeAction && !InAllianceZone());
    }

    private bool HoodAtAngle()
    {
        return Mathf.Abs(Mathf.DeltaAngle(hoodPivot.GetAngle(), targetHoodAngle)) <= hoodAngleTolerance;
    }

    private bool HoodAtLockAngle()
    {
        return Mathf.Abs(Mathf.DeltaAngle(hoodPivot.GetAngle(), targetHoodAngle)) <= hoodAngleLockTolerance;
    }

    private float GetHoodRpm(float shooterRpm)
    {
        return shooterRpm / hoodRpmReduction;
    }

    private bool ShooterUnderTrench()
    {
        foreach (var corner in shooterCorners)
        {
            if (UnderTrench(corner)) return true;
        }
        return false;
    }
}