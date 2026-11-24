using Games.Reefscape.Robots;
using MoSimLib;
using RobotFramework.Components;
using RobotFramework.Controllers.PidSystems;
using UnityEngine;

public class Overture : ReefscapeCommandRobotBase
{
    // Joints: add serialized references for this robot's mechanisms here.

    // PIDs: add serialized PidConstants here.

    // Setpoints are ScriptableObject assets defined in the companion OvertureSetpoint.cs.
    // Keep the model in its own script, as in other mods; nested structs do not
    // serialize correctly through the mod build pipeline.
    [Header("Coral Setpoints")]
    [SerializeField] private OvertureSetpoint stow;
    [SerializeField] private OvertureSetpoint intake;
    [SerializeField] private OvertureSetpoint l1;
    [SerializeField] private OvertureSetpoint l1Place;
    [SerializeField] private OvertureSetpoint l2;
    [SerializeField] private OvertureSetpoint l3;
    [SerializeField] private OvertureSetpoint l4;
    [SerializeField] private OvertureSetpoint l4Place;

    [Header("Algae Setpoints")]
    [SerializeField] private OvertureSetpoint lowAlgae;
    [SerializeField] private OvertureSetpoint highAlgae;
    [SerializeField] private OvertureSetpoint bargePrep;
    [SerializeField] private OvertureSetpoint bargePlace;

    // Intakes and piece states: add serialized coral / algae intakes and stow states here.

    // Audio: add serialized sources and clips here.

    // Runtime state: add joint targets and game-piece controller nodes here.

    protected override void Init()
    {
        base.Init();
        // TODO: Set initial joint PIDs.
        SetupGamePieceController();
        SetStartupValues();
        SetupStates();

        Scheduler.AddProtectedSystem(UpdateJoints, UpdateAudio);
        Scheduler.AddLateRecursive(UpdatePids);
    }

    private void SetupGamePieceController()
    {
        // TODO: Build coral / algae GamePieceGraphs, register intakes and controller nodes,
        // select the preload, and assign CoralPathController / AlgaePathController graphs.
    }

    private void SetStartupValues()
    {
        SetSetpoint(stow);
        // TODO: Initialize mechanism targets, toggles, and audio sources.
    }

    private void SetSetpoint(OvertureSetpoint setpoint)
    {
        if (setpoint == null) return;
        // TODO: Copy this robot's mechanism targets from the setpoint.
    }

    private void UpdatePids()
    {
        // TODO: Update each joint's PID constants for live tuning.
    }

    private void UpdateJoints()
    {
        // TODO: Apply this robot's mechanism targets to the joints.
    }

    private void UpdateAudio()
    {
        // TODO: Update mechanism and game-piece audio.
    }

    private void StowPieces(bool requestIntake = false, bool dontCoralIntake = false)
    {
        // TODO: Request coral / algae intake or stow using the current robot / intake mode.
    }

    private void PlacePiece()
    {
        // TODO: Request coral outtake or algae outtake / barge shooting.
    }

    private void SetupStates()
    {
        InState<StowState>()
            .OnMechanisms(() => SetSetpoint(stow))
            .OnGamePiece(() => StowPieces());

        InState<IntakeState>()
            .OnMechanisms(() => SetSetpoint(intake))
            .OnGamePiece(() => StowPieces(IntakeAction));

        InState<PlaceState>()
            .OnMechanisms(() =>
            {
                if (WasLastState<BargeState>()) SetSetpoint(bargePlace);
                else if (WasLastState<L4State>()) SetSetpoint(l4Place);
                else if (WasLastState<L1State>()) SetSetpoint(l1Place);
            })
            .OnGamePiece(PlacePiece);

        InState<L1State>()
            .OnMechanisms(() => SetSetpoint(l1))
            .OnGamePiece(() => StowPieces());

        InState<L2State>()
            .OnMechanisms(() => SetSetpoint(l2))
            .OnGamePiece(() => StowPieces());

        InState<L3State>()
            .OnMechanisms(() => SetSetpoint(l3))
            .OnGamePiece(() => StowPieces());

        InState<L4State>()
            .OnMechanisms(() => SetSetpoint(l4))
            .OnGamePiece(() => StowPieces());

        InState<LowAlgaeState>()
            .OnMechanisms(() => SetSetpoint(lowAlgae))
            .OnGamePiece(() => StowPieces());

        InState<HighAlgaeState>()
            .OnMechanisms(() => SetSetpoint(highAlgae))
            .OnGamePiece(() => StowPieces());

        InState<BargeState>()
            .OnMechanisms(() => SetSetpoint(bargePrep))
            .OnGamePiece(() => StowPieces());

        InState<RobotSpecialState>()
            .OnCheckTransition(() => new StowState(this));

        InState<ClimbState>()
            .OnMechanisms(() =>
            {
                // TODO: Choose climb preparation targets.
            })
            .OnGamePiece(() => StowPieces());

        InState<ClimbedState>()
            .OnMechanisms(() =>
            {
                // TODO: Choose climbed / retract targets.
            })
            .OnGamePiece(() => StowPieces());
    }
}
