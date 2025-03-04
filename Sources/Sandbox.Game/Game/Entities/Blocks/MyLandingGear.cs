﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Havok;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Engine.Physics;
using VRageMath;
using VRageRender;
using Sandbox.Game.Entities.Interfaces;
using System.Diagnostics;
using Sandbox.Game.Multiplayer;
using Sandbox.Game.World;
using Sandbox.Engine.Utils;
using Sandbox.Engine.Multiplayer;
using SteamSDK;

using System.Reflection;
using Sandbox.Common;

using VRage.Groups;
using Sandbox.Game.Gui;
using Sandbox.Game.Screens.Terminal.Controls;
using VRage.Utils;
using Sandbox.Definitions;
using VRageMath.PackedVector;
using Sandbox.Game.Localization;
using VRage;
using VRage.Game;
using VRage.Library.Utils;
using VRage.ModAPI;
using VRage.Network;
using VRage.Game.Entity;

namespace Sandbox.Game.Entities.Cube
{
    [MyCubeBlockType(typeof(MyObjectBuilder_LandingGear))]
    class MyLandingGear : MyFunctionalBlock, IMyLandingGear, Sandbox.ModAPI.IMyLandingGear
    {
        protected struct State
        {
            public long? OtherEntityId;
            public MyDeltaTransform? MasterToSlave;
            public Vector3? GearPivotPosition;
            public CompressedPositionOrientation? OtherPivot;
        }

        private MySoundPair m_lockSound;
        private MySoundPair m_unlockSound;
        private MySoundPair m_failedAttachSound;

        static List<HkBodyCollision> m_penetrations = new List<HkBodyCollision>();

        Matrix[] m_lockPositions;

        public Matrix[] LockPositions { get { return m_lockPositions; } }

        HkConstraint m_constraint;

        private HkConstraint SafeConstraint
        {
            get
            {
                if (m_constraint != null && !m_constraint.InWorld)
                {
                    Detach();
                }
                return m_constraint;
            }
        }

        private readonly Sync<LandingGearMode> m_lockModeSync;
        LandingGearMode m_lockMode = LandingGearMode.Unlocked;

        Action<IMyEntity> m_physicsChangedHandler;

        IMyEntity m_attachedTo;

        private bool m_needsToRetryLock = false;
        private int m_autolockTimer = 0;

        public LandingGearMode LockMode
        {
            get
            {
                return m_lockMode;
            }
            private set
            {
                if (m_lockMode != value)
                {
                    var old = m_lockMode;

                    m_lockMode = value;
                    UpdateEmissivity();
                    UpdateText();

                    var handler = LockModeChanged;
                    if (handler != null)
                    {
                        handler(this, old);
                    }
                }
            }
        }
        public bool IsLocked { get { return LockMode == LandingGearMode.Locked; } }

        public event LockModeChangedHandler LockModeChanged;
        private float m_breakForce;
        private readonly Sync<bool> m_autoLock;
        private readonly Sync<State> m_attachedState;

        private readonly Sync<float> m_breakForceSync;

        private long? m_attachedEntityId;

        float m_savedBreakForce = 0;

        bool m_converted = false;

        static MyLandingGear()
        {
            var stateWriter = new MyTerminalControl<MyLandingGear>.WriterDelegate((b, sb) => b.WriteLockStateValue(sb));

            var lockBtn = new MyTerminalControlButton<MyLandingGear>("Lock", MySpaceTexts.BlockActionTitle_Lock, MySpaceTexts.Blank, (b) => b.RequestLandingGearLock());
            lockBtn.Enabled = (b) => b.IsWorking;
            lockBtn.EnableAction(MyTerminalActionIcons.TOGGLE, (MyStringId?)null, stateWriter);
            MyTerminalControlFactory.AddControl(lockBtn);

            var unlockBtn = new MyTerminalControlButton<MyLandingGear>("Unlock", MySpaceTexts.BlockActionTitle_Unlock, MySpaceTexts.Blank, (b) => b.RequestLandingGearUnlock());
            unlockBtn.Enabled = (b) => b.IsWorking;
            unlockBtn.EnableAction(MyTerminalActionIcons.TOGGLE, (MyStringId?)null, stateWriter);
            MyTerminalControlFactory.AddControl(unlockBtn);

            var title = MyTexts.Get(MySpaceTexts.BlockActionTitle_SwitchLock);
            MyTerminalAction<MyLandingGear> switchLockAction = new MyTerminalAction<MyLandingGear>("SwitchLock", title, MyTerminalActionIcons.TOGGLE);
            switchLockAction.Action = (b) => b.RequestLandingGearSwitch();
            switchLockAction.Writer = stateWriter;
            MyTerminalControlFactory.AddAction(switchLockAction);

            var autoLock = new MyTerminalControlCheckbox<MyLandingGear>("Autolock", MySpaceTexts.BlockPropertyTitle_LandGearAutoLock, MySpaceTexts.Blank);
            autoLock.Getter = (b) => b.m_autoLock;
            autoLock.Setter = (b, v) => b.m_autoLock.Value = v;
            autoLock.EnableAction();
            MyTerminalControlFactory.AddControl(autoLock);

            if (MyFakes.LANDING_GEAR_BREAKABLE)
            {
                var brakeForce = new MyTerminalControlSlider<MyLandingGear>("BreakForce", MySpaceTexts.BlockPropertyTitle_BreakForce, MySpaceTexts.BlockPropertyDescription_BreakForce);
                brakeForce.Getter = (x) => x.BreakForce;
                brakeForce.Setter = (x, v) => x.m_breakForceSync.Value = v;
                brakeForce.DefaultValue = 1;
                brakeForce.Writer = (x, result) =>
                {
                    if (x.BreakForce >= MyObjectBuilder_LandingGear.MaxSolverImpulse) result.AppendStringBuilder(MyTexts.Get(MySpaceTexts.BlockPropertyValue_MotorAngleUnlimited));
                    else MyValueFormatter.AppendForceInBestUnit(x.BreakForce, result);
                };
                brakeForce.Normalizer = (b, v) => ThresholdToRatio(v);
                brakeForce.Denormalizer = (b, v) => RatioToThreshold(v);
                brakeForce.EnableActions();
                MyTerminalControlFactory.AddControl(brakeForce);
            }
        }

        public MyLandingGear()
        {
            m_physicsChangedHandler = new Action<IMyEntity>(PhysicsChanged);
            m_attachedState.ValidateNever();
            m_attachedState.ValueChanged += x => AttachedValueChanged();
            m_autoLock.ValueChanged += x => AutolockChanged();

            m_breakForceSync.ValueChanged += x => BreakForceChanged();

            m_lockModeSync.ValidateNever();
            m_lockModeSync.ValueChanged += x => OnLockModeChanged();
        }

        void OnLockModeChanged()
        {
            LockMode = m_lockModeSync;
        }

        void BreakForceChanged()
        {
            BreakForce = m_breakForceSync;
        }

        void AutolockChanged()
        {
            m_autolockTimer = 0;
            UpdateEmissivity();
            RaisePropertiesChanged();
        }

        public bool IsBreakable
        {
            get { return BreakForce < MyObjectBuilder_LandingGear.MaxSolverImpulse; }
        }

        public void RequestLandingGearSwitch()
        {
            if (LockMode == LandingGearMode.Locked)
                RequestLandingGearUnlock();
            else
                RequestLandingGearLock();
        }

        public void RequestLandingGearLock()
        {
            if (LockMode == LandingGearMode.ReadyToLock)
                RequestLock(true);
        }

        public void RequestLandingGearUnlock()
        {
            if (LockMode == LandingGearMode.Locked)
                RequestLock(false);
        }

        public float BreakForce
        {
            get { return m_breakForce; }
            set
            {
                if (m_breakForce != value)
                {
                    bool wasBreakable = IsBreakable;
                    m_breakForce = value;

                    if (wasBreakable != IsBreakable)
                    {
                        m_breakForce = value;
                        if (Sync.IsServer)
                        {
                            ResetLockConstraint(LockMode == LandingGearMode.Locked);
                        }
                        RaisePropertiesChanged();
                    }
                    else if (IsBreakable)
                    {
                        UpdateBrakeThreshold();
                        RaisePropertiesChanged();
                    }
                }
            }
        }

        private static float RatioToThreshold(float ratio)
        {
            return ratio >= 1 ? MyObjectBuilder_LandingGear.MaxSolverImpulse : MathHelper.InterpLog(ratio, 500f, MyObjectBuilder_LandingGear.MaxSolverImpulse);
        }

        private static float ThresholdToRatio(float threshold)
        {
            return threshold >= MyObjectBuilder_LandingGear.MaxSolverImpulse ? 1 : MathHelper.InterpLogInv(threshold, 500f, MyObjectBuilder_LandingGear.MaxSolverImpulse);
        }

        private void UpdateBrakeThreshold()
        {
            if (SafeConstraint != null && m_constraint.ConstraintData is HkBreakableConstraintData)
            {
                ((HkBreakableConstraintData)m_constraint.ConstraintData).Threshold = BreakForce;
                if (this.m_attachedTo != null && this.m_attachedTo.Physics != null)
                    ((MyPhysicsBody)this.m_attachedTo.Physics).RigidBody.Activate();
            }
        }

        private bool CanAutoLock { get { return m_autoLock && m_autolockTimer == 0; } }

        public bool AutoLock
        {
            get { return m_autoLock; }
            set
            {
                m_autoLock.Value = value;
            }
        }

        public override void Init(MyObjectBuilder_CubeBlock objectBuilder, MyCubeGrid cubeGrid)
        {
            base.Init(objectBuilder, cubeGrid);
            NeedsUpdate &= ~MyEntityUpdateEnum.EACH_100TH_FRAME;
            if (BlockDefinition is MyLandingGearDefinition)
            {
                var landingGearDefinition = (MyLandingGearDefinition)BlockDefinition;
                m_lockSound = new MySoundPair(landingGearDefinition.LockSound);
                m_unlockSound = new MySoundPair(landingGearDefinition.UnlockSound);
                m_failedAttachSound = new MySoundPair(landingGearDefinition.FailedAttachSound);
            }
            else
            {
                m_lockSound = new MySoundPair("ShipLandGearOn");
                m_unlockSound = new MySoundPair("ShipLandGearOff");
                m_failedAttachSound = new MySoundPair("ShipLandGearNothing01");
            }

            Flags |= EntityFlags.NeedsUpdateBeforeNextFrame | EntityFlags.NeedsUpdate10 | EntityFlags.NeedsUpdate;
            LoadDummies();
            SlimBlock.ComponentStack.IsFunctionalChanged += ComponentStack_IsFunctionalChanged;
            var builder = objectBuilder as MyObjectBuilder_LandingGear;
            if (builder.IsLocked)
            {
                // This mode will be applied during one-time update, when we have scene prepared.
                LockMode = LandingGearMode.Locked;
                m_needsToRetryLock = true;
                if (Sync.IsServer)
                {
                    m_attachedEntityId = builder.AttachedEntityId;
                }

                m_attachedState.Value = new State() { OtherEntityId = builder.AttachedEntityId, GearPivotPosition = builder.GearPivotPosition, OtherPivot = builder.OtherPivot, MasterToSlave = builder.MasterToSlave };
            }

            if (MyFakes.LANDING_GEAR_BREAKABLE)
            {
                m_savedBreakForce = RatioToThreshold(builder.BrakeForce);
            }
            else
            {
                m_savedBreakForce = RatioToThreshold(MyObjectBuilder_LandingGear.MaxSolverImpulse);
            }
            AutoLock = builder.AutoLock;
            m_lockModeSync.Value = builder.LockMode;

            IsWorkingChanged += MyLandingGear_IsWorkingChanged;
            UpdateText();
            AddDebugRenderComponent(new Components.MyDebugRenderComponentLandingGear(this));
        }

        void MyLandingGear_IsWorkingChanged(MyCubeBlock obj)
        {
            RaisePropertiesChanged();
            UpdateEmissivity();
        }

        public override void ContactPointCallback(ref MyGridContactInfo info)
        {
            if (info.CollidingEntity != null && m_attachedTo == info.CollidingEntity)
            {
                info.EnableDeformation = false;
                info.EnableParticles = false;
            }
        }

        private void LoadDummies()
        {
            m_lockPositions = Model.Dummies.Where(s => s.Key.ToLower().Contains("gear_lock")).Select(s => s.Value.Matrix).ToArray();
        }

        public override void OnModelChange()
        {
            base.OnModelChange();
            UpdateEmissivity();
        }

        public void GetBoxFromMatrix(MatrixD m, out Vector3 halfExtents, out Vector3D position, out Quaternion orientation)
        {
            var world = MatrixD.Normalize(m) * this.WorldMatrix;
            orientation = Quaternion.CreateFromRotationMatrix(world);
            halfExtents = Vector3.Abs(m.Scale) / 2;
            position = world.Translation;
        }

        private MyEntity FindBody(out Vector3D pivot)
        {
            pivot = Vector3D.Zero;
            if (CubeGrid.Physics == null)
                return null;
            Quaternion orientation;
            Vector3 halfExtents;
            foreach (var m in m_lockPositions)
            {
                GetBoxFromMatrix(m, out halfExtents, out pivot, out orientation);
                HkBoxShape boxShape;
                try
                {
                    halfExtents *= new Vector3(2.0f, 1.0f, 2.0f);
                    orientation.Normalize();
                    MyPhysics.GetPenetrationsBox(ref halfExtents, ref pivot, ref orientation, m_penetrations, MyPhysics.CollisionLayers.DefaultCollisionLayer);
                    boxShape = new HkBoxShape(halfExtents);
                    Matrix tranform = Matrix.CreateFromQuaternion(orientation);
                    //tranform.Translation = pivot;
                    //MyOrientedBoundingBoxD obb = new MyOrientedBoundingBoxD(new BoundingBoxD(-halfExtents, halfExtents),tranform);
                    //tranform.Translation = Vector3D.Zero;
                    //MyRenderProxy.DebugDrawOBB(obb, Color.Red, 1, false, false);

                    foreach (var obj in m_penetrations)
                    {
                        var entity = obj.GetCollisionEntity() as MyEntity;

                        if (entity == null)// || entity.Parent != null)
                            continue;

                        if (entity.GetPhysicsBody().WeldInfo.Children.Count > 0)
                        {
                            Matrix t2;
                            foreach (var child in entity.GetPhysicsBody().WeldInfo.Children)
                            {
                                var childEnt = child.Entity as MyEntity;
                                t2 = childEnt.GetPhysicsBody().WeldInfo.Transform * entity.Physics.RigidBody.GetRigidBodyMatrix();
                                t2.Translation = entity.Physics.ClusterToWorld(t2.Translation);
                                //obb = new MyOrientedBoundingBoxD((BoundingBoxD)childEnt.PositionComp.LocalAABB, t2);
                                //MyRenderProxy.DebugDrawOBB(obb, Color.Green, 1, false, false);
                                t2.Translation = t2.Translation - pivot;
                                if (MyPhysics.IsPenetratingShapeShape(boxShape, ref tranform, child.WeldedRigidBody.GetShape(), ref t2))
                                    if (
                                    CanAttachTo(obj, child.Entity as MyEntity))
                                    {
                                        return child.Entity as MyEntity;
                                    }
                            }
                            t2 = entity.Physics.RigidBody.GetRigidBodyMatrix();
                            t2.Translation = entity.Physics.ClusterToWorld(t2.Translation) - pivot;

                            if (MyPhysics.IsPenetratingShapeShape(boxShape, ref tranform, entity.GetPhysicsBody().GetShape(), ref t2)
                                && CanAttachTo(obj, entity))
                            {
                                return entity;
                            }

                        }
                        else if (CanAttachTo(obj, entity))
                            return entity;
                    }
                }
                finally
                {
                    boxShape.Base.RemoveReference();
                    m_penetrations.Clear();
                }
            }
            return null;
        }

        private bool CanAttachTo(HkBodyCollision obj, MyEntity entity)
        {
            //Gregory: Check parent also! (Fix for Welder bug)
            if (entity == CubeGrid || entity.Parent == CubeGrid)
            {
                return false;
            }

            if (!obj.Body.IsFixed && obj.Body.IsFixedOrKeyframed)
                return false;

            // Dont want to lock to fixed/keyframed object
            if (entity is Sandbox.Game.Entities.Character.MyCharacter)
            {
                return false;
            }
            return true;
        }

        public override void UpdateVisual()
        {
            base.UpdateVisual();
            UpdateEmissivity();
        }

        private void UpdateEmissivity()
        {
            if (InScene)
            {
                switch (LockMode)
                {
                    case LandingGearMode.Locked:
                        MyCubeBlock.UpdateEmissiveParts(Render.RenderObjectIDs[0], 1.0f, Color.ForestGreen, Color.White);
                        break;

                    case LandingGearMode.ReadyToLock:
                        MyCubeBlock.UpdateEmissiveParts(Render.RenderObjectIDs[0], 1.0f, Color.Goldenrod, Color.White);
                        break;

                    case LandingGearMode.Unlocked:
                        if (CanAutoLock && Enabled)
                            MyCubeBlock.UpdateEmissiveParts(Render.RenderObjectIDs[0], 1.0f, Color.SteelBlue, Color.SteelBlue);
                        else
                            MyCubeBlock.UpdateEmissiveParts(Render.RenderObjectIDs[0], 1.0f, Color.Black, Color.Black);
                        break;
                }
            }
        }

        private void UpdateText()
        {
            DetailedInfo.Clear();
            DetailedInfo.AppendStringBuilder(MyTexts.Get(MyCommonTexts.BlockPropertiesText_Type));
            DetailedInfo.Append(BlockDefinition.DisplayNameText);
            DetailedInfo.Append("\n");
            DetailedInfo.AppendStringBuilder(MyTexts.Get(MySpaceTexts.BlockPropertiesText_LockState));
            WriteLockStateValue(DetailedInfo);
            RaisePropertiesChanged();
        }

        private void WriteLockStateValue(StringBuilder sb)
        {
            if (LockMode == LandingGearMode.Locked)
                sb.AppendStringBuilder(MyTexts.Get(MySpaceTexts.BlockPropertyValue_Locked));
            else if (LockMode == LandingGearMode.ReadyToLock)
                sb.AppendStringBuilder(MyTexts.Get(MySpaceTexts.BlockPropertyValue_ReadyToLock));
            else
                sb.AppendStringBuilder(MyTexts.Get(MySpaceTexts.BlockPropertyValue_Unlocked));
        }

        public override MyObjectBuilder_CubeBlock GetObjectBuilderCubeBlock(bool copy = false)
        {
            var gear = (MyObjectBuilder_LandingGear)base.GetObjectBuilderCubeBlock(copy);
            gear.IsLocked = LockMode == LandingGearMode.Locked;
            gear.BrakeForce = ThresholdToRatio(BreakForce);
            gear.AutoLock = AutoLock;
            gear.LockSound = m_lockSound.ToString();
            gear.UnlockSound = m_unlockSound.ToString();
            gear.FailedAttachSound = m_failedAttachSound.ToString();
            gear.AttachedEntityId = m_attachedEntityId;
            if (m_attachedEntityId.HasValue)
            {
                gear.MasterToSlave = m_attachedState.Value.MasterToSlave;
                gear.GearPivotPosition = m_attachedState.Value.GearPivotPosition;
                gear.OtherPivot = m_attachedState.Value.OtherPivot;
            }
            gear.LockMode = m_lockModeSync.Value;
            return gear;
        }

        public override void OnAddedToScene(object source)
        {
            base.OnAddedToScene(source);
            UpdateEmissivity();
        }

        public override void OnRemovedFromScene(object source)
        {
            base.OnRemovedFromScene(source);
            //for when new grid is created from splitting an existing grid
            EnqueueRetryLock();
            Detach();
        }

        public override void UpdateBeforeSimulation()
        {
            RetryLock();
            base.UpdateBeforeSimulation();
        }

        public override void UpdateOnceBeforeFrame()
        {
            base.UpdateOnceBeforeFrame();

            m_breakForceSync.Value = m_savedBreakForce;
        }

        public override void UpdateAfterSimulation10()
        {
            // TODO: change to phantom
            base.UpdateAfterSimulation10();

            if (Sync.IsServer == false)
            {
                return;
            }

            if (LockMode != LandingGearMode.Locked)
            {
                Vector3D pivot;
                var entity = FindBody(out pivot);
                if (entity != null)
                {
                    if (CanAutoLock && Sync.IsServer)
                    {
                        AttachRequest(true);
                    }
                    else
                    {
                        m_lockModeSync.Value = LandingGearMode.ReadyToLock;
                    }
                }
                else
                {
                    m_lockModeSync.Value = LandingGearMode.Unlocked;
                }
            }

            if (m_autolockTimer != 0 && MySandboxGame.TotalGamePlayTimeInMilliseconds - m_autolockTimer > 3 * 1000)
            {
                AutoLock = true;
            }
        }

        public override void UpdateAfterSimulation100()
        {
            base.UpdateAfterSimulation100();
            NeedsUpdate &= ~MyEntityUpdateEnum.EACH_100TH_FRAME;
            m_needsToRetryLock = true;
            RetryLock();
        }

        private void RetryLock()
        {
            if (m_needsToRetryLock)
            {
                if (Sync.IsServer)
                {
                    RetryLockServer();
                }
                else
                {
                    RetryLockClient();
                }
            }
        }

        private int m_retryCounter = 0;
        private void RetryLockServer()
        {
            Vector3D pivot;
            var body = FindBody(out pivot);
            if (body == null && m_retryCounter < 3)
            {
                m_retryCounter++;//when loading game, subpart which this gear is attached to may not exist yet (depends on UpdateBeforeSim block order)
                return;
            }
            m_retryCounter = 0;
            if (body != null && ((m_attachedEntityId.HasValue && body.EntityId == m_attachedEntityId.Value) || m_attachedEntityId.HasValue == false))
            {
                if (m_attachedTo == null || m_attachedTo.Physics == null || body.Physics.RigidBody != ((MyPhysicsBody)m_attachedTo.Physics).RigidBody)
                {
                    ResetLockConstraint(locked: true);
                }
            }
            else
            {
                long? old = m_attachedEntityId;
                ResetLockConstraint(locked: false);
                m_attachedEntityId = old;
                NeedsUpdate |= MyEntityUpdateEnum.EACH_100TH_FRAME;
            }
            m_needsToRetryLock = false;
        }

        private void RetryLockClient()
        {
            State state = m_attachedState.Value;
            if (state.OtherEntityId.HasValue == false)
            {
                m_needsToRetryLock = false;
                return;
            }

            long entityId = state.OtherEntityId.Value;
            MyEntity body;
            MyEntities.TryGetEntityById(entityId, out body);

            if (body == null)
            {
                NeedsUpdate |= MyEntityUpdateEnum.EACH_100TH_FRAME;
                return;
            }

            if (m_attachedEntityId.HasValue && m_attachedEntityId.Value != entityId)
            {
                Detach();
            }

            m_attachedEntityId = entityId;

            if (state.MasterToSlave.HasValue && state.GearPivotPosition.HasValue && state.OtherPivot.HasValue)
            {
                WorldMatrix = MatrixD.Multiply(state.MasterToSlave.Value, body.WorldMatrix);

                Attach(body, state.GearPivotPosition.Value, state.OtherPivot.Value.Matrix);
            }

            m_needsToRetryLock = false;
        }

        protected override void Closing()
        {
            Detach();
            base.Closing();
        }

        public void ResetAutolock()
        {
            //AutoLock = false;
            m_autolockTimer = MySandboxGame.TotalGamePlayTimeInMilliseconds;
            UpdateEmissivity();
        }

        [Event, Reliable, Server, Broadcast]
        private void AttachFailed()
        {
            StartSound(m_failedAttachSound);
        }

        private void Attach(long entityID, Vector3 gearSpacePivot, CompressedPositionOrientation otherBodySpacePivot)
        {
            m_attachedEntityId = entityID;
            MyEntity otherEntity;
            if (MyEntities.TryGetEntityById(entityID, out otherEntity))
            {
                Attach(otherEntity, gearSpacePivot, otherBodySpacePivot.Matrix);
            }
        }

        private void Attach(MyEntity entity, Vector3 gearSpacePivot, Matrix otherBodySpacePivot)
        {
            if (CubeGrid.Physics != null && CubeGrid.Physics.Enabled)
            {
                var body = entity.Physics.RigidBody;
                var handle = StateChanged;

                if (MyFakes.WELD_LANDING_GEARS && CanWeldTo(entity, ref otherBodySpacePivot))
                {
                    if (m_attachedTo != null || entity == null)
                        return;

                    if (entity is MyVoxelBase)
                    {
                        if (CubeGrid.Physics.RigidBody.IsFixed == false)
                        {
                            CubeGrid.Physics.ConvertToStatic();
                            m_converted = true;
                        }
                    }
                    else
                    {
                        MyWeldingGroups.Static.CreateLink(EntityId, CubeGrid, entity);
                    }
                    //OnConstraintAdded(GridLinkTypeEnum.LandingGear, entity);
                    m_lockModeSync.Value = LandingGearMode.Locked;
                    m_attachedTo = entity;
                    m_attachedTo.OnPhysicsChanged += m_physicsChangedHandler;
                    this.OnPhysicsChanged += m_physicsChangedHandler;
                    if (CanAutoLock)
                        ResetAutolock();

                    OnConstraintAdded(GridLinkTypeEnum.Physical, entity);
                    //OnConstraintAdded(GridLinkTypeEnum.NoContactDamage, entity);

                    if (!m_needsToRetryLock)
                        StartSound(m_lockSound);

                    if (handle != null) handle(true);

                    return;
                }

                //var entity = body.GetBody().Entity;
                Debug.Assert(m_attachedTo == null, "Already attached");
                Debug.Assert(entity != null, "Landing gear is attached to body which has no entity");
                Debug.Assert(m_constraint == null);

                if (m_attachedTo != null || entity == null || m_constraint != null)
                    return;

                body.Activate();
                CubeGrid.Physics.RigidBody.Activate();

                m_attachedTo = entity;

                m_attachedTo.OnPhysicsChanged += m_physicsChangedHandler;

                this.OnPhysicsChanged += m_physicsChangedHandler;

                Matrix gearLocalSpacePivot = Matrix.Identity;
                gearLocalSpacePivot.Translation = gearSpacePivot;

                var fixedData = new HkFixedConstraintData();
                if (MyFakes.OVERRIDE_LANDING_GEAR_INERTIA)
                {
                    fixedData.SetInertiaStabilizationFactor(MyFakes.LANDING_GEAR_INTERTIA);
                }
                else
                {
                    fixedData.SetInertiaStabilizationFactor(1);
                }

                fixedData.SetSolvingMethod(HkSolvingMethod.MethodStabilized);
                fixedData.SetInBodySpace(gearLocalSpacePivot, otherBodySpacePivot, CubeGrid.Physics, entity.Physics as MyPhysicsBody);

                HkConstraintData data = fixedData;

                if (MyFakes.LANDING_GEAR_BREAKABLE && BreakForce < MyObjectBuilder_LandingGear.MaxSolverImpulse)
                {
                    var breakData = new HkBreakableConstraintData(fixedData);
                    fixedData.Dispose();

                    breakData.Threshold = BreakForce;
                    breakData.ReapplyVelocityOnBreak = true;
                    breakData.RemoveFromWorldOnBrake = true;

                    data = breakData;
                }

                if (!m_needsToRetryLock)
                    StartSound(m_lockSound);

                m_constraint = new HkConstraint(CubeGrid.Physics.RigidBody, body, data);
                CubeGrid.Physics.AddConstraint(m_constraint);
                m_constraint.Enabled = true;

                m_lockModeSync.Value = LandingGearMode.Locked;
                if (CanAutoLock)
                    ResetAutolock();

                OnConstraintAdded(GridLinkTypeEnum.Physical, entity);
                OnConstraintAdded(GridLinkTypeEnum.NoContactDamage, entity);

                if (handle != null) handle(true);
            }
        }

        private bool CanWeldTo(MyEntity entity, ref Matrix otherBodySpacePivot)
        {
            if (BreakForce < MyObjectBuilder_LandingGear.MaxSolverImpulse)
                return false;
            var grid = entity as MyCubeGrid;
            if (grid != null)
            {
                Vector3I cube;
                grid.FixTargetCube(out cube, otherBodySpacePivot.Translation * grid.GridSizeR);
                var block = grid.GetCubeBlock(cube);
                if (block != null && block.FatBlock is Sandbox.Game.Entities.MyAirtightHangarDoor)
                    return false;
            }
            if (entity.Parent != null)
                return false;
            return true;
        }

        private void Detach()
        {
            if (CubeGrid.Physics == null || m_attachedTo == null)
                return;
            Debug.Assert(m_attachedTo != null, "Attached entity is null");
            m_lockModeSync.Value = LandingGearMode.Unlocked;
            var attachedTo = m_attachedTo;
            if (m_attachedTo != null)
            {
                m_attachedTo.OnPhysicsChanged -= m_physicsChangedHandler;
            }
            this.OnPhysicsChanged -= m_physicsChangedHandler;

            m_attachedTo = null;
            m_attachedEntityId = null;

            if (m_converted)
            {
                CubeGrid.Physics.ConvertToDynamic(CubeGrid.GridSizeEnum == MyCubeSize.Large);
                m_converted = false;
            }

            if (MyFakes.WELD_LANDING_GEARS && MyWeldingGroups.Static.LinkExists(EntityId, CubeGrid, (MyEntity)m_attachedTo))
            {
                MyWeldingGroups.Static.BreakLink(EntityId, CubeGrid, (MyEntity)attachedTo);
            }
            else
            {
                if (m_constraint != null)
                {
                    CubeGrid.Physics.RemoveConstraint(m_constraint);
                    m_constraint.Dispose();
                    m_constraint = null;
                }
                OnConstraintRemoved(GridLinkTypeEnum.NoContactDamage, attachedTo);
            }
            OnConstraintRemoved(GridLinkTypeEnum.Physical, attachedTo);
            if (!m_needsToRetryLock && !MarkedForClose)
                StartSound(m_unlockSound);
            var handle = StateChanged;
            if (handle != null) handle(false);
        }

        void PhysicsChanged(IMyEntity entity)
        {
            if (entity.Physics == null)
            {
                Detach();
            }
            else if (LockMode == LandingGearMode.Locked)
            {
                if (Sync.IsServer)
                {
                    m_needsToRetryLock = true;
                }
            }
        }

        public void EnqueueRetryLock()
        {
            if (m_needsToRetryLock)
                return;
            if (LockMode == LandingGearMode.Locked)
            {
                if (Sync.IsServer)
                {
                    m_needsToRetryLock = true;
                }
            }
        }

        void ComponentStack_IsFunctionalChanged()
        {
        }

        [Event, Reliable, Server]
        private void ResetLockConstraint(bool locked)
        {
            if (CubeGrid.Physics == null)
                return;

            Detach();

            if (locked)
            {
                Vector3D pivot;
                var entity = FindBody(out pivot);
                if (entity != null)
                {
                    AttachEntity(pivot, entity);
                }
            }
            else
            {
                m_lockModeSync.Value = LandingGearMode.Unlocked;
            }
            m_needsToRetryLock = false;
        }

        public void RequestLock(bool enable)
        {
            if (IsWorking)
            {
                MyMultiplayer.RaiseEvent(this, x => x.AttachRequest, enable);
            }

        }

        private void StartSound(MySoundPair cueEnum)
        {
            if (m_soundEmitter != null)
                m_soundEmitter.PlaySound(cueEnum, true);
        }

        event Action<bool> StateChanged;
        event Action<bool> Sandbox.ModAPI.IMyLandingGear.StateChanged
        {
            add { StateChanged += value; }
            remove { StateChanged -= value; }
        }

        IMyEntity Sandbox.ModAPI.Ingame.IMyLandingGear.GetAttachedEntity()
        {
            return m_attachedTo;
        }

        [Event, Reliable, Server]
        void AttachRequest(bool enable)
        {
            if (enable)
            {
                Vector3D pivot;
                var otherEntity = FindBody(out pivot);
                if (otherEntity != null)
                {
                    AttachEntity(pivot, otherEntity);
                }
                else
                {
                    MyMultiplayer.RaiseEvent(this, x => x.AttachFailed);
                }
            }
            else
            {
                m_attachedState.Value = new State() { OtherEntityId = null };
                ResetLockConstraint(false);
            }
        }

        private void AttachEntity(Vector3D pivot, MyEntity otherEntity)
        {
            var gearClusterMatrix = CubeGrid.Physics.RigidBody.GetRigidBodyMatrix();
            var otherClusterMatrix = otherEntity.Physics.RigidBody.GetRigidBodyMatrix();

            // Calculate world (cluster) matrix of pivot
            Matrix pivotCluster = gearClusterMatrix;
            pivotCluster.Translation = CubeGrid.Physics.WorldToCluster(pivot);

            // Convert cluser-space to local space
            Vector3 gearPivotPosition = (pivotCluster * Matrix.Invert(gearClusterMatrix)).Translation;
            Matrix other = pivotCluster * Matrix.Invert(otherClusterMatrix);
            CompressedPositionOrientation otherPivot = new CompressedPositionOrientation(ref other);
            long OtherEntity = otherEntity.EntityId;

            MatrixD masterToSlave = CubeGrid.WorldMatrix * MatrixD.Invert(otherEntity.WorldMatrix);
            m_attachedState.Value = new State() { OtherEntityId = OtherEntity, GearPivotPosition = gearPivotPosition, OtherPivot = otherPivot, MasterToSlave = masterToSlave };
            Attach(otherEntity, gearPivotPosition, other);
        }

        void AttachedValueChanged()
        {
            if (Sync.IsServer)
            {
                return;
            }

            State state = m_attachedState.Value;
            if (state.OtherEntityId.HasValue)
            {
                if (state.OtherEntityId.Value != m_attachedEntityId)
                {
                    m_attachedEntityId = state.OtherEntityId.Value;

                    MyEntity otherEntity;
                    if (MyEntities.TryGetEntityById(state.OtherEntityId.Value, out otherEntity))
                    {
                        if (Sync.IsServer == false)
                        {
                            this.CubeGrid.WorldMatrix = MatrixD.Multiply(state.MasterToSlave.Value, otherEntity.WorldMatrix);
                            Attach(otherEntity, state.GearPivotPosition.Value, state.OtherPivot.Value.Matrix);
                        }
                    }
                }
            }
            else
            {
                ResetLockConstraint(false);
            }
        }
    }
}
