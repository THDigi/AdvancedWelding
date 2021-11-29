using Digi.AdvancedWelding;
using ProtoBuf;

namespace Digi.Sync
{
    // Must include all packets
    [ProtoInclude(10, typeof(DetachModePacket))]
    [ProtoInclude(11, typeof(DetachProgressPacket))]
    [ProtoInclude(20, typeof(PrecisionPacket))]
    [ProtoInclude(30, typeof(WeldEffectsPacket))]
    [ProtoInclude(31, typeof(DetachEffectsPacket))]
    public abstract partial class PacketBase { }
}