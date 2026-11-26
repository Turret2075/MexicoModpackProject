using Games.Rebuilt.Components;
using Games.Rebuilt.Robots;
using MoSimLib;
using RobotFramework.Components;
using RobotFramework.Controllers.PidSystems;
using RobotFramework.Enums;
using UnityEngine;

public class Tecdroid : RebuiltRobotBase
{
    [Header("Joints")]
    [Tooltip("Lintake. Slides on the same axis as IronPulse.")]
    [SerializeField] private GenericJoint intakeJoint;
    [SerializeField] private GenericJoint hoodPivot;

    [Header("Rollers")]
    [Tooltip("Single roller.")]
    [SerializeField] private RollerGroup shooterRollers;
    [Tooltip("Single roller. Spins opposite to the shooter (set 'flipped' in the prefab).")]
    [SerializeField] private RollerGroup hoodRollers;
    [Tooltip("Belt (bandas), not rollers. Feeds at a multiple of the shooter speed, like CompetitiveConcept's feeder.")]
    [SerializeField] private RollerGroup floorBelt;
    [Tooltip("Single roller (transfer). Fuel rests on colliders above it.")]
    [SerializeField] private RollerGroup uptakeRollers;
    [Tooltip("Two rollers spinning in the same direction.")]
    [SerializeField] private RollerGroup intakeRollers;

    [Header("PIDS")]
    [SerializeField] private PidConstants intakePidConstants;
    [SerializeField] private PidConstants hoodPidConstants;
    [SerializeField] private PidConstants hoodShootingPidConstants;

    [Header("Alliance Zone Interpolation")]
    [SerializeField] private RebuiltInterpolation allianceZoneFlywheelInterpolation;
    [SerializeField] private RebuiltInterpolation allianceZoneHoodInterpolation;

    [Header("Pass Zone Interpolation")]
    [SerializeField] private RebuiltInterpolation passZoneFlywheelInterpolation;
    [SerializeField] private RebuiltInterpolation passZoneHoodInterpolation;

    [Header("Intake Values")]
    [SerializeField] private JointAxis intakeAxis = JointAxis.X;
    [Tooltip("Lintake travel in meters when deployed.")]
    [SerializeField] private float intakeExtension = 0.295f;
    [Tooltip("Lintake travel in meters when not shooting and intake is not held. Partial retraction.")]
    [SerializeField] private float intakeRetractedExtension = 0.07f;
    [SerializeField] private float intakeIdleExtension = 0.295f;
    [SerializeField] private float intakeRpm = 2000f;
    [Tooltip("Intake roller speed while shooting with the intake button not held. Overrides intakeRpm.")]
    [SerializeField] private float intakingRpm = 2125f;

    [Header("Intake Retract After Shot")]
    [Tooltip("Seconds of feeding before the Lintake retracts. It stays retracted until the intake button is released.")]
    [SerializeField, Min(0f)] private float retractAfterShotDelay = 0f;

    [Header("Shooter Values")]
    [SerializeField] private float uptakeRpm = 4500f;
    [Tooltip("Belt speed while feeding = shooter speed x this value (CompetitiveConcept uses 2.3).")]
    [SerializeField] private float floorBeltMultiplier = 1.625f;
    [Tooltip("Belt reverse speed used by outtake special (RobotSpecial2).")]
    [SerializeField] private float floorReverseRpm = 3900f;
    [Tooltip("Reverse speed on the floor belt when feeding stops. 0 disables.")]
    [SerializeField, Min(0f)] private float floorBackoffRpm = 0f;
    [Tooltip("Maximum auto-aim yaw error in degrees before feed rollers are allowed to run.")]
    [SerializeField] private float aimTolerance = 3f;
    [Tooltip("Maximum hood pivot angle error in degrees before feed rollers are allowed to run.")]
    [SerializeField, Min(0f)] private float hoodAngleTolerance = 0.5f;
    [SerializeField, Min(0f)] private float hoodAngleLockTolerance = 0.1f;
    [Tooltip("How long all shot readiness checks must remain true before feeding.")]
    [SerializeField, Min(0f)] private float feedReadyDebounceTime = 0f;
    [Tooltip("Reverse speed on the uptake when feeding stops, to back fuel off the flywheel. 0 disables.")]
    [SerializeField, Min(0f)] private float uptakeBackoffRpm = 0f;
    [Tooltip("Hood only travels from 0 to 30 degrees (real HoodConstants limit).")]
    [SerializeField] private Vector2 hoodAngleLimits = new(0f, 30f);
    [SerializeField] private float manualShotRpm = 2400f;
    [SerializeField] private float manualHoodAngle = 25f;
    [SerializeField, Min(0.01f)] private float hoodRpmReduction = 1.5f;

    [Header("Trench Avoidance")]
    [SerializeField] private Transform[] shooterCorners;

    [Header("Shoot On The Move")]
    [SerializeField] private SOTMSettings sotmSettings;

    [Header("Debug")]
    [SerializeField] private float aimErrorDebug;
    [SerializeField] private bool alignedToShootDebug;

    private float targetIntakeExtension;
    private float targetHoodAngle;
    private bool intakeOut;
    private bool retractedAfterShot;
    private bool intakeUsed;
    private float shotTimer;
    private bool hoodLocked;
    private float feedReadyTimer;
    private bool feedLatched;
    private bool _wantsShooting;
    private RebuiltAutoAimController aim;
    private Rigidbody robotRigidbody;

    protected override void Init()
    {
        intakeJoint.SetPid(intakePidConstants);
        hoodPivot.SetPid(hoodPidConstants);

        allianceZoneFlywheelInterpolation.Initialize(this);
        allianceZoneHoodInterpolation.Initialize(this);
        passZoneFlywheelInterpolation.Initialize(this);
        passZoneHoodInterpolation.Initialize(this);

        targetIntakeExtension = intakeRetractedExtension;
        targetHoodAngle = 0f;
        intakeOut = false;
        retractedAfterShot = false;
        intakeUsed = false;
        shotTimer = 0f;
        _wantsShooting = false;

        if (!TryGetComponent(out aim))
        {
            Debug.LogError($"{nameof(Tecdroid)} requires a {nameof(RebuiltAutoAimController)}.", this);
        }

        robotRigidbody = GetComponent<Rigidbody>();

        // Shooter must run before Intake so the retract timer sees the current feed state.
        Scheduler.AddSubsystems(HandleAutoAim, Shooter, Intake);
        Scheduler.AddProtectedSystem(UpdateJoints, UpdateDriveSpeed);
        Scheduler.AddLateRecursive(UpdatePids);
    }

    private void UpdatePids()
    {
        intakeJoint.UpdatePid(intakePidConstants);
        hoodPivot.UpdatePid(WantsToShoot() && HoodAtAngle() ? hoodShootingPidConstants : hoodPidConstants);
    }

    // Special 2: outtake (reverses uptake, floor belt and intake rollers).
    private void Intake()
    {
        UpdateIntakeRetractAfterShot();

        if (intakeOut)
        {
            targetIntakeExtension = intakeExtension;
        }
        else if (retractedAfterShot || !intakeUsed)
        {
            targetIntakeExtension = intakeRetractedExtension;
        }
        else
        {
            targetIntakeExtension = intakeIdleExtension;
        }

        if (RobotSpecial2)
        {
            intakeRollers.SetRpm(-(intakeRpm / 2f));
        }
        else if (IsShooting() && !IntakeAction)
        {
            intakeRollers.SetRpm(intakingRpm);
        }
        else if (IntakeAction)
        {
            intakeRollers.SetRpm(intakeRpm);
        }
        else
        {
            intakeRollers.Stop();
        }
    }

    // Intake held: Lintake goes out.
    // Shooting with intake not held: it stays out until feeding has run for retractAfterShotDelay, then pulls all
    // the way in for as long as shooting continues.
    // Not shooting and intake not held: it rests partially retracted at intakeIdleExtension.
    private void UpdateIntakeRetractAfterShot()
    {
        bool intakePressed = IntakeAction;

        if (intakePressed)
        {
            intakeOut = true;
            intakeUsed = true;
            retractedAfterShot = false;
            shotTimer = 0f;
        }
        else if (IsShooting())
        {
            shotTimer += Time.fixedDeltaTime;
            if (shotTimer >= retractAfterShotDelay)
            {
                intakeOut = false;
                retractedAfterShot = true;
            }
        }
        else
        {
            intakeOut = false;
            retractedAfterShot = false;
            shotTimer = 0f;
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

        SetFeedRollers(FeedReady(), shooterSpeed);
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

    // The floor is a belt: while feeding it runs at a multiple of the shooter speed, like CompetitiveConcept's feeder.
    private void SetFeedRollers(bool feeding, float shooterSpeed)
    {
        if (RobotSpecial2)
        {
            uptakeRollers.SetRpm(-uptakeRpm / 2f);
            floorBelt.SetRpm(-floorReverseRpm);
        }
        else if (feeding)
        {
            uptakeRollers.SetRpm(uptakeRpm);
            floorBelt.SetRpm(shooterSpeed * floorBeltMultiplier);
        }
        else
        {
            uptakeRollers.SetRpm(-uptakeBackoffRpm);
            floorBelt.SetRpm(-floorBackoffRpm);
        }
    }

    private void StopShooter()
    {
        feedLatched = false;
        feedReadyTimer = 0f;
        shooterRollers.SetRpm(0f);
        hoodRollers.SetRpm(0f);
        SetFeedRollers(false, 0f);
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
        intakeJoint.SetLinearTarget(39.3701f * targetIntakeExtension).withAxis(intakeAxis);
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
        return _wantsShooting;
    }

    private bool IsPassShot()
    {
        return AutoPass || (OuttakeAction && !InAllianceZone());
    }

    // Shooting means the shoot input is active and the feed has latched on.
    private bool IsShooting()
    {
        return WantsToShoot() && feedLatched;
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