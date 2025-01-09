using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using Events;
using Events.Car;
using Events.Game;
using Events.GameMode;
using Events.Player;
using Events.RaceEnd;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using UnityEngine;

namespace DistanceTelemetryPlugin
{
    [BepInPlugin(modGUID, modName, modVersion)]
    public sealed class Mod : BaseUnityPlugin, IDisposable
    {
        //Mod Details
        private const string modGUID = "Distance.TelemetryPlugin";
        private const string modName = "Distance Telemetry Plugin";
        private const string modVersion = "1.2.0";

        //Config Entry Settings
        public static string FilePrefixKey = "File Prefix";
        public static string UdpHostKey = "Connection Host";
        public static string UdpPortKey = "Connection Port";

        //Config Entries
        public static ConfigEntry<string> FilePrefix { get; set; }
        public static ConfigEntry<string> UdpHost { get; set; }
        public static ConfigEntry<int> UdpPort { get; set; }

        //Public Variables
        public bool active = false;
        public StreamWriter logFile;

        //Private Variables
        private CarLogic car_log;
        private Telemetry data;
        private FileSystem fs = new FileSystem();
        FileStream fileStream;



        private Guid instance_id;
        private Guid race_id;
        
        private LocalPlayerControlledCar localCar;
        private PlayerEvents playerEvents;
        private Rigidbody car_rg;
        private Stopwatch sw;
        private TextWriter data_writer;
        private UdpClient udpClient;


        //Other
        public static ManualLogSource Log;
        public static Mod Instance;

        
        public Mod()
        {
            udpClient = new UdpClient();
            sw = new Stopwatch();
            Log = new ManualLogSource(modName);
            
        }

        //Unity MonoBehaviour Functions
        private void Awake()
        {
            
            if (Instance == null)
            {
                Instance = this;
            }

            
            instance_id = Guid.NewGuid();
            Log = BepInEx.Logging.Logger.CreateLogSource(modGUID);
            Logger.LogInfo("Thanks for using the Distance Telemtry Plugin!");
            Logger.LogInfo("[Telemetry] Initializing...");
            Logger.LogInfo($"[Telemetry] Instance ID {instance_id:B}...");

            //Config Setup
            FilePrefix = Config.Bind("General",
                FilePrefixKey,
                "Telemetry",
                new ConfigDescription("The name of the json file that the telemetry writes to"));

            UdpHost = Config.Bind("General",
                UdpHostKey,
                "",
                new ConfigDescription("The host name to connect to for streaming data over UDP (\"\" to disable UDP streaming, \"127.0.0.1\" for localhost)"));

            UdpPort = Config.Bind("General",
                UdpPortKey,
                12345,
                new ConfigDescription("The port number to connect to for streaming data over UDP (-1 to disable UDP streaming, defaults to 12345)"));

            //Tries to connect to host, if it can't then it outputs the telemetry to the jsonl file.
            if (!TryConnectToHost())
            {
                string telemetryDirectory = fs.CreateDirectory("Telemetry");

                var logFIle = Path.Combine(telemetryDirectory, $"{FilePrefix.Value}_{string.Format("{0:yyyy-MM-dd_HH-mm-ss}", DateTime.Now)}.jsonl");
                Log.LogInfo($"[Telemetry] Writing to {logFIle}...");
                
                fileStream = fs.CreateFile(logFIle);

                data_writer = new StreamWriter(fileStream);
                
                Logger.LogInfo($"[Telemetry] Opening new filestream for {fileStream}...");
            }

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
            
            StaticEvent<LocalCarHitFinish.Data>.Unsubscribe(new StaticEvent<LocalCarHitFinish.Data>.Delegate(RaceEnded));
            StaticEvent<Go.Data>.Unsubscribe(new StaticEvent<Go.Data>.Delegate(RaceStarted));
            StaticEvent<PauseToggled.Data>.Unsubscribe(new StaticEvent<PauseToggled.Data>.Delegate(OnGamePaused));
            UnSubscribeFromEvents();
        }

        private void FixedUpdate()
        {
            if (sw.IsRunning && active)
            {
                if(localCar == null)
                {
                    Logger.LogInfo("Local Car is null, trying to find it...");
                }

                if (!localCar.ExistsAndIsEnabled())
                {
                    localCar = G.Sys.PlayerManager_.localPlayers_[0].playerData_.localCar_;

                    Logger.LogInfo("Subscribing to events...");
                    SubscribeToEvents();
                }
                car_rg = localCar.GetComponent<Rigidbody>();
                car_log = localCar.carLogic_;

                data = new UpdateTelemetry
                {
                    Level = G.Sys.GameManager_.LevelName_,
                    Mode = G.Sys.GameManager_.ModeName_,
                    RealTime = DateTime.Now,
                    Time = sw.Elapsed.TotalSeconds,
                    Speed_KPH = localCar.carStats_.GetKilometersPerHour(),
                    Speed_MPH = localCar.carStats_.GetMilesPerHour(),
                    Heat = car_log.heat_,
                    Pos = localCar.transform.position,
                    Rot = localCar.transform.rotation,
                    EulRot = localCar.transform.rotation.eulerAngles,
                    Vel = car_rg.velocity,
                    AngVel = car_rg.angularVelocity,
                    Inputs = new Inputs
                    {
                        Boost = car_log.CarDirectives_.Boost_,
                        Steer = car_log.CarDirectives_.Steer_,
                        Grip = car_log.CarDirectives_.Grip_,
                        Gas = car_log.CarDirectives_.Gas_,
                        Brake = car_log.CarDirectives_.Brake_,
                        Rotation = new Vector3(car_log.CarDirectives_.Rotation_.x, car_log.CarDirectives_.Rotation_.y, car_log.CarDirectives_.Rotation_.z)
                    },
                    Grav = car_rg.useGravity,
                    Drag = car_rg.drag,
                    AngularDrag = car_rg.angularDrag,
                    Wings = localCar.WingsActive_,
                    HasWings = localCar.WingsEnabled_,
                    AllWheelsContacting = car_log.CarStats_.AllWheelsContacting_,
                    Tires = new Tires
                    {
                        TireFL = new Tire
                        {
                            Pos = car_log.CarStats_.WheelFL_.hubTrans_.position.y,
                            Contact = car_log.CarStats_.WheelFL_.IsInContact_,
                            Suspension = car_log.CarStats_.WheelFL_.SuspensionDistance_
                        },
                        TireFR = new Tire
                        {
                            Pos = car_log.CarStats_.WheelFR_.hubTrans_.position.y,
                            Contact = car_log.CarStats_.WheelFR_.IsInContact_,
                            Suspension = car_log.CarStats_.WheelFR_.SuspensionDistance_
                        },
                        TireBL = new Tire
                        {
                            Pos = car_log.CarStats_.WheelBL_.hubTrans_.position.y,
                            Contact = car_log.CarStats_.WheelBL_.IsInContact_,
                            Suspension = car_log.CarStats_.WheelBL_.SuspensionDistance_
                        },
                        TireBR = new Tire
                        {
                            Pos = car_log.CarStats_.WheelBR_.hubTrans_.position.y,
                            Contact = car_log.CarStats_.WheelBR_.IsInContact_,
                            Suspension = car_log.CarStats_.WheelBR_.SuspensionDistance_
                        }
                    },
                    DriveWheelAvgRotVel = car_log.CarStats_.DriveWheelAvgRotVel_,
                    DriveWheelAvgRPM = car_log.CarStats_.DriveWheelAvgRPM_
                };

                Callback(data);
            }
        }

        //Normal Functions
        public void Callback(Telemetry data)
        {
            data.Sender_ID = instance_id.ToString("B");
            data.Race_ID = race_id.ToString("B");

            var json = JsonConvert.SerializeObject(data,new JsonSerializerSettings { Converters = new[] { new StringEnumConverter() }, ReferenceLoopHandling = ReferenceLoopHandling.Ignore });

            //Checking whether or not it's connected to the host. Attempts a reconnect before continuing. 
            if (udpClient.Client.Connected)
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(json);
                udpClient.Send(bytes, bytes.Length);
            }
            else
            {

                if (UdpPort.Value != -1 && UdpHost.Value != "")
                {
                    Logger.LogInfo("[Telemetry] Reconnecting...");
                    if (TryConnectToHost())
                    {
                        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
                        udpClient.Send(bytes, bytes.Length);
                    }
                }
                else if (data_writer != null && (fileStream?.CanWrite ?? false)) //Write to the file
                {
                    data_writer.WriteLine(json);                    
                    data_writer.Flush();

                }
            }
        }

        private void LocalVehicle_CheckpointPassed(CheckpointHit.Data eventData)
        {
            data = new CheckpointTelmetry
            {
                Level = G.Sys.GameManager_.LevelName_,
                Mode = G.Sys.GameManager_.ModeName_,
                RealTime = DateTime.Now,
                Time = sw.Elapsed.TotalSeconds,
                CheckpointIndex = eventData.handle_.id_,
                TrackT = eventData.trackT_
            };
            Callback(data);
        }

        private void LocalVehicle_Collided(Impact.Data eventData)
        {
            data = new CollisionTelemetry
            {
                Level = G.Sys.GameManager_.LevelName_,
                Mode = G.Sys.GameManager_.ModeName_,
                RealTime = DateTime.Now,
                Time = sw.Elapsed.TotalSeconds,
                Target = eventData.impactedCollider_.name,
                Pos = eventData.pos_,
                Speed = eventData.speed_
            };
            Callback(data);
        }

        private void LocalVehicle_Destroyed(Death.Data eventData)
        {
            data = new ExplodedDestroyedTelemetry
            {
                Level = G.Sys.GameManager_.LevelName_,
                Mode = G.Sys.GameManager_.ModeName_,
                RealTime = DateTime.Now,
                Time = sw.Elapsed.TotalSeconds,
                Cause = eventData.causeOfDeath
            };
            Callback(data);
        }

        private void LocalVehicle_Exploded(Explode.Data eventData)
        {
            data = new ExplodedDestroyedTelemetry
            {
                Level = G.Sys.GameManager_.LevelName_,
                Mode = G.Sys.GameManager_.ModeName_,
                RealTime = DateTime.Now,
                Time = sw.Elapsed.TotalSeconds,
                Cause = eventData.causeOfDeath
            };
            Callback(data);
        }

        private void LocalVehicle_Honked(Horn.Data eventData)
        {
            data = new HonkedTelemetry
            {
                Level = G.Sys.GameManager_.LevelName_,
                Mode = G.Sys.GameManager_.ModeName_,
                RealTime = DateTime.Now,
                Time = sw.Elapsed.TotalSeconds,
                Power = eventData.hornPercent_,
                Pos = eventData.position_
            };
            Callback(data);
        }

        private void LocalVehicle_Finished(Events.Player.Finished.Data eventData)
        {
            data = new FinishTelemetry
            {
                Level = G.Sys.GameManager_.LevelName_,
                Mode = G.Sys.GameManager_.ModeName_,
                RealTime = DateTime.Now,
                Time = sw.Elapsed.TotalSeconds,
                FinalTime = eventData.finishData_,
                FinishType = eventData.finishType_
            };
            Callback(data);
        }

        private void LocalVehicle_Jumped(Jump.Data eventData)
        {
            data = new JumpTelemetry
            {
                Level = G.Sys.GameManager_.LevelName_,
                Mode = G.Sys.GameManager_.ModeName_,
                RealTime = DateTime.Now,
                Time = sw.Elapsed.TotalSeconds,
            };
            Callback(data);
        }

        private void LocalVehicle_Respawn(CarRespawn.Data eventData)
        {
            data = new RespawnTelemetry
            {
                Level = G.Sys.GameManager_.LevelName_,
                Mode = G.Sys.GameManager_.ModeName_,
                RealTime = DateTime.Now,
                Time = sw.Elapsed.TotalSeconds,                
                Pos = eventData.position_,
                Rot = eventData.rotation_,
                EulRot = eventData.rotation_.eulerAngles
            };
            Callback(data);
        }

        private void LocalVehicle_Split(Split.Data eventData)
        {
            data = new SplitTelemetry
            {
                Level = G.Sys.GameManager_.LevelName_,
                Mode = G.Sys.GameManager_.ModeName_,
                RealTime = DateTime.Now,
                Time = sw.Elapsed.TotalSeconds,                
                Penetration = eventData.penetration,
                SeparationSpeed = eventData.separationSpeed
            };
            Callback(data);
        }

        private void LocalVehicle_TrickComplete(TrickComplete.Data eventData)
        {
            data = new TrickTelemetry
            {
                Level = G.Sys.GameManager_.LevelName_,
                Mode = G.Sys.GameManager_.ModeName_,
                RealTime = DateTime.Now,
                Time = sw.Elapsed.TotalSeconds,                
                Points = eventData.points_,
                Cooldown = eventData.cooldownAmount_,
                Grind = eventData.grindMeters_,
                Wallride = eventData.wallRideMeters_,
                Ceiling = eventData.ceilingRideMeters_
            };
            Callback(data);
        }

        private void OnConfigChanged(object sender, EventArgs e)
        {
            SettingChangedEventArgs settingChangedEventArgs = e as SettingChangedEventArgs;

            if (settingChangedEventArgs == null) return;

            TryConnectToHost();
        }

        private void OnGamePaused(PauseToggled.Data eventData)
        {
            if (eventData.paused_)
            {
                active = false;
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
            data = new RaceStartedTelemetry
            {
                Level = G.Sys.GameManager_.LevelName_,
                Mode = G.Sys.GameManager_.ModeName_,
                RealTime = DateTime.Now,
                Time = sw.Elapsed.TotalSeconds
            };
            Callback(data);
        }

        private void RaceEnded(LocalCarHitFinish.Data eventData)
        {
            Log.LogInfo("{Telemetry] Finished...");
            data = new RaceEndedTelemetry
            {
                Level = G.Sys.GameManager_.LevelName_,
                Mode = G.Sys.GameManager_.ModeName_,
                RealTime = DateTime.Now,
                Time = sw.Elapsed.TotalSeconds
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

        private void UnSubscribeFromEvents()
        {
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

        private bool TryConnectToHost()
        {
            if (UdpPort.Value != -1 && UdpHost.Value != "")
            {
                Logger.LogInfo($"[Telemetry] Connecting to {UdpHost.Value}:{UdpPort}...");
                try
                {
                    udpClient.Connect(UdpHost.Value, UdpPort.Value);
                }
                catch (Exception ex)
                {
                    Logger.LogInfo($"[Telemetry] Failed to Connect to {UdpHost.Value}:{UdpPort}...");
                    Logger.LogInfo(ex);
                    Logger.LogInfo("\nTelemetry Disabled, if you want to write to the jsonl file, please set the host to an empty string and the port to -1 \n[Telemetry]");
                    return false;
                }
                Logger.LogInfo("[Telemetry] Connected!");
                return true;
            }
            return false;
        }

        public void Dispose()
        {
            try
            {
                fileStream?.Dispose();
                udpClient?.Close();
                data_writer?.Dispose();
            }
            catch (Exception ex)
            {
                Logger.LogError($"[Telemetry] Failed to dispose of filestream...");
                Logger.LogError(ex);

            }
        }
    }
    
    
}
