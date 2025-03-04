﻿#region Using

using Sandbox.Common;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.Engine.Utils;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.GameSystems;
using Sandbox.Game.GameSystems.Electricity;
using Sandbox.Game.Gui;
using Sandbox.Game.GUI;
using Sandbox.Game.Localization;
using Sandbox.Game.Multiplayer;
using Sandbox.Game.Screens.Helpers;
using Sandbox.Game.Weapons;
using Sandbox.Game.World;
using Sandbox.Graphics;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using System.Diagnostics;
using System.Text;
using Sandbox.Engine.Networking;
using Sandbox.Game.EntityComponents;
using VRage;
using VRage.Game.Entity.UseObject;
using VRage.Input;
using VRage.Library.Utils;
using VRage.Utils;
using VRageMath;
using IMyModdingControllableEntity = Sandbox.ModAPI.Interfaces.IMyControllableEntity;
using VRage.ObjectBuilders;
using VRage.ModAPI;
using VRage.Network;
using Sandbox.Game.Entities.UseObject;
using VRage.Game.Gui;
using VRage.Game.Components;
using VRage.Game.Entity;
using Sandbox.Engine.Physics;
using Sandbox.Game.AI;
using Sandbox.Engine.Multiplayer;
using VRage.Game;
using VRage.Serialization;
#endregion

namespace Sandbox.Game.Entities
{
    public enum ControllerPriority
    {
        AutoPilot = 1,
        Primary = 2,
        Secondary = 3
    };
    public partial class MyShipController : MyTerminalBlock, IMyControllableEntity, IMyRechargeSocketOwner, IMyShipController
    {
        #region Fields

        public MyGridGyroSystem GridGyroSystem;
        public MyGridSelectionSystem GridSelectionSystem;
        public MyResourceDistributorComponent GridResourceDistributor
        {
            get { return (CubeGrid != null) ? CubeGrid.GridSystems.ResourceDistributor : null; }
        }
        public MyGridReflectorLightSystem GridReflectorLights;
        public MyGridWheelSystem GridWheels
        {
            get { return (CubeGrid != null) ? CubeGrid.GridSystems.WheelSystem : null; }
        }

        public MyEntityThrustComponent EntityThrustComponent
        {
            get { return (CubeGrid != null) ? CubeGrid.Components.Get<MyEntityThrustComponent>() : null; }
        }

        private readonly Sync<bool> m_controlThrusters;
        private readonly Sync<bool> m_controlWheels;

        private readonly Sync<bool> m_dampenersEnabled;

        private bool m_reactorsSwitched = true;

        protected MyRechargeSocket m_rechargeSocket;

        MyHudNotification m_notificationReactorsOff;
        MyHudNotification m_notificationReactorsOn;
        MyHudNotification m_notificationLeave;
        MyHudNotification m_notificationTerminal;
        MyHudNotification m_inertiaDampenersNotification;

        MyHudNotification m_noWeaponNotification;
        MyHudNotification m_weaponSelectedNotification;
        MyHudNotification m_outOfAmmoNotification;
        MyHudNotification m_weaponNotWorkingNotification;

        MyHudNotification m_noControlNotification;

        protected virtual MyStringId LeaveNotificationHintText { get { return MySpaceTexts.NotificationHintLeaveCockpit; } }

        protected bool m_enableFirstPerson = false;
        protected bool m_enableShipControl = true;
        protected bool m_enableBuilderCockpit = false;
        public bool EnableShipControl { get { return m_enableShipControl; } }

        // This value can be in some advanced settings
        static float RollControlMultiplier = 0.2f;

        bool m_forcedFPS;

        //        MyGunTypeEnum? m_selectedGunType;
        MyDefinitionId? m_selectedGunId;

        public MyToolbar Toolbar;
        public MyToolbar BuildToolbar;
        public bool BuildingMode = false;
        public bool hasPower = false;

        protected MyEntity3DSoundEmitter m_soundEmitter;
        protected MySoundPair m_baseIdleSound;

        protected static MyTerminalControlCheckbox<MyShipController>  m_horizonIndicator;

        /// <summary>
        /// Raycaster used for showing block info when active.
        /// </summary>
        private MyCasterComponent raycaster = null;

        private int m_switchWeaponCounter = 0;

        bool IsWaitingForWeaponSwitch
        {
            get
            {
                return m_switchWeaponCounter != 0;
            }
        }

        private bool[] m_isShooting;

        protected bool IsShooting(MyShootActionEnum action)
        {
            return m_isShooting[(int)action];
        }

        public bool IsShooting()
        {
            foreach (MyShootActionEnum value in MyEnum<MyShootActionEnum>.Values)
            {
                if (m_isShooting[(int)value])
                    return true;
            }
            return false;
        }

        #endregion

        public VRage.Groups.MyGroups<MyCubeGrid, MyGridPhysicalGroupData> ControlGroup
        {
            get { return MyCubeGridGroups.Static.Physical; }
        }

        #region Init

        static MyShipController()
        {
            if (MyFakes.ENABLE_WHEEL_CONTROLS_IN_COCKPIT)
            {
                var controlThrusters = new MyTerminalControlCheckbox<MyShipController>("ControlThrusters", MySpaceTexts.TerminalControlPanel_Cockpit_ControlThrusters, MySpaceTexts.TerminalControlPanel_Cockpit_ControlThrusters);
                controlThrusters.Getter = (x) => x.ControlThrusters;
                controlThrusters.Setter = (x, v) => x.ControlThrusters = v;
                controlThrusters.Visible = (x) => x.m_enableShipControl;
                controlThrusters.Enabled = (x) => x.IsMainCockpitFree();
                var action = controlThrusters.EnableAction();
                if (action != null)
                    action.Enabled = (x) => x.m_enableShipControl;
                MyTerminalControlFactory.AddControl(controlThrusters);

                var controlWheels = new MyTerminalControlCheckbox<MyShipController>("ControlWheels", MySpaceTexts.TerminalControlPanel_Cockpit_ControlWheels, MySpaceTexts.TerminalControlPanel_Cockpit_ControlWheels);
                controlWheels.Getter = (x) => x.ControlWheels;
                controlWheels.Setter = (x, v) => x.ControlWheels = v;
                controlWheels.Visible = (x) => x.m_enableShipControl;
                controlWheels.Enabled = (x) => x.GridWheels.WheelCount > 0 && x.IsMainCockpitFree();
                action = controlWheels.EnableAction();
                if (action != null)
                    action.Enabled = (x) => x.m_enableShipControl;
                MyTerminalControlFactory.AddControl(controlWheels);

                var handBrake = new MyTerminalControlCheckbox<MyShipController>("HandBrake", MySpaceTexts.TerminalControlPanel_Cockpit_Handbrake, MySpaceTexts.TerminalControlPanel_Cockpit_Handbrake);
                handBrake.Getter = (x) => x.CubeGrid.GridSystems.WheelSystem.HandBrake;
                handBrake.Setter = (x, v) => x.CubeGrid.SetHandbrakeRequest(v);
                handBrake.Visible = (x) => x.m_enableShipControl;
                handBrake.Enabled = (x) => x.GridWheels.WheelCount > 0 && x.IsMainCockpitFree();
                action = handBrake.EnableAction();
                if (action != null)
                    action.Enabled = (x) => x.m_enableShipControl;
                MyTerminalControlFactory.AddControl(handBrake);
            }

            if (MyFakes.ENABLE_DAMPENERS_OVERRIDE)
            {
                var dampenersOverride = new MyTerminalControlCheckbox<MyShipController>("DampenersOverride", MySpaceTexts.ControlName_InertialDampeners, MySpaceTexts.ControlName_InertialDampeners);
                dampenersOverride.Getter = (x) =>
                {
                    return x.EntityThrustComponent != null && x.EntityThrustComponent.DampenersEnabled;
                };
                dampenersOverride.Setter = (x, v) => x.EnableDampingInternal(v, true);
                dampenersOverride.Visible = (x) => x.m_enableShipControl;

                var action = dampenersOverride.EnableAction();
                if (action != null)
                {
                    action.Enabled = (x) => x.m_enableShipControl;// x.EnableShipControl;
                }
                dampenersOverride.Enabled = (x) => x.IsMainCockpitFree();
                MyTerminalControlFactory.AddControl(dampenersOverride);
            }

            var mainCockpit = new MyTerminalControlCheckbox<MyShipController>("MainCockpit", MySpaceTexts.TerminalControlPanel_Cockpit_MainCockpit, MySpaceTexts.TerminalControlPanel_Cockpit_MainCockpit);
            mainCockpit.Getter = (x) => x.IsMainCockpit;
            mainCockpit.Setter = (x, v) => x.IsMainCockpit = v;
            mainCockpit.Enabled = (x) => x.IsMainCockpitFree();
            mainCockpit.Visible = (x) => x.CanBeMainCockpit();
            mainCockpit.EnableAction();

            MyTerminalControlFactory.AddControl(mainCockpit);

			m_horizonIndicator = new MyTerminalControlCheckbox<MyShipController>("HorizonIndicator", MySpaceTexts.TerminalControlPanel_Cockpit_HorizonIndicator, MySpaceTexts.TerminalControlPanel_Cockpit_HorizonIndicator);
			m_horizonIndicator.Getter = (x) => x.HorizonIndicatorEnabled;
			m_horizonIndicator.Setter = (x, v) => x.HorizonIndicatorEnabled = v;
			m_horizonIndicator.Enabled = (x) => MyFakes.ENABLE_PLANETS;
			m_horizonIndicator.Visible = (x) => MyFakes.ENABLE_PLANETS;
			m_horizonIndicator.EnableAction();
			MyTerminalControlFactory.AddControl(m_horizonIndicator);           
        }

        public virtual MyCharacter Pilot
        {
            get { return null; }
        }

        protected virtual ControllerPriority Priority
        {
            get
            {
                return ControllerPriority.Primary;
            }
        }

        public MyShipController()
        {
            m_isShooting = new bool[(int)MyEnum<MyShootActionEnum>.Range.Max + 1];
            ControllerInfo.ControlAcquired += OnControlAcquired;
            ControllerInfo.ControlReleased += OnControlReleased;
            GridSelectionSystem = new MyGridSelectionSystem(this);
            m_soundEmitter = new MyEntity3DSoundEmitter(this);

            m_isMainCockpit.ValueChanged += (x) => MainCockpitChanged();
            m_dampenersEnabled.ValueChanged += (x) => DampenersEnabledChanged();
        }

        public override void Init(MyObjectBuilder_CubeBlock objectBuilder, MyCubeGrid cubeGrid)
        {
            SyncFlag = true;
            base.Init(objectBuilder, cubeGrid);

            //MyDebug.AssertDebug(objectBuilder.TypeId == typeof(MyObjectBuilder_ShipController));
            var def = MyDefinitionManager.Static.GetCubeBlockDefinition(objectBuilder.GetId());
            m_enableFirstPerson = BlockDefinition.EnableFirstPerson || MySession.Static.Settings.Enable3rdPersonView == false;
            m_enableShipControl = BlockDefinition.EnableShipControl;
            m_enableBuilderCockpit = BlockDefinition.EnableBuilderCockpit;


            m_rechargeSocket = new MyRechargeSocket();

            MyObjectBuilder_ShipController shipControllerOb = (MyObjectBuilder_ShipController)objectBuilder;

            // No need for backward compatibility of selected weapon, we just leave it alone
            //            m_selectedGunType = shipControllerOb.SelectedGunType;
            m_selectedGunId = shipControllerOb.SelectedGunId;

            ControlThrusters = shipControllerOb.ControlThrusters;
            ControlWheels = shipControllerOb.ControlWheels;

            if (shipControllerOb.IsMainCockpit)
            {
                IsMainCockpit = true;
            }

			HorizonIndicatorEnabled = shipControllerOb.HorizonIndicatorEnabled;
            Toolbar = new MyToolbar(ToolbarType); ;
            Toolbar.Init(shipControllerOb.Toolbar, this);

            BuildToolbar = new MyToolbar(MyToolbarType.BuildCockpit);
            BuildToolbar.Init(shipControllerOb.BuildToolbar, this);


            SlimBlock.ComponentStack.IsFunctionalChanged += ComponentStack_IsFunctionalChanged;

            // TODO: Seems like overkill
            if (Sync.IsServer && false)
            {
                //Because of simulating thrusts
                NeedsUpdate |= MyEntityUpdateEnum.EACH_FRAME;
            }

			NeedsUpdate |= MyEntityUpdateEnum.EACH_100TH_FRAME;

            m_baseIdleSound = BlockDefinition.PrimarySound;

            CubeGrid.OnGridSplit += CubeGrid_OnGridSplit;
            Components.ComponentAdded += OnComponentAdded;
            Components.ComponentRemoved += OnComponentRemoved;
        }

        protected virtual void ComponentStack_IsFunctionalChanged()
        {
        }

        public override MyObjectBuilder_CubeBlock GetObjectBuilderCubeBlock(bool copy = false)
        {
            MyObjectBuilder_ShipController objectBuilder = (MyObjectBuilder_ShipController)base.GetObjectBuilderCubeBlock(copy);

            objectBuilder.SelectedGunId = m_selectedGunId;
            objectBuilder.UseSingleWeaponMode = m_singleWeaponMode;
            objectBuilder.ControlThrusters = m_controlThrusters;
            objectBuilder.ControlWheels = m_controlWheels;
            objectBuilder.Toolbar = Toolbar.GetObjectBuilder();
            objectBuilder.BuildToolbar = BuildToolbar.GetObjectBuilder();
            objectBuilder.IsMainCockpit = m_isMainCockpit;
			objectBuilder.HorizonIndicatorEnabled = HorizonIndicatorEnabled;

            return objectBuilder;
        }

        #endregion

        #region View

        public virtual MatrixD GetHeadMatrix(bool includeY, bool includeX = true, bool forceBoneMatrix = false, bool forceHeadBone = false)
        {
            var world = PositionComp.WorldMatrix;
            return world;
        }

        public override MatrixD GetViewMatrix()
        {
            var head = GetHeadMatrix(!ForceFirstPersonCamera, !ForceFirstPersonCamera);

            MatrixD result;
            MatrixD.Invert(ref head, out result);
            return result;
        }

        public bool PrimaryLookaround
        {
            get { return !m_enableShipControl; }
        }

        public void MoveAndRotate(Vector3 moveIndicator, Vector2 rotationIndicator, float rollIndicator)
        {
            if (MyFakes.ENABLE_WHEEL_CONTROLS_IN_COCKPIT)
            {
                if ((ControllerInfo.IsLocallyControlled() || Sync.IsServer) && GridWheels != null && ControlWheels && m_enableShipControl)
                {
                    if (MyInput.Static.IsGameControlPressed(MyControlsSpace.JUMP))
                        CubeGrid.GridSystems.WheelSystem.Brake = true;
                    else
                        CubeGrid.GridSystems.WheelSystem.Brake = false;
                    if (MyInput.Static.IsNewGameControlPressed(MyControlsSpace.LANDING_GEAR))
                        CubeGrid.GridSystems.WheelSystem.HandBrake = !CubeGrid.GridSystems.WheelSystem.HandBrake;
                }
            }
            // No movement, no change, early return
            if (m_enableShipControl && moveIndicator == Vector3.Zero && rotationIndicator == Vector2.Zero && rollIndicator == 0.0f)
            {
                //if (ControllerInfo.Controller.IsLocalPlayer() || Sync.IsServer)
                if ((ControllerInfo.IsLocallyControlled() && CubeGrid.GridSystems.ControlSystem.IsLocallyControlled) || (Sync.IsServer && false))
                {
                    ClearMovementControl();
                }
                return;
            }

            if (IsMainCockpit == false && CubeGrid.HasMainCockpit())
            {
                return;
            }

            if (EntityThrustComponent == null && GridGyroSystem == null && GridWheels == null)
                return;

            //System.Diagnostics.Debug.Assert(GridResourceDistributor != null);
            //System.Diagnostics.Debug.Assert(GridGyroSystem != null);
            //System.Diagnostics.Debug.Assert(EntityThrustComponent != null);

            if (GridResourceDistributor == null)
                return;

            if (!Sync.Players.HasExtendedControl(this, this.CubeGrid))
                return;

            if (!m_enableShipControl)
                return;

            var thrustComponent = EntityThrustComponent;
            try
            {
                // Engine off, no control forces, early return
                if (CubeGrid.GridSystems.ResourceDistributor.ResourceState != MyResourceStateEnum.NoPower)
                {
                    // mouse pixels will do maximal rotation
                    const float pixelsForMaxRotation = 20;
                    rotationIndicator /= pixelsForMaxRotation;
                    Vector2.ClampToSphere(ref rotationIndicator, 1.0f);
                    rollIndicator *= RollControlMultiplier;

                    Matrix orientMatrix;
                    Orientation.GetMatrix(out orientMatrix);

                    var controlThrust = Vector3.Transform(moveIndicator, orientMatrix);
                    var controlTorque = Vector3.Transform(new Vector3(-rotationIndicator.X, -rotationIndicator.Y, -rollIndicator), orientMatrix);
                    Vector3.ClampToSphere(controlTorque, 1.0f);

                    
                    if(thrustComponent != null)
                        thrustComponent.ControlThrust = controlThrust;
                    if(GridGyroSystem != null)
                        GridGyroSystem.ControlTorque = controlTorque;

                    if (MyFakes.ENABLE_WHEEL_CONTROLS_IN_COCKPIT)
                    {
                        if (GridWheels != null && ControlWheels)
                        {
                            GridWheels.CockpitMatrix = orientMatrix;
                            GridWheels.AngularVelocity = moveIndicator;
                        }
                    }
                }
            }
            finally
            {
	            var controlThrust = Vector3.Zero;
	            var controlTorque = Vector3.Zero;
                if (thrustComponent != null)
                    controlThrust = thrustComponent.ControlThrust;
	            if (GridGyroSystem != null)
		            controlTorque = GridGyroSystem.ControlTorque;

                // Need it every frame because of MP interpolation
                CubeGrid.SendControlThrust(Vector3B.Round(controlThrust * 127.0f));
            }
        }

        public void MoveAndRotateStopped()
        {
            ClearMovementControl();
        }

        private void ClearMovementControl()
        {
            if (!m_enableShipControl)
                return;

            var thrustComponent = EntityThrustComponent;
            if (thrustComponent != null)
                 thrustComponent.ControlThrust = Vector3.Zero;
            if (GridGyroSystem != null)
                GridGyroSystem.ControlTorque = Vector3.Zero;
            // Need it every frame because of MP interpolation
            CubeGrid.SendControlThrust(Vector3B.Zero);

            if (MyFakes.ENABLE_WHEEL_CONTROLS_IN_COCKPIT)
            {
                if (GridWheels != null)
                    GridWheels.AngularVelocity = Vector3.Zero;
            }
        }

        public bool ForceFirstPersonCamera
        {
            get
            {
                return (m_forcedFPS && m_enableFirstPerson);
            }
            set
            {
                if (m_forcedFPS != value)
                {
                    m_forcedFPS = value;

                    UpdateCameraAfterChange(false);
                }
            }
        }

        public bool EnableFirstPerson
        {
            get
            {
                return m_enableFirstPerson;
            }
        }

        #endregion

        #region Update

        public override void UpdatingStopped()
        {
            base.UpdatingStopped();

            ClearMovementControl();
        }

        public override void UpdateAfterSimulation()
        {
            base.UpdateAfterSimulation();

            UpdateShipInfo();

            //Debug.Assert(GridGyroSystem != null && EntityThrustComponent != null && Parent.Physics != null && m_cameraSpring != null && m_cameraShake != null, "CALL PROGRAMMER, this cant happen");

            // Vector3.One is max power, larger values will be clamped
            //if (EntityThrustComponent != null && GridGyroSystem != null && ControllerInfo.Controller.IsLocalPlayer())
            //{
            //    if (
            //        (EntityThrustComponent.ControlThrust != Vector3.Zero) ||
            //        (GridGyroSystem.ControlTorque != Vector3.Zero)
            //        )
            //    {
            //        CubeGrid.SyncObject.RequestControlThrustAndTorque(Vector3.Zero, Vector3.Zero);
            //        EntityThrustComponent.ControlThrust = Vector3.Zero;
            //        GridGyroSystem.ControlTorque = Vector3.Zero;
            //    }
            //}

            if (ControllerInfo.Controller != null && MySession.Static.LocalHumanPlayer != null && ControllerInfo.Controller == MySession.Static.LocalHumanPlayer.Controller)
            {
                var shipController = CubeGrid.GridSystems.ControlSystem.GetController();
                if (shipController == ControllerInfo.Controller)
                {
                    if (m_noControlNotification != null)
                    {
                        MyHud.Notifications.Remove(m_noControlNotification);
                        m_noControlNotification = null;
                    }
                }
                else
                {
                    if (m_noControlNotification == null && EnableShipControl)
                    {
                        if (shipController == null && CubeGrid.GridSystems.ControlSystem.GetShipController() != null)
                        {
                            m_noControlNotification = new MyHudNotification(MySpaceTexts.Notification_NoControlAutoPilot, 0);
                        }
                        else
                        {
                            if (CubeGrid.HasMainCockpit() && CubeGrid.IsMainCockpit(this) == false)
                                m_noControlNotification = new MyHudNotification(MySpaceTexts.Notification_NoControlNotMain, 0);
                            else
                                if (CubeGrid.IsStatic)
                                    m_noControlNotification = new MyHudNotification(MySpaceTexts.Notification_NoControlStation, 0);
                                else
                                    m_noControlNotification = new MyHudNotification(MySpaceTexts.Notification_NoControl, 0);
                        }
                        MyHud.Notifications.Add(m_noControlNotification);
                    }
                }
            }

            foreach (MyShootActionEnum action in MyEnum<MyShootActionEnum>.Values)
            {
                if (IsShooting(action))
                {
                    Shoot(action);
                }
            }

            if (CanBeMainCockpit())
            {
                if (CubeGrid.HasMainCockpit() && CubeGrid.IsMainCockpit(this) == false)
                {
                    DetailedInfo.Clear();
                    DetailedInfo.AppendStringBuilder(MyTexts.Get(MySpaceTexts.BlockPropertiesText_MainCockpit));
                    DetailedInfo.Append(": " + CubeGrid.MainCockpit.CustomName);
                }
                else
                {
                    DetailedInfo.Clear();
                }
            }

            this.HandleBuldingMode();           
        }
        
        /// <summary>
        /// Handles logic related with 'building from cockpit'.
        /// </summary>
        private void HandleBuldingMode() 
        {
            if ((BuildingMode && MySession.Static.IsCameraControlledObject() == false) ||
                (MyInput.Static.IsNewKeyPressed(MyKeys.G) && MyInput.Static.IsAnyCtrlKeyPressed() && !MyInput.Static.IsAnyMousePressed() && !(MySession.Static.ControlledEntity is MyRemoteControl)
                && m_enableBuilderCockpit && CanBeMainCockpit() && MySession.Static.IsCameraControlledObject() == true))
            {
                BuildingMode = !BuildingMode;
                MyGuiAudio.PlaySound(MyGuiSounds.HudUse);
                Toolbar.Unselect();
                if (BuildingMode)
                {
                    MyHud.Crosshair.ChangeDefaultSprite(MyHudTexturesEnum.Target_enemy, 0.01f);
                    MyHud.Notifications.Add(MyNotificationSingletons.BuildingModeOn);
                }
                else
                {
                    MyHud.Crosshair.ResetToDefault();
                    MyHud.Notifications.Add(MyNotificationSingletons.BuildingModeOff);
                }
            }
        }

        public override void UpdateOnceBeforeFrame()
        {
            base.UpdateOnceBeforeFrame();
        }

        public override void UpdateBeforeSimulation10()
        {
            //System.Diagnostics.Debug.Assert(GridResourceDistributor != null);
            //System.Diagnostics.Debug.Assert(GridGyroSystem != null);
            //System.Diagnostics.Debug.Assert(EntityThrustComponent != null);

            //if (this.Controller.Player != null && this.CubeGrid.Controller == null)
            //    this.CubeGrid.Controller = this;

            //if (GridResourceDistributor == null)
            //    return;
            //if (GridGyroSystem == null)
            //    return;
            //if (EntityThrustComponent == null)
            //    return;

            UpdateShipInfo10();

            base.UpdateBeforeSimulation10();
        }

        private void UpdateShipInfo()
        {
            if (!MySandboxGame.IsDedicated && MySession.Static.LocalHumanPlayer != null)
            {
                if (ControllerInfo.Controller != MySession.Static.LocalHumanPlayer.Controller)
                {
                    return;
                }
            }
            hasPower = CubeGrid.GridSystems.ResourceDistributor.ResourceState != MyResourceStateEnum.NoPower;
            //These values are cached
            if (GridResourceDistributor != null)
            {
                MyHud.ShipInfo.FuelRemainingTime = GridResourceDistributor.RemainingFuelTimeByType(MyResourceDistributorComponent.ElectricityId);
                MyHud.ShipInfo.Reactors = GridResourceDistributor.MaxAvailableResourceByType(MyResourceDistributorComponent.ElectricityId);
                MyHud.ShipInfo.ResourceState = GridResourceDistributor.ResourceStateByType(MyResourceDistributorComponent.ElectricityId);
				MyHud.ShipInfo.AllEnabledRecently = GridResourceDistributor.AllEnabledRecently;
            }
            if (GridGyroSystem != null)
                MyHud.ShipInfo.GyroCount = GridGyroSystem.GyroCount;

            var thrustComponent = EntityThrustComponent;
            if (thrustComponent != null)
            {
                MyHud.ShipInfo.ThrustCount = thrustComponent.ThrustCount;
                MyHud.ShipInfo.DampenersEnabled = thrustComponent.DampenersEnabled;
            }
        }

        protected virtual void UpdateShipInfo10(bool controlAcquired = false)
        {
            if (ControllerInfo.IsLocallyHumanControlled())
            {
                if (GridResourceDistributor != null)
                {
                    MyHud.ShipInfo.PowerUsage = (GridResourceDistributor.MaxAvailableResourceByType(MyResourceDistributorComponent.ElectricityId) != 0.0f)
                        ? (GridResourceDistributor.TotalRequiredInputByType(MyResourceDistributorComponent.ElectricityId) / GridResourceDistributor.MaxAvailableResourceByType(MyResourceDistributorComponent.ElectricityId))
                        : 0.0f;


                    MyHud.ShipInfo.NumberOfBatteries = GridResourceDistributor.GetSourceCount(MyResourceDistributorComponent.ElectricityId, MyStringHash.GetOrCompute("Battery"));

                    GridResourceDistributor.UpdateHud(MyHud.SinkGroupInfo);

                    if (GridResourceDistributor.SourcesEnabledByType(MyResourceDistributorComponent.ElectricityId) != MyMultipleEnabledEnum.NoObjects)
                    {
                        if (GridResourceDistributor.SourcesEnabledByType(MyResourceDistributorComponent.ElectricityId) == MyMultipleEnabledEnum.AllEnabled)
                        {
                            MyHud.Notifications.Remove(m_notificationReactorsOn);
                            MyHud.Notifications.Add(m_notificationReactorsOff);
                        }
                        else
                        {
                            MyHud.Notifications.Remove(m_notificationReactorsOff);
                            MyHud.Notifications.Add(m_notificationReactorsOn);
                        }
                    }
                } //GridResourceDistributor

				UpdateShipMass();

                if (Parent.Physics != null)
                {
                    if (ControlWheels && GridWheels.WheelCount > 0)
                        MyHud.ShipInfo.SpeedInKmH = true;
                    else
                        MyHud.ShipInfo.SpeedInKmH = false;
                    MyHud.ShipInfo.Speed = Parent.Physics.LinearVelocity.Length();
                }
                if (GridReflectorLights!=null)
                    MyHud.ShipInfo.ReflectorLights = GridReflectorLights.ReflectorsEnabled;
                MyHud.ShipInfo.LandingGearsTotal = CubeGrid.GridSystems.LandingSystem.TotalGearCount;
                MyHud.ShipInfo.LandingGearsLocked = CubeGrid.GridSystems.LandingSystem[LandingGearMode.Locked];
                MyHud.ShipInfo.LandingGearsInProximity = CubeGrid.GridSystems.LandingSystem[LandingGearMode.ReadyToLock];
            }
        }

        private void UpdateShipMass()
        {
            Debug.Assert(ControllerInfo.IsLocallyHumanControlled());

            MyHud.ShipInfo.Mass = 0;
			MyCubeGrid parentGrid = Parent as MyCubeGrid;
			if (parentGrid == null)
				return;

            MyHud.ShipInfo.Mass = parentGrid.GetCurrentMass(Pilot);
        }

        public override void UpdateBeforeSimulation100()
        {
            //System.Diagnostics.Debug.Assert(GridResourceDistributor != null);
            //System.Diagnostics.Debug.Assert(GridGyroSystem != null);
            //System.Diagnostics.Debug.Assert(EntityThrustComponent != null);
            if (m_soundEmitter != null)
            {
                m_soundEmitter.Update();
                UpdateSoundState();
            }
            if (GridResourceDistributor == null || GridGyroSystem == null || EntityThrustComponent == null)
                return;

            // This is here probably to give control to the second player when the first one leaves his cockpit. TODO: Do it properly
            //TryExtendControlToGroup();

            base.UpdateBeforeSimulation100();
        }

        // Returns true when the first controller has priority
        public static bool HasPriorityOver(MyShipController first, MyShipController second)
        {
            Debug.Assert(first.CubeGrid != null, "Ship controller cube grid was null");
            Debug.Assert(second.CubeGrid != null, "Ship controller cube grid was null");

            if (first.Priority < second.Priority) return true;
            if (first.Priority > second.Priority) return false;

            if (first.CubeGrid.Physics == null && second.CubeGrid.Physics == null)
            {
                return first.CubeGrid.BlocksCount > second.CubeGrid.BlocksCount;
            }
            else if (first.CubeGrid.Physics != null && second.CubeGrid.Physics != null && first.CubeGrid.Physics.Shape.MassProperties.HasValue && second.CubeGrid.Physics.Shape.MassProperties.HasValue)
            {
                return first.CubeGrid.Physics.Shape.MassProperties.Value.Mass > second.CubeGrid.Physics.Shape.MassProperties.Value.Mass;
            }
            else
            {
                return first.CubeGrid.Physics == null;
            }
        }

#warning Get rid of this once everything is working without it
        private void TryExtendControlToGroup()
        {
            Debug.Assert(Sync.IsServer, "Extending grid control on client is forbidden!");
            if (!Sync.IsServer) return;

            //Try to get control of group, early return on fail (you control whole group or nothing)
            if (m_enableShipControl && ControllerInfo.Controller != null)
            {
                bool canTakeControl = false;
                bool forceReleaseControl = false;

                var group = ControlGroup.GetGroup(CubeGrid);

                var groupController = CubeGrid.GridSystems.ControlSystem.GetController();
                if (group != null)
                {
                    if (groupController == null)
                    {
                        canTakeControl = true;
                    }
                    else
                    {
                        var shipController = groupController.ControlledEntity as MyShipController;
                        if (shipController != null)
                        {
                            if (this.Priority < shipController.Priority)
                            {
                                canTakeControl = true;
                                forceReleaseControl = true;
                            }
                            else
                            {
                                //use shape massprops because physics mass can be of welded body
                                if (CubeGrid.Physics.Shape.MassProperties.HasValue && shipController.CubeGrid.Physics.Shape.MassProperties.HasValue && 
                                    this.CubeGrid.Physics.Shape.MassProperties.Value.Mass > shipController.CubeGrid.Physics.Shape.MassProperties.Value.Mass)
                                {
                                    canTakeControl = true;
                                }
                            }
                        }
                    }
                }

                if (canTakeControl)
                {
                    if (groupController != null)
                    {
                        var shipController = groupController.ControlledEntity as MyShipController;
                        foreach (var node in group.Nodes)
                            Sync.Players.TryReduceControl(shipController, node.NodeData);

                        if (forceReleaseControl)
                        {
                            shipController.ForceReleaseControl();
                        }
                    }

                    Sync.Players.SetControlledEntity(ControllerInfo.Controller.Player.Id, this);

                    foreach (var node in group.Nodes)
                        Sync.Players.TryExtendControl(this, node.NodeData);
                }

                if (Sync.Players.HasExtendedControl(this, CubeGrid))
                {
                    var thrustComponent = EntityThrustComponent;
                    if(thrustComponent != null)
                        thrustComponent.Enabled = m_controlThrusters;
                }
            }
        }
        #endregion

        #region Notifications

        private void RefreshControlNotifications()
        {
            RemoveControlNotifications();

            if (m_notificationReactorsOn == null)
            {
                var controlName = MyInput.Static.GetGameControl(MyControlsSpace.TOGGLE_REACTORS).GetControlButtonName(MyGuiInputDeviceEnum.Keyboard);
                m_notificationReactorsOn = new MyHudNotification(MySpaceTexts.NotificationHintTurnPowerOn, 0);
                if (!MyInput.Static.IsJoystickConnected())
                    m_notificationReactorsOn.SetTextFormatArguments(controlName);
                else
                    m_notificationReactorsOn.SetTextFormatArguments(MyControllerHelper.GetCodeForControl(MySpaceBindingCreator.CX_SPACESHIP, MyControlsSpace.TOGGLE_REACTORS));
                m_notificationReactorsOn.Level = MyNotificationLevel.Control;
            }

            if (m_notificationReactorsOff == null)
            {
                var controlName = MyInput.Static.GetGameControl(MyControlsSpace.TOGGLE_REACTORS).GetControlButtonName(MyGuiInputDeviceEnum.Keyboard);
                m_notificationReactorsOff = new MyHudNotification(MySpaceTexts.NotificationHintTurnPowerOff, 0);
                if (!MyInput.Static.IsJoystickConnected())
                    m_notificationReactorsOff.SetTextFormatArguments(controlName);
                else
                    m_notificationReactorsOff.SetTextFormatArguments(MyControllerHelper.GetCodeForControl(MySpaceBindingCreator.CX_SPACESHIP, MyControlsSpace.TOGGLE_REACTORS));
                m_notificationReactorsOff.Level = MyNotificationLevel.Control;
            }

            if (m_notificationLeave == null)
            {
                var controlName = MyInput.Static.GetGameControl(MyControlsSpace.USE).GetControlButtonName(MyGuiInputDeviceEnum.Keyboard);
                m_notificationLeave = new MyHudNotification(LeaveNotificationHintText, 0);
                if (!MyInput.Static.IsJoystickConnected())
                    m_notificationLeave.SetTextFormatArguments(controlName);
                else
                    m_notificationLeave.SetTextFormatArguments(MyControllerHelper.GetCodeForControl(MySpaceBindingCreator.CX_SPACESHIP, MyControlsSpace.USE));
                m_notificationLeave.Level = MyNotificationLevel.Control;
            }

            if (m_notificationTerminal == null)
            {
                var controlName = MyInput.Static.GetGameControl(MyControlsSpace.TERMINAL).GetControlButtonName(MyGuiInputDeviceEnum.Keyboard);
                if (!MyInput.Static.IsJoystickConnected())
                {
                    m_notificationTerminal = new MyHudNotification(MySpaceTexts.NotificationHintOpenShipControlPanel, 0);
                    m_notificationTerminal.SetTextFormatArguments(controlName);
                    m_notificationTerminal.Level = MyNotificationLevel.Control;
                }
                else
                {
                    m_notificationTerminal = null;
                }
            }

            if (m_notificationWeaponMode == null)
            {
                var controlName = MyInput.Static.GetGameControl(MyControlsSpace.CUBE_COLOR_CHANGE).GetControlButtonName(MyGuiInputDeviceEnum.Mouse);
                if (!MyInput.Static.IsJoystickConnected())
                {
                    m_notificationWeaponMode = new MyHudNotification(MySpaceTexts.NotificationHintSwitchWeaponMode, 0);
                    m_notificationWeaponMode.SetTextFormatArguments(controlName);
                    m_notificationWeaponMode.Level = MyNotificationLevel.Control;
                }
                else
                {
                    m_notificationWeaponMode = null;
                }
            }

            MyHud.Notifications.Add(m_notificationLeave);
            if (m_notificationTerminal != null)
                MyHud.Notifications.Add(m_notificationTerminal);
            if (m_notificationWeaponMode != null)
                MyHud.Notifications.Add(m_notificationWeaponMode);
        }

        private void RemoveControlNotifications()
        {
            if (m_notificationReactorsOff != null)
                MyHud.Notifications.Remove(m_notificationReactorsOff);

            if (m_notificationReactorsOn != null)
                MyHud.Notifications.Remove(m_notificationReactorsOn);

            if (m_notificationLeave != null)
                MyHud.Notifications.Remove(m_notificationLeave);

            if (m_notificationTerminal != null)
                MyHud.Notifications.Remove(m_notificationTerminal);

            if (m_notificationWeaponMode != null)
                MyHud.Notifications.Remove(m_notificationWeaponMode);
        }

        #endregion

        #region Object control

        void OnComponentAdded(System.Type arg1, MyEntityComponentBase arg2)
        {
            if (arg1 == typeof(MyCasterComponent))
            {
                raycaster = arg2 as MyCasterComponent;
                this.PositionComp.OnPositionChanged += OnPositionChanged;
            }
        }

        void OnComponentRemoved(System.Type arg1, MyEntityComponentBase arg2)
        {
            if (arg1 == typeof(MyCasterComponent))
            {
                raycaster = null;
                this.PositionComp.OnPositionChanged -= OnPositionChanged;
            }
        }

        void OnPositionChanged(MyPositionComponentBase obj)
        {
            MatrixD worldMatrix = obj.WorldMatrix;
            if (raycaster != null)
                raycaster.OnWorldPosChanged(ref worldMatrix);
        }

        public override void OnRemovedFromScene(object source)
        {
            base.OnRemovedFromScene(source);
        }

        public override void OnAddedToScene(object source)
        {
            bool nearFlag = Render.NearFlag;
            Render.NearFlag = false;

            base.OnAddedToScene(source);
        }

        protected virtual void OnControlAcquired_UpdateCamera()
        {
        }

        protected virtual bool IsCameraController()
        {
            return false;
        }

        protected void OnControlAcquired(MyEntityController controller)
        {
            // Try to take control of ship
            // This won't be here at all
            if (MySession.Static.LocalHumanPlayer == controller.Player)
            {
                if (MyFakes.ENABLE_WHEEL_CONTROLS_IN_COCKPIT)
                    GridWheels.InitControl();

                if (MySession.Static.CameraController is MyEntity && IsCameraController())
                    MySession.Static.SetCameraController(MyCameraControllerEnum.Entity, this);

                //if (MyGuiScreenGamePlay.Static != null)
                //    MySession.Static.CameraAttachedToChanged += Static_CameraAttachedToChanged;

                if (MySession.Static.Settings.RespawnShipDelete && CubeGrid.IsRespawnGrid)
                    MyHud.Notifications.Add(MyNotificationSingletons.RespawnShipWarning);

                Static_CameraAttachedToChanged(null, null);

                RefreshControlNotifications();

                if (IsCameraController())
                {
                    OnControlAcquired_UpdateCamera();
                }

                MyHud.HideAll();
                MyHud.ShipInfo.Show(null);
                MyHud.Crosshair.ResetToDefault();
                MyHud.CharacterInfo.Show(null);
                MyHud.SinkGroupInfo.Visible = true;
                MyHud.GravityIndicator.Entity = this;
                MyHud.GravityIndicator.Show(null);
                MyHud.OreMarkers.Visible = true;
                MyHud.LargeTurretTargets.Visible = true;
            }
            else
            {
#warning TODO: Add player name change support
                //controller.Player.OnDisplayNameChanged += UpdateHudMarker;
                UpdateHudMarker();
            }

            if (m_enableShipControl && (IsMainCockpit == true || CubeGrid.HasMainCockpit() == false))
            {
                var group = ControlGroup.GetGroup(CubeGrid);
                if (group != null)
                {
                    group.GroupData.ControlSystem.AddControllerBlock(this);
                }
                /*if (Sync.IsServer)
                {
                    var group = ControlGroup.GetGroup(CubeGrid);
                    if (group != null)
                    {
                        if (!CubeGrid.GridSystems.ControlSystem.ControllerSteamId.HasValue)
                        {
                            foreach (var node in group.Nodes)
                                Sync.Controllers.TryExtendControl(this, node.NodeData);
                        }
                    }
                    else
                        Sync.Controllers.TryExtendControl(this, CubeGrid);

                }*/

                GridSelectionSystem.OnControlAcquired();
            }

            if (BuildingMode && (MySession.Static.ControlledEntity is MyRemoteControl)) BuildingMode = false;
            if (BuildingMode)
            {
                MyHud.Crosshair.ChangeDefaultSprite(MyHudTexturesEnum.Target_enemy, 0.01f);
            }
            else
            {
                MyHud.Crosshair.ResetToDefault();
            }

            var thrustComponent = EntityThrustComponent;
            if (controller == Sync.Players.GetEntityController(CubeGrid) && thrustComponent != null)
                thrustComponent.Enabled = m_controlThrusters;

            UpdateShipInfo10(true);

            NeedsUpdate |= MyEntityUpdateEnum.EACH_FRAME;

            if (Sync.IsServer || controller.Player == MySession.Static.LocalHumanPlayer)
                NeedsUpdate |= MyEntityUpdateEnum.EACH_10TH_FRAME | MyEntityUpdateEnum.EACH_100TH_FRAME;
        }

        protected virtual void OnControlReleased_UpdateCamera()
        {

        }

        protected virtual void OnControlReleased(MyEntityController controller)
        {
            var thrustComponent = EntityThrustComponent;
            // Release control of the ship
            if (Sync.Players.GetEntityController(this) == controller && thrustComponent != null)
                thrustComponent.Enabled = true;

            if (MySession.Static.LocalHumanPlayer == controller.Player)
            {
                OnControlReleased_UpdateCamera();

                ForceFirstPersonCamera = false;

                if (MyGuiScreenGamePlay.Static != null)
                {
                    Static_CameraAttachedToChanged(null, null);
                    //MySession.Static.CameraAttachedToChanged -= Static_CameraAttachedToChanged;
                }

                MyHud.Notifications.Remove(MyNotificationSingletons.RespawnShipWarning);

                RemoveControlNotifications();

                if (thrustComponent != null)
                {
                    ClearMovementControl();
                }

                MyHud.ShipInfo.Hide();
                MyHud.GravityIndicator.Hide();
                MyHud.Crosshair.HideDefaultSprite();
                MyHud.Crosshair.Recenter();
                MyHud.LargeTurretTargets.Visible = false;
                MyHud.Notifications.Remove(m_noControlNotification);
                m_noControlNotification = null;
            }
            else
            {
                if (!MyFakes.ENABLE_RADIO_HUD)
                {
                    MyHud.LocationMarkers.UnregisterMarker(this);
                }
#warning TODO: Add player name changing support
                //controller.Player.OnDisplayNameChanged -= UpdateHudMarker;
            }

            if (IsShooting())
            {
                EndShootAll();
            }

            if (m_enableShipControl && (IsMainCockpit == true || CubeGrid.HasMainCockpit() == false))
            {
                if (GridSelectionSystem != null)
                {
                    GridSelectionSystem.OnControlReleased();
                }

                //if (Sync.IsServer)
                /*{
                    var group = ControlGroup.GetGroup(CubeGrid);
                    Debug.Assert(group != null, "Grid should be in group when player lefts cockpit?");
                    if (group != null)
                    {
                        foreach (var node in group.Nodes)
                            Sync.Controllers.RemoveControlledEntity(node.NodeData);
                    }
                }*/
                var group = ControlGroup.GetGroup(CubeGrid);
                if (group != null)
                {
                    group.GroupData.ControlSystem.RemoveControllerBlock(this);
                }
            }
        }

        //Will be called when someone kicks player out of controller
        public virtual void ForceReleaseControl()
        {

        }

        void UpdateHudMarker()
        {
            if (!MyFakes.ENABLE_RADIO_HUD)
            {
                MyHud.LocationMarkers.RegisterMarker(this, new MyHudEntityParams()
                {
                    FlagsEnum = MyHudIndicatorFlagsEnum.SHOW_TEXT,
                    Text = new StringBuilder(ControllerInfo.Controller.Player.DisplayName),
                    ShouldDraw = MyHud.CheckShowPlayerNamesOnHud,
                    MustBeDirectlyVisible = true,
                });
            }
        }

        protected virtual bool ShouldSit()
        {
            return !m_enableShipControl;
        }

        #endregion

        #region Interactions
        void Static_CameraAttachedToChanged(IMyCameraController oldController, IMyCameraController newController)
        {
            if (MySession.Static.ControlledEntity == this && newController != MyThirdPersonSpectator.Static && newController != this)
            {
                EndShootAll();
            }

            UpdateCameraAfterChange();
        }

        protected virtual void UpdateCameraAfterChange(bool resetHeadLocalAngle = true)
        {
        }

        public void Shoot(MyShootActionEnum action)
        {
            if (m_enableShipControl && !IsWaitingForWeaponSwitch)
            {
                MyGunStatusEnum status;
                IMyGunObject<MyDeviceBase> gun;
                if (GridSelectionSystem.CanShoot(action, out status, out gun))
                {
                    GridSelectionSystem.Shoot(action);
                }
            }
        }

        public void Zoom(bool newKeyPress)
        {
        }

        public void Use()
        {
            MyGuiAudio.PlaySound(MyGuiSounds.HudUse);
            RaiseControlledEntityUsed();
        }

        public void RaiseControlledEntityUsed()
        {
            MyMultiplayer.RaiseEvent(this, x => x.sync_ControlledEntity_Used);
        }

        public void UseContinues()
        {
        }

        public void UseFinished()
        {
        }

        public void Crouch()
        {
        }

        public void Jump()
        {
        }

        public void SwitchWalk()
        {
        }

        public void Sprint(bool enabled)
        {
        }

        public void Up()
        {
        }

        public void Down()
        {
        }

        public virtual void ShowInventory()
        {
        }

        public virtual void ShowTerminal()
        {
        }

        public void SwitchBroadcasting()
        {

        }

        public void SwitchDamping()
        {
            if (m_enableShipControl && EntityThrustComponent != null)
                EnableDampingInternal(!EntityThrustComponent.DampenersEnabled, true);
        }

        internal void EnableDampingInternal(bool enableDampeners, bool updateProxy)
        {
            if (EntityThrustComponent == null)
                return;

            EntityThrustComponent.DampenersEnabled = enableDampeners;

            if (updateProxy)
            {
                m_dampenersEnabled.Value = enableDampeners;
            }

            if (ControllerInfo.IsLocallyHumanControlled())
            {
                if (m_inertiaDampenersNotification == null)
                    m_inertiaDampenersNotification = new MyHudNotification();
                m_inertiaDampenersNotification.Text = (EntityThrustComponent.DampenersEnabled ? MyCommonTexts.NotificationInertiaDampenersOn : MyCommonTexts.NotificationInertiaDampenersOff);
                MyHud.Notifications.Add(m_inertiaDampenersNotification);
            }
        }

        public virtual void SwitchThrusts()
        {
        }

        public void Die()
        {
        }

        public void SwitchLights()
        {
            if (m_enableShipControl)
            {
                if (GridReflectorLights.ReflectorsEnabled == MyMultipleEnabledEnum.AllDisabled)
                    GridReflectorLights.ReflectorsEnabled = MyMultipleEnabledEnum.AllEnabled;
                else
                    // When some lights are on, we consider lights to be on and want to disable them.
                    GridReflectorLights.ReflectorsEnabled = MyMultipleEnabledEnum.AllDisabled;
            }
        }

        public void SwitchLeadingGears()
        {
            if (m_enableShipControl)
            {
                CubeGrid.GridSystems.LandingSystem.Switch();
                CubeGrid.GridSystems.ConveyorSystem.ToggleConnectors();
            }
        }

        public void SwitchReactors()
        {
            if (m_enableShipControl)
            {
                if (m_reactorsSwitched)
                {
                    CubeGrid.SendPowerDistributorState(MyMultipleEnabledEnum.AllDisabled, MySession.Static.LocalPlayerId);
                }
                else
                {
                    CubeGrid.SendPowerDistributorState(MyMultipleEnabledEnum.AllEnabled, MySession.Static.LocalPlayerId);
                }
                CubeGrid.ActivatePhysics();
                m_reactorsSwitched = !m_reactorsSwitched;
            }
        }

        #endregion

        #region HUD

        public void DrawHud(IMyCameraController camera, long playerId)
        {
            if (GridSelectionSystem != null)
                GridSelectionSystem.DrawHud(camera, playerId);

            var to = PositionComp.GetPosition() + 1000 * PositionComp.WorldMatrix.Forward;

            Vector2 target = Vector2.Zero;
            if (MyHudCrosshair.GetProjectedVector(to, ref target))
            {
                if (BuildingMode)
                    target.Y += 0.17f * MyGuiManager.GetHudSize().Y;
                MyHud.Crosshair.ChangePosition(target);
            }

            if (raycaster != null && raycaster.HitBlock != null)
            {

                MyHud.BlockInfo.Visible = true;

                MyHud.BlockInfo.MissingComponentIndex = -1;
                MyHud.BlockInfo.BlockName = raycaster.HitBlock.BlockDefinition.DisplayNameText;
                MyHud.BlockInfo.BlockIcon = raycaster.HitBlock.BlockDefinition.Icon;
                MyHud.BlockInfo.BlockIntegrity = raycaster.HitBlock.Integrity / raycaster.HitBlock.MaxIntegrity;
                MyHud.BlockInfo.CriticalIntegrity = raycaster.HitBlock.BlockDefinition.CriticalIntegrityRatio;
                MyHud.BlockInfo.CriticalComponentIndex = raycaster.HitBlock.BlockDefinition.CriticalGroup;
                MyHud.BlockInfo.OwnershipIntegrity = raycaster.HitBlock.BlockDefinition.OwnershipIntegrityRatio;

                MySlimBlock.SetBlockComponents(MyHud.BlockInfo, raycaster.HitBlock);
            }

        }


        #endregion

        #region Properties

        public MyEntity TopGrid
        {
            get
            {
                return Parent;
            }
        }

        public MyEntity IsUsing
        {
            get { return null; }
        }

        public virtual bool IsLargeShip()
        {
            return true;
        }

        public override Vector3D LocationForHudMarker
        {
            get
            {
                return base.LocationForHudMarker + (0.65 * CubeGrid.GridSize * BlockDefinition.Size.Y * PositionComp.WorldMatrix.Up);
            }
        }

        public new MyShipControllerDefinition BlockDefinition
        {
            get { return base.BlockDefinition as MyShipControllerDefinition; }
        }

        #endregion

        #region Player controlled parameters

        public bool ControlThrusters
        {
            get { return m_controlThrusters; }
            set
            {
                m_controlThrusters.Value = value;
                if (EntityThrustComponent != null && Sync.Players.HasExtendedControl(this, CubeGrid))
                    EntityThrustComponent.Enabled = m_controlThrusters;
            }
        }

        public bool ControlWheels
        {
            get { return m_controlWheels; }
            set { m_controlWheels.Value = value; }
        }

        #endregion


        //public MyGunTypeEnum? GetWeaponType(MyObjectBuilderType weapon)
        //{
        //    if (weapon == typeof(MyObjectBuilder_Drill))
        //        return MyGunTypeEnum.Drill;

        //    else if (weapon == typeof(MyObjectBuilder_SmallMissileLauncher))
        //        return MyGunTypeEnum.Missile;

        //    else if (weapon == typeof(MyObjectBuilder_SmallGatlingGun))
        //        return MyGunTypeEnum.Projectile;

        //    else if (weapon == typeof(MyObjectBuilder_ShipGrinder))
        //        return MyGunTypeEnum.AngleGrinder;

        //    else if (weapon == typeof(MyObjectBuilder_ShipWelder))
        //        return MyGunTypeEnum.Welder;

        //    else
        //        return null;
        //}


        public bool CanSwitchToWeapon(MyDefinitionId? weapon)
        {
            if (weapon == null) return true;

            var type = weapon.Value.TypeId;
            if (type == typeof(MyObjectBuilder_Drill) ||
                type == typeof(MyObjectBuilder_SmallMissileLauncher) ||
                type == typeof(MyObjectBuilder_SmallGatlingGun) ||
                type == typeof(MyObjectBuilder_ShipGrinder) ||
                type == typeof(MyObjectBuilder_ShipWelder) ||
                type == typeof(MyObjectBuilder_SmallMissileLauncherReload))
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        public void SwitchToWeapon(MyDefinitionId weapon)
        {
            if (m_enableShipControl)
            {
                SwitchToWeaponInternal(weapon, true);
            }
        }

		public void SwitchToWeapon(MyToolbarItemWeapon weapon)
		{
			if (m_enableShipControl)
			{
				SwitchToWeaponInternal((weapon != null ? weapon.Definition.Id : (MyDefinitionId?)null), true);
			}
		}

        void SwitchToWeaponInternal(MyDefinitionId? weapon, bool updateSync)
        {
            if (updateSync)
            {
                RequestSwitchToWeapon(weapon, null, 0);
                return;
            }

            StopCurrentWeaponShooting();

            MyAnalyticsHelper.ReportActivityEnd(this, "item_equip");
            if (weapon.HasValue)
            {
                //    var gun = GetWeaponType(weapon.Value.TypeId);

                SwitchToWeaponInternal(weapon);

                string[] weaponNameParts = ((System.Type)weapon.Value.TypeId).Name.Split('_');
                MyAnalyticsHelper.ReportActivityStart(this, "item_equip", "character", "ship_item_usage", weaponNameParts.Length > 1 ? weaponNameParts[1] : weaponNameParts[0]);
            }
            else
            {
                m_selectedGunId = null;
                GridSelectionSystem.SwitchTo(null);
            }
        }

        void SwitchToWeaponInternal(MyDefinitionId? gunId)
        {
            GridSelectionSystem.SwitchTo(gunId, m_singleWeaponMode);
            m_selectedGunId = gunId;

            Debug.Assert(gunId != null, "gunType Should not be null when switching weapon. (Cestmir)");

            if (ControllerInfo.IsLocallyHumanControlled())
            {
                if (m_weaponSelectedNotification == null)
                    m_weaponSelectedNotification = new MyHudNotification(MyCommonTexts.NotificationSwitchedToWeapon);
                m_weaponSelectedNotification.SetTextFormatArguments(MyDeviceBase.GetGunNotificationName(m_selectedGunId.Value));
                MyHud.Notifications.Add(m_weaponSelectedNotification);
            }
        }

        void SwitchAmmoMagazineInternal(bool sync)
        {
            if (sync)
            {
                MyMultiplayer.RaiseEvent(this, x => x.OnSwitchAmmoMagazineRequest);
                return;
            }

            if (m_enableShipControl && !IsWaitingForWeaponSwitch)
            {
                GridSelectionSystem.SwitchAmmoMagazine();
            }
        }

        void SwitchAmmoMagazineSuccess()
        {
            if (GridSelectionSystem.CanSwitchAmmoMagazine())
            {
                SwitchAmmoMagazineInternal(false);
            }
        }

        private void ShowShootNotification(MyGunStatusEnum status, IMyGunObject<MyDeviceBase> weapon)
        {
            if (!ControllerInfo.IsLocallyHumanControlled())
                return;

            switch (status)
            {
                case MyGunStatusEnum.NotSelected:
                    if (m_noWeaponNotification == null)
                    {
                        m_noWeaponNotification = new MyHudNotification(MyCommonTexts.NotificationNoWeaponSelected, 2000, font: MyFontEnum.Red);
                        MyHud.Notifications.Add(m_noWeaponNotification);
                    }

                    MyHud.Notifications.Add(m_noWeaponNotification);
                    break;
                case MyGunStatusEnum.OutOfAmmo:
                    if (m_outOfAmmoNotification == null)
                    {
                        m_outOfAmmoNotification = new MyHudNotification(MyCommonTexts.OutOfAmmo, 2000, font: MyFontEnum.Red);
                    }

                    if (weapon is MyCubeBlock)
                        m_outOfAmmoNotification.SetTextFormatArguments((weapon as MyCubeBlock).DisplayNameText);

                    MyHud.Notifications.Add(m_outOfAmmoNotification);
                    break;
                case MyGunStatusEnum.NotFunctional:
                case MyGunStatusEnum.OutOfPower:
                    if (m_weaponNotWorkingNotification == null)
                    {
                        m_weaponNotWorkingNotification = new MyHudNotification(MyCommonTexts.NotificationWeaponNotWorking, 2000, font: MyFontEnum.Red);
                    }

                    if (weapon is MyCubeBlock)
                        m_weaponNotWorkingNotification.SetTextFormatArguments((weapon as MyCubeBlock).DisplayNameText);

                    MyHud.Notifications.Add(m_weaponNotWorkingNotification);
                    break;
                default:
                    break;
            }
        }

        public override void OnRegisteredToGridSystems()
        {
            GridGyroSystem = CubeGrid.GridSystems.GyroSystem;
            GridReflectorLights = CubeGrid.GridSystems.ReflectorLightSystem;

            if (MyFakes.ENABLE_WHEEL_CONTROLS_IN_COCKPIT)
            {
                ControlThrusters = m_controlThrusters;
                ControlWheels = m_controlWheels;
            }

            CubeGrid.AddedToLogicalGroup += CubeGrid_AddedToLogicalGroup;
            CubeGrid.RemovedFromLogicalGroup += CubeGrid_RemovedFromLogicalGroup;
            SetWeaponSystem(CubeGrid.GridSystems.WeaponSystem);

            base.OnRegisteredToGridSystems();
        }

        public override void OnUnregisteredFromGridSystems()
        {
            //System.Diagnostics.Debug.Assert(EntityThrustComponent != null);
            //System.Diagnostics.Debug.Assert(GridGyroSystem != null);
            //System.Diagnostics.Debug.Assert(GridWeaponSystem != null);

            if (EntityThrustComponent != null)
            {
                ClearMovementControl();
            }

            CubeGrid.AddedToLogicalGroup -= CubeGrid_AddedToLogicalGroup;
            CubeGrid.RemovedFromLogicalGroup -= CubeGrid_RemovedFromLogicalGroup;
            CubeGrid_RemovedFromLogicalGroup();

            GridGyroSystem = null;
            GridReflectorLights = null;

            base.OnUnregisteredFromGridSystems();
        }

        private void CubeGrid_RemovedFromLogicalGroup()
        {
            GridSelectionSystem.WeaponSystem = null;
            GridSelectionSystem.SwitchTo(null);
        }

        private void CubeGrid_AddedToLogicalGroup(MyGridLogicalGroupData obj)
        {
            SetWeaponSystem(obj.WeaponSystem);
        }

        public void SetWeaponSystem(MyGridWeaponSystem weaponSystem)
        {
            GridSelectionSystem.WeaponSystem = weaponSystem;
            GridSelectionSystem.SwitchTo(m_selectedGunId, m_singleWeaponMode);
        }

        public override void UpdateVisual()
        {
            if (Render.NearFlag)
            {
				Render.ColorMaskHsv = SlimBlock.ColorMaskHSV;

                //TODO: Find out how to correctly change Near model
                return;
            }

            base.UpdateVisual();
        }


        #region Multiplayer

        protected virtual void sync_UseSuccess(UseActionEnum actionEnum, IMyControllableEntity user)
        {
        }

        void sync_UseFailed(UseActionEnum actionEnum, UseActionResult actionResult, IMyControllableEntity user)
        {
            if (user != null && user.ControllerInfo.IsLocallyHumanControlled())
            {
                if (actionResult == UseActionResult.UsedBySomeoneElse)
                    MyHud.Notifications.Add(new MyHudNotification(MyCommonTexts.AlreadyUsedBySomebodyElse, 2500, MyFontEnum.Red));
                else if (actionResult == UseActionResult.AccessDenied)
                    MyHud.Notifications.Add(MyNotificationSingletons.AccessDenied);
                else if (actionResult == UseActionResult.Unpowered)
                    MyHud.Notifications.Add(new MyHudNotification(MySpaceTexts.BlockIsNotPowered, 2500, MyFontEnum.Red));
                else if (actionResult == UseActionResult.CockpitDamaged)
                    MyHud.Notifications.Add(new MyHudNotification(MySpaceTexts.Notification_CockpitIsDamaged, 2500, MyFontEnum.Red));
            }
        }

        [Event,Reliable,Server,Broadcast]
        protected void sync_ControlledEntity_Used()
        {
            OnControlledEntity_Used();
        }

        protected virtual void OnControlledEntity_Used() { }

        #endregion

        public MyEntity Entity
        {
            get { return this; }
        }

        private MyControllerInfo m_info = new MyControllerInfo();
        protected bool m_singleWeaponMode;
        protected Vector3 m_headLocalPosition;
        private MyHudNotification m_notificationWeaponMode;
        public MyControllerInfo ControllerInfo { get { return m_info; } }


        void SwitchToWeaponSuccess(MyDefinitionId? weapon, MyObjectBuilder_Base weaponObjectBuilder, long weaponEntityId)
        {
            SwitchToWeaponInternal(weapon, false);
        }

        MyRechargeSocket IMyRechargeSocketOwner.RechargeSocket
        {
            get
            {
                return m_rechargeSocket;
            }
        }

        public void BeginShoot(MyShootActionEnum action)
        {
            if (!IsWaitingForWeaponSwitch)
            {
                MyGunStatusEnum status = MyGunStatusEnum.OK;
                IMyGunObject<MyDeviceBase> gun = null;
                bool canShoot = GridSelectionSystem.CanShoot(action, out status, out gun);

                if (status != MyGunStatusEnum.OK)
                {
                    ShowShootNotification(status, gun);
                }

                BeginShootSync(action);
            }
        }

        protected void EndShootAll()
        {
            foreach (MyShootActionEnum action in MyEnum<MyShootActionEnum>.Values)
            {
                if (IsShooting(action))
                {
                    EndShoot(action);
                }
            }
        }

        private void StopCurrentWeaponShooting()
        {
            foreach (MyShootActionEnum action in MyEnum<MyShootActionEnum>.Values)
            {
                if (IsShooting(action))
                {
                    GridSelectionSystem.EndShoot(action);
                }
            }
        }

        public void EndShoot(MyShootActionEnum action)
        {
            if (BuildingMode)
            {
                if (Pilot!=null)
                    Pilot.EndShoot(action);
            }
            else
                EndShootSync(action);
        }

        public void OnBeginShoot(MyShootActionEnum action)
        {
            MyGunStatusEnum status = MyGunStatusEnum.OK;
            IMyGunObject<MyDeviceBase> gun = null;
            bool canShoot = GridSelectionSystem.CanShoot(action, out status, out gun);
            if (canShoot == false && status != MyGunStatusEnum.OK && status != MyGunStatusEnum.Cooldown)
            {
                ShootBeginFailed(action, status, gun);
            }
        }

        public void OnEndShoot(MyShootActionEnum action)
        {
            GridSelectionSystem.EndShoot(action);
        }

        private void ShootBeginFailed(MyShootActionEnum action, MyGunStatusEnum status, IMyGunObject<MyDeviceBase> failedGun)
        {
            failedGun.BeginFailReaction(action, status);
        }

        protected override void Closing()
        {
            if (MyFakes.ENABLE_NEW_SOUNDS)
                StopLoopSound();

            IsMainCockpit = false;
            CubeGrid.OnGridSplit -= CubeGrid_OnGridSplit;
            Components.ComponentAdded -= OnComponentAdded;
            Components.ComponentRemoved -= OnComponentRemoved;
            base.Closing();
        }

        protected virtual void UpdateSoundState()
        {

        }

        protected virtual void StartLoopSound()
        {

        }

        protected virtual void StopLoopSound()
        {

        }

        public void RemoveUsers(bool local)
        {
            if (local)
            {
                RemoveLocal();
            }
            else
            {
                RaiseControlledEntityUsed();
            }
        }

        protected virtual void RemoveLocal()
        {

        }

        internal void SwitchWeaponMode()
        {
            SingleWeaponMode = !SingleWeaponMode;
        }

        public bool SingleWeaponMode
        {
            get { return m_singleWeaponMode; }
            private set
            {
                if (m_singleWeaponMode != value)
                {
                    m_singleWeaponMode = value;
					if (m_selectedGunId.HasValue)
						SwitchToWeapon(m_selectedGunId.Value);
					else
						SwitchToWeapon(null);
                }
            }
        }

        private readonly Sync<bool> m_isMainCockpit;
        public bool IsMainCockpit
        {
            get
            {
                return m_isMainCockpit;
            }
            set
            {
               m_isMainCockpit.Value = value;
            }
        }

        private void SetMainCockpit(bool value)
        {
            if (value)
            {
                if (CubeGrid.HasMainCockpit() && !CubeGrid.IsMainCockpit(this))
                {
                    IsMainCockpit = false;
                    return;
                }
            }
            IsMainCockpit = value;
        }

        private void MainCockpitChanged()
        {
            if (m_isMainCockpit)
            {
                CubeGrid.SetMainCockpit(this);
            }
            else
            {
                if (CubeGrid.IsMainCockpit(this))
                {
                    CubeGrid.SetMainCockpit(null);
                }
            }
        }

        protected virtual bool CanBeMainCockpit()
        {
            return false;
        }

        protected bool IsMainCockpitFree()
        {
            return CubeGrid.HasMainCockpit() == false || CubeGrid.IsMainCockpit(this);
        }

		readonly Sync<bool> m_horizonIndicatorEnabled;
        
		public bool HorizonIndicatorEnabled
		{
			get { return m_horizonIndicatorEnabled; }
			set
			{
				m_horizonIndicatorEnabled.Value = value;
			}
		}

		protected void SetHorizonIndicator(bool newValue)
		{
			if (HorizonIndicatorEnabled == newValue)
				return;
		}

        public virtual MyToolbarType ToolbarType
        {
            get
            {
                return m_enableShipControl ? MyToolbarType.Ship : MyToolbarType.Seat;
            }
        }

        bool IMyModdingControllableEntity.ForceFirstPersonCamera
        {
            get
            {
                return ForceFirstPersonCamera;
            }
            set
            {
                ForceFirstPersonCamera = value;
            }
        }

        MatrixD Sandbox.ModAPI.Interfaces.IMyControllableEntity.GetHeadMatrix(bool includeY, bool includeX, bool forceHeadAnim, bool forceHeadBone)
        {
            return GetHeadMatrix(includeY, includeX, forceHeadAnim);
        }

        void Sandbox.ModAPI.Interfaces.IMyControllableEntity.MoveAndRotate(Vector3 moveIndicator, Vector2 rotationIndicator, float rollIndicator)
        {
            MoveAndRotate(moveIndicator, rotationIndicator, rollIndicator);
        }

        void Sandbox.ModAPI.Interfaces.IMyControllableEntity.MoveAndRotateStopped()
        {
            MoveAndRotateStopped();
        }

        void Sandbox.ModAPI.Interfaces.IMyControllableEntity.Use()
        {
            Use();
        }

        void Sandbox.ModAPI.Interfaces.IMyControllableEntity.UseContinues()
        {
            UseContinues();
        }

        void Sandbox.ModAPI.Interfaces.IMyControllableEntity.Jump()
        {
            Jump();
        }

        void Sandbox.ModAPI.Interfaces.IMyControllableEntity.Up()
        {
            Up();
        }

        void Sandbox.ModAPI.Interfaces.IMyControllableEntity.Crouch()
        {
            Crouch();
        }

        void Sandbox.ModAPI.Interfaces.IMyControllableEntity.Down()
        {
            Down();
        }

        void Sandbox.ModAPI.Interfaces.IMyControllableEntity.ShowInventory()
        {
            ShowInventory();
        }

        void Sandbox.ModAPI.Interfaces.IMyControllableEntity.ShowTerminal()
        {
            ShowTerminal();
        }

        void Sandbox.ModAPI.Interfaces.IMyControllableEntity.SwitchThrusts()
        {
            SwitchThrusts();
        }

        void Sandbox.ModAPI.Interfaces.IMyControllableEntity.SwitchDamping()
        {
            SwitchDamping();
        }

        void Sandbox.ModAPI.Interfaces.IMyControllableEntity.SwitchLights()
        {
            SwitchLights();
        }

        void Sandbox.ModAPI.Interfaces.IMyControllableEntity.SwitchLeadingGears()
        {
            SwitchLeadingGears();
        }

        void Sandbox.ModAPI.Interfaces.IMyControllableEntity.SwitchReactors()
        {
            SwitchReactors();
        }

        void Sandbox.ModAPI.Interfaces.IMyControllableEntity.SwitchHelmet()
        {

        }

        void Sandbox.ModAPI.Interfaces.IMyControllableEntity.Die()
        {
            Die();
        }


        bool Sandbox.ModAPI.Interfaces.IMyControllableEntity.EnabledThrusts
        {
            get { return false; }
        }

        bool Sandbox.ModAPI.Interfaces.IMyControllableEntity.EnabledDamping
        {
            get { return EntityThrustComponent != null && EntityThrustComponent.DampenersEnabled; }
        }

        bool Sandbox.ModAPI.Interfaces.IMyControllableEntity.EnabledLights
        {
            get { return GridReflectorLights.ReflectorsEnabled == MyMultipleEnabledEnum.AllEnabled; }
        }

        bool Sandbox.ModAPI.Interfaces.IMyControllableEntity.EnabledLeadingGears
        {
            get 
            {
                var state = CubeGrid.GridSystems.LandingSystem.Locked;
                return state == MyMultipleEnabledEnum.Mixed || state == MyMultipleEnabledEnum.AllEnabled;
            }
        }

        bool Sandbox.ModAPI.Interfaces.IMyControllableEntity.EnabledReactors
        {
            get { return GridResourceDistributor.SourcesEnabled == MyMultipleEnabledEnum.AllEnabled; }
        }

        bool IMyControllableEntity.EnabledBroadcasting
        {
            get { return false; }
        }

        bool Sandbox.ModAPI.Interfaces.IMyControllableEntity.EnabledHelmet
        {
            get { return false; }
        }

        void IMyControllableEntity.SwitchAmmoMagazine()
        {
            if (m_enableShipControl)
            {
                if (GridSelectionSystem.CanSwitchAmmoMagazine())
                {
                    SwitchAmmoMagazineInternal(true);
                }
            }
        }

        bool IMyControllableEntity.CanSwitchAmmoMagazine()
        {
            return m_selectedGunId.HasValue && GridSelectionSystem.CanSwitchAmmoMagazine();
        }

        public virtual float HeadLocalXAngle
        {
            get;
            set;
        }

        public virtual float HeadLocalYAngle
        {
            get;
            set;
        }

        bool IMyShipController.IsUnderControl { get { return ControllerInfo.Controller != null; } }

        bool IMyShipController.ControlWheels
        {
            get { return ControlWheels; }
        }
        bool IMyShipController.ControlThrusters
        {
            get { return ControlThrusters; }
        }

        bool IMyShipController.HandBrake
        {
            get
            {
                return CubeGrid.GridSystems.WheelSystem.HandBrake;
            }
        }
        bool IMyShipController.DampenersOverride
        {
            get {
	            return EntityThrustComponent != null && EntityThrustComponent.DampenersEnabled;
            }
        }

        void CubeGrid_OnGridSplit(MyCubeGrid grid1, MyCubeGrid grid2)
        {
            CheckGridCokpit(grid1);
            CheckGridCokpit(grid2);
        }

        bool HasCockpit(MyCubeGrid grid)
        {
            return grid.CubeBlocks.Contains(this.SlimBlock);
        }

        void CheckGridCokpit(MyCubeGrid grid)
        {
            if (HasCockpit(grid) == false)
            {
                if (grid.IsMainCockpit(this) && CubeGrid != grid)
                {
                    grid.SetMainCockpit(null);
                }
            }
        }

        public MyEntityCameraSettings GetCameraEntitySettings()
        {
            return null;
        }

        public MyStringId ControlContext
        {
            get { return MySpaceBindingCreator.CX_SPACESHIP; }
        }

        internal override void SetDamageEffect(bool show)
        {
            if (BlockDefinition.DamagedSound != null)
                if (show)
                    m_soundEmitter.PlaySound(BlockDefinition.DamagedSound, true);
                else
                    if (m_soundEmitter.SoundId == BlockDefinition.DamagedSound.SoundId)
                        m_soundEmitter.StopSound(false);
            base.SetDamageEffect(show);
        }

        internal override void StopDamageEffect()
        {
            if (BlockDefinition.DamagedSound != null && m_soundEmitter.SoundId == BlockDefinition.DamagedSound.SoundId)
                m_soundEmitter.StopSound(true);
            base.StopDamageEffect();
        }

        void DampenersEnabledChanged()
        {
            EnableDampingInternal(m_dampenersEnabled.Value, false);
        }

        void RequestSwitchToWeapon(MyDefinitionId? weapon, MyObjectBuilder_EntityBase weaponObjectBuilder, long weaponEntityId)
        {
            if (!Sync.IsServer)
            {
                m_switchWeaponCounter++;
            }

            SerializableDefinitionId? def = weapon;
            MyMultiplayer.RaiseEvent(this, x => x.SwitchToWeaponMessage, def, weaponObjectBuilder, weaponEntityId);
        }

        [Event, Reliable,Server]
        void SwitchToWeaponMessage(SerializableDefinitionId? weapon, [Serialize(MyObjectFlags.Dynamic | MyObjectFlags.Nullable, DynamicSerializerType = typeof(MyObjectBuilderDynamicSerializer))] MyObjectBuilder_EntityBase weaponObjectBuilder, long weaponEntityId)
        {
            if(CanSwitchToWeapon(weapon) == false)
            {
                if (MyEventContext.Current.IsLocallyInvoked)
                {
                    OnSwitchToWeaponFailure(weapon, weaponObjectBuilder, weaponEntityId);
                }
                else
                {
                    MyMultiplayer.RaiseEvent(this, x => x.OnSwitchToWeaponFailure, weapon, weaponObjectBuilder, weaponEntityId, MyEventContext.Current.Sender);
                }
                return;
            }

            if (weaponObjectBuilder != null && weaponObjectBuilder.EntityId == 0)
            {
                weaponObjectBuilder = (MyObjectBuilder_EntityBase)weaponObjectBuilder.Clone();
                weaponObjectBuilder.EntityId = weaponEntityId == 0 ? MyEntityIdentifier.AllocateId() : weaponEntityId;
            }
            OnSwitchToWeaponSuccess(weapon, weaponObjectBuilder, weaponEntityId);

            MyMultiplayer.RaiseEvent(this, x => x.OnSwitchToWeaponSuccess, weapon, weaponObjectBuilder, weaponEntityId);

        }

        [Event, Reliable, Client]
        void OnSwitchToWeaponFailure(SerializableDefinitionId? weapon, [Serialize(MyObjectFlags.Dynamic | MyObjectFlags.Nullable, DynamicSerializerType = typeof(MyObjectBuilderDynamicSerializer))] MyObjectBuilder_EntityBase weaponObjectBuilder, long weaponEntityId)
        {
            if (!Sync.IsServer)
            {
                m_switchWeaponCounter--;
            }
        }

        [Event, Reliable, Broadcast]
        void OnSwitchToWeaponSuccess(SerializableDefinitionId? weapon, [Serialize(MyObjectFlags.Dynamic | MyObjectFlags.Nullable, DynamicSerializerType = typeof(MyObjectBuilderDynamicSerializer))] MyObjectBuilder_EntityBase weaponObjectBuilder, long weaponEntityId)
        {
            if (!Sync.IsServer)
            {
                // Update the counter only if we are waiting for it
                if (m_switchWeaponCounter > 0)
                {
                    m_switchWeaponCounter--;
                }
            }

            SwitchToWeaponSuccess(weapon, weaponObjectBuilder, weaponEntityId);
        }

        public void RequestUse(UseActionEnum actionEnum, IMyControllableEntity usedBy)
        {
            MyMultiplayer.RaiseEvent(this, x => x.RequestUseMessage, actionEnum, usedBy.Entity.EntityId);
        }

        [Event, Reliable, Server]
        void RequestUseMessage(UseActionEnum useAction, long usedById)
        {
            MyEntity controlledEntity;
            bool entityExists = MyEntities.TryGetEntityById<MyEntity>(usedById, out controlledEntity);
            IMyControllableEntity controllableEntity = controlledEntity as IMyControllableEntity;
            Debug.Assert(controllableEntity != null, "Controllable entity needs to get control from another controllable entity");
            Debug.Assert(entityExists);

            UseActionResult useResult = UseActionResult.OK;

            if (entityExists && (useResult = (this as IMyUsableEntity).CanUse(useAction, controllableEntity)) == UseActionResult.OK)
            {
                MyMultiplayer.RaiseEvent(this, x => x.UseSuccessCallback, useAction, usedById, useResult);
                UseSuccessCallback(useAction, usedById, useResult);
            }
            else
            {
                MyMultiplayer.RaiseEvent(this, x => x.UseFailureCallback, useAction, usedById, useResult, MyEventContext.Current.Sender);
            }
        }

        [Event, Reliable, Broadcast]
        void UseSuccessCallback(UseActionEnum useAction, long usedById,UseActionResult useResult)
        {
            MyEntity controlledEntity;
            if (MyEntities.TryGetEntityById<MyEntity>(usedById, out controlledEntity))
            {
                var controllableEntity = controlledEntity as IMyControllableEntity;
                Debug.Assert(controllableEntity != null, "Controllable entity needs to get control from another controllable entity");

                if (controllableEntity != null)
                {
                    VRage.Game.MyRelationsBetweenPlayerAndBlock relation = VRage.Game.MyRelationsBetweenPlayerAndBlock.NoOwnership;
                    var cubeBlock = this as MyCubeBlock;
                    if (cubeBlock != null && controllableEntity.ControllerInfo.Controller != null)
                    {
                        relation = cubeBlock.GetUserRelationToOwner(controllableEntity.ControllerInfo.Controller.Player.Identity.IdentityId);
                    }

                    if (relation.IsFriendly())
                    {
                        sync_UseSuccess(useAction, controllableEntity);
                    }
                    else
                    {
                        sync_UseFailed(useAction, useResult, controllableEntity);
                    }
                }
            }
        }

        [Event, Reliable, Client]
        void UseFailureCallback(UseActionEnum useAction, long usedById, UseActionResult useResult)
        {
            MyEntity controlledEntity;
            bool userFound = MyEntities.TryGetEntityById<MyEntity>(usedById, out controlledEntity);
            Debug.Assert(userFound);
            IMyControllableEntity controllableEntity = controlledEntity as IMyControllableEntity;
            Debug.Assert(controllableEntity != null, "Controllable entity needs to get control from another controllable entity");
            sync_UseFailed(useAction, useResult, controllableEntity);
        }


        [Event, Reliable, Server]
        void OnSwitchAmmoMagazineRequest()
        {
            if ((this as IMyControllableEntity).CanSwitchAmmoMagazine() == false)
            {
                return;
            }

            SwitchAmmoMagazineSuccess();
            MyMultiplayer.RaiseEvent(this, x => x.OnSwitchAmmoMagazineSuccess);
        }

        [Event, Reliable, Broadcast]
        void OnSwitchAmmoMagazineSuccess()
        {
            SwitchAmmoMagazineSuccess();
        }

        public void BeginShootSync(MyShootActionEnum action = MyShootActionEnum.PrimaryAction)
        {
            StartShooting(action);

            MyMultiplayer.RaiseEvent(this, x => x.ShootBeginCallback, action);

            if (MyFakes.SIMULATE_QUICK_TRIGGER)
                EndShootInternal(action);
        }

        [Event, Reliable,Server, BroadcastExcept]
        void ShootBeginCallback(MyShootActionEnum action)
        {
            bool wouldCallStartTwice = Sync.IsServer && MyEventContext.Current.IsLocallyInvoked;
            if (!wouldCallStartTwice)
            {
                StartShooting(action);
            }
        }

        private void StartShooting(MyShootActionEnum action)
        {
            m_isShooting[(int)action] = true;
            OnBeginShoot(action);
        }

        private void StopShooting(MyShootActionEnum action)
        {
            m_isShooting[(int)action] = false;
            OnEndShoot(action);
        }

        public void EndShootSync(MyShootActionEnum action = MyShootActionEnum.PrimaryAction)
        {
            if (MyFakes.SIMULATE_QUICK_TRIGGER) return;

            EndShootInternal(action);
        }

        private void EndShootInternal(MyShootActionEnum action = MyShootActionEnum.PrimaryAction)
        {
            MyMultiplayer.RaiseEvent(this, x => x.ShootEndCallback, action);

            StopShooting(action);
        }

        [Event,Reliable,Server,BroadcastExcept]
        void ShootEndCallback(MyShootActionEnum action)
        {
            bool wouldCallStopTwice = Sync.IsServer && MyEventContext.Current.IsLocallyInvoked;
            if (!wouldCallStopTwice)
            {
                StopShooting(action);
            }
        }

    }
}

