﻿using System.Collections.Generic;
using System.Xml.Serialization;
using ProtoBuf;
using System.ComponentModel;
using System.Diagnostics;
using VRage.Utils;
using System.ComponentModel.DataAnnotations;
using VRage.ObjectBuilders;
using VRage.Serialization;

namespace VRage.Game
{
    [ProtoContract]
    [MyObjectBuilderDefinition]
    public class MyObjectBuilder_SessionSettings : MyObjectBuilder_Base
    {
        [XmlIgnore]
        public const uint DEFAULT_AUTOSAVE_IN_MINUTES = 5;

        [ProtoMember]
        [Display(Name = "Game mode")]
        [GameRelation(Game.Shared)]
        public MyGameModeEnum GameMode = MyGameModeEnum.Creative;

        [ProtoMember]
        [Display(Name = "Inventory size multiplier")]
        [GameRelation(Game.Shared)]
        public float InventorySizeMultiplier = 10;

        [ProtoMember]
        [Display(Name = "Assembler speed multiplier")]
        [GameRelation(Game.SpaceEngineers)]
        public float AssemblerSpeedMultiplier = 3;

        [ProtoMember]
        [Display(Name = "Assembler efficiency multiplier")]
        [GameRelation(Game.SpaceEngineers)]
        public float AssemblerEfficiencyMultiplier = 3;

        [ProtoMember]
        [Display(Name = "Refinery speed multiplier")]
        [GameRelation(Game.SpaceEngineers)]
        public float RefinerySpeedMultiplier = 3;

        [ProtoMember]
        public MyOnlineModeEnum OnlineMode = MyOnlineModeEnum.PRIVATE;

        [ProtoMember]
        [Display(Name = "Max players")]
        [GameRelation(Game.Shared)]
        [Range(2, int.MaxValue)]
        public short MaxPlayers = 4;

        [ProtoMember]
        [Display(Name = "Max floating objects")]
        [GameRelation(Game.SpaceEngineers)]
        [Range(2, int.MaxValue)]
        public short MaxFloatingObjects = 56;

        [ProtoMember]
        [Display(Name = "Environment hostility")]
        [GameRelation(Game.SpaceEngineers)]
        // Only used in quickstart - Scenarios have there own Settings
        public MyEnvironmentHostilityEnum EnvironmentHostility = MyEnvironmentHostilityEnum.NORMAL;

        [ProtoMember]
        [Display(Name = "Auto healing")]
        [GameRelation(Game.SpaceEngineers)]
        public bool AutoHealing = true;

        [ProtoMember]
        [Display(Name = "Enable Copy&Paste")]
        [GameRelation(Game.Shared)]
        public bool EnableCopyPaste = true;

        //[ProtoMember]
        public bool AutoSave
        {
            get { Debug.Fail("Obsolete."); return AutoSaveInMinutes > 0; }
            set { AutoSaveInMinutes = value ? DEFAULT_AUTOSAVE_IN_MINUTES : 0; }
        }
        public bool ShouldSerializeAutoSave() { return false; }

        [ProtoMember]
        [Display(Name = "Weapons enabled")]
        [GameRelation(Game.SpaceEngineers)]
        public bool WeaponsEnabled = true;

        [ProtoMember]
        [Display(Name = "Show player names on HUD")]
        [GameRelation(Game.SpaceEngineers)]
        public bool ShowPlayerNamesOnHud = true;

        [ProtoMember]
        [Display(Name = "Thruster damage")]
        [GameRelation(Game.SpaceEngineers)]
        public bool ThrusterDamage = true;

        [ProtoMember]
        [Display(Name = "Cargo ships enabled")]
        [GameRelation(Game.SpaceEngineers)]
        public bool CargoShipsEnabled = true;

        [ProtoMember]
        [Display(Name = "Enable spectator")]
        [GameRelation(Game.Shared)]
        public bool EnableSpectator = false;

        [ProtoMember]
        [Display(Name = "Remove trash")]
        [GameRelation(Game.SpaceEngineers)]
        public bool RemoveTrash = true;

        /// <summary>
        /// Size of the edge of the world area cube.
        /// Don't use directly, as it is error-prone (it's km instead of m and edge size instead of half-extent)
        /// Rather use MyEntities.WorldHalfExtent()
        /// </summary>
        [ProtoMember]
        [Display(Name = "World size in Km")]
        [GameRelation(Game.SpaceEngineers)]
        public int WorldSizeKm = 0;

        [ProtoMember]
        [Display(Name = "Respawn ship delete")]
        [GameRelation(Game.SpaceEngineers)]
        public bool RespawnShipDelete = true;

        [ProtoMember]
        [Display(Name = "Reset ownership")]
        [GameRelation(Game.SpaceEngineers)]
        public bool ResetOwnership = false;

        [ProtoMember]
        [Display(Name = "Welder speed multiplier")]
        [GameRelation(Game.SpaceEngineers)]
        public float WelderSpeedMultiplier = 2;

        [ProtoMember]
        [Display(Name = "Grinder speed multiplier")]
        [GameRelation(Game.SpaceEngineers)]
        public float GrinderSpeedMultiplier = 2;

        [ProtoMember]
        [Display(Name = "Realistic sound")]
        [GameRelation(Game.SpaceEngineers)]
        public bool RealisticSound = false;

        [ProtoMember]
        [Display(Name = "Client can save")]
        [GameRelation(Game.Shared)]
        [XmlIgnore]
        [NoSerialize]
        public bool ClientCanSave { get { return false; } set { Debug.Fail("Client saving not supported anymore"); } }

        [ProtoMember]
        [Display(Name = "Hack speed multiplier")]
        [GameRelation(Game.SpaceEngineers)]
        public float HackSpeedMultiplier = 0.33f;

        [ProtoMember]
        [Display(Name = "Permanent death")]
        [GameRelation(Game.SpaceEngineers)]
        public bool? PermanentDeath = false;

        [ProtoMember]
        [Display(Name = "AutoSave in minutes")]
        [GameRelation(Game.Shared)]
        [Range(0, int.MaxValue)]
        public uint AutoSaveInMinutes = DEFAULT_AUTOSAVE_IN_MINUTES;

        [ProtoMember]
        [Display(Name = "Spawnship time multiplier")]
        [GameRelation(Game.SpaceEngineers)]
        public float SpawnShipTimeMultiplier = 0.5f;

        [ProtoMember]
        [Display(Name = "Procedural density")]
        [GameRelation(Game.SpaceEngineers)]
        public float ProceduralDensity = 0f;
        public bool ShouldSerializeProceduralDensity() { return ProceduralDensity > 0; }

        [ProtoMember]
        [Display(Name = "Procedural seed")]
        [GameRelation(Game.SpaceEngineers)]
        public int ProceduralSeed = 0;
        public bool ShouldSerializeProceduralSeed() { return ProceduralDensity > 0; }

        [ProtoMember]
        [Display(Name = "Destructible blocks")]
        [GameRelation(Game.SpaceEngineers)]
        public bool DestructibleBlocks = true;

        [ProtoMember]
        [Display(Name = "Enable ingame scripts")]
        [GameRelation(Game.SpaceEngineers)]
        public bool EnableIngameScripts = true;

        [ProtoMember]
        [Display(Name = "View distance")]
        [GameRelation(Game.SpaceEngineers)]
        public int ViewDistance = 15000;

        [ProtoMember]
        [Display(Name = "Flora density")]
        [GameRelation(Game.SpaceEngineers)]
        public int FloraDensity = 20;

        [ProtoMember]
        [DefaultValue(false)]// must leave default value here because it fails to deserialize world if it finds old save where this was nullable (bleh)
        [Display(Name = "Enable tool shake")]
        [GameRelation(Game.SpaceEngineers)]
        public bool EnableToolShake = false;

        [ProtoMember]
        //[Display(Name = "")] //do not display this
        [Display(Name = "Voxel generator version")]
        [GameRelation(Game.SpaceEngineers)]
        public int VoxelGeneratorVersion = 0;

        [ProtoMember]
        [Display(Name = "Enable oxygen")]
        [GameRelation(Game.SpaceEngineers)]
        public bool EnableOxygen = false;

        [ProtoMember]
        [Display(Name = "Enable 3rd person view")]
        [GameRelation(Game.SpaceEngineers)]
        public bool Enable3rdPersonView = true;

        [ProtoMember]
        [Display(Name = "Enable encounters")]
        [GameRelation(Game.SpaceEngineers)]
        public bool EnableEncounters = true;

        [ProtoMember]
        [Display(Name = "Enable flora")]
        [GameRelation(Game.SpaceEngineers)]
        public bool EnableFlora = true;

        [ProtoMember]
        [Display(Name = "Enable Station Voxel Support")]
        [GameRelation(Game.SpaceEngineers)]
        public bool EnableStationVoxelSupport = true;

        [ProtoMember]
        [Display(Name = "Enable Sun Rotation")]
        [GameRelation(Game.SpaceEngineers)]
        public bool EnableSunRotation = true;

        // Should have been named "EnableRespawnShips" to avoid a negative
        // but it's alread public now
        [ProtoMember]
        [Display(Name = "Disable respawn ships / carts")]
        [GameRelation(Game.Shared)]
        public bool DisableRespawnShips = false;

        [ProtoMember]
        [Display(Name = "")]
        [GameRelation(Game.SpaceEngineers)]
        public bool ScenarioEditMode = false;

        [ProtoMember]
        [GameRelation(Game.MedievalEngineers)]
        [Display(Name = "")]
        public bool Battle = false;

        [ProtoMember]
        [Display(Name = "")]
        [GameRelation(Game.SpaceEngineers)]
        public bool Scenario = false;

        [ProtoMember]
        [Display(Name = "")]
        [GameRelation(Game.SpaceEngineers)]
        public bool CanJoinRunning = false;

        [ProtoMember]
        public int PhysicsIterations = 4;

        [ProtoMember]
        [Display(Name = "Sun rotation interval")]
        [GameRelation(Game.SpaceEngineers)]
        public float SunRotationIntervalMinutes = 2 * 60; // 2 hours

        [ProtoMember]
        [Display(Name = "Enable jetpack")]
        [GameRelation(Game.SpaceEngineers)]
        public bool EnableJetpack = true;

        [ProtoMember]
        [Display(Name = "Spawn with tools")]
        [GameRelation(Game.SpaceEngineers)]
        public bool SpawnWithTools = true;

        [ProtoMember]
        [Display(Name = "")]
        [GameRelation(Game.SpaceEngineers)]
        public bool StartInRespawnScreen = false;

        [ProtoMember]
        [Display(Name = "Enable voxel destruction")]
        [GameRelation(Game.SpaceEngineers)]
        public bool EnableVoxelDestruction = true;

        [ProtoMember]
        [Display(Name = "")]
        [GameRelation(Game.SpaceEngineers)]
        public int MaxDrones = 5;

        [ProtoMember]
        [Display(Name = "Enable drones")]
        [GameRelation(Game.SpaceEngineers)]
        public bool EnableDrones = true;

        [ProtoMember]
        [Display(Name = "Enable cyberhounds")]
        [GameRelation(Game.SpaceEngineers)]
        public bool? EnableCyberhounds = true;

        [ProtoMember]
        [Display(Name = "Enable spiders")]
        [GameRelation(Game.SpaceEngineers)]
        public bool? EnableSpiders;

        [ProtoMember]
        [Display(Name = "Flora Density Multiplier")]
        [GameRelation(Game.Shared)]
        public float FloraDensityMultiplier = 1f;

        public void LogMembers(MyLog log, LoggingOptions options)
        {
            log.WriteLine("Settings:");
            using (var indentLock = log.IndentUsing(options))
            {
                log.WriteLine("GameMode = " + GameMode);
                log.WriteLine("MaxPlayers = " + MaxPlayers);
                log.WriteLine("OnlineMode = " + OnlineMode);
                log.WriteLine("AutoHealing = " + AutoHealing);
                log.WriteLine("WeaponsEnabled = " + WeaponsEnabled);
                log.WriteLine("ThrusterDamage = " + ThrusterDamage);
                log.WriteLine("EnableSpectator = " + EnableSpectator);
                log.WriteLine("EnableCopyPaste = " + EnableCopyPaste);
                log.WriteLine("MaxFloatingObjects = " + MaxFloatingObjects);
                log.WriteLine("CargoShipsEnabled = " + CargoShipsEnabled);
                log.WriteLine("EnvironmentHostility = " + EnvironmentHostility);
                log.WriteLine("ShowPlayerNamesOnHud = " + ShowPlayerNamesOnHud);
                log.WriteLine("InventorySizeMultiplier = " + InventorySizeMultiplier);
                log.WriteLine("RefinerySpeedMultiplier = " + RefinerySpeedMultiplier);
                log.WriteLine("AssemblerSpeedMultiplier = " + AssemblerSpeedMultiplier);
                log.WriteLine("AssemblerEfficiencyMultiplier = " + AssemblerEfficiencyMultiplier);
                log.WriteLine("WelderSpeedMultiplier = " + WelderSpeedMultiplier);
                log.WriteLine("GrinderSpeedMultiplier = " + GrinderSpeedMultiplier);
                log.WriteLine("ClientCanSave = " + ClientCanSave);
                log.WriteLine("HackSpeedMultiplier = " + HackSpeedMultiplier);
                log.WriteLine("PermanentDeath = " + PermanentDeath);
                log.WriteLine("DestructibleBlocks =  " + DestructibleBlocks);
                log.WriteLine("EnableScripts =  " + EnableIngameScripts);
                log.WriteLine("AutoSaveInMinutes = " + AutoSaveInMinutes);
                log.WriteLine("SpawnShipTimeMultiplier = " + SpawnShipTimeMultiplier);
                log.WriteLine("ProceduralDensity = " + ProceduralDensity);
                log.WriteLine("ProceduralSeed = " + ProceduralSeed);
                log.WriteLine("DestructibleBlocks = " + DestructibleBlocks);
                log.WriteLine("EnableIngameScripts = " + EnableIngameScripts);
                log.WriteLine("ViewDistance = " + ViewDistance);
                log.WriteLine("Battle = " + Battle);
                log.WriteLine("Voxel destruction = " + EnableVoxelDestruction);
            }
        }
    }

    public enum MyPhysicsPerformanceEnum
    {
        Fast = 4,
        Normal = 8,
        Precise = 32,
    }

    public enum MyOnlineModeEnum
    {
        OFFLINE,
        PUBLIC,
        FRIENDS,
        PRIVATE
    }

    public enum MyEnvironmentHostilityEnum
    {
        SAFE,
        NORMAL,
        CATACLYSM,
        CATACLYSM_UNREAL,
    }

    public enum MyGameModeEnum
    {
        Creative,
        Survival,
    }

    [XmlRoot("MyConfigDedicated")]
    public class MyConfigDedicatedData<T> where T : MyObjectBuilder_SessionSettings, new()
    {
        public T SessionSettings = new T();
        public SerializableDefinitionId Scenario;
        public string LoadWorld;
        public string IP = "0.0.0.0";
        public int SteamPort = 8766;
        public int ServerPort = 27016;
        public int AsteroidAmount = 4;
        [XmlArrayItem("unsignedLong")]
        public List<string> Administrators = new List<string>();
        public List<ulong> Banned = new List<ulong>();
        public List<ulong> Mods = new List<ulong>();
        public ulong GroupID = 0;
        public string ServerName = "";
        public string WorldName = "";
        public bool PauseGameWhenEmpty = false;
        public bool IgnoreLastSession = false;
    }
}