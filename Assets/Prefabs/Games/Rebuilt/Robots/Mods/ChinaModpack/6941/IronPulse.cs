using Games.Rebuilt.Components;
using Games.Rebuilt.Robots;
using MoSimCore.BaseClasses.GameManagement;
using MoSimCore.Enums;
using RobotFramework.Components;
using RobotFramework.Controllers.PidSystems;
using RobotFramework.Enums;
using UnityEngine;
using UnityEngine.Serialization;

namespace Prefabs.Games.Rebuilt.Robots.Mods.ChinaMod.IronPulse
{
    public class IronPulse : RebuiltRobotBase
    {
        [Header("Joints")] 
        [SerializeField] private GenericTurret turret;
        [SerializeField] private GenericJoint dyeRotorJoint;
        [SerializeField] private GenericJoint intakeJoint;
        [SerializeField] private GenericJoint kickerBarJoint;
        [SerializeField] private GenericJoint hoodJoint;

        [Header("Rollers")] 
        [SerializeField] private RollerGroup intakeRollers;
        [SerializeField] private RollerGroup indexerRollers;
        [SerializeField] private RollerGroup shooterRollers;
        [SerializeField] private RollerGroup hoodRollers;
        [Tooltip("Rollers whose sound is only heard while their button is held. Their own loops stay muted; " +
                 "the robot plays the clip itself so it can fade out after the roller stops.")]
        [SerializeField] private InteractionRoller shooterAudioRoller;
        [SerializeField] private InteractionRoller intakeAudioRoller;
        [SerializeField] private RollerSound shooterSound = new() { pitchPerRpm = 0.0009f, pitchRange = new Vector2(0.7f, 1.5f), volumePerRpm = 0.0006f, volumeRange = new Vector2(0f, 1f) };
        [SerializeField] private RollerSound intakeSound = new() { pitchPerRpm = 0.00118f, pitchRange = new Vector2(0.4f, 1f), volumePerRpm = 0.0001f, volumeRange = new Vector2(0f, 0.1f) };
        [SerializeField, Min(0f)] private float rollerAudioFadeIn = 0.15f;
        [SerializeField, Min(0f)] private float rollerAudioFadeOut = 0.5f;

        [System.Serializable]
        private class RollerSound
        {
            public float pitchPerRpm;
            public Vector2 pitchRange;
            public float volumePerRpm;
            public Vector2 volumeRange;
            [System.NonSerialized] public AudioSource rollerSource;
            [System.NonSerialized] public AudioSource source;
            [System.NonSerialized] public float pitch;
            [System.NonSerialized] public float volume;
            [System.NonSerialized] public float gain;
        }
        [Tooltip("A ball wedged against the turret in a full hopper stops it dead. After this long with no progress the rotor stirs the pile to free it. 0 = off.")]
        [SerializeField] private float turretJamSeconds = 0.4f;
        [SerializeField] private float shooterToHoodReduction;
        [Tooltip("Hood speed ratio for passes. Fast passes with the true 1 in hood wobbled at the alliance-zone ratio. 0 = same as shooterToHoodReduction.")]
        [SerializeField] private float passShooterToHoodReduction;

        [Header("PIDS")] 
        [SerializeField] private PidConstants turretPidConstants;
        [SerializeField] private PidConstants turretWrapPidConstants;
        // A wrap ends with up to 150 degrees still to go. This loop covers that and hands over within a couple of degrees; normal
        // tracking never uses it.
        [SerializeField] private PidConstants turretCatchUpPidConstants;
        [SerializeField] private float turretCatchUpExitDegrees = 2f;
        [SerializeField] private PidConstants dyeRotorPidConstants;
        [SerializeField] private PidConstants intakePidConstants;
        [SerializeField] private PidConstants kickerBarPidConstants;
        [SerializeField] private PidConstants hoodPidConstants;

        [Header("Alliance Zone Interpolation")]
        
        // [SerializeField] private Interpolation interp;
        [SerializeField] private IronPulseInterp interp;

        [Header("Shooter Values")] [SerializeField]
        private float feederSpeedMultiplier = 2.3f;

        [SerializeField] private float indexerRpm = 2000f;
        [SerializeField] private float turretErrorTolerance = 2f;
        [SerializeField] private float hoodErrorTolerance = 1.5f;
        [SerializeField] private Vector2 hoodAngleLimits = new(0f, 35f);
        [SerializeField] private float manualShotRpm = 2000f;
        [SerializeField] private float manualHoodAngle = 25f;

        [Tooltip("Hood angle while not shooting. Kept low so fuel cannot get under a raised hood and hold it up, which would stop the feed.")]
        [SerializeField] private float hoodIdleAngle = 12f;

        [Header("Drive")] [SerializeField, Range(0f, 1f)]
        private float shootingDriveMultiplier = 0.55f;

        [SerializeField, Range(0f, 1f)] private float passDriveMultiplier = 0.55f;

        [Header("Shoot On The Move")] [SerializeField]
        private bool enableShootOnMove = true;

        [SerializeField, Min(0f)] private float hubTimeOfFlight = 1.2f;
        [SerializeField] private float flightTimePerMeter = 0.1f;
        [SerializeField, Min(0f)] private float passTimeOfFlight = 1.25f;
        [SerializeField] private float passFlightTimeScale = 0.8f;
        [SerializeField] private float turretLeadTime = 0.04f;
        [SerializeField] private float interpLeadTime = 0.2f;
        [SerializeField] private float velocityDampeningValue;

        [Header("Pass Wall Bias")] [SerializeField]
        private bool enablePassWallBias = true;

        [SerializeField] private float passWallBiasScalar = 1f;
        [SerializeField] private Vector2 passFieldWallZLimits = new(-4.105f, 4.105f);
        [SerializeField] private float passWallMargin = 1.5f;

        [Header("Dye Rotor Values")] [SerializeField]
        private JointAxis dyeRotorAxis = JointAxis.Y;

        [SerializeField] private float dyeRotorAngularVelocity;
        [SerializeField] private float dyeRotorReverseVelocity;


        [Header("Intake Values")] [SerializeField]
        private JointAxis intakeAxis = JointAxis.X;

        [SerializeField] private float intakeExtension;
        [SerializeField] private float intakeRpm;

        [Header("Auto Jostle")] [SerializeField]
        private float autoJostleStartDelay = 1f;

        [SerializeField] private float autoJostleStowDwell = 0.12f;
        [SerializeField] private float autoJostleDeployDwell = 0.15f;

        [Tooltip("Share of the intake's travel each agitation stroke pulls back. 1 = all the way in.")]
        [SerializeField, Range(0f, 1f)] private float autoJostleStroke = 0.4f;

        [Header("Kicker Bar Values")] [SerializeField]
        private JointAxis kickerBarAxis = JointAxis.X;

        [SerializeField] private float kickerBarAngle = -100f;

        [Header("LEDs")] [SerializeField] private Renderer[] ledStrips;
        [SerializeField] private float ledBrightness = 1f;

        // The team's default LED command: states and timings from their code, green after a volley from their test video
        // (about 0.45 s). Visual only. One 1 x N texture is shared by every strip. Plain fully saturated colour at 1, no glow:
        // owner wants vibrant, not bright. Nothing above 1 blooms and no light tints the robot.
        private const int LedCount = 24;
        private const int LedScannerLength = 8;
        private const float LedScannerStep = 0.03f;
        private const float LedBlink = 0.04f;
        private const float LedAfterShooting = 0.45f;
        private static readonly Color32 LedOff = new Color32(0, 0, 0, 255);
        private static readonly Color LedRed = new Color(1f, 0f, 0f);
        private static readonly Color LedBlue = new Color(0f, 0f, 1f);
        private static readonly Color LedGreen = new Color(0f, 1f, 0f);
        private Texture2D ledTexture;
        private Color32[] ledPixels;
        private Color32[] ledShown;
        private MaterialPropertyBlock ledBlock;
        private float ledLastShootTime = -100f;

        private float targetHoodAngle;
        private bool intakeDeployed;
        private float turretJamTimer;
        private float turretJamBestError = float.MaxValue;
        private float turretJamSampleTimer;
        private bool turretJammed;
        private bool feedEnabled;
        private bool turretCatchingUp;
        private bool wasIntakeDeployed;
        private bool kickerDeployed;
        private float autoJostleTimer;
        private bool autoJostleOut;
        private float autoJostleShootStartTime = -1f;
        private bool autoJostleActive;
        private bool autoJostleRememberedDeployed;
        private Rigidbody robotRigidbody;
        private Vector3 lastVelocity;
        private Vector3 smoothedVelocity;

        // Visual only: a pile of fuel makes the solver jitter the kicker by a fraction of a degree, which no physics setting
        // removes. Its meshes follow a filtered pose in the robot's frame; colliders and motion are untouched, and real
        // movement (more than the snap limits away) passes straight through.
        private const float KickerVisualSnapDegrees = 1.5f;
        private const float KickerVisualSnapMeters = 0.004f;
        private const float KickerVisualResponse = 5f;
        private Transform kickerVisual;
        private Vector3 kickerVisualLocalPos;
        private Quaternion kickerVisualLocalRot;
        private Vector3 kickerFilteredPos;
        private Quaternion kickerFilteredRot;
        private bool kickerFilterReady;

        private RebuiltInterpolation allianceZoneFlywheelInterpolation;
        private RebuiltInterpolation allianceZoneHoodInterpolation;
        private RebuiltInterpolation passZoneFlywheelInterpolation;
        private RebuiltInterpolation passZoneHoodInterpolation;

        protected override void Init()
        {
            turret.Initialize(this);
            turret.SetPid(turretPidConstants);
            turret.SetTrackContinuously(true);

            InitLeds();

            dyeRotorJoint.SetPid(dyeRotorPidConstants);
            intakeJoint.SetPid(intakePidConstants);
            kickerBarJoint.SetPid(kickerBarPidConstants);
            hoodJoint.SetPid(hoodPidConstants);
            

            allianceZoneFlywheelInterpolation = interp.AZFlywheelInterp;
            allianceZoneHoodInterpolation = interp.AZHoodInterp;
            passZoneFlywheelInterpolation = interp.NZFlywheelInterp;
            passZoneHoodInterpolation = interp.NZHoodInterp;
            
            interp.SetTransformOverride(turret.transform);
            
            allianceZoneFlywheelInterpolation.Initialize(this);
            allianceZoneHoodInterpolation.Initialize(this);
            passZoneFlywheelInterpolation.Initialize(this);
            passZoneHoodInterpolation.Initialize(this);

            targetHoodAngle = 0f;
            intakeDeployed = false;
            wasIntakeDeployed = false;
            kickerDeployed = false;
            TryGetComponent(out robotRigidbody);
            lastVelocity = Vector3.zero;
            kickerVisual = kickerBarJoint.transform.Find("Model");
            if (kickerVisual != null)
            {
                kickerVisualLocalPos = kickerVisual.localPosition;
                kickerVisualLocalRot = kickerVisual.localRotation;
            }

            Scheduler.AddSubsystems(HandleTurret, Shooter, IntakeRollers, UpdateDriveSpeed);
            Scheduler.AddProtectedSystem(UpdateJoints);
            Scheduler.AddLateRecursive(UpdatePids);
            Scheduler.AddRecursive(handleDampedVelocity);
        }

        // Overrides the scheduler's LateUpdate (a private one hid it, so the late-recursive UpdatePids never ran).
        protected override void LateUpdate()
        {
            base.LateUpdate();

            // Audio follows the buttons: the flywheel idles between shots but should only be heard while shooting.
            UpdateRollerSound(shooterAudioRoller, shooterSound, WantsToShoot());
            UpdateRollerSound(intakeAudioRoller, intakeSound, IntakeAction);

            UpdateLeds();

            if (kickerVisual == null) return;

            Transform kicker = kickerBarJoint.transform;
            Vector3 rawPos = transform.InverseTransformPoint(kicker.TransformPoint(kickerVisualLocalPos));
            Quaternion rawRot = Quaternion.Inverse(transform.rotation) * (kicker.rotation * kickerVisualLocalRot);

            if (!kickerFilterReady || Quaternion.Angle(kickerFilteredRot, rawRot) > KickerVisualSnapDegrees ||
                Vector3.Distance(kickerFilteredPos, rawPos) > KickerVisualSnapMeters)
            {
                kickerFilteredPos = rawPos;
                kickerFilteredRot = rawRot;
                kickerFilterReady = true;
            }
            else
            {
                float blend = 1f - Mathf.Exp(-KickerVisualResponse * Time.deltaTime);
                kickerFilteredPos = Vector3.Lerp(kickerFilteredPos, rawPos, blend);
                kickerFilteredRot = Quaternion.Slerp(kickerFilteredRot, rawRot, blend);
            }

            kickerVisual.SetPositionAndRotation(transform.TransformPoint(kickerFilteredPos), transform.rotation * kickerFilteredRot);
        }

        private void handleDampedVelocity()
        {
            Vector3 turretVelocity = robotRigidbody.GetPointVelocity(hoodRollers.transform.position);
            float blend = velocityDampeningValue > 0f ? velocityDampeningValue : 1f;
            smoothedVelocity = Vector3.Lerp(smoothedVelocity, turretVelocity, blend);
            lastVelocity = turretVelocity;
        }

        private void UpdatePids()
        {
            turret.UpdatePid(GetTurretPid());
            dyeRotorJoint.UpdatePid(dyeRotorPidConstants);
            intakeJoint.UpdatePid(intakePidConstants);
            kickerBarJoint.UpdatePid(kickerBarPidConstants);
            hoodJoint.UpdatePid(hoodPidConstants);
        }

        private void HandleTurret()
        {
            bool inAllianceZone = InAllianceZone();

            if (AutoPass || !inAllianceZone)
            {
                turret.SetPassMapClosest();
                // A pass carries the robot's velocity like any other shot, so it needs the same lead or it leaves the field.
                turret.SetCustomTarget(GetShootOnMoveTarget(GetWallBiasedPassTarget(turret.GetCurrentTarget())));
            }
            else
            {
                turret.SetDefaultMap(GenericTurret.DefaultMap.Hub);
                turret.SetCustomTarget(GetShootOnMoveTarget(turret.GetCurrentTarget()));
            }

            turret.Align();
            // Right after Align, which is what works out whether the turret is unwinding.
            turret.UpdatePid(GetTurretPid());
        }

        private PidConstants GetTurretPid()
        {
            if (turret.IsWrapping())
            {
                turretCatchingUp = true;
                return turretWrapPidConstants;
            }

            if (Mathf.Abs(turret.GetError()) <= turretCatchUpExitDegrees)
            {
                turretCatchingUp = false;
            }

            return turretCatchingUp ? turretCatchUpPidConstants : turretPidConstants;
        }

        private void Shooter()
        {
            RebuiltInterpolation flywheelInterpolation = GetActiveFlywheelInterpolation();
            RebuiltInterpolation hoodInterpolation = GetActiveHoodInterpolation();
            float shooterSpeed = flywheelInterpolation.Interpolate();
            float shooterAngle = hoodInterpolation.Interpolate();
            
            if (!WantsToShoot())
            {
                feedEnabled = false;
                StopShooter();
                targetHoodAngle = Mathf.Clamp(hoodIdleAngle, hoodAngleLimits.x, hoodAngleLimits.y);
                return;
            }

            if (ManualShoot)
            {
                shooterRollers.SetRpm(manualShotRpm);
                hoodRollers.SetRpm(manualShotRpm * shooterToHoodReduction);
                targetHoodAngle = manualHoodAngle;
                feedEnabled = shooterRollers.AtSpeed() && HoodReady();
                if (feedEnabled)
                {
                    indexerRollers.SetRpm(indexerRpm);
                }
                else
                {
                    indexerRollers.Stop();
                }
                return;
            }

            SetInterpolationTarget();

            shooterRollers.SetRpm(shooterSpeed);
            bool passing = GetActiveFlywheelInterpolation() == passZoneFlywheelInterpolation;
            hoodRollers.SetRpm(shooterSpeed * (passing && passShooterToHoodReduction > 0f ? passShooterToHoodReduction : shooterToHoodReduction));
            targetHoodAngle = Mathf.Clamp(shooterAngle, hoodAngleLimits.x, hoodAngleLimits.y);

            feedEnabled = TurretReady() && shooterRollers.AtSpeed() && HoodReady();
            if (feedEnabled)
            {
                indexerRollers.SetRpm(indexerRpm);
            }
            else
            {
                indexerRollers.Stop();
            }
        }

        private void UpdateJoints()
        {
            if (IntakeAction)
            {
                intakeDeployed = true;
                kickerDeployed = true;
            }

            // Robot Special 2 = intake in. No outtake button (owner 2026-11-15: the hopper only drains through the roller nip,
            // a ball or so at a time, so the button was removed).
            if (RobotSpecial2)
            {
                // Beats agitation too: it would otherwise put the intake back out when it ends.
                intakeDeployed = false;
                autoJostleActive = false;
                autoJostleRememberedDeployed = false;
            }

            UpdateAutoJostle();

            float rotorVelocity = WantsToShoot() ? (feedEnabled || turretJammed ? dyeRotorAngularVelocity : 0f) : -dyeRotorReverseVelocity;
            float intakeTarget = GetIntakeTarget();

            dyeRotorJoint.SetAngularVelocity(rotorVelocity).WithAxis(dyeRotorAxis);
            intakeJoint.SetLinearTarget(39.3701f * GetIntakeTarget()).withAxis(intakeAxis);
            kickerBarJoint.SetTargetAngle(kickerDeployed ? kickerBarAngle : 0f).withAxis(kickerBarAxis);
            hoodJoint.SetTargetAngle(targetHoodAngle).withAxis(JointAxis.X);

            wasIntakeDeployed = intakeDeployed;
        }

        private void IntakeRollers()
        {
            if (IntakeAction)
            {
                intakeRollers.SetRpm(intakeRpm);
                return;
            }

            intakeRollers.Stop();
        }

        // TODO: Should setup a recursive lookup for tof and predicted pose
        private void SetInterpolationTarget()
        {
            if (AutoPass || !InAllianceZone())
            {
                Vector3 passReference = turret.GetCurrentTarget() - smoothedVelocity * interpLeadTime;
                passZoneFlywheelInterpolation.SetCustomTarget(passReference);
                passZoneHoodInterpolation.SetCustomTarget(passReference);
                return;
            }

            allianceZoneFlywheelInterpolation.SetDefaultMap(DefaultMap.Hub);
            allianceZoneHoodInterpolation.SetDefaultMap(DefaultMap.Hub);

            Vector3 compensatedTarget = GetShootOnMoveTarget(allianceZoneFlywheelInterpolation.GetCurrentTarget()) - smoothedVelocity * interpLeadTime;
            allianceZoneFlywheelInterpolation.SetCustomTarget(compensatedTarget);
            allianceZoneHoodInterpolation.SetCustomTarget(compensatedTarget);
        }

        private RebuiltInterpolation GetActiveFlywheelInterpolation()
        {
            return IsPassShot() ? passZoneFlywheelInterpolation : allianceZoneFlywheelInterpolation;
        }

        private RebuiltInterpolation GetActiveHoodInterpolation()
        {
            return IsPassShot() ? passZoneHoodInterpolation : allianceZoneHoodInterpolation;
        }

        private float GetFlightTime(float distance)
        {
            float flightTime = hubTimeOfFlight + flightTimePerMeter * (distance - 4f);
            return IsPassShot() ? flightTime * passFlightTimeScale : flightTime;
        }

        private Vector3 GetShootOnMoveTarget(Vector3 target)
        {
            if (!enableShootOnMove || robotRigidbody == null)
            {
                return target;
            }

            Vector3 origin = turret.GetReferencePoint().position;
            float flightTime = IsPassShot() ? passTimeOfFlight : GetFlightTime(Vector3.Distance(origin, target));
            Vector3 leadTarget = target - smoothedVelocity * flightTime;
            Vector3 offset = leadTarget - origin;
            float planar = offset.x * offset.x + offset.z * offset.z;

            if (planar < 0.25f)
            {
                return leadTarget;
            }

            float bearingRate = (offset.x * smoothedVelocity.z - offset.z * smoothedVelocity.x) / planar * Mathf.Rad2Deg;
            return origin + Quaternion.AngleAxis(bearingRate * turretLeadTime, Vector3.up) * offset;
        }

        private Vector3 GetWallBiasedPassTarget(Vector3 target)
        {
            if (!enablePassWallBias || turret == null)
            {
                return target;
            }

            Vector3 hubTarget = turret.GetDefaultMapPosition(GenericTurret.DefaultMap.Hub);
            Vector3 turretPosition = turret.transform.position;
            Vector2 hubDirection = new(hubTarget.x - turretPosition.x, hubTarget.z - turretPosition.z);
            float hubDistance = hubDirection.magnitude;

            if (hubDistance <= Mathf.Epsilon)
            {
                return target;
            }

            float hubAngle = Mathf.Atan2(hubDirection.y, hubDirection.x);
            float wallDirection = target.z >= 0f ? 1f : -1f;
            float requestedOffset = Mathf.Cos(hubAngle) * (passWallBiasScalar / hubDistance) * wallDirection;
            float minWallZ = Mathf.Min(passFieldWallZLimits.x, passFieldWallZLimits.y) + passWallMargin;
            float maxWallZ = Mathf.Max(passFieldWallZLimits.x, passFieldWallZLimits.y) - passWallMargin;
            float distanceToWall = wallDirection > 0f
                ? maxWallZ - target.z
                : target.z - minWallZ;

            float clampedMagnitude = Mathf.Min(Mathf.Abs(requestedOffset), Mathf.Max(0f, distanceToWall));
            target.z += Mathf.Sign(requestedOffset) * clampedMagnitude;
            target.z = Mathf.Clamp(target.z, minWallZ, maxWallZ);
            return target;
        }

        // Off target, trying to get there, robot not turning away from it, and no closer than 0.2 s ago: something is holding the turret.
        private void UpdateTurretJam()
        {
            bool tryingToAim = turretJamSeconds > 0f && WantsToShoot() && !ManualShoot && !turret.IsWrapping() && !TurretReady()
                && Mathf.Abs(robotRigidbody.angularVelocity.y) < 0.5f;
            if (!tryingToAim)
            {
                turretJamBestError = float.MaxValue;
                turretJamSampleTimer = 0f;
                turretJamTimer = 0f;
                turretJammed = false;
                return;
            }

            float error = Mathf.Abs(turret.GetError());
            turretJamSampleTimer += Time.fixedDeltaTime;
            turretJamTimer += Time.fixedDeltaTime;
            if (turretJamSampleTimer >= 0.2f)
            {
                turretJamSampleTimer = 0f;
                if (error < turretJamBestError - 0.5f) turretJamTimer = 0f;
                turretJamBestError = error;
            }

            turretJammed = turretJamTimer >= turretJamSeconds;
        }

        private void UpdateAutoJostle()
        {
            bool actuallyShooting = WantsToShoot() && shooterRollers.AtSpeed() && HoodReady() && (ManualShoot || TurretReady());
            if (actuallyShooting)
            {
                if (autoJostleShootStartTime < 0f)
                {
                    autoJostleShootStartTime = Time.time;
                }
            }
            else
            {
                autoJostleShootStartTime = -1f;
            }

            // Agitate only with the intake already out; a stowed intake stays in.
            UpdateTurretJam();
            bool jostleEnabled = (actuallyShooting || turretJammed) && !IntakeAction && !RobotSpecial2 && (intakeDeployed || autoJostleActive) && Time.time - autoJostleShootStartTime >= autoJostleStartDelay;

            if (jostleEnabled)
            {
                if (!autoJostleActive)
                {
                    autoJostleActive = true;
                    autoJostleRememberedDeployed = intakeDeployed;
                    intakeDeployed = true;
                    autoJostleTimer = 0f;
                    autoJostleOut = true;
                }

                autoJostleTimer += Time.fixedDeltaTime;
                float dwell = autoJostleOut ? autoJostleDeployDwell : autoJostleStowDwell;
                if (autoJostleTimer >= dwell)
                {
                    autoJostleTimer = 0f;
                    autoJostleOut = !autoJostleOut;
                }
            }
            else
            {
                if (autoJostleActive)
                {
                    intakeDeployed = autoJostleRememberedDeployed;
                }
                autoJostleActive = false;
                autoJostleTimer = 0f;
                autoJostleOut = false;
            }
        }

        private float GetIntakeTarget()
        {
            if (autoJostleActive)
            {
                return autoJostleOut ? intakeExtension : intakeExtension * (1f - autoJostleStroke);
            }

            if (!intakeDeployed)
            {
                return 0f;
            }

            return intakeExtension;
        }

        private void UpdateDriveSpeed()
        {
            DriveController.SetDriveMp(!WantsToShoot() ? 1f : IsPassShot() ? passDriveMultiplier : shootingDriveMultiplier);
        }

        private void InitLeds()
        {
            if (ledStrips == null || ledStrips.Length == 0) return;
            ledTexture = new Texture2D(LedCount, 1, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            ledPixels = new Color32[LedCount];
            ledShown = new Color32[LedCount];
            ledBlock = new MaterialPropertyBlock();
            ledBlock.SetTexture("_BaseMap", ledTexture);
            ledBlock.SetColor("_BaseColor", Color.white * ledBrightness);
            foreach (Renderer strip in ledStrips)
            {
                if (strip != null) strip.SetPropertyBlock(ledBlock);
            }
        }

        private void UpdateLeds()
        {
            if (ledTexture == null) return;

            if (WantsToShoot()) ledLastShootTime = Time.time;

            BaseGameManager manager = BaseGameManager.Instance;
            bool enabledState = manager == null || manager.RobotState == RobotState.Enabled;
            if (!enabledState)
            {
                LedScanner(Alliance == Alliance.Blue ? LedBlue : LedRed);
            }
            else if (manager != null && manager.GameState == GameState.Auto)
            {
                LedRainbow();
            }
            else if (WantsToShoot())
            {
                LedFlash(LedRed);
            }
            else if (Time.time - ledLastShootTime < LedAfterShooting)
            {
                LedFlash(LedGreen);
            }
            else if (IntakeAction)
            {
                LedFlash(LedBlue);
            }
            else
            {
                LedScanner(LedBlue);
            }

            if (!System.Linq.Enumerable.SequenceEqual(ledPixels, ledShown))
            {
                System.Array.Copy(ledPixels, ledShown, LedCount);
                ledTexture.SetPixels32(ledPixels);
                ledTexture.Apply(false);
            }
        }

        private void LedFlash(Color color)
        {
            bool on = Mathf.FloorToInt(Time.time / LedBlink) % 2 == 0;
            Color32 pixel = on ? (Color32)color : LedOff;
            for (int i = 0; i < LedCount; i++) ledPixels[i] = pixel;
        }

        private void LedScanner(Color color)
        {
            int travel = LedCount - LedScannerLength;
            int step = Mathf.FloorToInt(Time.time / LedScannerStep) % (2 * travel);
            int start = step <= travel ? step : 2 * travel - step;
            for (int i = 0; i < LedCount; i++)
            {
                ledPixels[i] = i >= start && i < start + LedScannerLength ? (Color32)color : LedOff;
            }
        }

        private void LedRainbow()
        {
            for (int i = 0; i < LedCount; i++)
            {
                ledPixels[i] = Color.HSVToRGB(Mathf.Repeat(i / (float)LedCount + Time.time * 0.6f, 1f), 1f, 1f);
            }
        }

        // The roller adds its AudioSource at runtime and only plays while it turns, so it can't fade after a stop.
        // Mute it and play the same clip from a robot-owned source driven by the roller's real RPM.
        private void UpdateRollerSound(InteractionRoller roller, RollerSound sound, bool held)
        {
            if (roller == null) return;
            if (sound.rollerSource == null)
            {
                if (!roller.TryGetComponent(out sound.rollerSource) || sound.rollerSource.clip == null) return;
                sound.source = roller.gameObject.AddComponent<AudioSource>();
                sound.source.clip = sound.rollerSource.clip;
                sound.source.outputAudioMixerGroup = sound.rollerSource.outputAudioMixerGroup;
                sound.source.loop = true;
                sound.source.playOnAwake = false;
                sound.source.spatialBlend = sound.rollerSource.spatialBlend;
                sound.pitch = sound.pitchRange.x;
            }
            sound.rollerSource.mute = true;

            bool enabled = held && BaseGameManager.Instance.RobotState != RobotState.Disabled;
            float k = 1f - Mathf.Exp(-8f * Time.deltaTime);
            // Released: hold the last pitch/volume and just fade, so the tail doesn't follow the roller stopping.
            if (enabled)
            {
                float rpm = Mathf.Abs(roller.realRpm);
                sound.pitch = Mathf.Lerp(sound.pitch, Mathf.Clamp(rpm * sound.pitchPerRpm, sound.pitchRange.x, sound.pitchRange.y), k);
                sound.volume = Mathf.Lerp(sound.volume, Mathf.Clamp(rpm * sound.volumePerRpm, sound.volumeRange.x, sound.volumeRange.y), k);
            }
            sound.gain = Mathf.MoveTowards(sound.gain, enabled ? 1f : 0f,
                Time.deltaTime / Mathf.Max(0.01f, enabled ? rollerAudioFadeIn : rollerAudioFadeOut));

            if (sound.gain <= 0f)
            {
                if (sound.source.isPlaying) sound.source.Stop();
                sound.pitch = sound.pitchRange.x;
                sound.volume = 0f;
                return;
            }
            if (!sound.source.isPlaying)
            {
                sound.source.time = Random.Range(0f, sound.source.clip.length);
                sound.source.Play();
            }
            sound.source.pitch = sound.pitch;
            sound.source.volume = sound.volume * sound.gain;
        }

        private bool WantsToShoot()
        {
            return OuttakeAction || AutoPass || ManualShoot;
        }

        private bool IsPassShot()
        {
            return AutoPass || !InAllianceZone();
        }

        private bool TurretReady()
        {
            return !turret.IsWrapping() && Mathf.Abs(turret.GetError()) <= turretErrorTolerance;
        }

        private bool HoodReady()
        {
            return Mathf.Abs(Mathf.DeltaAngle(hoodJoint.GetAngle(), targetHoodAngle)) <= hoodErrorTolerance;
        }

        private void StopShooter()
        {
            indexerRollers.Stop();
        }
    }
}
