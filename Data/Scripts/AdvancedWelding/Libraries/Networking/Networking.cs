using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Utils;

namespace Digi.Sync
{
    public class Networking
    {
        public static bool IsPlayer => !MyAPIGateway.Utilities.IsDedicated;

        public readonly ushort ChannelId;

        public Action<string> LogInfo;
        public Action<Exception> LogException;
        public Action<string> LogError;

        private IMyModContext Mod;
        private List<IMyPlayer> TempPlayers = null;

        /// <summary>
        /// <paramref name="channelId"/> must be unique from all other mods that also use network packets.
        /// </summary>
        public Networking(ushort channelId)
        {
            ChannelId = channelId;
        }

        /// <summary>
        /// Register packet monitoring, not necessary if you don't want the local machine to handle incomming packets.
        /// </summary>
        public void Register(IMyModContext mod)
        {
            Mod = mod;
            MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(ChannelId, ReceivedPacket);

            LogInfo = LogInfo ?? MyLog.Default.WriteLine;
            LogError = LogError ?? DefaultLogError;
            LogException = LogException ?? DefaultLogException;
        }

        /// <summary>
        /// This must be called on world unload if you called <see cref="Register"/>.
        /// </summary>
        public void Unregister()
        {
            MyAPIGateway.Multiplayer.UnregisterSecureMessageHandler(ChannelId, ReceivedPacket);
        }

        /// <summary>
        /// Send a packet to the server.
        /// Works from clients and server.
        /// <para><paramref name="serialized"/> = input pre-serialized data if you have it or leave null otherwise.</para>
        /// </summary>
        public void SendToServer(PacketBase packet, byte[] serialized = null)
        {
            if(MyAPIGateway.Multiplayer.IsServer) // short-circuit local call to avoid unnecessary serialization
            {
                HandlePacket(packet, MyAPIGateway.Multiplayer.MyId);
                return;
            }

            if(serialized == null)
                serialized = MyAPIGateway.Utilities.SerializeToBinary(packet);

            MyAPIGateway.Multiplayer.SendMessageToServer(ChannelId, serialized);
        }

        /// <summary>
        /// Send a packet to a specific player.
        /// Only works server side.
        /// <para><paramref name="serialized"/> = input pre-serialized data if you have it or leave null otherwise.</para>
        /// </summary>
        public void SendToPlayer(PacketBase packet, ulong steamId, byte[] serialized = null)
        {
            if(!MyAPIGateway.Multiplayer.IsServer)
                throw new Exception("Clients can't send packets to other clients directly!");

            if(serialized == null)
                serialized = MyAPIGateway.Utilities.SerializeToBinary(packet);

            MyAPIGateway.Multiplayer.SendMessageTo(ChannelId, serialized, steamId);
        }

        /// <summary>
        /// Sends packet to all players except server player and supplied packet's sender.
        /// Only works server side.
        /// </summary>
        public void SendToOthers(PacketBase packet)
        {
            if(packet == null)
                throw new ArgumentNullException("packet");

            RelayToOthers(packet, null);
        }

        /// <summary>
        /// Sends serialized packet bytes to all players except server player and supplied packet's sender.
        /// Only works server side.
        /// </summary>
        public void SendToOthers(byte[] serialized = null)
        {
            if(serialized == null)
                throw new ArgumentNullException("serialized");

            RelayToOthers(null, serialized);
        }

        void RelayToOthers(PacketBase packet = null, byte[] serialized = null, ulong senderSteamId = 0)
        {
            if(!MyAPIGateway.Multiplayer.IsServer)
                throw new Exception("Clients can't send directly to other clients! Send to server and use RelayMode to have it broadcasted to everyone else.");

            if(packet == null && serialized == null)
                throw new ArgumentException("Both 'packet' and 'serialized' arguments are null, at least one must be given!");

            if(TempPlayers == null)
                TempPlayers = new List<IMyPlayer>(MyAPIGateway.Session.SessionSettings.MaxPlayers);
            else
                TempPlayers.Clear();

            MyAPIGateway.Players.GetPlayers(TempPlayers);

            foreach(IMyPlayer p in TempPlayers)
            {
                // skip sending to self (server player) or back to sender
                if(p.SteamUserId == MyAPIGateway.Multiplayer.ServerId || p.SteamUserId == senderSteamId)
                    continue;

                if(serialized == null) // only serialize if necessary, and only once.
                    serialized = MyAPIGateway.Utilities.SerializeToBinary(packet);

                if(!MyAPIGateway.Multiplayer.SendMessageTo(ChannelId, serialized, p.SteamUserId))
                {
                    LogError($"Failed to send packet to {p.SteamUserId.ToString()}, game gives no further details, bugreport to author!");
                }
            }

            TempPlayers.Clear();
        }

        void ReceivedPacket(ushort channelId, byte[] serialized, ulong senderSteamId, bool isSenderServer) // executed when a packet is received on this machine
        {
            try
            {
                PacketBase packet = MyAPIGateway.Utilities.SerializeFromBinary<PacketBase>(serialized);
                HandlePacket(packet, senderSteamId, serialized);
            }
            catch(Exception e)
            {
                LogException(e);
            }
        }

        void HandlePacket(PacketBase packet, ulong senderSteamId, byte[] serialized = null)
        {
            // validate OriginalSenderSteamId
            if(MyAPIGateway.Session.IsServer && senderSteamId != packet.OriginalSenderSteamId)
            {
                LogError($"{GetType().FullName} WARNING: packet {packet.GetType().Name} from {senderSteamId.ToString()} has altered SenderSteamId to {packet.OriginalSenderSteamId.ToString()}. I replaced it with the proper id, but if this triggers for everyone then it's a bug somewhere.");

                packet.OriginalSenderSteamId = senderSteamId;
                serialized = null; // force reserialize
            }

            RelayMode relay = RelayMode.NoRelay;
            packet.Received(ref relay, senderSteamId);

            if(MyAPIGateway.Session.IsServer && relay != RelayMode.NoRelay)
            {
                if(relay == RelayMode.RelayOriginal)
                    RelayToOthers(packet, serialized, senderSteamId);
                else if(relay == RelayMode.RelayWithChanges)
                    RelayToOthers(packet, null, senderSteamId);
                else
                    throw new Exception($"Unknown relay mode: {relay.ToString()}");
            }
        }

        void DefaultLogError(string error)
        {
            MyLog.Default.WriteLineAndConsole($"{Mod.ModName} ERROR: {error}");

            if(MyAPIGateway.Session?.Player != null)
                MyAPIGateway.Utilities.ShowNotification($"[ERROR: {Mod.ModName}: {error} | Send SpaceEngineers.Log to mod author]", 10000, MyFontEnum.Red);
        }

        void DefaultLogException(Exception e)
        {
            MyLog.Default.WriteLineAndConsole($"{Mod.ModName} ERROR: {e.Message}\n{e.StackTrace}");

            if(MyAPIGateway.Session?.Player != null)
                MyAPIGateway.Utilities.ShowNotification($"[ERROR: {Mod.ModName}: {e.Message} | Send SpaceEngineers.Log to mod author]", 10000, MyFontEnum.Red);
        }
    }

    public enum RelayMode
    {
        /// <summary>
        /// Does not get sent to other clients.
        /// </summary>
        NoRelay = 0,

        /// <summary>
        /// Broadcast the received bytes to all clients except sender.
        /// </summary>
        RelayOriginal,

        /// <summary>
        /// Re-serialize the packet (to apply changes) and broadcast to all clients except sender.
        /// </summary>
        RelayWithChanges,
    }
}