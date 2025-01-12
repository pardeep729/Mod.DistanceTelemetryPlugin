using System;

using UnityEngine;

using static Events.Car.Death;

namespace DistanceTelemetryPlugin
{

    public enum TelemetryEvent
    {
        Update,
        Checkpoint,
        Collision,
        Destroyed,
        Exploded,
        Honked,
        Finish,
        Jump,
        Respawn,
        Split,
        Trick,
        Start,
        End
    }

    public struct Inputs
    {
        public bool Boost;
        public float Steer;
        public bool Grip;
        public float Gas;
        public float Brake;
        public Vector3 Rotation;
    }

    public struct Tire
    {
        public float Pos;
        public bool Contact;
        public float Suspension;
    }

    public struct Tires
    {
        public Tire TireFL;
        public Tire TireFR;
        public Tire TireBL;
        public Tire TireBR;
    }



    public abstract class Telemetry
    {
        public string Sender_ID;
        public string Race_ID;
        public abstract TelemetryEvent Event { get; }
        public string Level;
        public string Mode;
        public DateTime RealTime;
        public double Time;

    }

    public class UpdateTelemetry : Telemetry
    {

        public override TelemetryEvent Event => TelemetryEvent.Update;

        public float Speed_KPH;
        public float Speed_MPH;
        public float Heat;
        public Vector3 Pos;
        public Quaternion Rot;
        public Vector3 EulRot;
        public Vector3 Vel;
        public Vector3 AngVel;
        public Inputs Inputs;
        public bool Grav;
        public float Drag;
        public float AngularDrag;
        public bool Wings;
        public bool HasWings;
        public bool AllWheelsContacting;
        public Tires Tires;
        public float DriveWheelAvgRotVel;
        public float DriveWheelAvgRPM;


    }

    public class CheckpointTelmetry : Telemetry
    {

        public override TelemetryEvent Event => TelemetryEvent.Checkpoint;
        public int CheckpointIndex;
        public float TrackT;
    }

    public class CollisionTelemetry : Telemetry
    {
        public override TelemetryEvent Event => TelemetryEvent.Collision;
        public string Target;
        public Vector3 Pos;
        public float Speed;
    }



    public class ExplodedDestroyedTelemetry : Telemetry
    {
        public override TelemetryEvent Event => TelemetryEvent.Exploded;
        public Cause Cause;
    }

    public class HonkedTelemetry : Telemetry
    {
        public override TelemetryEvent Event => TelemetryEvent.Honked;
        public float Power;
        public Vector3 Pos;
    }

    public class FinishTelemetry : Telemetry
    {
        public override TelemetryEvent Event => TelemetryEvent.Finish;
        public int FinalTime;

        public FinishType FinishType;
    }

    public class JumpTelemetry : Telemetry
    {
        public override TelemetryEvent Event => TelemetryEvent.Jump;
    }

    public class RaceStartedTelemetry : Telemetry
    {
        public override TelemetryEvent Event => TelemetryEvent.Start;
    }

    public class RaceEndedTelemetry : Telemetry
    {
        public override TelemetryEvent Event => TelemetryEvent.End;
    }

    public class RespawnTelemetry : Telemetry
    {
        public override TelemetryEvent Event => TelemetryEvent.Respawn;
        public Vector3 Pos;
        public Quaternion Rot;
        public Vector3 EulRot;
    }

    public class SplitTelemetry : Telemetry
    {
        public override TelemetryEvent Event => TelemetryEvent.Split;
        public float Penetration;
        public float SeparationSpeed;
    }

    public class TrickTelemetry : Telemetry
    {
        public override TelemetryEvent Event => TelemetryEvent.Trick;
        public int Points;
        public float Cooldown;
        public float Grind;
        public float Wallride;
        public float Ceiling;
    }

}