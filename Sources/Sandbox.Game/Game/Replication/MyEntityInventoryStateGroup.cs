﻿using Sandbox.Engine.Multiplayer;
using Sandbox.Game.Screens.Helpers;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using VRage.Library.Collections;
using VRage.Network;
using VRage.ObjectBuilders;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.World;
using Sandbox.Game.Weapons;
using Sandbox.Game.Entities;
using VRage.Game.Entity;
using System.Collections.Specialized;
using VRage;
using VRage.Collections;
using Sandbox.Common.ObjectBuilders.Definitions;

namespace Sandbox.Game.Replication
{
    class MyEntityInventoryStateGroup : IMyStateGroup
    {
        struct InventoryDeltaInformation
        {
            public bool HasChanges;

            public uint MessageId;
            public List<uint> RemovedItems;
            public Dictionary<uint, MyFixedPoint> ChangedItems;
            public SortedDictionary<int,MyPhysicalInventoryItem> NewItems;
        }

        struct ClientInvetoryData
        {
            public MyPhysicalInventoryItem Item;
            public MyFixedPoint Amount;

        }

        class InventoryClientData
        {
            public uint CurrentMessageId;
            public InventoryDeltaInformation MainSendingInfo;
            public bool Dirty;
            public Dictionary<byte, InventoryDeltaInformation> SendPackets = new Dictionary<byte, InventoryDeltaInformation>();
            public List<InventoryDeltaInformation> FailedIncompletePackets = new List<InventoryDeltaInformation>();
            public Dictionary<uint,ClientInvetoryData> ClientItems = new Dictionary<uint,ClientInvetoryData>();
        }

        MyInventory Inventory { get; set; }

        readonly int m_inventoryIndex;

        Dictionary<ulong, InventoryClientData> m_clientInventoryUpdate;

        List<MyPhysicalInventoryItem> m_itemsToSend;

        HashSet<uint> m_foundDeltaItems;

        Action<MyInventoryBase> m_inventoryChangedDelegate;

        MyQueue<uint> m_recievedPacketIds;

        const int RECIEVED_PACKET_HISTORY = 256;

        //inventory has bigger priority than physics
        const float INVENTORY_PRIORITY_RAMP = 1000.0f;

        public StateGroupEnum GroupType { get { return StateGroupEnum.Inventory; } }


        public MyEntityInventoryStateGroup(MyInventory entity, bool attach)
        {
            m_inventoryChangedDelegate = InventoryChanged;
            Inventory = entity;
            if (attach)
            {
                Inventory.ContentsChanged += m_inventoryChangedDelegate;
            }
        }

        void InventoryChanged(MyInventoryBase obj)
        {
            if (m_clientInventoryUpdate == null)
            {
                return;
            }
            foreach (var clientData in m_clientInventoryUpdate)
            {
                m_clientInventoryUpdate[clientData.Key].Dirty = true;
            }
        }

        public void CreateClientData(MyClientStateBase forClient)
        {
            if (m_clientInventoryUpdate == null)
            {
                m_clientInventoryUpdate = new Dictionary<ulong, InventoryClientData>();
            }

            InventoryClientData data;
            if (m_clientInventoryUpdate.TryGetValue(forClient.EndpointId.Value, out data) == false)
            {
                m_clientInventoryUpdate[forClient.EndpointId.Value] = new InventoryClientData();
                data = m_clientInventoryUpdate[forClient.EndpointId.Value];
            }
            data.Dirty = false;

            List<MyPhysicalInventoryItem> items = Inventory.GetItems();

            foreach (var serverItem in items)
            {
                MyFixedPoint amount = serverItem.Amount;

                var gasItem = serverItem.Content as MyObjectBuilder_GasContainerObject;
                if (gasItem != null)
                {
                    amount = (MyFixedPoint)gasItem.GasLevel;
                }

                data.ClientItems[serverItem.ItemId] = new ClientInvetoryData() { Item = serverItem, Amount = amount };
            }
            
        }

        public void DestroyClientData(MyClientStateBase forClient)
        {
            if (m_clientInventoryUpdate != null)
            {
                m_clientInventoryUpdate.Remove(forClient.EndpointId.Value);
            }
        }

        public void ClientUpdate()
        {
        }

        public void Destroy()
        {
        }

        public float GetGroupPriority(int frameCountWithoutSync, MyClientInfo client)
        {
            InventoryClientData clientData = m_clientInventoryUpdate[client.EndpointId.Value];
            List<MyPhysicalInventoryItem> items = Inventory.GetItems();
            if (clientData.Dirty == false && clientData.FailedIncompletePackets.Count == 0)
            {
                return 0.0f;
            }

            if(clientData.FailedIncompletePackets.Count  > 0)
            {
                return 1.0f * INVENTORY_PRIORITY_RAMP * frameCountWithoutSync;
            }

            MyClientState state = (MyClientState)client.State;
            if (Inventory.Owner is MyCharacter)
            {
                MyCharacter character = Inventory.Owner as MyCharacter;
                MyPlayer player = MyPlayer.GetPlayerFromCharacter(character);

                if (player == null && character.IsUsing != null)
                {
                    MyShipController cockpit = (character.IsUsing as MyShipController);
                    if (cockpit != null && cockpit.ControllerInfo.Controller != null)
                    {
                        player = cockpit.ControllerInfo.Controller.Player;
                    }
                }

                if (player != null && player.Id.SteamId == client.EndpointId.Value)
                {
                    return 1.0f * INVENTORY_PRIORITY_RAMP * frameCountWithoutSync;
                }
            }

            if (state.ContextEntity is MyCharacter && state.ContextEntity == Inventory.Owner)
            {
                return 1.0f * INVENTORY_PRIORITY_RAMP * frameCountWithoutSync;
            }
          
            if (state.Context == MyClientState.MyContextKind.Inventory ||
                (state.Context == MyClientState.MyContextKind.Production && Inventory.Owner is MyAssembler))
            {
                return GetPriorityStateGroup(client) * INVENTORY_PRIORITY_RAMP * frameCountWithoutSync;
            }
            return 0;
        }

        public float GetPriorityStateGroup(MyClientInfo client)
        {
            MyClientState state = (MyClientState)client.State;

            if (Inventory.ForcedPriority.HasValue)
            {
                return Inventory.ForcedPriority.Value;
            }

            if (state.ContextEntity != null)
            {
                MyCubeGrid parent = state.ContextEntity.GetTopMostParent() as MyCubeGrid;

                if (parent != null)
                {
                    foreach (var block in parent.GridSystems.TerminalSystem.Blocks)
                    {
                        if (block == Inventory.Container.Entity)
                        {
                            if (state.Context == Sandbox.Engine.Multiplayer.MyClientState.MyContextKind.Production && (block is MyAssembler) == false)
                            {
                                continue;
                            }
                            return 1.0f;
                        }
                    }
                }
                else if (state.ContextEntity == Inventory.Owner)
                {
                    return 1.0f;
                }
            }
            return 0;
        }

        public void Serialize(BitStream stream, MyClientStateBase forClient, byte packetId, int maxBitPosition)
        {
            if (stream.Writing)
            {
                InventoryClientData clientData = m_clientInventoryUpdate[forClient.EndpointId.Value];
                bool needsSplit = false;
                if (clientData.FailedIncompletePackets.Count > 0)
                {
                    InventoryDeltaInformation failedPacket = clientData.FailedIncompletePackets[0];
                    clientData.FailedIncompletePackets.RemoveAtFast(0);

                    InventoryDeltaInformation reSendPacket = WriteInventory(ref failedPacket, stream, packetId, maxBitPosition, out needsSplit);
                    
                    if (needsSplit)
                    {
                        //resend split doesnt generate new id becaose it was part of allreadt sent message
                        clientData.FailedIncompletePackets.Add(CreateSplit(ref failedPacket, ref reSendPacket));
                    }

                    if (reSendPacket.HasChanges)
                    {
                        clientData.SendPackets[packetId] = reSendPacket;
                    }
                }
                else
                {   
                    InventoryDeltaInformation difference  = CalculateInventoryDiff(ref clientData);
                    difference.MessageId = clientData.CurrentMessageId;

                    clientData.MainSendingInfo = WriteInventory(ref difference, stream, packetId, maxBitPosition,out needsSplit);
                    if (needsSplit)
                    {
                        //split generate new id becaose its different message 
                        clientData.CurrentMessageId++;
                        InventoryDeltaInformation split = CreateSplit(ref difference, ref clientData.MainSendingInfo);
                        split.MessageId = clientData.CurrentMessageId;
                        clientData.FailedIncompletePackets.Add(split);
                    }

                    if (clientData.MainSendingInfo.HasChanges)
                    {
                        clientData.SendPackets[packetId] = clientData.MainSendingInfo;
                        clientData.CurrentMessageId++;
                    }

                    clientData.Dirty = false;
                }
            }
            else
            {
                ReadInventory(stream);
            }
        }

        private void ReadInventory(BitStream stream)
        {
            if(stream.ReadBool() == false)       
            {
                return;
            }

            if(m_recievedPacketIds == null)
            {
                m_recievedPacketIds = new MyQueue<uint>(RECIEVED_PACKET_HISTORY);
            }

            uint packetId = stream.ReadUInt32();

            bool apply = true;
            if (m_recievedPacketIds.Count == RECIEVED_PACKET_HISTORY)
            {
                m_recievedPacketIds.Dequeue();
            }

            if (m_recievedPacketIds.InternalArray.Contains(packetId) == false)
            {
                m_recievedPacketIds.Enqueue(packetId);
            }
            else
            {
                apply = false;
            }

            bool hasItems = stream.ReadBool();
            if(hasItems)
            {
                int numItems = stream.ReadInt32();

                for (int i = 0; i < numItems; ++i)
                {
                    uint itemId = stream.ReadUInt32();
                    MyFixedPoint amout = new MyFixedPoint();
                    amout.RawValue = stream.ReadInt64();

                    if (apply)
                    {
                        Inventory.UpdateItemAmoutClient(itemId, amout);
                    }
                }
            }

            hasItems = stream.ReadBool();
            if (hasItems)
            {
                int numItems = stream.ReadInt32();
                for (int i = 0; i < numItems; ++i)
                {
                    uint itemId = stream.ReadUInt32();
                    if (apply)
                    {
                        Inventory.RemoveItemClient(itemId);
                    }
                }
            }

            hasItems = stream.ReadBool();
            if (hasItems)
            {
                int numItems = stream.ReadInt32();

                for (int i = 0; i < numItems; ++i)
                {
                    int position = stream.ReadInt32();
                    MyPhysicalInventoryItem item;
                    VRage.Serialization.MySerializer.CreateAndRead(stream, out item, MyObjectBuilderSerializer.Dynamic);
                    if (apply)
                    {
                        Inventory.AddItem(position, item);
                    }
                }
            }

            Inventory.Refresh();
        }

        InventoryDeltaInformation CalculateInventoryDiff(ref InventoryClientData clientData)
        {
            if (m_itemsToSend == null)
            {
                m_itemsToSend = new List<MyPhysicalInventoryItem>();
            }

            if(m_foundDeltaItems == null )
            {
                m_foundDeltaItems = new HashSet<uint>();
            }
            m_foundDeltaItems.Clear();

            InventoryDeltaInformation delta = new InventoryDeltaInformation();
            delta.HasChanges = false;

            List<MyPhysicalInventoryItem> items = Inventory.GetItems();

            int serverPos = 0;
            foreach (var serverItem in items)
            {

                ClientInvetoryData clientItem;

                if (clientData.ClientItems.TryGetValue(serverItem.ItemId,out clientItem))
                {
                    if (clientItem.Item.Content.TypeId == serverItem.Content.TypeId &&
                        clientItem.Item.Content.SubtypeId == serverItem.Content.SubtypeId)
                    {
                        m_foundDeltaItems.Add(serverItem.ItemId);
                        delta.HasChanges = true;

                        MyFixedPoint serverAmount = serverItem.Amount;

                        var gasItem = serverItem.Content as MyObjectBuilder_GasContainerObject;
                        if (gasItem != null)
                        {
                            serverAmount = (MyFixedPoint)gasItem.GasLevel;
                        }

                        if (clientItem.Amount != serverAmount)
                        {
                            MyFixedPoint contentDelta = serverAmount - clientItem.Amount;
                            if (delta.ChangedItems == null)
                            {
                                delta.ChangedItems = new Dictionary<uint, MyFixedPoint>();
                            }
                            delta.ChangedItems[serverItem.ItemId] = contentDelta;
                        }                      
                    }
                }
                else
                {
                    if (delta.NewItems == null)
                    {
                        delta.NewItems = new SortedDictionary<int, MyPhysicalInventoryItem>();
                    }
                    delta.NewItems[serverPos] = serverItem;
                    delta.HasChanges = true;
                }

                serverPos++;
            }

            foreach (var clientItem in clientData.ClientItems)
            {
                if (delta.RemovedItems == null)
                {
                    delta.RemovedItems = new List<uint>();
                }
                if (m_foundDeltaItems.Contains(clientItem.Key) == false)
                {
                    delta.RemovedItems.Add(clientItem.Key);
                    delta.HasChanges = true;
                }
            }

            clientData.ClientItems.Clear();
            foreach (var serverItem in items)
            {
                MyFixedPoint amount = serverItem.Amount;

                var gasItem = serverItem.Content as MyObjectBuilder_GasContainerObject;
                if (gasItem != null)
                {
                    amount = (MyFixedPoint)gasItem.GasLevel;
                }

                clientData.ClientItems[serverItem.ItemId] = new ClientInvetoryData() { Item = serverItem, Amount = amount };
            }
            return delta;
        }

        private InventoryDeltaInformation WriteInventory(ref InventoryDeltaInformation packetInfo, BitStream stream, byte packetId, int maxBitPosition, out bool needsSplit)
        {
            Console.WriteLine(String.Format("sending: {0}, {1}", packetId, Inventory.Owner.ToString()));

            InventoryDeltaInformation sendPacketInfo = PrepareSendData(ref packetInfo, stream, maxBitPosition, out needsSplit);
            if (sendPacketInfo.HasChanges == false)
            {
                stream.WriteBool(false);
                return sendPacketInfo;
            }

            sendPacketInfo.MessageId = packetInfo.MessageId;

            stream.WriteBool(true);
            stream.WriteUInt32(sendPacketInfo.MessageId);
            stream.WriteBool(sendPacketInfo.ChangedItems != null);
            if (sendPacketInfo.ChangedItems != null)
            {
                stream.WriteInt32(sendPacketInfo.ChangedItems.Count);
                foreach (var item in sendPacketInfo.ChangedItems)
                {
                    stream.WriteUInt32(item.Key);
                    stream.WriteInt64(item.Value.RawValue);
                }
            }

            stream.WriteBool(sendPacketInfo.RemovedItems != null);
            if (sendPacketInfo.RemovedItems != null)
            {
                stream.WriteInt32(sendPacketInfo.RemovedItems.Count);
                foreach (var item in sendPacketInfo.RemovedItems)
                {
                    stream.WriteUInt32(item);
                }
            }

            stream.WriteBool(sendPacketInfo.NewItems != null);
            if (packetInfo.NewItems != null)
            {
                stream.WriteInt32(sendPacketInfo.NewItems.Count);
                foreach (var item in sendPacketInfo.NewItems)
                {
                    stream.WriteInt32(item.Key);
                    MyPhysicalInventoryItem itemTosend = item.Value;
                    VRage.Serialization.MySerializer.Write(stream, ref itemTosend, MyObjectBuilderSerializer.Dynamic);
                }
            }

            return sendPacketInfo;
        }

        private InventoryDeltaInformation PrepareSendData(ref InventoryDeltaInformation packetInfo, BitStream stream, int maxBitPosition,out bool needsSplit)
        {
            needsSplit = false;
            int startStreamPosition = stream.BitPosition;

            InventoryDeltaInformation sentData = new InventoryDeltaInformation();

            sentData.HasChanges = false;
            stream.WriteBool(false);
            stream.WriteUInt32(packetInfo.MessageId);
            stream.WriteBool(packetInfo.ChangedItems != null);

            if (packetInfo.ChangedItems != null)
            {
                stream.WriteInt32(packetInfo.ChangedItems.Count);
                if (stream.BitPosition > maxBitPosition)
                {
                    needsSplit = true;
                }
                else
                {
                    sentData.ChangedItems = new Dictionary<uint, MyFixedPoint>();
                    foreach (var item in packetInfo.ChangedItems)
                    {
                        stream.WriteUInt32(item.Key);
                        stream.WriteInt64(item.Value.RawValue);

                        if (stream.BitPosition <= maxBitPosition)
                        {
                            sentData.ChangedItems[item.Key] = item.Value;
                            sentData.HasChanges = true;
                        }
                        else
                        {
                            needsSplit = true;
                        }
                    }
                }
            }

            stream.WriteBool(packetInfo.RemovedItems != null);
            if (packetInfo.RemovedItems != null)
            {
                stream.WriteInt32(packetInfo.RemovedItems.Count);
                if (stream.BitPosition > maxBitPosition)
                {
                    needsSplit = true;
                }
                else
                {
                    sentData.RemovedItems = new List<uint>();
                    foreach (var item in packetInfo.RemovedItems)
                    {
                        stream.WriteUInt32(item);

                        if (stream.BitPosition <= maxBitPosition)
                        {
                            sentData.RemovedItems.Add(item);
                            sentData.HasChanges = true;
                        }
                        else
                        {
                            needsSplit = true;
                        }
                    }
                }
            }

            stream.WriteBool(packetInfo.NewItems != null);
            if (packetInfo.NewItems != null)
            {
                stream.WriteInt32(packetInfo.NewItems.Count);
                if (stream.BitPosition > maxBitPosition)
                {
                    needsSplit = true;
                }
                else
                {
                    sentData.NewItems = new SortedDictionary<int, MyPhysicalInventoryItem>();

                    foreach (var item in  packetInfo.NewItems)
                    {
                        MyPhysicalInventoryItem inventoryItem = item.Value;
                        VRage.Serialization.MySerializer.Write(stream, ref inventoryItem, MyObjectBuilderSerializer.Dynamic);

                        if (stream.BitPosition <= maxBitPosition)
                        {
                            sentData.NewItems[item.Key] = inventoryItem;
                            sentData.HasChanges = true;
                        }
                        else
                        {
                            needsSplit = true;
                        }
                    }
                }
            }
            stream.SetBitPositionWrite(startStreamPosition);
            return sentData;
        }

        private InventoryDeltaInformation CreateSplit(ref InventoryDeltaInformation originalData, ref InventoryDeltaInformation sentData)
        {
            InventoryDeltaInformation split = new InventoryDeltaInformation();

            split.MessageId = sentData.MessageId;
            if(originalData.ChangedItems != null)
            {
                if (sentData.ChangedItems == null)
                {
                    split.ChangedItems = new Dictionary<uint, MyFixedPoint>();    
                    foreach (var item in originalData.ChangedItems)
                    {
                        split.ChangedItems[item.Key] = item.Value;
                    }
                }
                else if(originalData.ChangedItems.Count != sentData.ChangedItems.Count)
                {
                    split.ChangedItems = new Dictionary<uint, MyFixedPoint>();
                    foreach (var item in originalData.ChangedItems)
                    {
                        if (sentData.ChangedItems.ContainsKey(item.Key) == false)
                        {
                            split.ChangedItems[item.Key] = item.Value;
                        }
                    }
                }
            }

            if (originalData.RemovedItems != null)
            {        
                if (sentData.RemovedItems == null)
                {
                    split.RemovedItems = new List<uint>();
                    foreach (var item in originalData.RemovedItems)
                    {
                        split.RemovedItems.Add(item);
                    }
                }
                else if(originalData.RemovedItems.Count != sentData.RemovedItems.Count)
                {
                    split.RemovedItems = new List<uint>();
                    foreach (var item in originalData.RemovedItems)
                    {
                        if (sentData.RemovedItems.Contains(item) == false)
                        {
                            split.RemovedItems.Add(item);
                        }
                    }
                }
            }

            if (originalData.NewItems != null)
            {
                if (sentData.NewItems == null)
                {
                    split.NewItems = new SortedDictionary<int, MyPhysicalInventoryItem>();
                    foreach (var item in originalData.NewItems)
                    {
                        split.NewItems[item.Key] = item.Value;
                    }
                }
                else if (originalData.NewItems.Count != sentData.NewItems.Count)
                {
                    split.NewItems = new SortedDictionary<int, MyPhysicalInventoryItem>();
                    foreach (var item in originalData.NewItems)
                    {
                        if (sentData.NewItems.ContainsKey(item.Key) == false)
                        {
                            split.NewItems[item.Key] = item.Value;
                        }
                    }
                }
            }

            return split;
        }

        public void OnAck(MyClientStateBase forClient, byte packetId, bool delivered)
        {
            Console.WriteLine(String.Format("delivery: {0}, {1}", packetId, delivered));
            InventoryClientData clientData = m_clientInventoryUpdate[forClient.EndpointId.Value];
            InventoryDeltaInformation packetInfo;
            if (clientData.SendPackets.TryGetValue(packetId, out packetInfo))
            {
                if (delivered == false)
                {

                    clientData.FailedIncompletePackets.Add(packetInfo);
                }

                clientData.SendPackets.Remove(packetId);
            }
        }

        public void ForceSend(MyClientStateBase clientData)
        {

        }
    }
}
