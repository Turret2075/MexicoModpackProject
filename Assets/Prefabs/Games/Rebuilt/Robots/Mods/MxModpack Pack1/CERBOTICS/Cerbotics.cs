using Games.Rebuilt.Components;
using Games.Rebuilt.Robots;
using MoSimCore.Enums;
using MoSimLib;
using RobotFramework.Components;
using RobotFramework.Controllers.PidSystems;
using RobotFramework.Enums;
using UnityEngine;

public class Cerbotics : RebuiltRobotBase
{
    [Header("Joints")]
    [SerializeField] private GenericJoint intakeJoint; // Lintake, slides on the same axis as IronPulse
    [SerializeField] private GenericJoint hoodPivot;

    [Header("Rollers")]
    [SerializeField] private RollerGroup drumRollers;
    [SerializeField] private RollerGroup hoodRollers;
    [SerializeField] private RollerGroup floorRollers;
    [SerializeField] private RollerGroup uptakeRollers;
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
    [SerializeField] private float intakeExtension;
    [SerializeField] private float intakeRpm = 2000f;

    [Header("Intake Retract After Shot")]
    [Tooltip("Seconds of feeding before the Lintake retracts. It stays retracted until the intake button is released.")]
    [SerializeField, Min(0f)] private float retractAfterShotDelay = 0.5f;

    [Header("Shooter Values")]
    [SerializeField] private float uptakeRpm = 2000f;
    [SerializeField] private float floorRpm = 2000f;
    [Tooltip("Maximum auto-aim yaw error in degrees before feed rollers are allowed to run.")]
    [SerializeField] private float aimTolerance = 2f;
    [Tooltip("Maximum hood pivot angle error in degrees before feed rollers are allowed to run.")]
    [SerializeField, Min(0f)] private float hoodAngleTolerance = 0.5f;
    [Tooltip("How long all shot readiness checks must remain true before feeding.")]
    [SerializeField, Min(0f)] private float feedReadyDebounceTime = 0.06f;
    [SerializeField] private Vector2 hoodAngleLimits = new(0f, 35f);
    [SerializeField] private float manualShotRpm = 2000f;
    [SerializeField] private float manualHoodAngle = 25f;
    [SerializeField, Min(0.01f)] private float hoodRpmReduction = 1.5f;
    [SerializeField] private float stopShooterRpm = -1000f;

    [Header("Shoot On The Move")]
    [SerializeField] private SOTMSettings sotmSettings;

    private float targetHoodAngle;
    private float targetIntakeExtension;
    private bool intakeDeployed;
    private bool intakeRetractedAfterShot;
    private float shotTimer;
    private bool feedLatched;
    private float feedReadyTimer;
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

        targetHoodAngle = 0f;
        targetIntakeExtension = 0f;
        intakeDeployed = false;
        intakeRetractedAfterShot = false;
        shotTimer = 0f;
        feedLatched = false;
        feedReadyTimer = 0f;

        if (!TryGetComponent(out aim))
        {
            Debug.LogError($"{nameof(Cerbotics)} requires a {nameof(RebuiltAutoAimController)}.", this);
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

    private void Intake()
    {
        UpdateIntakeRetractAfterShot();

        intakeDeployed = IntakeAction && !intakeRetractedAfterShot;
        targetIntakeExtension = intakeDeployed ? intakeExtension : 0f;

        if (RobotSpecial2)
        {
            intakeRollers.SetRpm(-(intakeRpm / 2f));
        }
        else if (intakeDeployed)
        {
            intakeRollers.SetRpm(intakeRpm);
        }
        else
        {
            intakeRollers.Stop();
        }
    }

    // Shot, wait, retract. Once feeding has run for retractAfterShotDelay, the Lintake pulls in and stays in
    // until the intake button is released.
    private void UpdateIntakeRetractAfterShot()
    {
        if (!IntakeAction)
        {
            intakeRetractedAfterShot = false;
            shotTimer = 0f;
            return;
        }

        if (intakeRetractedAfterShot)
        {
            return;
        }

        if (!WantsToShoot())
        {
            shotTimer = 0f;
        }
        else if (feedLatched)
        {
            shotTimer += Time.fixedDeltaTime;
            if (shotTimer >= retractAfterShotDelay)
            {
                intakeRetractedAfterShot = true;
            }
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
            ApplyTimeOfFlightToInterpolation(allianceZoneFlywheelInterpolation, allianceZoneHoodInterpolation);
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

        if (ManualShoot)
        {
            drumRollers.SetRpm(manualShotRpm);
            hoodRollers.SetRpm(GetHoodRpm(manualShotRpm));
            targetHoodAngle = manualHoodAngle;
            UpdateFeedLatch();
            SetFeedRollers(feedLatched);
            return;
        }

        SetInterpolationTarget();

        RebuiltInterpolation flywheelInterpolation = GetActiveFlywheelInterpolation();
        RebuiltInterpolation hoodInterpolation = GetActiveHoodInterpolation();
        float shooterSpeed = flywheelInterpolation.Interpolate();
        float shooterAngle = hoodInterpolation.Interpolate();

        drumRollers.SetRpm(shooterSpeed);
        hoodRollers.SetRpm(GetHoodRpm(shooterSpeed));
        targetHoodAngle = Mathf.Clamp(shooterAngle, hoodAngleLimits.x, hoodAngleLimits.y);

        UpdateFeedLatch();
        SetFeedRollers(feedLatched);
    }

    private void UpdateFeedLatch()
    {
        if (feedLatched)
        {
            return;
        }

        if (CanRunFeed())
        {
            feedReadyTimer += Time.fixedDeltaTime;

            if (feedReadyTimer >= feedReadyDebounceTime)
            {
                feedLatched = true;
            }

            return;
        }

        feedReadyTimer = 0f;
    }

    // Same as VOLTEC: RobotSpecial2 reverses the feed (uptake at half speed, floor full) regardless of shooting.
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
            uptakeRollers.SetRpm(-uptakeRpm);
            floorRollers.Stop();
        }
    }

    private void SetInterpolationTarget()
    {
        if (IsPassShot())
        {
            passZoneFlywheelInterpolation.SetPassMapClosest();
            passZoneHoodInterpolation.SetPassMapClosest();
            ApplyTimeOfFlightToInterpolation(passZoneFlywheelInterpolation, passZoneHoodInterpolation);
            return;
        }

        allianceZoneFlywheelInterpolation.SetDefaultMap(DefaultMap.Hub);
        allianceZoneHoodInterpolation.SetDefaultMap(DefaultMap.Hub);
        ApplyTimeOfFlightToInterpolation(allianceZoneFlywheelInterpolation, allianceZoneHoodInterpolation);
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
        hoodPivot.freeAngularAxis(JointAxis.X);
    }

    private void UpdateDriveSpeed()
    {
        DriveController.SetDriveMp(WantsToShoot() ? sotmSettings.shootingDriveMultiplier : 1f);
    }

    private bool WantsToShoot()
    {
        return OuttakeAction || AutoPass || ManualShoot;
    }

    private bool IsPassShot()
    {
        return AutoPass || (OuttakeAction && !InAllianceZone());
    }

    private bool CanRunFeed()
    {
        return ShooterAligned() && drumRollers.AtSpeed() && hoodRollers.AtSpeed() && HoodAtAngle();
    }

    private bool HoodAtAngle()
    {
        return Mathf.Abs(Mathf.DeltaAngle(hoodPivot.GetAngle(), targetHoodAngle)) <= hoodAngleTolerance;
    }

    private bool ShooterAligned()
    {
        if (ManualShoot)
        {
            return true;
        }

        if (aim == null)
        {
            return false;
        }

        return Mathf.Abs(aim.GetRotationError()) <= aimTolerance;
    }

    private void StopShooter()
    {
        feedLatched = false;
        feedReadyTimer = 0f;
        drumRollers.SetRpm(stopShooterRpm);
        hoodRollers.SetRpm(stopShooterRpm);
        SetFeedRollers(false);
    }

    private float GetHoodRpm(float shooterRpm)
    {
        return shooterRpm / hoodRpmReduction;
    }
}