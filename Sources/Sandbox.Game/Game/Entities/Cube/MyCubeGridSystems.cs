﻿#region Using

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Engine.Utils;
using Sandbox.Game.GameSystems;
using Sandbox.Game.GameSystems.Electricity;
using Sandbox.Game.Weapons;
using Sandbox.Game.World;
using VRage;
using VRageMath;
using VRageRender;

using Sandbox.Game.GameSystems.Conveyors;
using System.Text;
using Sandbox.Game.EntityComponents;
using VRage.Game;
using IMyLandingGear = Sandbox.Game.Entities.Interfaces.IMyLandingGear;

#endregion

namespace Sandbox.Game.Entities.Cube
{
    public class MyCubeGridSystems
    {
        internal MyResourceDistributorComponent ResourceDistributor { get; private set; }
        internal MyGridTerminalSystem TerminalSystem { get; private set; }
        internal MyGridConveyorSystem ConveyorSystem { get; private set; }
        internal MyGridGyroSystem GyroSystem { get; private set; }
        internal MyGridWeaponSystem WeaponSystem { get; private set; }
        internal MyGridReflectorLightSystem ReflectorLightSystem { get; private set; }
        internal MyGridWheelSystem WheelSystem { get; private set; }
        internal MyGridLandingSystem LandingSystem { get; private set; }
        internal MyGroupControlSystem ControlSystem { get; private set; }
        internal MyGridCameraSystem CameraSystem { get; private set; }
        /// <summary>
        /// Can be null if Oxygen option is disabled
        /// </summary>
        public MyGridGasSystem GasSystem { get; private set; }
        public MyGridJumpDriveSystem JumpSystem { get; private set; }

        private readonly MyCubeGrid m_cubeGrid;
        protected MyCubeGrid CubeGrid { get { return m_cubeGrid; } }

        private Action<MyBlockGroup> m_terminalSystem_GroupAdded;
        private Action<MyBlockGroup> m_terminalSystem_GroupRemoved;

        private bool m_blocksRegistered = false;

        private readonly HashSet<MyResourceSinkComponent> m_tmpSinks = new HashSet<MyResourceSinkComponent>();

        public MyCubeGridSystems(MyCubeGrid grid)
        {
            m_cubeGrid = grid;

            m_terminalSystem_GroupAdded = TerminalSystem_GroupAdded;
            m_terminalSystem_GroupRemoved = TerminalSystem_GroupRemoved;

            GyroSystem = new MyGridGyroSystem(m_cubeGrid);
            WeaponSystem = new MyGridWeaponSystem();
            ReflectorLightSystem = new MyGridReflectorLightSystem(m_cubeGrid);
            if (MyFakes.ENABLE_WHEEL_CONTROLS_IN_COCKPIT)
            {
                WheelSystem = new MyGridWheelSystem(m_cubeGrid);
            }
            ConveyorSystem = new MyGridConveyorSystem(m_cubeGrid);
            LandingSystem = new MyGridLandingSystem();
            ControlSystem = new MyGroupControlSystem();
            CameraSystem = new MyGridCameraSystem(m_cubeGrid);

            if (MySession.Static.Settings.EnableOxygen)
            {
                GasSystem = new MyGridGasSystem(m_cubeGrid);
            }
            if (MyPerGameSettings.EnableJumpDrive)
            {
                JumpSystem = new MyGridJumpDriveSystem(m_cubeGrid);
            }

            m_blocksRegistered = true;
        }

        public virtual void Init(MyObjectBuilder_CubeGrid builder)
        {
	        var thrustComp = CubeGrid.Components.Get<MyEntityThrustComponent>();
			if(thrustComp != null)
				thrustComp.DampenersEnabled = builder.DampenersEnabled;

            if (WheelSystem != null)
                m_cubeGrid.SetHandbrakeRequest(builder.Handbrake);

            if (MySession.Static.Settings.EnableOxygen)
            {
                GasSystem.Init(builder.OxygenAmount);
            }

            if (MyPerGameSettings.EnableJumpDrive)
            {
                JumpSystem.Init(builder.JumpDriveDirection, builder.JumpRemainingTime);
            }

            var thrustComponent = CubeGrid.Components.Get<MyEntityThrustComponent>();
            if (thrustComponent != null)
                thrustComponent.MergeAllGroupsDirty();
        }

        public virtual void BeforeBlockDeserialization(MyObjectBuilder_CubeGrid builder)
        {
            ConveyorSystem.BeforeBlockDeserialization(builder.ConveyorLines);
        }

        public virtual void AfterBlockDeserialization()
        {
            ConveyorSystem.AfterBlockDeserialization();
            ConveyorSystem.ResourceSink.Update();
        }

        public void UpdateBeforeSimulation()
        {
			ProfilerShort.Begin("Thrusters and Gyro");
	        MyEntityThrustComponent thrustComp;
			if(CubeGrid.Components.TryGet(out thrustComp))
				thrustComp.UpdateBeforeSimulation();

            GyroSystem.UpdateBeforeSimulation();
            ProfilerShort.End();

            if (MyFakes.ENABLE_WHEEL_CONTROLS_IN_COCKPIT)
            {
                ProfilerShort.Begin("Wheels");
                WheelSystem.UpdateBeforeSimulation();
                ProfilerShort.End();
            }

            ProfilerShort.Begin("Conveyors");
            ConveyorSystem.UpdateBeforeSimulation();
            ProfilerShort.End();

            ProfilerShort.Begin("Control");
            ControlSystem.UpdateBeforeSimulation();
            ProfilerShort.End();

            ProfilerShort.Begin("Cameras");
            CameraSystem.UpdateBeforeSimulation();
            ProfilerShort.End();

            if (MySession.Static.Settings.EnableOxygen)
            {
                ProfilerShort.Begin("Oxygen");
                GasSystem.UpdateBeforeSimulation();
                ProfilerShort.End();
            }

            if (MyPerGameSettings.EnableJumpDrive)
            {
                ProfilerShort.Begin("Jump");
                JumpSystem.UpdateBeforeSimulation();
                ProfilerShort.End();
            }
        }

        public virtual void PrepareForDraw()
        {
            ConveyorSystem.PrepareForDraw();
            CameraSystem.PrepareForDraw();
        }

        public void UpdatePower()
        {
			ProfilerShort.Begin("GridSystems.UpdatePower");
            if (ResourceDistributor != null)
                ResourceDistributor.UpdateBeforeSimulation10();
			ProfilerShort.End();
        }

        public virtual void UpdateOnceBeforeFrame()
        {
        }

        public virtual void UpdateBeforeSimulation10()
        {
            UpdatePower();
            CameraSystem.UpdateBeforeSimulation10();
            ConveyorSystem.UpdateBeforeSimulation10();
        }

        public virtual void UpdateBeforeSimulation100()
        {
            if (MySession.Static.Settings.EnableOxygen)
            {
                GasSystem.UpdateBeforeSimulation100();
            }
        }

        public virtual void GetObjectBuilder(MyObjectBuilder_CubeGrid ob)
        {
	        var thrustComp = CubeGrid.Components.Get<MyEntityThrustComponent>();
	        ob.DampenersEnabled = thrustComp == null || thrustComp.DampenersEnabled;

            ConveyorSystem.SerializeLines(ob.ConveyorLines);
            if (ob.ConveyorLines.Count == 0)
                ob.ConveyorLines = null;

            if (WheelSystem != null)
                ob.Handbrake = WheelSystem.HandBrake;

            if (MySession.Static.Settings.EnableOxygen)
            {
                ob.OxygenAmount = GasSystem.GetOxygenAmount();
            }

            if (MyPerGameSettings.EnableJumpDrive)
            {
                ob.JumpDriveDirection = JumpSystem.GetJumpDriveDirection();
                ob.JumpRemainingTime = JumpSystem.GetRemainingJumpTime();
            }
        }

        public virtual void AddGroup(MyBlockGroup group)
        {
            if (TerminalSystem != null)
            {
                TerminalSystem.GroupAdded -= m_terminalSystem_GroupAdded;
                TerminalSystem.AddUpdateGroup(group);
                TerminalSystem.GroupAdded += m_terminalSystem_GroupAdded;
            }
        }

        public virtual void RemoveGroup(MyBlockGroup group)
        {
            if (TerminalSystem != null)
            {
                TerminalSystem.GroupRemoved -= m_terminalSystem_GroupRemoved;
                TerminalSystem.RemoveGroup(group);
                TerminalSystem.GroupRemoved += m_terminalSystem_GroupRemoved;
            }
        }

        public virtual void OnAddedToGroup(MyGridLogicalGroupData group)
        {
            Debug.Assert(group.TerminalSystem != null, "Terminal system is null!");
            TerminalSystem = group.TerminalSystem;
            ResourceDistributor = group.ResourceDistributor;
            WeaponSystem = group.WeaponSystem;

            m_cubeGrid.OnBlockAdded += ResourceDistributor.CubeGrid_OnBlockAddedOrRemoved;
            m_cubeGrid.OnBlockRemoved += ResourceDistributor.CubeGrid_OnBlockAddedOrRemoved;

            ResourceDistributor.AddSink(GyroSystem.ResourceSink);
            ResourceDistributor.AddSink(ConveyorSystem.ResourceSink);
            ResourceDistributor.UpdateBeforeSimulation10();

            ConveyorSystem.ResourceSink.IsPoweredChanged += ResourceDistributor.ConveyorSystem_OnPoweredChanged;

            foreach (var g in m_cubeGrid.BlockGroups)
                TerminalSystem.AddUpdateGroup(g);
            TerminalSystem.GroupAdded += m_terminalSystem_GroupAdded;
            TerminalSystem.GroupRemoved += m_terminalSystem_GroupRemoved;

            foreach (var block in m_cubeGrid.GetBlocks())
            {
                if (block.FatBlock == null)
                    continue;
                if (!block.FatBlock.MarkedForClose)
                {
                    var functionalBlock = block.FatBlock as MyTerminalBlock;
                    if (functionalBlock != null)
                        TerminalSystem.Add(functionalBlock);

                    var producer = block.FatBlock.Components.Get<MyResourceSourceComponent>();
                    if (producer != null)
                        ResourceDistributor.AddSource(producer);

                    var consumer = block.FatBlock.Components.Get<MyResourceSinkComponent>();
                    if (consumer != null)
                        ResourceDistributor.AddSink(consumer);

                    var socketOwner = block.FatBlock as IMyRechargeSocketOwner;
                    if (socketOwner != null)
                        socketOwner.RechargeSocket.ResourceDistributor = group.ResourceDistributor;

                    var weapon = block.FatBlock as IMyGunObject<MyDeviceBase>;
                    if (weapon != null)
                        WeaponSystem.Register(weapon);
                }
            }
        }

        public virtual void OnRemovedFromGroup(MyGridLogicalGroupData group)
        {
            Debug.Assert(TerminalSystem == group.TerminalSystem, "Removing grid from diferent group then it was added to!");
            if (m_blocksRegistered)
            {
                ProfilerShort.Begin("Removing block groups from grid group");
                TerminalSystem.GroupAdded -= m_terminalSystem_GroupAdded;
                TerminalSystem.GroupRemoved -= m_terminalSystem_GroupRemoved;
                foreach (var g in m_cubeGrid.BlockGroups)
                    TerminalSystem.RemoveGroup(g);
                ProfilerShort.End();

                foreach (var block in m_cubeGrid.GetBlocks())
                {
                    if (block.FatBlock == null)
                        continue;

                    var functionalBlock = block.FatBlock as MyTerminalBlock;
                    if (functionalBlock != null)
                        TerminalSystem.Remove(functionalBlock);

                    var producer = block.FatBlock.Components.Get<MyResourceSourceComponent>();
                    if (producer != null)
                        ResourceDistributor.RemoveSource(producer);

                    var consumer = block.FatBlock.Components.Get<MyResourceSinkComponent>();
                    if (consumer != null)
                        ResourceDistributor.RemoveSink(consumer, resetSinkInput: false, markedForClose: block.FatBlock.MarkedForClose);

                    var socketOwner = block.FatBlock as IMyRechargeSocketOwner;
                    if (socketOwner != null)
                        socketOwner.RechargeSocket.ResourceDistributor = null;

                    var weapon = block.FatBlock as IMyGunObject<MyDeviceBase>;
                    if (weapon != null)
                        WeaponSystem.Unregister(weapon);
                }
            }

            ConveyorSystem.ResourceSink.IsPoweredChanged -= ResourceDistributor.ConveyorSystem_OnPoweredChanged;
            group.ResourceDistributor.RemoveSink(ConveyorSystem.ResourceSink, resetSinkInput: false);
            group.ResourceDistributor.RemoveSink(GyroSystem.ResourceSink, resetSinkInput: false);
            group.ResourceDistributor.UpdateBeforeSimulation10();

            m_cubeGrid.OnBlockAdded -= ResourceDistributor.CubeGrid_OnBlockAddedOrRemoved;
            m_cubeGrid.OnBlockRemoved -= ResourceDistributor.CubeGrid_OnBlockAddedOrRemoved;

            ResourceDistributor = null;
            TerminalSystem = null;
            WeaponSystem = null;
        }

        public void OnAddedToGroup(MyGridPhysicalGroupData group)
        {
            ControlSystem = group.ControlSystem;

            foreach (var block in m_cubeGrid.GetBlocks())
            {
                if (block.FatBlock == null)
                    continue;

                var controllerBlock = block.FatBlock as MyShipController;
                if (controllerBlock != null && controllerBlock.ControllerInfo.Controller != null && controllerBlock.EnableShipControl)
                {
                    ControlSystem.AddControllerBlock(controllerBlock);
                }
            }

            ControlSystem.AddGrid(CubeGrid);
        }

        public void OnRemovedFromGroup(MyGridPhysicalGroupData group)
        {
            ControlSystem.RemoveGrid(CubeGrid);

            if (m_blocksRegistered)
            {
                foreach (var block in m_cubeGrid.GetBlocks())
                {
                    if (block.FatBlock == null)
                        continue;

                    var controllerBlock = block.FatBlock as MyShipController;
                    if (controllerBlock != null && controllerBlock.ControllerInfo.Controller != null && controllerBlock.EnableShipControl)
                    {
                        ControlSystem.RemoveControllerBlock(controllerBlock);
                    }
                }
            }

            ControlSystem = null;
        }

        public virtual void BeforeGridClose()
        {
            ConveyorSystem.IsClosing = true;
            ReflectorLightSystem.IsClosing = true;
        }

        public virtual void AfterGridClose()
        {
            ConveyorSystem.AfterGridClose();
            if (MyPerGameSettings.EnableJumpDrive)
            {
                JumpSystem.AfterGridClose();
            }
            m_blocksRegistered = false;
        }

        public virtual void DebugDraw()
        {
            if (MyDebugDrawSettings.DEBUG_DRAW_GRID_TERMINAL_SYSTEMS)
            {
                MyRenderProxy.DebugDrawText3D(m_cubeGrid.WorldMatrix.Translation, TerminalSystem.GetHashCode().ToString(), Color.NavajoWhite, 1.0f, false);
            }

            if (MyDebugDrawSettings.DEBUG_DRAW_CONVEYORS)
            {
                ConveyorSystem.DebugDraw(m_cubeGrid);
                ConveyorSystem.DebugDrawLinePackets();
            }

            if (MySession.Static.Settings.EnableOxygen && MyDebugDrawSettings.DEBUG_DRAW_OXYGEN)
            {
                GasSystem.DebugDraw();
            }
        }

        public virtual bool IsTrash()
        {
            // Powered grids are not trash
            if (this.ResourceDistributor.ResourceState != MyResourceStateEnum.NoPower)
                return false;

            // Controlled grids are not trash
            if (ControlSystem.IsControlled)
                return false;

            return true;
        }

        public virtual void RegisterInSystems(MyCubeBlock block)
        {
            if (ResourceDistributor != null)
            {
                var powerProducer = block.Components.Get<MyResourceSourceComponent>();
                if (powerProducer != null)
                    ResourceDistributor.AddSource(powerProducer);

                var powerConsumer = block.Components.Get<MyResourceSinkComponent>();
                if (powerConsumer != null)
                    ResourceDistributor.AddSink(powerConsumer);

                var socketOwner = block as IMyRechargeSocketOwner;
                if (socketOwner != null)
                    socketOwner.RechargeSocket.ResourceDistributor = ResourceDistributor;
            }

            if (WeaponSystem != null)
            {
                var weapon = block as IMyGunObject<MyDeviceBase>;
                if (weapon != null)
                    WeaponSystem.Register(weapon);
            }

            if (TerminalSystem != null)
            {
                var functionalBlock = block as MyTerminalBlock;
                if (functionalBlock != null)
                    TerminalSystem.Add(functionalBlock);
            }

            // CH: We probably don't need to register controller blocks here. Block that's being added to a grid should not have a controller set
            var controllableBlock = block as MyShipController;
            Debug.Assert(controllableBlock == null || controllableBlock.ControllerInfo.Controller == null, "Controller of added block is not null. Call Cestmir");
            /*if (ControlSystem != null)
            {
                var controllableBlock = block as MyShipController;
                if (controllableBlock != null && controllableBlock.ControllerInfo.Controller != null)
                    ControlSystem.AddControllerBlock(controllableBlock);
            }*/

            var inventoryBlock = (block != null && block.HasInventory) ? block : null;
            if (inventoryBlock != null)
                ConveyorSystem.Add(inventoryBlock);

            var conveyorBlock = block as IMyConveyorEndpointBlock;
            if (conveyorBlock != null)
            {
                conveyorBlock.InitializeConveyorEndpoint();
                ConveyorSystem.AddConveyorBlock(conveyorBlock);
            }

            var segmentBlock = block as IMyConveyorSegmentBlock;
            if (segmentBlock != null)
            {
                segmentBlock.InitializeConveyorSegment();
                ConveyorSystem.AddSegmentBlock(segmentBlock);
            }

            var reflectorLight = block as MyReflectorLight;
            if (reflectorLight != null)
                ReflectorLightSystem.Register(reflectorLight);

            if (MyFakes.ENABLE_WHEEL_CONTROLS_IN_COCKPIT)
            {
                var wheel = block as MyMotorSuspension;
                if (wheel != null)
                    WheelSystem.Register(wheel);
            }

            var landingGear = block as IMyLandingGear;
            if (landingGear != null)
                LandingSystem.Register(landingGear);

            var gyro = block as MyGyro;
            if (gyro != null)
                GyroSystem.Register(gyro);

            var camera = block as MyCameraBlock;
            if (camera != null)
                CameraSystem.Register(camera);

            block.OnRegisteredToGridSystems();
        }

        public virtual void UnregisterFromSystems(MyCubeBlock block)
        {
            // Note: ResourceDistributor, WeaponSystem and TemrminalSystem can be null on closing (they are not in the ship but in the logical group). That's why they are null-checked
            if (ResourceDistributor != null)
            {
                ProfilerShort.Begin("Unregister Power producer");
                var powerProducer = block.Components.Get<MyResourceSourceComponent>();
                if (powerProducer != null)
                    ResourceDistributor.RemoveSource(powerProducer);

                ProfilerShort.BeginNextBlock("Unregister Power consumer");
                var powerConsumer = block.Components.Get<MyResourceSinkComponent>();
                if (powerConsumer != null)
                    ResourceDistributor.RemoveSink(powerConsumer);
                ProfilerShort.End();

                var socketOwner = block as IMyRechargeSocketOwner;
                if (socketOwner != null)
                    socketOwner.RechargeSocket.ResourceDistributor = null;
            }

            ProfilerShort.Begin("Unregister gun object");
            if (WeaponSystem != null)
            {
                var weapon = block as IMyGunObject<MyDeviceBase>;
                if (weapon != null)
                    WeaponSystem.Unregister(weapon);
            }

            ProfilerShort.BeginNextBlock("Unregister functional block");
            if (TerminalSystem != null)
            {
                var functionalBlock = block as MyTerminalBlock;
                if (functionalBlock != null)
                    TerminalSystem.Remove(functionalBlock);
            }

            // CH: We probably don't need to unregister controller blocks here. It's done in ShipController's OnUnregisteredFromGridSystems
            /*ProfilerShort.BeginNextBlock("Unregister controller block");
            if (ControlSystem != null)
            {
                var controllableBlock = block as MyShipController;
                if (controllableBlock != null && controllableBlock.ControllerInfo.Controller != null)
                    ControlSystem.RemoveControllerBlock(controllableBlock);
            }*/

            ProfilerShort.BeginNextBlock("Unregister inventory block");
            var inventoryBlock = (block != null &&  block.HasInventory) ? block : null ;
            if (inventoryBlock != null && inventoryBlock.HasInventory)
                ConveyorSystem.Remove(inventoryBlock);

            ProfilerShort.BeginNextBlock("Unregister conveyor block");
            var conveyorBlock = block as IMyConveyorEndpointBlock;
            if (conveyorBlock != null)
                ConveyorSystem.RemoveConveyorBlock(conveyorBlock);

            ProfilerShort.BeginNextBlock("Unregister segment block");
            var segmentBlock = block as IMyConveyorSegmentBlock;
            if (segmentBlock != null)
                ConveyorSystem.RemoveSegmentBlock(segmentBlock);

            ProfilerShort.BeginNextBlock("Unregister Reflector light");
            var reflectorLight = block as MyReflectorLight;
            if (reflectorLight != null)
                ReflectorLightSystem.Unregister(reflectorLight);

            if (MyFakes.ENABLE_WHEEL_CONTROLS_IN_COCKPIT)
            {
                ProfilerShort.BeginNextBlock("Unregister wheel");
                var wheel = block as MyMotorSuspension;
                if (wheel != null)
                    WheelSystem.Unregister(wheel);
            }

            ProfilerShort.BeginNextBlock("Unregister landing gear");
            var gear = block as IMyLandingGear;
            if (gear != null)
                LandingSystem.Unregister(gear);

            ProfilerShort.BeginNextBlock("Unregister gyro");
            var gyro = block as MyGyro;
            if (gyro != null)
                GyroSystem.Unregister(gyro);

            ProfilerShort.BeginNextBlock("Unregister camera");
            var camera = block as MyCameraBlock;
            if (camera != null)
                CameraSystem.Unregister(camera);

            ProfilerShort.BeginNextBlock("block.OnUnregisteredFromGridSystems()");
            block.OnUnregisteredFromGridSystems();

            ProfilerShort.End();
        }

        public void SyncObject_PowerProducerStateChanged(MyMultipleEnabledEnum enabledState,long playerId)
        {
            // Include the batteries for total power shutdown
            foreach (var block in CubeGrid.GetBlocks())
            {
                if (block.FatBlock is MyBatteryBlock)
                    ((MyBatteryBlock)block.FatBlock).Enabled = enabledState == MyMultipleEnabledEnum.AllEnabled ? true : false;
            }
            ResourceDistributor.ChangeSourcesState(MyResourceDistributorComponent.ElectricityId, enabledState, playerId);
        }

        private void TerminalSystem_GroupRemoved(MyBlockGroup group)
        {
            if (group.CubeGrid != null && group.CubeGrid != m_cubeGrid)
                return;
            foreach (var g in m_cubeGrid.BlockGroups)
                if (g.Name.CompareTo(group.Name) == 0)
                {
                    Debug.Assert(g != group, "TerminalSystem should have own group copy");
                    g.Blocks.Clear();
                    m_cubeGrid.BlockGroups.Remove(g);
                    m_cubeGrid.ModifyGroup(g);
                    break;
                }
        }

        private void TerminalSystem_GroupAdded(MyBlockGroup group)
        {
            foreach (var g in m_cubeGrid.BlockGroups)
                if (g.Name.CompareTo(group.Name) == 0)
                {
                    var oldCount = g.Blocks.Count;
                    g.Blocks.Clear();
                    foreach (var b in group.Blocks)
                        if (b.CubeGrid == m_cubeGrid)
                            g.Blocks.Add(b);
                    if (oldCount != g.Blocks.Count)
                        m_cubeGrid.ModifyGroup(g);
                    if (g.Blocks.Count == 0)
                        m_cubeGrid.BlockGroups.Remove(g);
                    return;
                }
            var gr = new MyBlockGroup(m_cubeGrid);
            gr.Name.AppendStringBuilder(group.Name);
            foreach (var b in group.Blocks)
                if (b.CubeGrid == m_cubeGrid)
                    gr.Blocks.Add(b);
            if (gr.Blocks.Count > 0)
            {
                m_cubeGrid.BlockGroups.Add(gr);
                m_cubeGrid.ModifyGroup(gr);
            }
        }
    }
}
