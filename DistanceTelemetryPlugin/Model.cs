using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

using UnityEngine;

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

    
    public class UpdateTelemetry : Telemetry
    {
        
        public float Speed_KPH;
        public float Speed_MPH;
        public float Heat;
        public Vector3 Pos;
        public Vector3 Rot;
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
        
    }

    public class Telemetry
    {
        public string Sender_ID;
        public string Race_ID;
        public string Level;
        public string Mode;
        public DateTime RealTime;
        public double Time;
        public TelemetryEvent Event;
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
}
