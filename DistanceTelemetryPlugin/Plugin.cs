using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using Events;
using Events.Car;
using Events.Game;
using Events.GameMode;
using Events.Player;
using Events.RaceEnd;
using JsonFx.Json;
using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace DistanceTelemetryPlugin
{
    [BepInPlugin(modGUID, modName, modVersion)]
    public sealed class Mod : BaseUnityPlugin
    {
        //Mod Details
        private const string modGUID = "Distance.TelemetryPlugin";
        private const string modName = "Distance Telemetry Plugin";
        private const string modVersion = "1.0.0";

        //Config Entry Settings
        public static string FilePrefixKey = "File Prefix";

        //Config Entries
        public static ConfigEntry<string> FilePrefix { get; set; }

        //Public Variables
        public bool active = false;
        public StreamWriter logFile;

        //Private Variables
        private bool has_wings;
        private bool wings;
        private CarLogic car_log;
        private Dictionary<string, object> data;
        private FileSystem fs = new FileSystem();
        private Guid instance_id;
        private Guid race_id;
        private JsonWriter writer = new JsonWriter();
        private LocalPlayerControlledCar localCar;
        private PlayerEvents playerEvents;
        private Rigidbody car_rg;
        private Stopwatch sw = new Stopwatch();
        private TextWriter data_writer;


        //Other
        public static ManualLogSource Log = new ManualLogSource(modName);
        public static Mod Instance;

        //Unity MonoBehaviour Functions
        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }

            writer.Settings.PrettyPrint = true;
            instance_id = Guid.NewGuid();
            Log = BepInEx.Logging.Logger.CreateLogSource(modGUID);
            Logger.LogInfo("Thanks for using the Distance Telemtry Plugin!");
            Logger.LogInfo("[Telemetry] Initializing...");
            Logger.LogInfo($"[Telemetry] Instance ID {instance_id.ToString("B")}...");

            //Config Setup
            FilePrefix = Config.Bind("General",
                FilePrefixKey,
                "Telemetry",
                new ConfigDescription("The name of the json file that the telemetry writes to"));

            //This does not create a directory where intended (if at all), double check how custom cars writes it since it's done correctly(?) there.
            string telemetryDirectory = fs.CreateDirectory("Telemetry");
            FileStream fileStream = fs.CreateFile(Path.Combine(telemetryDirectory, $"{FilePrefix.Value}_{string.Format("{0:yyyy-MM-dd_HH-mm-ss}", DateTime.Now)}.jsonl"));
            data_writer = new StreamWriter(fileStream);
            Logger.LogInfo($"[Telemetry] Opening new filestream for {fileStream}...");

            //Apply Patches (None to patch here!)
            Logger.LogInfo("Loaded!");
        }

        private void OnEnable()
        {
            StaticEvent<LocalCarHitFinish.Data>.Subscribe(new StaticEvent<LocalCarHitFinish.Data>.Delegate(RaceEnded));
            StaticEvent<Go.Data>.Subscribe(new StaticEvent<Go.Data>.Delegate(RaceStarted));
            StaticEvent<PauseToggled.Data>.Subscribe(new StaticEvent<PauseToggled.Data>.Delegate(OnGamePaused));
        }

        private void OnDisable()
        {
            Logger.LogInfo("Unsubscribing to events...");
            StaticEvent<LocalCarHitFinish.Data>.Unsubscribe(new StaticEvent<LocalCarHitFinish.Data>.Delegate(RaceEnded));
            StaticEvent<Go.Data>.Unsubscribe(new StaticEvent<Go.Data>.Delegate(RaceStarted));
            StaticEvent<PauseToggled.Data>.Unsubscribe(new StaticEvent<PauseToggled.Data>.Delegate(OnGamePaused));
            playerEvents.Unsubscribe(new InstancedEvent<TrickComplete.Data>.Delegate(LocalVehicle_TrickComplete));
            playerEvents.Unsubscribe(new InstancedEvent<Split.Data>.Delegate(LocalVehicle_Split));
            playerEvents.Unsubscribe(new InstancedEvent<CheckpointHit.Data>.Delegate(LocalVehicle_CheckpointPassed));
            playerEvents.Unsubscribe(new InstancedEvent<Impact.Data>.Delegate(LocalVehicle_Collided));
            playerEvents.Unsubscribe(new InstancedEvent<Death.Data>.Delegate(LocalVehicle_Destroyed));
            playerEvents.Unsubscribe(new InstancedEvent<Jump.Data>.Delegate(LocalVehicle_Jumped));
            playerEvents.Unsubscribe(new InstancedEvent<CarRespawn.Data>.Delegate(LocalVehicle_Respawn));
            playerEvents.Unsubscribe(new InstancedEvent<Events.Player.Finished.Data>.Delegate(LocalVehicle_Finished));
            playerEvents.Unsubscribe(new InstancedEvent<Explode.Data>.Delegate(LocalVehicle_Exploded));
            playerEvents.Unsubscribe(new InstancedEvent<Horn.Data>.Delegate(LocalVehicle_Honked));
        }

        private void FixedUpdate()
        {
            if (sw.IsRunning && active)
            {
                if (!localCar.ExistsAndIsEnabled())
                {
                    localCar = G.Sys.PlayerManager_.localPlayers_[0].playerData_.localCar_;
                    Logger.LogInfo("Subscribing to events...");
                    SubscribeToEvents();
                }
                car_rg = localCar.GetComponent<Rigidbody>();
                car_log = localCar.carLogic_;

                data = new Dictionary<string, object>
                {
                    ["Level"] = G.Sys.GameManager_.LevelName_,
                    ["Mode"] = G.Sys.GameManager_.ModeName_,
                    ["Real Time"] = DateTime.Now,
                    ["Time"] = sw.Elapsed.TotalSeconds,
                    ["Event"] = "update",
                    ["Speed_KPH"] = localCar.carStats_.GetKilometersPerHour(),
                    ["Speed_MPH"] = localCar.carStats_.GetMilesPerHour(),
                    ["Heat"] = car_log.heat_
                };
                Dictionary<string, object> position = new Dictionary<string, object>
                {
                    ["X"] = localCar.transform.position.x,
                    ["Y"] = localCar.transform.position.y,
                    ["Z"] = localCar.transform.position.z
                };
                Dictionary<string, object> rotation = new Dictionary<string, object>
                {
                    ["X"] = localCar.transform.rotation.x,
                    ["Y"] = localCar.transform.rotation.y,
                    ["Z"] = localCar.transform.rotation.z,
                    ["W"] = localCar.transform.rotation.w
                };
                Dictionary<string, object> velocity = new Dictionary<string, object>
                {
                    ["X"] = car_rg.velocity.x,
                    ["Y"] = car_rg.velocity.y,
                    ["Z"] = car_rg.velocity.z
                };
                Dictionary<string, object> angular_velocity = new Dictionary<string, object>
                {
                    ["X"] = car_rg.angularVelocity.x,
                    ["Y"] = car_rg.angularVelocity.y,
                    ["Z"] = car_rg.angularVelocity.z
                };
                Dictionary<string, object> inputs = new Dictionary<string, object>
                {
                    ["Boost"] = car_log.CarDirectives_.Boost_,
                    ["Steer"] = car_log.CarDirectives_.Steer_,
                    ["Grip"] = car_log.CarDirectives_.Grip_,
                    ["Gas"] = car_log.CarDirectives_.Gas_,
                    ["Brake"] = car_log.CarDirectives_.Brake_
                };
                Dictionary<string, object> rotation_ctl = new Dictionary<string, object>
                {
                    ["X"] = car_log.CarDirectives_.Rotation_.x,
                    ["Y"] = car_log.CarDirectives_.Rotation_.y,
                    ["Z"] = car_log.CarDirectives_.Rotation_.z
                };
                data["Pos"] = position;
                data["Rot"] = rotation;
                data["Vel"] = velocity;
                data["Ang Vel"] = angular_velocity;
                inputs["Rotation"] = rotation_ctl;
                data["Inputs"] = inputs;
                data["Grav"] = car_rg.useGravity;
                data["Drag"] = car_rg.drag;
                data["Angular Drag"] = car_rg.angularDrag;
                data["Wings"] = localCar.WingsActive_;
                data["Has Wings"] = localCar.WingsEnabled_;
                data["All Wheels Contacting"] = car_log.CarStats_.AllWheelsContacting_;
                data["Drive Wheel AVG Rot Vel"] = car_log.CarStats_.DriveWheelAvgRotVel_;
                data["Drive Wheel AVG RPM"] = car_log.CarStats_.DriveWheelAvgRPM_;

                Callback(data);
            }
        }

        //Normal Functions
        public void Callback(Dictionary<string, object> data)
        {
            data["Sender_ID"] = instance_id.ToString("B");
            data["Race_ID"] = race_id.ToString("B");

            writer.Settings.PrettyPrint = false;
            writer.Write(data, data_writer);
            data_writer.WriteLine();
            data_writer.Flush();
        }

        private void LocalVehicle_CheckpointPassed(CheckpointHit.Data eventData)
        {
            data = new Dictionary<string, object>
            {
                ["Level"] = G.Sys.GameManager_.LevelName_,
                ["Mode"] = G.Sys.GameManager_.ModeName_,
                ["Real Time"] = DateTime.Now,
                ["Time"] = sw.Elapsed.TotalSeconds,
                ["Event"] = "checkpoint",
                ["Checkpoint Index"] = eventData.handle_,
                ["TrackT"] = eventData.trackT_
            };
            Callback(data);
        }

        private void LocalVehicle_Collided(Impact.Data eventData)
        {
            data = new Dictionary<string, object>
            {
                ["Level"] = G.Sys.GameManager_.LevelName_,
                ["Mode"] = G.Sys.GameManager_.ModeName_,
                ["Real Time"] = DateTime.Now,
                ["Time"] = sw.Elapsed.TotalSeconds,
                ["Event"] = "collision",
                ["Target"] = eventData.impactedCollider_.name
            };
            Dictionary<string, object> position = new Dictionary<string, object>
            {
                ["X"] = eventData.pos_.x,
                ["Y"] = eventData.pos_.y,
                ["Z"] = eventData.pos_.z
            };
            data["Pos"] = position;
            data["Speed"] = eventData.speed_;
            Callback(data);
        }

        private void LocalVehicle_Destroyed(Death.Data eventData)
        {
            data = new Dictionary<string, object>
            {
                ["Level"] = G.Sys.GameManager_.LevelName_,
                ["Mode"] = G.Sys.GameManager_.ModeName_,
                ["Real Time"] = DateTime.Now,
                ["Time"] = sw.Elapsed.TotalSeconds,
                ["Event"] = "destroyed",
                ["Cause"] = eventData.causeOfDeath
            };
            Callback(data);
        }

        private void LocalVehicle_Exploded(Explode.Data eventData)
        {
            data = new Dictionary<string, object>
            {
                ["Level"] = G.Sys.GameManager_.LevelName_,
                ["Mode"] = G.Sys.GameManager_.ModeName_,
                ["Real Time"] = DateTime.Now,
                ["Time"] = sw.Elapsed.TotalSeconds,
                ["Event"] = "exploded",
                ["Cause"] = eventData.causeOfDeath
            };
            Callback(data);
        }

        private void LocalVehicle_Honked(Horn.Data eventData)
        {
            data = new Dictionary<string, object>
            {
                ["Level"] = G.Sys.GameManager_.LevelName_,
                ["Mode"] = G.Sys.GameManager_.ModeName_,
                ["Real Time"] = DateTime.Now,
                ["Time"] = sw.Elapsed.TotalSeconds,
                ["Event"] = "honked",
                ["Power"] = eventData.hornPercent_
            };
            Dictionary<string, object> position = new Dictionary<string, object>
            {
                ["X"] = eventData.position_.x,
                ["Y"] = eventData.position_.y,
                ["Z"] = eventData.position_.z
            };
            data["Pos"] = position;
            Callback(data);
        }

        private void LocalVehicle_Finished(Events.Player.Finished.Data eventData)
        {
            data = new Dictionary<string, object>
            {
                ["Level"] = G.Sys.GameManager_.LevelName_,
                ["Mode"] = G.Sys.GameManager_.ModeName_,
                ["Real Time"] = DateTime.Now,
                ["Time"] = sw.Elapsed.TotalSeconds,
                ["Event"] = "finish",
                ["Final Time"] = eventData.finishData_,
                ["Finish Type"] = eventData.finishType_
            };
            Callback(data);
        }

        private void LocalVehicle_Jumped(Jump.Data eventData)
        {
            data = new Dictionary<string, object>
            {
                ["Level"] = G.Sys.GameManager_.LevelName_,
                ["Mode"] = G.Sys.GameManager_.ModeName_,
                ["Real Time"] = DateTime.Now,
                ["Time"] = sw.Elapsed.TotalSeconds,
                ["Event"] = "jump"
            };
            Callback(data);
        }

        private void LocalVehicle_Respawn(CarRespawn.Data eventData)
        {
            data = new Dictionary<string, object>
            {
                ["Level"] = G.Sys.GameManager_.LevelName_,
                ["Mode"] = G.Sys.GameManager_.ModeName_,
                ["Real Time"] = DateTime.Now,
                ["Time"] = sw.Elapsed.TotalSeconds,
                ["Event"] = "respawn"
            };
            Dictionary<string, object> position = new Dictionary<string, object>
            {
                ["X"] = eventData.position_.x,
                ["Y"] = eventData.position_.y,
                ["Z"] = eventData.position_.z
            };
            Dictionary<string, object> rotation = new Dictionary<string, object>
            {
                ["Pitch"] = eventData.rotation_.eulerAngles.x,
                ["Roll"] = eventData.rotation_.eulerAngles.z,
                ["Yaw"] = eventData.rotation_.eulerAngles.y
            };
            data["Pos"] = position;
            data["Rot"] = rotation;
            Callback(data);
        }

        private void LocalVehicle_Split(Split.Data eventData)
        {
            data = new Dictionary<string, object>
            {
                ["Level"] = G.Sys.GameManager_.LevelName_,
                ["Mode"] = G.Sys.GameManager_.ModeName_,
                ["Real Time"] = DateTime.Now,
                ["Time"] = sw.Elapsed.TotalSeconds,
                ["Event"] = "split",
                ["Penetration"] = eventData.penetration,
                ["Separation Speed"] = eventData.separationSpeed
            };
            Callback(data);
        }

        private void LocalVehicle_TrickComplete(TrickComplete.Data eventData)
        {
            data = new Dictionary<string, object>
            {
                ["Level"] = G.Sys.GameManager_.LevelName_,
                ["Mode"] = G.Sys.GameManager_.ModeName_,
                ["Real Time"] = DateTime.Now,
                ["Time"] = sw.Elapsed.TotalSeconds,
                ["Event"] = "trick",
                ["Points"] = eventData.points_,
                ["Cooldown"] = eventData.cooldownAmount_,
                ["Grind"] = eventData.grindMeters_,
                ["Wallride"] = eventData.wallRideMeters_,
                ["Ceiling"] = eventData.ceilingRideMeters_
            };
            Callback(data);
        }

        private void OnConfigChanged(object sender, EventArgs e)
        {
            SettingChangedEventArgs settingChangedEventArgs = e as SettingChangedEventArgs;

            if (settingChangedEventArgs == null) return;
        }

        private void OnGamePaused(PauseToggled.Data eventData)
        {
            if (eventData.paused_)
            {
                active = false;
            }
            else
            {
                active = true;
            }
        }

        private void RaceStarted(Go.Data eventData)
        {
            Log.LogInfo("[Telemetry] Starting...");
            race_id = Guid.NewGuid();
            if (sw.IsRunning)
            {
                sw.Stop();
            }
            sw = Stopwatch.StartNew();
            active = true;
            data = new Dictionary<string, object>
            {
                ["Level"] = G.Sys.GameManager_.LevelName_,
                ["Mode"] = G.Sys.GameManager_.ModeName_,
                ["Real Time"] = DateTime.Now,
                ["Event"] = "start",
                ["Time"] = sw.Elapsed.TotalSeconds
            };
            Callback(data);
        }

        private void RaceEnded(LocalCarHitFinish.Data eventData)
        {
            Log.LogInfo("{Telemetry] Finished...");
            data = new Dictionary<string, object>
            {
                ["Level"] = G.Sys.GameManager_.LevelName_,
                ["Mode"] = G.Sys.GameManager_.ModeName_,
                ["Real Time"] = DateTime.Now,
                ["Event"] = "end",
                ["Time"] = sw.Elapsed.TotalSeconds
            };
            sw.Stop();
            active = false;
            Callback(data);
        }

        private void SubscribeToEvents()
        {
            playerEvents = localCar.playerDataLocal_.Events_;
            playerEvents.Subscribe(new InstancedEvent<TrickComplete.Data>.Delegate(LocalVehicle_TrickComplete));
            playerEvents.Subscribe(new InstancedEvent<Split.Data>.Delegate(LocalVehicle_Split));
            playerEvents.Subscribe(new InstancedEvent<CheckpointHit.Data>.Delegate(LocalVehicle_CheckpointPassed));
            playerEvents.Subscribe(new InstancedEvent<Impact.Data>.Delegate(LocalVehicle_Collided));
            playerEvents.Subscribe(new InstancedEvent<Death.Data>.Delegate(LocalVehicle_Destroyed));
            playerEvents.Subscribe(new InstancedEvent<Jump.Data>.Delegate(LocalVehicle_Jumped));
            playerEvents.Subscribe(new InstancedEvent<CarRespawn.Data>.Delegate(LocalVehicle_Respawn));
            playerEvents.Subscribe(new InstancedEvent<Events.Player.Finished.Data>.Delegate(LocalVehicle_Finished));
            playerEvents.Subscribe(new InstancedEvent<Explode.Data>.Delegate(LocalVehicle_Exploded));
            playerEvents.Subscribe(new InstancedEvent<Horn.Data>.Delegate(LocalVehicle_Honked));
        }
    }
}
