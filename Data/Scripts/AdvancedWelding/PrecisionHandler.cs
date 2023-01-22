using System;
using System.Collections.Generic;
using Digi.Sync;
using ProtoBuf;
using Sandbox.Game;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Weapons;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Input;
using VRage.Utils;
using VRageMath;
using BlendTypeEnum = VRageRender.MyBillboard.BlendTypeEnum;
using IAltControllableEntity = Sandbox.Game.Entities.IMyControllableEntity;

namespace Digi.AdvancedWelding
{
    [ProtoContract(UseProtoMembersOnly = true)]
    public class PrecisionPacket : PacketBase
    {
        public static event Action<PrecisionPacket> OnReceive;

        [ProtoMember(1)]
        public long GridId;

        [ProtoMember(2)]
        public Vector3I BlockPos;

        public PrecisionPacket() { } // Empty constructor required for deserialization

        public override void Received(ref RelayMode relay, ulong senderSteamId)
        {
            OnReceive?.Invoke(this);
        }
    }

    // Server and client side
    public class PrecisionHandler : ComponentBase, IUpdatable
    {
        // NOTE: if one changes this, should also edit ChatCommands.HelpText
        static readonly MyStringId ControlForAction = MyControlsSpace.SECONDARY_TOOL_ACTION;

        public class PrecisionData
        {
            public long GridEntId;
            public Vector3I BlockPos;
        }

        IMySlimBlock TargetBlock;
        Vector3D BlockLocalCenter;

        PrecisionPacket Packet;
        Dictionary<ulong, PrecisionData> Server_PrecisionData;

        public PrecisionHandler(AdvancedWeldingMod main) : base(main)
        {
        }

        public override void Register()
        {
            Packet = new PrecisionPacket();

            if(Networking.IsPlayer)
            {
                Main.GrinderHandler.GrinderChanged += Local_EquippedGrinderChanged;
            }

            if(MyAPIGateway.Session.IsServer)
            {
                Server_PrecisionData = new Dictionary<ulong, PrecisionData>();

                Main.GrindDamageHandler.GrindingBlock += Server_GrindingBlock;
                Main.GrindDamageHandler.GrindingFloatingObject += Server_GrindingFloatingObject;

                PrecisionPacket.OnReceive += Server_PacketReceived;
                MyVisualScriptLogicProvider.PlayerDisconnected += Server_PlayerDisconnected;
            }
        }

        public override void Dispose()
        {
            PrecisionPacket.OnReceive -= Server_PacketReceived;
            MyVisualScriptLogicProvider.PlayerDisconnected -= Server_PlayerDisconnected;
        }

        void Server_PlayerDisconnected(long identityId)
        {
            ulong steamId = MyAPIGateway.Players.TryGetSteamId(identityId);
            Server_PrecisionData.Remove(steamId);
        }

        void Server_PacketReceived(PrecisionPacket packet)
        {
            if(packet.GridId == 0)
            {
                Server_PrecisionData.Remove(packet.OriginalSenderSteamId);
            }
            else
            {
                PrecisionData data;
                if(!Server_PrecisionData.TryGetValue(packet.OriginalSenderSteamId, out data))
                {
                    data = new PrecisionData();
                    Server_PrecisionData[packet.OriginalSenderSteamId] = data;
                }

                data.GridEntId = packet.GridId;
                data.BlockPos = packet.BlockPos;
            }
        }

        void Server_GrindingBlock(IMySlimBlock block, ref MyDamageInformation info, IMyAngleGrinder grinder, ulong attackerSteamId)
        {
            PrecisionData data;
            if(!Server_PrecisionData.TryGetValue(attackerSteamId, out data) || data.GridEntId == 0)
                return;

            if(data.GridEntId != block.CubeGrid.EntityId || data.BlockPos != block.Min)
            {
                info.Amount = 0; // prevent grinding untargetted block
            }
        }

        void Server_GrindingFloatingObject(IMyFloatingObject floatingObject, ref MyDamageInformation info, ulong attackerSteamId)
        {
            PrecisionData data;
            if(!Server_PrecisionData.TryGetValue(attackerSteamId, out data) || data.GridEntId == 0)
                return;

            // prevent all floating object damage while in precision mode
            info.Amount = 0;
        }

        void Local_EquippedGrinderChanged(IMyAngleGrinder grinder)
        {
            SetUpdate(this, grinder != null);

            if(grinder == null)
            {
                SetLocalTarget(null);
            }
        }

        void IUpdatable.Update()
        {
            IMyAngleGrinder grinder = Main.GrinderHandler?.EquippedGrinder;
            if(grinder == null)
                return;

            bool inputReadable = !MyAPIGateway.Gui.IsCursorVisible && !MyAPIGateway.Gui.ChatEntryVisible;
            bool heldPressed = false;
            bool newReleased = false;

            if(inputReadable)
            {
                if(MyAPIGateway.Input.IsJoystickLastUsed)
                {
                    IAltControllableEntity ctrlEnt = (IAltControllableEntity)MyAPIGateway.Session.ControlledObject;
                    IMyControllerControl control = MyAPIGateway.Input.GetControl(ctrlEnt.ControlContext, ControlForAction);
                    IMyControllerControl controlAux = MyAPIGateway.Input.GetControl(ctrlEnt.AuxiliaryContext, ControlForAction);

                    heldPressed = (control?.IsPressed() ?? false) || (controlAux?.IsPressed() ?? false);
                    newReleased = (control?.IsNewReleased() ?? false) || (controlAux?.IsNewReleased() ?? false);
                }
                else
                {
                    heldPressed = MyAPIGateway.Input.IsGameControlPressed(ControlForAction);
                    newReleased = MyAPIGateway.Input.IsNewGameControlReleased(ControlForAction);
                }
            }

            if(TargetBlock == null && inputReadable && heldPressed)
            {
                MyCasterComponent casterComp = grinder?.Components?.Get<MyCasterComponent>();
                IMySlimBlock target = casterComp?.HitBlock as IMySlimBlock;
                if(target != null)
                {
                    SetLocalTarget(target);
                }
            }

            if(TargetBlock != null && (!inputReadable || newReleased))
            {
                SetLocalTarget(null);
            }

            if(TargetBlock != null)
            {
                bool finishedGrinding = TargetBlock.IsFullyDismounted;

                // TODO: some targetting sprite?
                //Vector3D blockCenterPos = Vector3D.Transform(BlockLocalCenter, TargetBlock.CubeGrid.WorldMatrix);
                //float blockSize = (TargetBlock.Max - TargetBlock.Min + Vector3I.One).AbsMin() * TargetBlock.CubeGrid.GridSize;
                //Vector4 color = (finishedGrinding ? Color.Lime : Color.Blue);
                //MyTransparentGeometry.AddPointBillboard(MyStringId.GetOrCompute("WhiteDot"), color, blockCenterPos, blockSize, 0, blendType: BlendTypeEnum.AdditiveTop);

                if(finishedGrinding)
                    Main.Notifications.Print(Channel.Precision, $"Precision Grind: target was grinded!", MyFontEnum.Green, 16);
                else
                    Main.Notifications.Print(Channel.Precision, $"Precision Grind: locked to {TargetBlock.BlockDefinition.DisplayNameText}", MyFontEnum.Debug, 16);
            }
        }

        void SetLocalTarget(IMySlimBlock block)
        {
            if(block == TargetBlock)
                return;

            TargetBlock = block;

            if(block != null)
            {
                block.ComputeWorldCenter(out BlockLocalCenter);
                BlockLocalCenter = Vector3D.Transform(BlockLocalCenter, block.CubeGrid.WorldMatrixInvScaled);

                Packet.GridId = block.CubeGrid.EntityId;
                Packet.BlockPos = block.Min;
            }
            else
            {
                Packet.GridId = 0;
                Packet.BlockPos = default(Vector3I);
            }

            Main.Networking.SendToServer(Packet);

            //Main.Notifications.Print(Channel.Other, $"Precision Grind: set={(block?.BlockDefinition?.DisplayNameText ?? "null")}", MyFontEnum.Debug, 3 * 1000);
        }
    }
}