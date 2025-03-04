﻿using System.Collections.Generic;
using VRage;

namespace VRageRender
{    
    public class MyRenderMessageSetRenderEntityData : MyRenderMessageBase
    {
        public uint ID;
        public MyModelData ModelData = new MyModelData();

        public override MyRenderMessageType MessageClass { get { return MyRenderMessageType.StateChangeOnce; } }
        public override MyRenderMessageEnum MessageType { get { return MyRenderMessageEnum.SetRenderEntityData; } }
    }
}
