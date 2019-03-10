using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;

namespace Digi.AdvancedWelding.MP
{
    public class Networking
    {
        public readonly ushort PacketId;

        /// <summary>
        /// If local machine has a render/player. Does not exclude being a server.
        /// </summary>
        public static bool IsPlayer => MyAPIGateway.Session.Player != null;

        /// <summary>
        /// This includes singleplayer, player-hosted game and DS.
        /// Opposite of this is a network connected player.
        /// </summary>
        public static bool IsServer => MyAPIGateway.Session.IsServer;

        /// <summary>
        /// This only returns true if local machine is a dedicated server (no render/player).
        /// </summary>
        public static bool IsDS => MyAPIGateway.Multiplayer.IsServer && MyAPIGateway.Utilities.IsDedicated;

        private bool registered = false;
        private readonly List<IMyPlayer> tempPlayers = new List<IMyPlayer>();

        public Networking(ushort packetId)
        {
            PacketId = packetId;
        }

        public void Register(bool registerListener = true)
        {
            registered = true;

            if(registerListener)
            {
                MyAPIGateway.Multiplayer.RegisterMessageHandler(PacketId, ReceivedPacket);
            }
        }

        public void Unregister()
        {
            registered = false;
            MyAPIGateway.Multiplayer.UnregisterMessageHandler(PacketId, ReceivedPacket);
        }

        private void ReceivedPacket(byte[] rawData) // executed when a packet is received on this machine
        {
            try
            {
                var packet = MyAPIGateway.Utilities.SerializeFromBinary<PacketBase>(rawData);

                bool relay = false;
                bool includeSender = false;

                packet.Received(ref relay, ref includeSender);

                if(relay)
                    RelayToClients(packet, includeSender, rawData);
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }

        /// <summary>
        /// Send a packet to the server.
        /// Works from clients and server.
        /// </summary>
        /// <param name="packet"></param>
        public void SendToServer(PacketBase packet)
        {
            if(!registered)
                throw new Exception("Networking not registered!");

            var bytes = MyAPIGateway.Utilities.SerializeToBinary(packet);

            MyAPIGateway.Multiplayer.SendMessageToServer(PacketId, bytes);
        }

        /// <summary>
        /// Send a packet to a specific player.
        /// Only works server side.
        /// </summary>
        /// <param name="packet"></param>
        /// <param name="steamId"></param>
        public void SendToPlayer(PacketBase packet, ulong steamId)
        {
            if(!IsServer)
                return;

            if(!registered)
                throw new Exception("Networking not registered!");

            var bytes = MyAPIGateway.Utilities.SerializeToBinary(packet);

            MyAPIGateway.Multiplayer.SendMessageTo(PacketId, bytes, steamId);
        }

        /// <summary>
        /// Sends packet (or supplied bytes) to all players except server player and supplied packet's sender.
        /// Only works server side.
        /// </summary>
        public void RelayToClients(PacketBase packet, bool includeSender, byte[] rawData = null)
        {
            if(!IsServer)
                return;

            if(!registered)
                throw new Exception("Networking not registered!");

            tempPlayers.Clear();
            MyAPIGateway.Players.GetPlayers(tempPlayers);

            if(tempPlayers.Count == 0)
                return;

            // trigger for server side if it's the sender without the ability to re-relay which will cause an infinite loop
            if(includeSender && packet.SenderId == MyAPIGateway.Multiplayer.ServerId)
            {
                bool ignore1 = false;
                bool ignore2 = false;
                packet.Received(ref ignore1, ref ignore2);
            }

            foreach(var p in tempPlayers)
            {
                if(p.SteamUserId == MyAPIGateway.Multiplayer.ServerId)
                    continue; // always ignore server to avoid loopback

                if(!includeSender && p.SteamUserId == packet.SenderId)
                    continue;

                if(rawData == null)
                    rawData = MyAPIGateway.Utilities.SerializeToBinary(packet);

                MyAPIGateway.Multiplayer.SendMessageTo(PacketId, rawData, p.SteamUserId);
            }

            tempPlayers.Clear();
        }
    }
}
