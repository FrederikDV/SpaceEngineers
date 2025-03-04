﻿using System;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Common.ObjectBuilders.Definitions;
using Sandbox.Definitions;
using Sandbox.Engine.Networking;
using Sandbox.Engine.Physics;
using Sandbox.Engine.Utils;
using Sandbox.Game.EntityComponents;
using Sandbox.Game.GameSystems;
using Sandbox.Game.Gui;
using Sandbox.Game.Localization;
using Sandbox.Game.Multiplayer;
using Sandbox.Game.World;
using VRage;
using VRage.Game;
using VRage.Utils;
using VRageMath;
using Vector3 = VRageMath.Vector3;
using VRage.Game.Components;
using Sandbox.Game.GUI;

namespace Sandbox.Game.Entities.Character.Components
{
	public class MyCharacterJetpackComponent : MyCharacterComponent
	{
		private MyJetpackThrustComponent ThrustComp { get { return Character.Components.Get<MyEntityThrustComponent>() as MyJetpackThrustComponent; } }

		public float CurrentAutoEnableDelay { get; set; }
		public float ForceMagnitude { get; private set; }
		public float MinPowerConsumption { get; private set; }
		public float MaxPowerConsumption { get; private set; }
		public Vector3 FinalThrust { get { return ThrustComp.FinalThrust; } }

		public bool CanDrawThrusts { get { return Character.ActualUpdateFrame >= 2; } }
		public bool DampenersTurnedOn { get { return ThrustComp.DampenersEnabled; } }
        public MyGasProperties FuelDefinition { get; private set; }
        public MyFuelConverterInfo FuelConverterDefinition { get; private set; }
        public bool IsPowered { get { return (MySession.Static.LocalCharacter == Character &&MySession.Static.IsAdminModeEnabled) || (ThrustComp != null && ThrustComp.IsThrustPoweredByType(Character, ref FuelDefinition.Id)); } }
		public bool Running { get { return TurnedOn && IsPowered && !Character.IsDead; } }
		public bool TurnedOn { get; private set; }

        public float MinPlanetaryInfluence { get; private set; }
        public float MaxPlanetaryInfluence { get; private set; }
        public float EffectivenessAtMaxInfluence { get; private set; }
        public float EffectivenessAtMinInfluence { get; private set; }
        public bool NeedsAtmosphereForInfluence { get; private set; }
        public float ConsumptionFactorPerG { get; private set; }

		private bool IsFlying { get; set; }

		private MyHudNotification m_inertiaDampenersNotification;
		private MyHudNotification m_jetpackToggleNotification;

		private const float AUTO_ENABLE_JETPACK_INTERVAL = 1; //s

		private bool m_isOnPlanetSurface = false;
		private int m_planetSurfaceRaycastCounter = 0;

		public MyCharacterJetpackComponent()
		{
			CurrentAutoEnableDelay = 0;
			TurnedOn = false;
		}

		public virtual void Init(MyObjectBuilder_Character characterBuilder)
		{
			if (characterBuilder == null)
				return;

			m_inertiaDampenersNotification = new MyHudNotification();
			m_jetpackToggleNotification = new MyHudNotification();

			CurrentAutoEnableDelay = characterBuilder.AutoenableJetpackDelay;

			if (ThrustComp != null)
				Character.Components.Remove<MyJetpackThrustComponent>();

		    var thrustProperties = Character.Definition.Jetpack.ThrustProperties;

		    FuelConverterDefinition = null;;
		    FuelConverterDefinition = !MyFakes.ENABLE_HYDROGEN_FUEL ? new MyFuelConverterInfo { Efficiency = 1.0f } : Character.Definition.Jetpack.ThrustProperties.FuelConverter;

		    MyDefinitionId fuelId = new MyDefinitionId();
		    if (!FuelConverterDefinition.FuelId.IsNull())
		        fuelId = thrustProperties.FuelConverter.FuelId;

		    MyGasProperties fuelDef = null;
            if (MyFakes.ENABLE_HYDROGEN_FUEL)
                MyDefinitionManager.Static.TryGetDefinition(fuelId, out fuelDef);

            FuelDefinition = fuelDef ?? new MyGasProperties // Use electricity by default
            {
                Id = MyResourceDistributorComponent.ElectricityId,
                EnergyDensity = 1f,
            };

            ForceMagnitude = thrustProperties.ForceMagnitude;
			MinPowerConsumption = thrustProperties.MinPowerConsumption;
			MaxPowerConsumption = thrustProperties.MaxPowerConsumption;
		    MinPlanetaryInfluence = thrustProperties.MinPlanetaryInfluence;
		    MaxPlanetaryInfluence = thrustProperties.MaxPlanetaryInfluence;
		    EffectivenessAtMinInfluence = thrustProperties.EffectivenessAtMinInfluence;
		    EffectivenessAtMaxInfluence = thrustProperties.EffectivenessAtMaxInfluence;
		    NeedsAtmosphereForInfluence = thrustProperties.NeedsAtmosphereForInfluence;
		    ConsumptionFactorPerG = thrustProperties.ConsumptionFactorPerG;

			MyEntityThrustComponent thrustComp = new MyJetpackThrustComponent();
			thrustComp.Init();
			Character.Components.Add(thrustComp);


			ThrustComp.DampenersEnabled = characterBuilder.DampenersEnabled;

			foreach (Vector3I direction in Base6Directions.IntDirections)
			{
				ThrustComp.Register(Character, direction);	// Preferably there should be a jetpack entity (equipment) that could hold the thrusts instead of the character
			}
		    thrustComp.ResourceSink(Character).TemporaryConnectedEntity = Character;
            Character.SuitRechargeDistributor.AddSink(thrustComp.ResourceSink(Character));
			TurnOnJetpack(characterBuilder.JetpackEnabled, true, true, true);
		}

		public virtual void GetObjectBuilder(MyObjectBuilder_Character characterBuilder)
		{
			characterBuilder.DampenersEnabled = DampenersTurnedOn;
			characterBuilder.JetpackEnabled = TurnedOn;
			characterBuilder.AutoenableJetpackDelay = CurrentAutoEnableDelay;
		}

		public override void OnBeforeRemovedFromContainer()
		{
			Character.SuitRechargeDistributor.RemoveSink(ThrustComp.ResourceSink(Character), true, Entity.MarkedForClose);
			base.OnBeforeRemovedFromContainer();
		}

		public override void UpdateBeforeSimulation()
		{
			ThrustComp.UpdateBeforeSimulation();
		}
        public override void OnCharacterDead()
        {
            base.OnCharacterDead();
            TurnOnJetpack(false);
        }
		public void TurnOnJetpack(bool newState, bool fromLoad = false, bool updateSync = true, bool fromInit = false)
		{
			bool originalNewState = newState;

			newState = newState && MySession.Static.Settings.EnableJetpack;
			newState = newState && Character.Definition.Jetpack != null;
			newState = newState && (!MySession.Static.SurvivalMode || MyFakes.ENABLE_JETPACK_IN_SURVIVAL||MySession.Static.IsAdminModeEnabled);

			bool valueChanged = TurnedOn != newState;
			TurnedOn = newState;
            ThrustComp.Enabled = newState;
			ThrustComp.ControlThrust = Vector3.Zero;
            ThrustComp.MarkDirty();
            ThrustComp.UpdateBeforeSimulation();
		    if (!ThrustComp.Enabled)
		        ThrustComp.SetRequiredFuelInput(ref FuelDefinition.Id, 0f, null);

            ThrustComp.ResourceSink(Character).Update();

			if (!Character.ControllerInfo.IsLocallyControlled() && !fromInit && !Sync.IsServer && !MyFakes.CHARACTER_SERVER_SYNC)
				return;

            MyCharacterMovementEnum currentMovementState = Character.GetCurrentMovementState();
            if (currentMovementState == MyCharacterMovementEnum.Sitting)
                return;

			Character.StopFalling();

			bool noHydrogen = false;
			bool canUseJetpack = newState;

            if (!IsPowered && canUseJetpack &&(MySession.Static.IsAdminModeEnabled == false || MySession.Static.LocalCharacter != Character))
			{
				canUseJetpack = false;
				noHydrogen = true;
			}

			if (canUseJetpack)
				Character.IsUsing = null;

            if (MySession.Static.ControlledEntity == Character && valueChanged && !fromLoad)
			{
				m_jetpackToggleNotification.Text = (noHydrogen) ? MySpaceTexts.NotificationJetpackOffNoHydrogen
													 : (canUseJetpack || (originalNewState)) ? MySpaceTexts.NotificationJetpackOn
																		  : MySpaceTexts.NotificationJetpackOff;
				MyHud.Notifications.Add(m_jetpackToggleNotification);

                if (canUseJetpack)
                {
                    MyAnalyticsHelper.ReportActivityStart(Character, "jetpack", "character", string.Empty, string.Empty);
                }
                else
                {
                    MyAnalyticsHelper.ReportActivityEnd(Character, "jetpack");
                }

                //unable sound + turn off jetpack
                if (noHydrogen)
                {
                    MyGuiAudio.PlaySound(MyGuiSounds.HudUnable);
                    TurnedOn = false;
                    ThrustComp.Enabled = false;
                    ThrustComp.ControlThrust = Vector3.Zero;
                    ThrustComp.MarkDirty();
                    ThrustComp.UpdateBeforeSimulation();
                    ThrustComp.SetRequiredFuelInput(ref FuelDefinition.Id, 0f, null);
                    ThrustComp.ResourceSink(Character).Update();
                }
			}

			var characterProxy = Character.Physics.CharacterProxy;
			if (characterProxy != null)
			{
				characterProxy.Forward = Character.WorldMatrix.Forward;
				characterProxy.Up = Character.WorldMatrix.Up;
				characterProxy.EnableFlyingState(Running);

				if (currentMovementState != MyCharacterMovementEnum.Died)
				{
                    if (!Running && (characterProxy.GetState() == HkCharacterStateType.HK_CHARACTER_IN_AIR || (int)characterProxy.GetState() == MyCharacter.HK_CHARACTER_FLYING))
						Character.StartFalling();
                    //If we are in any state but not standing and new state is to be flying, dont change to standing. Else is probably ok?
                    else if (currentMovementState != MyCharacterMovementEnum.Standing && !newState) 
					{
						Character.PlayCharacterAnimation("Idle", MyBlendOption.Immediate, MyFrameOption.Loop, 0.2f);
                        Character.SetCurrentMovementState(MyCharacterMovementEnum.Standing);
						currentMovementState = Character.GetCurrentMovementState();
					}
				}

				if (Running && currentMovementState != MyCharacterMovementEnum.Died)
				{
					Character.PlayCharacterAnimation("Jetpack", MyBlendOption.Immediate, MyFrameOption.Loop, 0.0f);
                    Character.SetCurrentMovementState(MyCharacterMovementEnum.Flying);
					Character.SetLocalHeadAnimation(0, 0, 0.3f);

					// If the character is running when enabling the jetpack, these will keep making him fly in the same direction always if not zeroed
					characterProxy.PosX = 0;
					characterProxy.PosY = 0;
				}

				// When disabling the jetpack normally during the game in zero-G, disable jetpack autoenable
				if (!fromLoad && !newState && characterProxy.Gravity.LengthSquared() <= 0.1f)
				{
					CurrentAutoEnableDelay = -1;
				}
			}     
		}

		public void UpdateFall()
		{
			if (CurrentAutoEnableDelay < AUTO_ENABLE_JETPACK_INTERVAL)
				return;

			ThrustComp.DampenersEnabled = true;
			TurnOnJetpack(true);
			CurrentAutoEnableDelay = -1;
		}

		public void MoveAndRotate(ref Vector3 moveIndicator, ref Vector2 rotationIndicator, bool canRotate)
		{
			var characterPhysics = Character.Physics;
			var characterProxy = characterPhysics.CharacterProxy;

			ThrustComp.ControlThrust = Vector3.Zero;

			const MyCharacterMovementEnum newMovementState = MyCharacterMovementEnum.Flying;

			Character.SwitchAnimation(newMovementState);

            Character.SetCurrentMovementState(newMovementState);

			bool wantsFlyDown = (Character.MovementFlags & MyCharacterMovementFlags.FlyDown) == MyCharacterMovementFlags.FlyDown;
			bool wantsFlyUp = (Character.MovementFlags & MyCharacterMovementFlags.FlyUp) == MyCharacterMovementFlags.FlyUp;

			IsFlying = moveIndicator.LengthSquared() != 0;

			var proxyState = characterProxy != null ? characterProxy.GetState() : 0;
            if ((proxyState == HkCharacterStateType.HK_CHARACTER_IN_AIR || (int)proxyState == MyCharacter.HK_CHARACTER_FLYING))
			{
				Character.PlayCharacterAnimation("Jetpack", MyBlendOption.Immediate, MyFrameOption.Loop, 0.2f);

				Character.CanJump = true;
			}

			if (canRotate && rotationIndicator.X != 0)
			{
				MatrixD rotationMatrix = Character.WorldMatrix.GetOrientation();
				Vector3D translation = Character.WorldMatrix.Translation + Character.WorldMatrix.Up;

				if (Character.Definition.VerticalPositionFlyingOnly)
				{
                    Character.SetHeadLocalXAngle(MathHelper.Clamp(Character.HeadLocalXAngle - rotationIndicator.X * Character.RotationSpeed, MyCharacter.MIN_HEAD_LOCAL_X_ANGLE, MyCharacter.MAX_HEAD_LOCAL_X_ANGLE));
				}
				else
				{
                    rotationMatrix = rotationMatrix * MatrixD.CreateFromAxisAngle(Character.WorldMatrix.Right, rotationIndicator.X * -0.02 * Character.RotationSpeed);
				}

				rotationMatrix.Translation = translation - rotationMatrix.Up;

				//Enable if we want limit character rotation in collisions
				//if (m_shapeContactPoints.Count < 2)
				{
					Character.WorldMatrix = rotationMatrix;
					Character.ClearShapeContactPoints();
				}
			}

            Vector3 moveDirection = moveIndicator;
            if (Character.Definition.VerticalPositionFlyingOnly)
            {
                float angleSign = Math.Sign(Character.HeadLocalXAngle);
                double headAngle = Math.Abs(MathHelper.ToRadians(Character.HeadLocalXAngle));
                double exponent = 1.95;
                double smoothedAngle = Math.Pow(headAngle, exponent);
                smoothedAngle *= headAngle / Math.Pow(MathHelper.ToRadians(MyCharacter.MAX_HEAD_LOCAL_X_ANGLE), exponent);
                MatrixD rotationMatrix = MatrixD.CreateFromAxisAngle(Vector3D.Right, angleSign * smoothedAngle);
                moveDirection = Vector3D.Transform(moveDirection, rotationMatrix);
            }

			if (wantsFlyUp || wantsFlyDown)
				moveDirection += (wantsFlyUp ? 1 : -1) * Vector3.Up;

			if(!Vector3.IsZero(moveDirection))
				moveDirection.Normalize();

			ThrustComp.ControlThrust = moveDirection * ForceMagnitude;
		}

		public bool UpdatePhysicalMovement()
		{
			if (!Running)
				return false;

			var characterPhysics = Character.Physics;

			var characterProxy = characterPhysics.CharacterProxy;
            if (characterProxy != null)
            {
                if (characterProxy.LinearVelocity.Length() < MyCharacter.MINIMAL_SPEED)
                    characterProxy.LinearVelocity = Vector3.Zero;
            }

            var rigidBody = characterPhysics.RigidBody;
            if (rigidBody != null)
            {
                rigidBody.Gravity = Vector3.Zero;

                if (MySession.Static.SurvivalMode || MyFakes.ENABLE_PLANETS_JETPACK_LIMIT_IN_CREATIVE)
                {
                    Vector3 planetGravity = MyGravityProviderSystem.CalculateNaturalGravityInPoint(Character.PositionComp.WorldAABB.Center);

                    if (planetGravity != Vector3.Zero)
                        rigidBody.Gravity = planetGravity * MyPerGameSettings.CharacterGravityMultiplier;
                }

                return true;
            }
            else if(characterProxy != null)
            { 
			    characterProxy.Gravity = Vector3.Zero;

                if (MySession.Static.SurvivalMode || MyFakes.ENABLE_PLANETS_JETPACK_LIMIT_IN_CREATIVE)
                {
                    Vector3 planetGravity = MyGravityProviderSystem.CalculateNaturalGravityInPoint(Character.PositionComp.WorldAABB.Center);

                    if (planetGravity != Vector3.Zero)
                        characterProxy.Gravity = planetGravity * MyPerGameSettings.CharacterGravityMultiplier;
                }

                return true;
            }

            return false;
		}

		public void EnableDampeners(bool enable, bool updateSync = true)
		{
			if (DampenersTurnedOn == enable)
				return;

			ThrustComp.DampenersEnabled = enable;
		}

		public void SwitchDamping()
		{
			if (Character.GetCurrentMovementState() == MyCharacterMovementEnum.Died)
				return;

			EnableDampeners(!DampenersTurnedOn, true);

            m_inertiaDampenersNotification.Text = (DampenersTurnedOn ? MyCommonTexts.NotificationInertiaDampenersOn : MyCommonTexts.NotificationInertiaDampenersOff);
			MyHud.Notifications.Add(m_inertiaDampenersNotification);
		}

		public void SwitchThrusts()
		{
			if (Character.GetCurrentMovementState() != MyCharacterMovementEnum.Died
				&& ((MyPerGameSettings.Game != GameEnum.ME_GAME
				|| !MySession.Static.SurvivalMode)||MySession.Static.IsAdminModeEnabled))
			{
				TurnOnJetpack(!TurnedOn);
			}
		}

		public override string ComponentTypeDebugString { get { return "Jetpack Component"; } }
	}
}
