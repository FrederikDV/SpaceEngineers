﻿
using Sandbox.Common.ObjectBuilders;
using Sandbox.Common.ObjectBuilders.Definitions;
using System;
using System.Text;
using VRage.Game;
using VRage.Game.Definitions;
using VRage.Utils;
using VRageMath;

namespace Sandbox.Definitions
{
    [MyDefinitionType(typeof(MyObjectBuilder_PhysicalItemDefinition))]
    public class MyPhysicalItemDefinition : MyDefinitionBase
    {
        public Vector3 Size; // in meters
        public float Mass; // in Kg
        public string Model;
        public MyStringId? IconSymbol;
        public float Volume; // in m3
        public MyStringHash PhysicalMaterial;
        public bool CanSpawnFromScreen;
        public bool RotateOnSpawnX = false;
        public bool RotateOnSpawnY = false;
        public bool RotateOnSpawnZ = false;
        public int Health;
        public MyDefinitionId? DestroyedPieceId = null;
        public int DestroyedPieces = 0;
        public StringBuilder ExtraInventoryTooltipLine;

        public bool HasIntegralAmounts
        {
            get
            {
                return Id.TypeId != typeof(MyObjectBuilder_Ingot) &&
                       Id.TypeId != typeof(MyObjectBuilder_Ore);
            }
        }

        protected override void Init(MyObjectBuilder_DefinitionBase builder)
        {
            base.Init(builder);

            var ob = builder as MyObjectBuilder_PhysicalItemDefinition;
            MyDebug.AssertDebug(ob != null);
            this.Size = ob.Size;
            this.Mass = ob.Mass;
            this.Model = ob.Model;
            this.Volume = ob.Volume.HasValue? ob.Volume.Value / 1000f : ob.Size.Volume;
            if (string.IsNullOrEmpty(ob.IconSymbol))
                this.IconSymbol = null;
            else
                this.IconSymbol = MyStringId.GetOrCompute(ob.IconSymbol);
            PhysicalMaterial = MyStringHash.GetOrCompute(ob.PhysicalMaterial);
            CanSpawnFromScreen = ob.CanSpawnFromScreen;
            RotateOnSpawnX = ob.RotateOnSpawnX;
            RotateOnSpawnY = ob.RotateOnSpawnY;
            RotateOnSpawnZ = ob.RotateOnSpawnZ;
            Health = ob.Health;
            if (ob.DestroyedPieceId.HasValue)
            {
                DestroyedPieceId = ob.DestroyedPieceId.Value;
            }
            DestroyedPieces = ob.DestroyedPieces;
            if (ob.ExtraInventoryTooltipLine != null)
                ExtraInventoryTooltipLine = new StringBuilder().Append(Environment.NewLine).Append(ob.ExtraInventoryTooltipLine);
            else
                ExtraInventoryTooltipLine = new StringBuilder();
        }
    }
}
