﻿using Sandbox.Common.AI;
using Sandbox.Common.ObjectBuilders.Definitions;
using Sandbox.Definitions;
using Sandbox.Game.AI;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.World;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using VRage.Game.ObjectBuilders.AI.Bot;

namespace Sandbox.Game.AI
{
    [MyBotType(typeof(MyObjectBuilder_AnimalBot))]
    public class MyAnimalBot : MyAgentBot
    {
        public MyCharacter AnimalEntity { get { return AgentEntity; } }
       // public MyAnimalBotActionProxy AnimalActions { get { return m_actions as MyAnimalBotActionProxy; } }
        public MyAnimalBotDefinition AnimalDefinition { get { return m_botDefinition as MyAnimalBotDefinition; } }

        public MyAnimalBot(MyPlayer player, MyBotDefinition botDefinition)
            : base(player, botDefinition)
        {
        }
    }
}
