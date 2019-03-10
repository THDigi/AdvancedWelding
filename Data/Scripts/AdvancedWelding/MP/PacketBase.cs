using ProtoBuf;
using Sandbox.ModAPI;

namespace Digi.AdvancedWelding.MP
{
    [ProtoInclude(2, typeof(DetachPacket))]
    [ProtoInclude(3, typeof(WeldEffectsPacket))]
    [ProtoContract]
    public abstract class PacketBase
    {
        [ProtoMember(1)]
        public readonly ulong SenderId;

        /// <summary>
        /// Set this to true for the packet to be relayed to clients.
        /// </summary>
        public bool Relay { get; protected set; } = false;

        public PacketBase()
        {
            SenderId = MyAPIGateway.Multiplayer.MyId;
        }

        /// <summary>
        /// Called when this packet is received on this machine.
        /// </summary>
        public abstract void Received(ref bool relay, ref bool includeSender);
    }
}
