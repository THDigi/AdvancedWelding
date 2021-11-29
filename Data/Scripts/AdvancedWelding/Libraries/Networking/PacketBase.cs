using ProtoBuf;
using Sandbox.ModAPI;

namespace Digi.Sync
{
    [ProtoContract]
    public abstract partial class PacketBase
    {
        /// <summary>
        /// Do not edit, assigned automatically.
        /// The original client sender of this packet.
        /// This gets validated serverside.
        /// </summary>
        [ProtoMember(1)]
        public ulong OriginalSenderSteamId;

        public PacketBase() // Empty constructor required for deserialization
        {
            OriginalSenderSteamId = MyAPIGateway.Multiplayer.MyId;
        }

        /// <summary>
        /// Called when this packet is received on this machine.
        /// <para><paramref name="relay"/> = relay this packet instance to other clients when received server-side.</para>
        /// <para><paramref name="senderSteamId"/> = the packet's sender. NOTE: relayed packets will have the server as the sender!</para>
        /// </summary>
        public abstract void Received(ref RelayMode relay, ulong senderSteamId);
    }
}
