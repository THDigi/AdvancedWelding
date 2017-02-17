using System;
using System.Collections.Generic;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRageMath;

namespace Digi.AdvancedWelding
{
    public class MergeGrids
    {
        public IMyTerminalBlock pad1, pad2;
        public int skipped = 0;

        public MergeGrids() { }
    }

    [MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation)]
    public class AdvancedWelding : MySessionComponentBase
    {
        public override void LoadData()
        {
            Log.SetUp("Advanced Welding", 510790477, "AdvancedWelding");
        }

        public static bool init { get; private set; }

        public static readonly ushort PACKET = 9472;

        private static bool detachCancelNotified = false;
        public static List<IMyTerminalBlock> pads = new List<IMyTerminalBlock>();
        public static List<MergeGrids> mergeQueue = new List<MergeGrids>();

        public void Init()
        {
            Log.Init();
            init = true;
            AngleGrinder.notified = false;
            AngleGrinder.detach = false;

            if(MyAPIGateway.Multiplayer.IsServer)
                MyAPIGateway.Multiplayer.RegisterMessageHandler(PACKET, PacketReceived);

            if(!(MyAPIGateway.Multiplayer.IsServer && MyAPIGateway.Utilities.IsDedicated))
                MyAPIGateway.Utilities.MessageEntered += MessageEntered;
        }

        protected override void UnloadData()
        {
            try
            {
                if(init)
                {
                    init = false;
                    MyAPIGateway.Utilities.MessageEntered -= MessageEntered;
                    MyAPIGateway.Multiplayer.UnregisterMessageHandler(PACKET, PacketReceived);
                }

                pads.Clear();
                mergeQueue.Clear();
            }
            catch(Exception e)
            {
                Log.Error(e);
            }

            Log.Close();
        }

        public override void UpdateAfterSimulation()
        {
            try
            {
                if(!init)
                {
                    if(MyAPIGateway.Session == null)
                        return;

                    Init();
                }

                if(mergeQueue.Count > 0)
                {
                    List<int> toRemove = new List<int>(mergeQueue.Count);

                    for(int i = 0; i < mergeQueue.Count; i++)
                    {
                        var merge = mergeQueue[i];

                        if(merge.skipped <= 10)
                        {
                            merge.skipped += 1;
                            continue;
                        }

                        var newGrid = MergeGrids(merge.pad1, merge.pad2);

                        if(newGrid == null)
                            newGrid = MergeGrids(merge.pad2, merge.pad1);

                        Log.Info("merged to " + newGrid);

                        toRemove.Add(i);
                    }

                    foreach(var i in toRemove)
                    {
                        mergeQueue.RemoveAt(i);
                    }

                    toRemove.Clear();
                    toRemove = null;
                }
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }

        private void PacketReceived(byte[] bytes)
        {
            try
            {
                int index = 0;
                long gridId = BitConverter.ToInt64(bytes, index);
                index += sizeof(long);

                var slimPos = new Vector3I(
                    BitConverter.ToInt32(bytes, index),
                    BitConverter.ToInt32(bytes, index + sizeof(int)),
                    BitConverter.ToInt32(bytes, index + sizeof(int) + sizeof(int)));
                index += sizeof(int) * 3;

                if(!MyAPIGateway.Entities.EntityExists(gridId))
                {
                    Log.Error("Can't find grid entity ID: " + gridId);
                    return;
                }

                var grid = MyAPIGateway.Entities.GetEntityById(gridId) as IMyCubeGrid;

                if(grid == null)
                {
                    Log.Error("Target entity is not a grid! gridId=" + gridId + "; ToString=" + grid.ToString());
                    return;
                }

                var slimBlock = grid.GetCubeBlock(slimPos);

                if(slimBlock == null)
                {
                    Log.Error("Target block does not exist in the grid; gridId=" + gridId + "; pos=" + slimPos);
                    return;
                }

                var blockName = ((MyCubeBlockDefinition)slimBlock.BlockDefinition).DisplayNameText;
                var blockObj = slimBlock.GetObjectBuilder();

                var gridObj = grid.GetObjectBuilder(false) as MyObjectBuilder_CubeGrid;
                gridObj.DisplayName = "(Detached " + blockName + ")";
                gridObj.IsStatic = false;
                gridObj.ConveyorLines = null;

                if(gridObj.OxygenAmount != null && gridObj.OxygenAmount.Length > 0)
                    gridObj.OxygenAmount = new float[0];

                if(gridObj.BlockGroups != null)
                    gridObj.BlockGroups.Clear();

                var addBlockObj = blockObj;

                foreach(var b in gridObj.CubeBlocks)
                {
                    if(b.EntityId == blockObj.EntityId)
                    {
                        addBlockObj = b;
                        break;
                    }
                }

                gridObj.CubeBlocks.Clear();
                gridObj.CubeBlocks.Add(addBlockObj);

                grid.RemoveBlock(slimBlock, true);

                MyAPIGateway.Entities.RemapObjectBuilder(gridObj);
                var ent = MyAPIGateway.Entities.CreateFromObjectBuilderAndAdd(gridObj);

                if(ent == null)
                {
                    Log.Error("Failed to create a new grid! object=" + gridObj);
                    return;
                }

                Log.Info("Detached " + blockName + ", new grid id=" + ent.EntityId);
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }

        public static Vector3I CalculateOffset(IMyTerminalBlock pad1, IMyTerminalBlock pad2)
        {
            Base6Directions.Direction pad1forward = Base6Directions.Direction.Up;
            Base6Directions.Direction pad1right = Base6Directions.GetPerpendicular(pad1forward);
            Base6Directions.Direction pad2forward = pad1forward;

            Base6Directions.Direction thisRight = pad1.Orientation.TransformDirection(pad1right);
            Base6Directions.Direction thisForward = pad1.Orientation.TransformDirection(pad1forward);

            Base6Directions.Direction otherBackward = Base6Directions.GetFlippedDirection(pad2.Orientation.TransformDirection(pad2forward));
            Base6Directions.Direction otherRight = pad2.CubeGrid.WorldMatrix.GetClosestDirection(pad1.CubeGrid.WorldMatrix.GetDirectionVector(thisRight));

            Vector3 myConstraint = pad1.Position;
            Vector3 otherConstraint = -(pad2.Position + pad2.PositionComp.LocalMatrix.GetDirectionVector(otherBackward));

            Vector3 toOtherOrigin;
            MatrixI rotation = MatrixI.CreateRotation(otherRight, otherBackward, thisRight, thisForward);
            Vector3.Transform(ref otherConstraint, ref rotation, out toOtherOrigin);

            return Vector3I.Round(myConstraint + toOtherOrigin);
        }

        public static IMyCubeGrid MergeGrids(IMyTerminalBlock pad1, IMyTerminalBlock pad2)
        {
            return (pad1.CubeGrid as IMyCubeGrid).MergeGrid_MergeBlock(pad2.CubeGrid as IMyCubeGrid, CalculateOffset(pad1, pad2));
        }

        public void MessageEntered(string msg, ref bool send)
        {
            if(msg.StartsWith("/detach", StringComparison.InvariantCultureIgnoreCase))
            {
                send = false;

                if(msg.StartsWith("/detach cancel"))
                {
                    if(AngleGrinder.detach)
                    {
                        AngleGrinder.detach = false;
                        MyAPIGateway.Utilities.ShowMessage(Log.modName, "Block detach mode turned off.");
                    }
                }
                else
                {
                    AngleGrinder.detach = true;

                    if(!AngleGrinder.isHolding)
                    {
                        AngleGrinder.SetToolStatus("Detach mode enabled, switch to your angle grinder to begin...", MyFontEnum.Blue, 5000);
                    }

                    if(!detachCancelNotified)
                    {
                        detachCancelNotified = true;
                        MyAPIGateway.Utilities.ShowMessage(Log.modName, "To cancel this mode just holster your grinder or type /detach cancel.");
                    }
                }
            }
        }
    }
}