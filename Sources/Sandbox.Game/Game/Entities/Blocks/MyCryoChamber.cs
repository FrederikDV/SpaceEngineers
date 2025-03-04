﻿using Sandbox.Common.ObjectBuilders;
using Sandbox.Common.ObjectBuilders.Definitions;
using Sandbox.Definitions;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.GameSystems.Electricity;
using Sandbox.Game.GUI;
using Sandbox.Game.Localization;
using Sandbox.Game.Multiplayer;
using Sandbox.Game.World;
using Sandbox.ModAPI.Interfaces;
using VRage.Game.Entity.UseObject;
using VRage.Import;
using VRage.Utils;
using VRageMath;
using VRageRender;
using VRage.ModAPI;
using Sandbox.Engine.Utils;
using Sandbox.Game.EntityComponents;
using VRage.Network;
using Sandbox.Engine.Multiplayer;
using System.Diagnostics;
using VRage;
using Sandbox.Game.Replication;
using VRage.Game;

namespace Sandbox.Game.Entities.Blocks
{
    [MyCubeBlockType(typeof(MyObjectBuilder_CryoChamber))]
    public class MyCryoChamber : MyCockpit
    {
        private MatrixD m_characterDummy;
        private MatrixD m_cameraDummy;

        //Use this default if initialization fails
        private string m_overlayTextureName = "Textures\\GUI\\Screens\\cryopod_interior.dds";

        MyPlayer.PlayerId? m_currentPlayerId;

        readonly Sync<MyPlayer.PlayerId?> m_attachedPlayerId;

        bool m_retryAttachPilot = false;

        public override bool IsInFirstPersonView
        {
            get { return true; }
            set { }
        }

        private new MyCryoChamberDefinition BlockDefinition
        {
            get { return (MyCryoChamberDefinition)base.BlockDefinition; }
        }

        protected override MyStringId LeaveNotificationHintText { get { return MySpaceTexts.NotificationHintLeaveCryoChamber; } }
        static MyCryoChamber()
        {
            m_horizonIndicator.Enabled = (x) => false;
            m_horizonIndicator.Visible = (x) => false;
        }
        public MyCryoChamber()
        {
            ControllerInfo.ControlAcquired += OnCryoChamberControlAcquired;
            m_attachedPlayerId.ValueChanged += (x) => AttachedPlayerChanged();
        }

        public override void Init(MyObjectBuilder_CubeBlock objectBuilder, MyCubeGrid cubeGrid)
        {
            m_cameraDummy = Matrix.Identity;
            m_characterDummy = Matrix.Identity;

            base.Init(objectBuilder, cubeGrid);

            if (ResourceSink == null)
            {
                // we've already created ResourceSink in ancestor!
                var sinkComp = new MyResourceSinkComponent();
                sinkComp.Init(
                    MyStringHash.GetOrCompute(BlockDefinition.ResourceSinkGroup),
                    BlockDefinition.IdlePowerConsumption,
                    this.CalculateRequiredPowerInput);
                sinkComp.IsPoweredChanged += Receiver_IsPoweredChanged;
                ResourceSink = sinkComp;
            }
            else
            {
                // override electricity settings
                ResourceSink.SetMaxRequiredInputByType(MyResourceDistributorComponent.ElectricityId, BlockDefinition.IdlePowerConsumption);
                ResourceSink.SetRequiredInputFuncByType(MyResourceDistributorComponent.ElectricityId, this.CalculateRequiredPowerInput);
                ResourceSink.IsPoweredChanged += Receiver_IsPoweredChanged;
            }

            var chamberOb = objectBuilder as MyObjectBuilder_CryoChamber;

            if (chamberOb.SteamId != null && chamberOb.SerialId != null)
            {
                m_currentPlayerId = new MyPlayer.PlayerId(chamberOb.SteamId.Value, chamberOb.SerialId.Value);
            }
            else
            {
                m_currentPlayerId = null;
            }


            var overlayTexture = BlockDefinition.OverlayTexture;
            if (!string.IsNullOrEmpty(overlayTexture))
            {
                m_overlayTextureName = overlayTexture;
            }

          
            HorizonIndicatorEnabled = false;

            NeedsUpdate |= MyEntityUpdateEnum.BEFORE_NEXT_FRAME | MyEntityUpdateEnum.EACH_10TH_FRAME;
        }

        private float CalculateRequiredPowerInput()
        {
            return BlockDefinition.IdlePowerConsumption;
        }

        void PowerDistributor_PowerStateChaged(MyResourceStateEnum newState)
        {
            UpdateIsWorking();
            UpdateEmissivity();
        }

        private void Receiver_IsPoweredChanged()
        {
            UpdateIsWorking();
            UpdateEmissivity();
        }

        protected override void PostBaseInit()
        {
            base.PostBaseInit();

            TryGetDummies();
        }

        private void TryGetDummies()
        {
            if (Model != null)
            {
                MyModelDummy cameraDummy;
                Model.Dummies.TryGetValue("camera", out cameraDummy);
                if (cameraDummy != null)
                {
                    m_cameraDummy = MatrixD.Normalize(cameraDummy.Matrix);
                }

                MyModelDummy characterDummy;
                Model.Dummies.TryGetValue("character", out characterDummy);
                if (characterDummy != null)
                {
                    m_characterDummy = MatrixD.Normalize(characterDummy.Matrix);
                }
            }
        }

        public override void UpdateVisual()
        {
            base.UpdateVisual();

            TryGetDummies();
        }

        public override void UpdateOnceBeforeFrame()
        {
            base.UpdateOnceBeforeFrame();

            UpdateEmissivity();

            if (m_attachedPlayerId.Value != m_currentPlayerId)
            {
                m_attachedPlayerId.Value = m_currentPlayerId;
            }
        }

        public override MyObjectBuilder_CubeBlock GetObjectBuilderCubeBlock(bool copy = false)
        {
            var chamberOb = (MyObjectBuilder_CryoChamber)base.GetObjectBuilderCubeBlock(copy);

            if (m_currentPlayerId != null)
            {
                chamberOb.SteamId = m_currentPlayerId.Value.SteamId;
                chamberOb.SerialId = m_currentPlayerId.Value.SerialId;
            }

            return chamberOb;
        }

        protected override void PlacePilotInSeat(MyCharacter pilot)
        {
            pilot.EnableLights(false);

            var jetpack = pilot.JetpackComp;
            if (jetpack != null)
                jetpack.TurnOnJetpack(false, false, false, false);

            pilot.Sit(true, MySession.Static.LocalCharacter == pilot, false, BlockDefinition.CharacterAnimation);


            pilot.SuitBattery.ResourceSource.Enabled = true;

            pilot.PositionComp.SetWorldMatrix(m_characterDummy * WorldMatrix, this);
            UpdateEmissivity(true);
        }

        protected void OnCryoChamberControlAcquired(MyEntityController controller)
        {
            m_currentPlayerId = controller.Player.Id;
        }

        protected override void RemovePilotFromSeat(MyCharacter pilot)
        {
            if (pilot == MySession.Static.LocalCharacter)
            {
                MyHudCameraOverlay.Enabled = false;
                this.Render.Visible = true;
            }

            m_currentPlayerId = null;
            m_attachedPlayerId.Value = null;
            
            UpdateEmissivity(false);
        }

        public override void OnModelChange()
        {
            base.OnModelChange();

            UpdateEmissivity();
        }

        public override UseActionResult CanUse(UseActionEnum actionEnum, IMyControllableEntity user)
        {
            if (IsWorking)
            {
                return base.CanUse(actionEnum, user);
            }
            else
            {
                return UseActionResult.Unpowered;
            }
        }

        public override void UpdateBeforeSimulation10()
        {
            base.UpdateBeforeSimulation10();

            if (MyFakes.ENABLE_OXYGEN_SOUNDS)
            {
                UpdateSound(Pilot != null && Pilot == MySession.Static.LocalCharacter);
            }
        }

        public override void UpdateAfterSimulation100()
        {
            base.UpdateAfterSimulation100();

            UpdateEmissivity();
            ResourceSink.Update();

            if (m_retryAttachPilot)
            {
                m_retryAttachPilot = false;
                AttachedPlayerChanged();
            }
        }

        private void SetOverlay()
        {
            MyHudCameraOverlay.TextureName = m_overlayTextureName;
            MyHudCameraOverlay.Enabled = true;
            this.Render.Visible = false;
        }

        private void UpdateEmissivity(bool isUsed)
        {
            UpdateIsWorking();

            if (IsWorking)
            {
                if (isUsed)
                {
                    MyCubeBlock.UpdateEmissiveParts(Render.RenderObjectIDs[0], 1.0f, Color.Cyan, Color.White);
                }
                else
                {
                    MyCubeBlock.UpdateEmissiveParts(Render.RenderObjectIDs[0], 1.0f, Color.Green, Color.White);
                }
            }
            else
            {
                MyCubeBlock.UpdateEmissiveParts(Render.RenderObjectIDs[0], 0.0f, Color.Red, Color.White);
            }
        }

        private bool IsPowered()
        {
            if (ResourceSink == null || !ResourceSink.IsPoweredByType(MyResourceDistributorComponent.ElectricityId)) return false;
            return m_rechargeSocket != null && m_rechargeSocket.ResourceDistributor != null && m_rechargeSocket.ResourceDistributor.ResourceState != MyResourceStateEnum.NoPower;
        }

        protected override bool CheckIsWorking()
        {
            return base.CheckIsWorking() && IsPowered();
        }

        private void UpdateEmissivity()
        {
            UpdateEmissivity(Pilot != null);
        }

        public override MatrixD GetHeadMatrix(bool includeY, bool includeX = true, bool forceHeadAnim = false, bool forceHeadBone = false)
        {
            return m_cameraDummy * PositionComp.WorldMatrix;
        }

        public override MatrixD GetViewMatrix()
        {
            MatrixD headMatrix = GetHeadMatrix(false);
            MatrixD viewMatrix;

            MatrixD.Invert(ref headMatrix, out viewMatrix);

            return viewMatrix;
        }

        public override MyToolbarType ToolbarType
        {
            get
            {
                return MyToolbarType.None;
            }
        }

        protected override void ComponentStack_IsFunctionalChanged()
        {
            //Cache the pilot reference
            var pilot = m_pilot;
            var controller = ControllerInfo.Controller;

            base.ComponentStack_IsFunctionalChanged();

            //Only kill him if his player is no longer online
            if (!IsFunctional && pilot != null && controller == null)
            {
                if (MySession.Static.CreativeMode)
                {
                    pilot.Close();
                }
                else
                {
                    pilot.DoDamage(1000f, MyDamageType.Unknown, false);
                }
            }

            UpdateEmissivity();
        }

        public override void OnUnregisteredFromGridSystems()
        {
            var pilot = m_pilot;
            var controller = ControllerInfo.Controller;

            base.OnUnregisteredFromGridSystems();

            //Only kill him if his player is no longer online
            if (pilot != null && controller == null)
            {
                if (MySession.Static.CreativeMode)
                {
                    pilot.Close();
                }
                //Pilot is killed by base in survival
            }

            m_soundEmitter.StopSound(true);
        }

        private bool IsLocalCharacterInside()
        {
            return MySession.Static.LocalCharacter != null && MySession.Static.LocalCharacter == Pilot;
        }

        private void UpdateSound(bool isUsed)
        {
            if (IsWorking)
            {
                if (isUsed)
                {
                    if (m_soundEmitter.SoundId != BlockDefinition.InsideSound.Arcade && m_soundEmitter.SoundId != BlockDefinition.InsideSound.Realistic)
                    {
                        m_soundEmitter.PlaySound(BlockDefinition.InsideSound, true);
                    }
                }
                else
                {
                    if (m_soundEmitter.SoundId != BlockDefinition.OutsideSound.Arcade && m_soundEmitter.SoundId != BlockDefinition.InsideSound.Realistic)
                    {
                        m_soundEmitter.PlaySound(BlockDefinition.OutsideSound, true);
                    }
                }
            }
            else
            {
                m_soundEmitter.StopSound(true);
            }

            m_soundEmitter.Update();
        }

        public void CameraAttachedToChanged(IMyCameraController oldController, IMyCameraController newController)
        {
            if (oldController == this)
            {
                MyRenderProxy.UpdateRenderObjectVisibility(Render.RenderObjectIDs[0], true, false);
            }
        }

        protected override void sync_UseSuccess(UseActionEnum actionEnum, IMyControllableEntity user)
        {
            /*if (user.Entity == MySession.Static.LocalCharacter)
            {
                MySession.Static.CameraAttachedToChanged += CameraAttachedToChanged;
            }*/

            base.sync_UseSuccess(actionEnum, user);
        }

        protected override void OnControlledEntity_Used()
        {
            var pilot = m_pilot;
            base.OnControlledEntity_Used();

            if (pilot != null && pilot == MySession.Static.LocalCharacter)
            {
                MySession.Static.SetCameraController(MyCameraControllerEnum.Entity, pilot);
                //MySession.Static.CameraAttachedToChanged -= CameraAttachedToChanged;
            }
        }

        protected override void OnControlAcquired_UpdateCamera()
        {
            //MySession.Static.CameraAttachedToChanged += CameraAttachedToChanged;
            SetOverlay();
            //MyRenderProxy.UpdateRenderObjectVisibility(Render.RenderObjectIDs[0], false, false);
            base.OnControlAcquired_UpdateCamera();
        }

        protected override void UpdateCockpitModel()
        {
            //Do nothing
        }

        public bool TryToControlPilot(MyPlayer player)
        {
            if (Pilot == null)
            {
                return false;
            }

            if (player.Id != m_currentPlayerId)
            {
                return false;
            }

            if (m_attachedPlayerId.Value == m_currentPlayerId)
            {
                AttachedPlayerChanged();
            }
            else
            {
                m_attachedPlayerId.Value = m_currentPlayerId;
            }

            return true;
        }

        internal void OnPlayerLoaded()
        {
            //MySession.Static.CameraAttachedToChanged += CameraAttachedToChanged;
        }

        void AttachedPlayerChanged()
        {
            if (m_attachedPlayerId.Value.HasValue == false)
            {
                return;
            }

            var playerId = new MyPlayer.PlayerId(m_attachedPlayerId.Value.Value.SteamId, m_attachedPlayerId.Value.Value.SerialId);
            var player = Sync.Players.GetPlayerById(playerId);
            if (player != null)
            {
                if (Pilot != null)
                {
                    if (player == MySession.Static.LocalHumanPlayer)
                    {
                        OnPlayerLoaded();

                        if (MySession.Static.CameraController != this)
                        {
                            MySession.Static.SetCameraController(MyCameraControllerEnum.Entity, this);
                        }
                    }

                    player.Controller.TakeControl(this);
                    player.Identity.ChangeCharacter(Pilot);
                }
                else
                {
                    if (player == MySession.Static.LocalHumanPlayer)
                    {
                        Debug.Fail("Selected cryo chamber doesn't have a pilot!");
                    }
                }
            }
            else
            {
                m_retryAttachPilot = true;
            }
        }
    }
}
