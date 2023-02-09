using System;
using System.Collections.Generic;
using System.Linq;
using Digi.Sync;
using ProtoBuf;
using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Weapons;
using VRage;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Input;
using VRage.ObjectBuilders;
using VRageMath;

namespace Digi.AdvancedWelding
{
    [ProtoContract(UseProtoMembersOnly = true)]
    public class DetachModePacket : PacketBase
    {
        public static event Action<DetachModePacket> OnReceive;

        [ProtoMember(1)]
        public bool Mode;

        public DetachModePacket() { } // Empty constructor required for deserialization

        public override void Received(ref RelayMode relay, ulong senderSteamId)
        {
            OnReceive?.Invoke(this);
        }
    }

    [ProtoContract(UseProtoMembersOnly = true)]
    public class DetachProgressPacket : PacketBase
    {
        public static event Action<DetachProgressPacket> OnReceive;

        [ProtoMember(1)]
        public DetachState State;

        [ProtoMember(2)]
        public int Progress;

        public DetachProgressPacket() { } // Empty constructor required for deserialization

        public override void Received(ref RelayMode relay, ulong senderSteamId)
        {
            OnReceive?.Invoke(this);
        }

        public void Send(ulong sendTo, DetachState state, int progress = 0)
        {
            State = state;
            Progress = progress;
            AdvancedWeldingMod.Instance.Networking.SendToPlayer(this, sendTo);
        }
    }

    public enum DetachState : byte
    {
        EnemyBlock,
        SingleBlock,
        NoStandalone,
        Detaching,
        ZeroGrindAmount,
        DetachComplete,
    }

    // Server and client side
    public class DetachHandler : ComponentBase, IUpdatable
    {
        const float GrinderCooldownMs = 250; // from MyAngleGrinder constructor calling base constructor.

        // NOTE: if one changes these, edit ChatCommands.HelpText aswell.
        const MyKeys DetachModeKey = MyKeys.Control;
        const MyJoystickButtonsEnum DetachModeButton = MyJoystickButtonsEnum.J05; // left bumper which is a modifier just like ctrl is

        DetachModePacket ModePacket;
        DetachProgressPacket ProgressPacket;
        DetachData Local_DetachData;
        Dictionary<ulong, DetachData> Server_DetachData;

        class DetachData
        {
            public bool DetachMode;
            public int GrindExpiresAtTick;
            public int GrindedTimes;
            public IMySlimBlock GrindedBlock;
        }

        public DetachHandler(AdvancedWeldingMod main) : base(main)
        {
        }

        public override void Register()
        {
            ModePacket = new DetachModePacket();
            ProgressPacket = new DetachProgressPacket();

            if(Networking.IsPlayer)
            {
                Local_DetachData = new DetachData();
                Main.GrinderHandler.GrinderChanged += Local_EquippedGrinderChanged;
                DetachProgressPacket.OnReceive += Player_DetachProgressPacketReceived;
            }

            if(MyAPIGateway.Session.IsServer)
            {
                Server_DetachData = new Dictionary<ulong, DetachData>();
                Main.GrindDamageHandler.GrindingBlock += Server_GrindingBlock;
                DetachModePacket.OnReceive += Server_DetachModePacketReceived;
                MyVisualScriptLogicProvider.PlayerDisconnected += Server_PlayerDisconnected;
            }
        }

        public override void Dispose()
        {
            DetachProgressPacket.OnReceive -= Player_DetachProgressPacketReceived;
            DetachModePacket.OnReceive -= Server_DetachModePacketReceived;
            MyVisualScriptLogicProvider.PlayerDisconnected -= Server_PlayerDisconnected;
        }

        void Server_PlayerDisconnected(long identityId)
        {
            ulong steamId = MyAPIGateway.Players.TryGetSteamId(identityId);
            Server_DetachData.Remove(steamId);
        }

        void Local_EquippedGrinderChanged(IMyAngleGrinder grinder)
        {
            SetUpdate(this, grinder != null);

            if(grinder == null)
                SetLocalDetach(false);
        }

        void IUpdatable.Update()
        {
            if(MyAPIGateway.Utilities.IsDedicated)
                return;

            IMyAngleGrinder grinder = Main.GrinderHandler.EquippedGrinder;
            if(grinder == null)
                return;

            bool shouldDetach = !MyAPIGateway.Gui.IsCursorVisible
                             && !MyAPIGateway.Gui.ChatEntryVisible
                             && (MyAPIGateway.Input.IsKeyPress(DetachModeKey) || MyAPIGateway.Input.IsJoystickButtonPressed(DetachModeButton));

            if(shouldDetach != Local_DetachData.DetachMode)
            {
                SetLocalDetach(shouldDetach);
            }
        }

        void SetLocalDetach(bool mode, bool notify = true)
        {
            if(mode == Local_DetachData.DetachMode)
                return;

            Local_DetachData.DetachMode = mode;
            Local_DetachData.GrindedBlock = null;
            Local_DetachData.GrindedTimes = 0;
            Local_DetachData.GrindExpiresAtTick = 0;

            ModePacket.Mode = mode;
            Main.Networking.SendToServer(ModePacket);

            if(notify)
            {
                if(mode)
                    Main.Notifications.Print(Channel.Detach, "Detach mode: Grind a block to detach it", MyFontEnum.Blue, 60 * 1000);
                else
                    Main.Notifications.Print(Channel.Detach, "Detach mode: Off", MyFontEnum.Debug, 1500);
            }
        }

        void Server_DetachModePacketReceived(DetachModePacket packet)
        {
            if(packet.Mode)
            {
                DetachData data;
                if(!Server_DetachData.TryGetValue(packet.OriginalSenderSteamId, out data))
                {
                    data = new DetachData();
                    Server_DetachData[packet.OriginalSenderSteamId] = data;
                }

                data.DetachMode = packet.Mode;
                data.GrindedBlock = null;
                data.GrindedTimes = 0;
                data.GrindExpiresAtTick = 0;
            }
            else
            {
                Server_DetachData.Remove(packet.OriginalSenderSteamId);
            }
        }

        void Player_DetachProgressPacketReceived(DetachProgressPacket packet)
        {
            // must synchronize these messages from server because MP clients don't get grind damage events.

            switch(packet.State)
            {
                case DetachState.EnemyBlock:
                    Main.Notifications.Print(Channel.Detach, "Detach Mode: Cannot detach enemy blocks.", MyFontEnum.Red, 3000);
                    break;
                case DetachState.SingleBlock:
                    Main.Notifications.Print(Channel.Detach, "Detach Mode: Nothing to detach from, block is free floating.", MyFontEnum.Debug, 2000);
                    break;
                case DetachState.NoStandalone:
                    Main.Notifications.Print(Channel.Detach, "Detach Mode: This block cannot exist standalone.", MyFontEnum.Red, 3000);
                    break;
                case DetachState.Detaching:
                    Main.Notifications.Print(Channel.Detach, $"Detach Mode: Detaching [{packet.Progress.ToString()}%]...", MyFontEnum.Debug, 500);
                    break;
                case DetachState.DetachComplete:
                    Main.Notifications.Print(Channel.Detach, "Detach Mode: Block is detached!", MyFontEnum.Green, 2000);
                    break;
                case DetachState.ZeroGrindAmount:
                    Main.Notifications.Print(Channel.Detach, "Detach Mode: Something else is preventing grinding, cannot detach.", MyFontEnum.Red, 1000);
                    break;
                default:
                    Main.Notifications.Print(Channel.Detach, $"Detach Mode: unknown state received: {packet.State.ToString()}", MyFontEnum.Red, 5000);
                    break;
            }
        }

        void Server_GrindingBlock(IMySlimBlock block, ref MyDamageInformation info, IMyAngleGrinder grinder, ulong attackerSteamId)
        {
            if(info.Amount <= 0)
                return; // already cancelled by something else, like precision mode, ignore.

            DetachData data = Server_DetachData.GetValueOrDefault(attackerSteamId);
            if(data == null || !data.DetachMode)
                return;

            float grindAmount = info.Amount; // store grinder speed for later use
            info.Amount = 0; // prevent grinding while detach mode is enabled

            MyCubeBlockDefinition blockDef = (MyCubeBlockDefinition)block.BlockDefinition;

            if(!blockDef.IsStandAlone || !blockDef.HasPhysics)
            {
                ProgressPacket.Send(attackerSteamId, DetachState.NoStandalone);
                return;
            }

            if(grindAmount == 0)
            {
                ProgressPacket.Send(attackerSteamId, DetachState.ZeroGrindAmount);
                return;
            }

            long owner = block.OwnerId;

            if(owner == 0 && block.CubeGrid.BigOwners != null && block.CubeGrid.BigOwners.Count > 0)
                owner = block.CubeGrid.BigOwners[0];

            if(owner == 0)
                owner = block.BuiltBy;

            if(owner != 0)
            {
                MyRelationsBetweenPlayerAndBlock relation = MyIDModule.GetRelationPlayerBlock(owner, grinder.OwnerIdentityId, MyOwnershipShareModeEnum.Faction);
                if(relation == MyRelationsBetweenPlayerAndBlock.Enemies)
                {
                    ProgressPacket.Send(attackerSteamId, DetachState.EnemyBlock);
                    return;
                }
            }

            MyCubeGrid grid = (MyCubeGrid)block.CubeGrid;

            if(grid.BlocksCount <= 1)
            {
                if(grid.DisplayName.StartsWith("(Detached"))
                    ProgressPacket.Send(attackerSteamId, DetachState.DetachComplete);
                else
                    ProgressPacket.Send(attackerSteamId, DetachState.SingleBlock);
                return;
            }

            // checking safezones doesn't seem necessary, as you cannot grind to begin with...

            if(data.GrindedBlock != block)
            {
                data.GrindedBlock = block;
                data.GrindedTimes = 0;
                data.GrindExpiresAtTick = 0;
            }

            int tick = MyAPIGateway.Session.GameplayFrameCounter;
            if(data.GrindExpiresAtTick == 0 || data.GrindExpiresAtTick > tick)
                data.GrindedTimes++;
            else
                data.GrindedTimes = 0;

            data.GrindExpiresAtTick = tick + 30;

            // make it require as much time as it normally would to grind to critical line
            float divideBy = (block.FatBlock != null ? block.FatBlock.DisassembleRatio : blockDef.DisassembleRatio);
            float finalDamage = (grindAmount / divideBy) * blockDef.IntegrityPointsPerSec;
            int grindTimesToDetach = (int)((block.MaxIntegrity * blockDef.CriticalIntegrityRatio) / finalDamage);

            grindTimesToDetach = Math.Max(grindTimesToDetach, (int)(1000 / GrinderCooldownMs)); // at least one second

            if(data.GrindedTimes >= grindTimesToDetach)
            {
                data.GrindedTimes = 0;
                ProgressPacket.Send(attackerSteamId, DetachState.DetachComplete);
                DetachBlock(block, grinder);
            }
            else
            {
                int progress = (data.GrindedTimes * 100) / grindTimesToDetach;
                ProgressPacket.Send(attackerSteamId, DetachState.Detaching, progress);
            }
        }

        static void DetachBlock(IMySlimBlock block, IMyAngleGrinder grinder)
        {
            if(!MyAPIGateway.Session.IsServer)
                return;

            MyCubeBlockDefinition blockDef = (MyCubeBlockDefinition)block.BlockDefinition;

            // HACK: disconnect block by making it think it has no mountpoints
            // does not affect other blocks with same definition because of the way Add/RemoveNeighbours() works, this does mean it could no longer work reliably in the future...
            MyCubeBlockDefinition.MountPoint[] originalMounts = blockDef.MountPoints;

            try
            {
                blockDef.MountPoints = new MyCubeBlockDefinition.MountPoint[0];
                block.CubeGrid.UpdateBlockNeighbours(block);
            }
            finally
            {
                blockDef.MountPoints = originalMounts;
            }

            // sending "to server" so that it happens serverside too if server is a player (or singleplayer).
            DetachEffectsPacket packet = new DetachEffectsPacket(block);
            AdvancedWeldingMod.Instance.Networking.SendToServer(packet);
        }
    }
}