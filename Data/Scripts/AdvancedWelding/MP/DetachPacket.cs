using System.Collections.Generic;
using ProtoBuf;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRageMath;

namespace Digi.AdvancedWelding.MP
{
    [ProtoContract]
    public class DetachPacket : PacketBase
    {
        public DetachPacket() { } // Empty constructor required for deserialization

        [ProtoMember(1)]
        private readonly long gridEntId;

        [ProtoMember(2)]
        private readonly Vector3I blockPos;

        public DetachPacket(long gridEntId, Vector3I blockPos)
        {
            this.gridEntId = gridEntId;
            this.blockPos = blockPos;
        }

        public override void Received(ref bool relay, ref bool includeSender)
        {
            IMyEntity ent;

            if(!MyAPIGateway.Entities.TryGetEntityById(gridEntId, out ent))
            {
                if(Networking.IsServer)
                    Log.Error($"Can't find grid entity ID: {gridEntId}");

                return;
            }

            var grid = ent as IMyCubeGrid;

            if(grid == null)
            {
                if(Networking.IsServer)
                    Log.Error($"Target entity is not a grid! gridId={gridEntId}; ent={ent})");

                return;
            }

            var slimBlock = grid.GetCubeBlock(blockPos);

            if(slimBlock == null)
            {
                if(Networking.IsServer)
                    Log.Error($"Target block does not exist in the grid; gridId={gridEntId}; pos={blockPos}");

                return;
            }

            if(Networking.IsPlayer)
            {
                AngleGrinder.PlayDetachSound(slimBlock);
            }

            if(Networking.IsServer)
            {
                relay = true;
                includeSender = false;

                var blockName = ((MyCubeBlockDefinition)slimBlock.BlockDefinition).DisplayNameText;
                var gridName = $"(Detached {blockName})";
                var blockOb = slimBlock.GetObjectBuilder();

                var gridOb = CreateNewGridOB(grid, blockOb, gridName);

                grid.RemoveBlock(slimBlock, true);

                var createdEnt = MyAPIGateway.Entities.CreateFromObjectBuilderAndAdd(gridOb);

                if(createdEnt == null)
                {
                    Log.Error($"Failed to create a new grid! obj={gridOb}");
                    return;
                }

                Log.Info($"Detached {blockName}; new grid id={ent.EntityId}");
            }
        }

        private static MyObjectBuilder_CubeGrid CreateNewGridOB(IMyCubeGrid grid, MyObjectBuilder_CubeBlock blockOb, string gridName)
        {
            var internalGrid = (MyCubeGrid)grid;
            var blockCount = internalGrid.BlocksCount;

            MyObjectBuilder_CubeGrid gridOb = null;

            if(blockCount > 1000)
            {
                gridOb = FastGridOBClone(internalGrid, blockOb, gridName);
            }
            else
            {
                gridOb = (MyObjectBuilder_CubeGrid)grid.GetObjectBuilder();
                gridOb.DisplayName = gridName;
                gridOb.IsStatic = false;
                gridOb.ConveyorLines = null;

                if(gridOb.OxygenAmount != null && gridOb.OxygenAmount.Length > 0)
                    gridOb.OxygenAmount = new float[0];

                if(gridOb.BlockGroups != null)
                    gridOb.BlockGroups.Clear();

                gridOb.CubeBlocks.Clear();
                gridOb.CubeBlocks.Add(blockOb);
            }

            MyAPIGateway.Entities.RemapObjectBuilder(gridOb);

            return gridOb;
        }

        private static MyObjectBuilder_CubeGrid FastGridOBClone(MyCubeGrid grid, MyObjectBuilder_CubeBlock blockOb, string gridName)
        {
            var gridOb = MyObjectBuilderSerializer.CreateNewObject<MyObjectBuilder_CubeGrid>();

            gridOb.DisplayName = gridName;
            gridOb.Name = null;
            gridOb.GridSizeEnum = grid.GridSizeEnum;
            gridOb.PersistentFlags = grid.Render.PersistentFlags;
            gridOb.ComponentContainer = grid.Components.Serialize(true);
            gridOb.PositionAndOrientation = new MyPositionAndOrientation(grid.WorldMatrix);

            gridOb.IsStatic = false;
            gridOb.IsUnsupportedStation = false;
            gridOb.IsRespawnGrid = false;

            gridOb.Editable = grid.Editable;
            gridOb.DestructibleBlocks = grid.DestructibleBlocks;
            gridOb.GridGeneralDamageModifier = grid.GridGeneralDamageModifier;
            gridOb.LocalCoordSys = grid.LocalCoordSystem;

            if(gridOb.CubeBlocks == null)
                gridOb.CubeBlocks = new List<MyObjectBuilder_CubeBlock>(1);

            gridOb.CubeBlocks.Add(blockOb);

            gridOb.CreatePhysics = grid.Physics != null;
            if(grid.Physics != null)
            {
                gridOb.LinearVelocity = grid.Physics.LinearVelocity;
                gridOb.AngularVelocity = grid.Physics.AngularVelocity;
            }

            gridOb.BlockGroups = new List<MyObjectBuilder_BlockGroup>(0);

            return gridOb;
        }
    }
}
