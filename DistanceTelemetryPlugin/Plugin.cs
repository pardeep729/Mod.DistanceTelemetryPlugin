using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using Events;
using Events.Car;
using Events.Game;
using Events.GameMode;
using Events.Player;
using Events.RaceEnd;
using Events.Scene;
using HarmonyLib;
using JsonFx.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
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
        public static string UdpHostKey = "UDP Host";
        public static string UdpPortKey = "UDP Port";
        public static string WriteIntervalKey = "Write Interval (Seconds)";

        //Config Entries
        public static ConfigEntry<string> FilePrefix { get; set; }
        public static ConfigEntry<string> UdpHost { get; set; }
        public static ConfigEntry<int> UdpPort { get; set; }
        public static ConfigEntry<float> WriteInterval { get; set; }

        //Public Variables
        public bool active = false;
        public bool subscribed = false;
        public StreamWriter logFile;

        //Private Variables
        private CarLogic car_log;
        private CarStats car_stats;
        private bool wingsEnabled;
        private Dictionary<string, object> data;
        private FileSystem fs = new FileSystem();
        FileStream fileStream;
        private Guid instance_id;
        private Guid race_id;
        private JsonWriter writer = new JsonWriter();
        private LocalPlayerControlledCar localCar;
        private PlayerEvents playerEvents;
        private Rigidbody car_rg;
        private Stopwatch sw;
        private TextWriter data_writer;
        private UdpClient udpClient;
        private Vector3 car_pyr;

        //Other
        public static ManualLogSource Log;
        public static Mod Instance;
        private readonly Queue<Dictionary<string, object>> telemetryQueue = new Queue<Dictionary<string, object>>();
        private readonly object queueLock = new object();
        private Thread fileWriterThread;
        private bool isRunning = true;

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

            writer.Settings.PrettyPrint = true;
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
                new ConfigDescription("The host name to connect to for streaming data over UDP (Use an empty string (\"\") to disable UDP streaming, \"127.0.0.1\" for localhost)"));

            UdpPort = Config.Bind("General",
                UdpPortKey,
                12345,
                new ConfigDescription("The port number to connect to for streaming data over UDP (-1 to disable UDP streaming, defaults to 12345)"));

            WriteInterval = Config.Bind("General",
                WriteIntervalKey,
                1.0f, // Default 1 second
                new ConfigDescription("How often to write telemetry data in seconds (e.g., 1.0 = once every second)"));
            //Tries to connect to host, if it can't then it outputs the telemetry to the jsonl file.
            if (!TryConnectToHost())
            {
                string telemetryDirectory = fs.CreateDirectory("Telemetry");

                var logFile = Path.Combine(telemetryDirectory, $"{FilePrefix.Value}_{string.Format("{0:yyyy-MM-dd_HH-mm-ss}", DateTime.Now)}.jsonl");
                Log.LogInfo($"[Telemetry] Writing to {logFile}...");

                fileStream = new FileStream(logFile, FileMode.Create, FileAccess.Write, FileShare.Read, 4096, FileOptions.WriteThrough);

                data_writer = new StreamWriter(fileStream);

                Logger.LogInfo($"[Telemetry] Opening new filestream for {fileStream}...");
            }

            UdpHost.SettingChanged += OnConfigChanged;
            UdpPort.SettingChanged += OnConfigChanged;

            //Apply Patches
            var harmony = new Harmony("distance.telemetryplugin.accessors");
            harmony.PatchAll(); // Apply all Harmony patches (not actually used for field access but good to initialize anyway)
            Logger.LogInfo("Harmony patches applied.");

            StartFileWriterThread();
            Logger.LogInfo("Loaded!");
        }

        /// <summary>
        /// Starts a background thread to write telemetry data at regular intervals.
        /// </summary>
        private void StartFileWriterThread()
        {
            fileWriterThread = new Thread(() =>
            {
                while (isRunning)
                {
                    Dictionary<string, object>[] batch;
                    lock (queueLock)
                    {
                        batch = telemetryQueue.ToArray();
                        telemetryQueue.Clear();
                    }

                    foreach (var item in batch)
                    {
                        Callback(item);
                    }

                    // Sleep for the configured interval (convert seconds to milliseconds)
                    Thread.Sleep((int)(WriteInterval.Value * 1000));
                }
            });

            fileWriterThread.IsBackground = true;
            fileWriterThread.Start();
        }


        private void OnEnable()
        {
            StaticEvent<LocalCarHitFinish.Data>.Subscribe(new StaticEvent<LocalCarHitFinish.Data>.Delegate(RaceEnded));
            StaticEvent<Go.Data>.Subscribe(new StaticEvent<Go.Data>.Delegate(RaceStarted));
            StaticEvent<PauseToggled.Data>.Subscribe(new StaticEvent<PauseToggled.Data>.Delegate(OnGamePaused));
            StaticEvent<BeginSceneSwitchFadeOut.Data>.Subscribe(new StaticEvent<BeginSceneSwitchFadeOut.Data>.Delegate(OnSceneSwitch));
        }

        private void OnDisable()
        {
            StaticEvent<LocalCarHitFinish.Data>.Unsubscribe(new StaticEvent<LocalCarHitFinish.Data>.Delegate(RaceEnded));
            StaticEvent<Go.Data>.Unsubscribe(new StaticEvent<Go.Data>.Delegate(RaceStarted));
            StaticEvent<PauseToggled.Data>.Unsubscribe(new StaticEvent<PauseToggled.Data>.Delegate(OnGamePaused));
            StaticEvent<BeginSceneSwitchFadeOut.Data>.Unsubscribe(new StaticEvent<BeginSceneSwitchFadeOut.Data>.Delegate(OnSceneSwitch));
            UnSubscribeFromEvents();
        }

        private void FixedUpdate()
        {
            if (sw.IsRunning && active)
            {
                if (!localCar.ExistsAndIsEnabled())
                {
                    localCar = G.Sys.PlayerManager_.LocalPlayers_[0].playerData_.LocalCar_;

                    if (!subscribed)
                    {
                        Logger.LogInfo("Subscribing to events...");
                        SubscribeToEvents();
                    }
                }

                car_rg = localCar.GetComponent<Rigidbody>();
                car_log = Accessors.GetCarLogic(localCar);
                car_pyr = PitchYawRoll(localCar.transform.rotation);
                car_stats = Accessors.GetCarStats(localCar);

                data = new Dictionary<string, object>
                {
                    ["Level"] = G.Sys.GameManager_.LevelName_,
                    ["Mode"] = G.Sys.GameManager_.ModeName_,
                    ["Real Time"] = DateTime.Now,
                    ["Time"] = sw.Elapsed.TotalSeconds,
                    ["Event"] = "update",
                    ["Speed_KPH"] = car_stats.GetKilometersPerHour(),
                    ["Speed_MPH"] = car_stats.GetMilesPerHour(),
                    ["Heat"] = car_log.Heat_
                };
                Dictionary<string, object> position = new Dictionary<string, object>
                {
                    ["X"] = localCar.transform.position.x,
                    ["Y"] = localCar.transform.position.y,
                    ["Z"] = localCar.transform.position.z
                };
                Dictionary<string, object> quaternion = new Dictionary<string, object>
                {
                    ["X"] = localCar.transform.rotation.x,
                    ["Y"] = localCar.transform.rotation.y,
                    ["Z"] = localCar.transform.rotation.z,
                    ["W"] = localCar.transform.rotation.w
                };
                Dictionary<string, object> rotation = new Dictionary<string, object>
                {
                    ["Pitch"] = car_pyr.x,
                    ["Yaw"] = car_pyr.y,
                    ["Roll"] = car_pyr.z
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
                Dictionary<string, object> tire_fl = new Dictionary<string, object>
                {
                    ["Pos"] = car_log.CarStats_.WheelFL_.hubTrans_.position.y,
                    ["Contact"] = car_log.CarStats_.WheelFL_.IsInContact_,
                    ["Suspension"] = car_log.CarStats_.WheelFL_.SuspensionDistance_
                };
                Dictionary<string, object> tire_fr = new Dictionary<string, object>
                {
                    ["Pos"] = car_log.CarStats_.WheelFR_.hubTrans_.position.y,
                    ["Contact"] = car_log.CarStats_.WheelFR_.IsInContact_,
                    ["Suspension"] = car_log.CarStats_.WheelFR_.SuspensionDistance_
                };
                Dictionary<string, object> tire_bl = new Dictionary<string, object>
                {
                    ["Pos"] = car_log.CarStats_.WheelBL_.hubTrans_.position.y,
                    ["Contact"] = car_log.CarStats_.WheelBL_.IsInContact_,
                    ["Suspension"] = car_log.CarStats_.WheelBL_.SuspensionDistance_
                };
                Dictionary<string, object> tire_br = new Dictionary<string, object>
                {
                    ["Pos"] = car_log.CarStats_.WheelBR_.hubTrans_.position.y,
                    ["Contact"] = car_log.CarStats_.WheelBR_.IsInContact_,
                    ["Suspension"] = car_log.CarStats_.WheelBR_.SuspensionDistance_
                };
                Dictionary<string, object> tires = new Dictionary<string, object>
                {
                    ["TireFL"] = tire_fl,
                    ["TireFR"] = tire_fr,
                    ["TireBL"] = tire_bl,
                    ["TireBR"] = tire_br
                };
                data["Pos"] = position;
                data["Quaternion"] = quaternion;
                data["Rot"] = rotation;
                data["Vel"] = velocity;
                data["Ang Vel"] = angular_velocity;
                inputs["Rotation"] = rotation_ctl;
                data["Inputs"] = inputs;
                data["Grav"] = car_rg.useGravity;
                data["Drag"] = car_rg.drag;
                data["Angular Drag"] = car_rg.angularDrag;
                data["Wings"] = localCar.WingsActive_;
                wingsEnabled = Accessors.GetWingsEnabled(localCar);
                data["Has Wings"] = wingsEnabled;
                data["All Wheels Contacting"] = car_log.CarStats_.AllWheelsContacting_;
                data["Tires"] = tires;
                data["Drive Wheel AVG Rot Vel"] = car_log.CarStats_.DriveWheelAvgRotVel_;
                data["Drive Wheel AVG RPM"] = car_log.CarStats_.DriveWheelAvgRPM_;

                // Callback
                lock (queueLock)
                {
                    telemetryQueue.Enqueue(data);
                }
            }
        }

        //Normal Functions
        /// <summary>
        /// Callback function that is called when an event is triggered.
        /// It sends the data to the UDP client or writes it to the file.
        /// </summary>
        /// <param name="data">Dictionary of data to be stored</param>
        public void Callback(Dictionary<string, object> data)
        {
            data["Sender_ID"] = instance_id.ToString("B");
            data["Race_ID"] = race_id.ToString("B");

            writer.Settings.PrettyPrint = false;

            string json = writer.Write(data);
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(json);

            // Try UDP
            if (udpClient.Client.Connected)
            {
                udpClient.Send(bytes, bytes.Length);
            }
            else if (UdpPort.Value != -1 && UdpHost.Value != "")
            {
                Logger.LogInfo("[Telemetry] Reconnecting...");
                if (TryConnectToHost())
                {
                    udpClient.Send(bytes, bytes.Length);
                }
            }
            else if (data_writer != null && (fileStream?.CanWrite ?? false))
            {
                data_writer.WriteLine(json);
                data_writer.Flush();
            }
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
                ["Checkpoint Index"] = eventData.handle_.ToString(),
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
            active = false;

            data = new Dictionary<string, object>
            {
                ["Level"] = G.Sys.GameManager_.LevelName_,
                ["Mode"] = G.Sys.GameManager_.ModeName_,
                ["Real Time"] = DateTime.Now,
                ["Time"] = sw.Elapsed.TotalSeconds,
                ["Event"] = "destroyed",
                ["Cause"] = eventData.causeOfDeath.ToString()
            };
            Callback(data);
        }

        private void LocalVehicle_Exploded(Explode.Data eventData)
        {
            active = false;

            data = new Dictionary<string, object>
            {
                ["Level"] = G.Sys.GameManager_.LevelName_,
                ["Mode"] = G.Sys.GameManager_.ModeName_,
                ["Real Time"] = DateTime.Now,
                ["Time"] = sw.Elapsed.TotalSeconds,
                ["Event"] = "exploded",
                ["Cause"] = eventData.causeOfDeath.ToString()
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
                ["Finish Type"] = eventData.finishType_.ToString()
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
            Vector3 pyr = PitchYawRoll(eventData.rotation_);
            active = true;

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
            Dictionary<string, object> quaternion = new Dictionary<string, object>
            {
                ["X"] = eventData.rotation_.x,
                ["Y"] = eventData.rotation_.y,
                ["Z"] = eventData.rotation_.z,
                ["W"] = eventData.rotation_.w
            };
            Dictionary<string, object> rotation = new Dictionary<string, object>
            {
                ["Pitch"] = pyr.x,
                ["Roll"] = pyr.z,
                ["Yaw"] = pyr.y
            };
            data["Pos"] = position;
            data["Quaternion"] = quaternion;
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

            TryConnectToHost();
        }

        private void OnGamePaused(PauseToggled.Data eventData)
        {
            if (eventData.paused_)
            {
                Log.LogInfo("{Telemetry] Paused...");
                active = false;
            }
            else
            {
                Log.LogInfo("{Telemetry] Resume...");
                active = true;
            }
        }

        private void OnSceneSwitch(BeginSceneSwitchFadeOut.Data eventData)
        {
            Log.LogInfo("{Telemetry] Finished...");
            active = false;
            subscribed = false;
            sw.Stop();
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
            subscribed = false;
            Callback(data);
        }

        private void SubscribeToEvents()
        {
            playerEvents = localCar.PlayerDataLocal_.Events_;
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
            subscribed = true;
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

        public static Vector3 PitchYawRoll(Quaternion q)
        {
            var yaw = (float)Math.Atan2(2 * q.y * q.w - 2 * q.x * q.z, 1 - 2 * q.y * q.y - 2 * q.z * q.z) * Mathf.Rad2Deg;
            var pitch = (float)Math.Atan2(2 * q.x * q.w - 2 * q.y * q.z, 1 - 2 * q.x * q.x - 2 * q.z * q.z) * Mathf.Rad2Deg;
            var roll = (float)Math.Asin(2 * q.x * q.y + 2 * q.z * q.w) * Mathf.Rad2Deg;

            return new Vector3(pitch, yaw, roll);
        }

        public void Dispose()
        {
            try
            {
                isRunning = false;
                fileWriterThread?.Join(); // Wait for thread to finish cleanly

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
