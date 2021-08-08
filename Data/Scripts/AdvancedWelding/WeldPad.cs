using System;
using System.Collections.Generic;
using Digi.AdvancedWelding.MP;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;
//using BlendTypeEnum = VRageRender.MyBillboard.BlendTypeEnum;

namespace Digi.AdvancedWelding
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_TerminalBlock), false, "SmallWeldPad", "LargeWeldPad")]
    public class WeldPad : MyGameLogicComponent
    {
        private const int TOOLSTATUS_TIMEOUT = 200;
        private const int NOTIFY_DISTANCE_SQ = 20 * 20;
        private const Base6Directions.Direction DIR_FORWARD = Base6Directions.Direction.Up;
        private const Base6Directions.Direction DIR_RIGHT = Base6Directions.Direction.Right;

        private bool master = false;
        private IMyCubeBlock pad;
        private IMyCubeBlock otherPad;
        private IMyHudNotification toolStatus;

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            NeedsUpdate = MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
        }

        public override void UpdateOnceBeforeFrame()
        {
            try
            {
                pad = (IMyCubeBlock)Entity;

                if(pad.CubeGrid?.Physics == null)
                    return;

                AdvancedWelding.Instance.Pads.Add(pad);

                NeedsUpdate = MyEntityUpdateEnum.EACH_10TH_FRAME;
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }

        public override void Close()
        {
            try
            {
                AdvancedWelding.Instance.Pads.Remove(pad);
                otherPad = null;
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }

        public void SetToolStatus(string text, string font, int aliveTime = TOOLSTATUS_TIMEOUT)
        {
            try
            {
                if(!Networking.IsPlayer)
                    return;

                if(pad.GetPlayerRelationToOwner() == MyRelationsBetweenPlayerAndBlock.Enemies)
                    return;

                var camPos = MyAPIGateway.Session.Camera.WorldMatrix.Translation;
                var padPos = pad.GetPosition();

                if(Vector3D.DistanceSquared(camPos, padPos) > NOTIFY_DISTANCE_SQ)
                    return;

                if(toolStatus == null)
                {
                    toolStatus = MyAPIGateway.Utilities.CreateNotification(text, aliveTime, font);
                }
                else
                {
                    toolStatus.Hide(); // required since SE v1.194
                    toolStatus.Font = font;
                    toolStatus.Text = text;
                    toolStatus.AliveTime = aliveTime;
                }

                toolStatus.Show();
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }

        public override void UpdateAfterSimulation10()
        {
            try
            {
                if(AdvancedWelding.Instance.Pads.Count <= 1)
                    return;

                if(!pad.IsWorking)
                    return;

                if(otherPad != null && (otherPad.Closed || otherPad.MarkedForClose || otherPad.CubeGrid?.Physics == null || !otherPad.IsWorking))
                {
                    otherPad = null;
                    master = false;
                    return;
                }

                if(otherPad != null && !master)
                    return; // only one pad 'thinks'

                var padGrid = pad.CubeGrid;
                Vector3D pos = pad.WorldMatrix.Translation + pad.WorldMatrix.Down * (padGrid.GridSize / 2);
                Vector3D otherPos = Vector3D.Zero;
                double distSq = 0;

                if(otherPad == null)
                {
                    foreach(var p in AdvancedWelding.Instance.Pads)
                    {
                        if(p.EntityId == pad.EntityId
                        || p.CubeGrid?.Physics == null
                        || p.CubeGrid.GridSizeEnum != padGrid.GridSizeEnum
                        || (padGrid.IsStatic && p.CubeGrid.IsStatic)
                        || !p.IsWorking)
                            continue;

                        otherPos = p.WorldMatrix.Translation + (-p.WorldMatrix.GetDirectionVector(DIR_FORWARD) * (p.CubeGrid.GridSize / 2));
                        distSq = Vector3D.DistanceSquared(pos, otherPos);

                        if(distSq <= p.CubeGrid.GridSize * p.CubeGrid.GridSize)
                        {
                            otherPad = p;

                            var logic = otherPad.GameLogic.GetAs<WeldPad>();

                            if(logic == null)
                                continue;

                            logic.otherPad = pad;
                            logic.master = false;
                            master = false;

                            var padGridInternal = (MyCubeGrid)padGrid;
                            var otherGridInternal = (MyCubeGrid)p.CubeGrid;
                            var parentGrid = GetMergeParent(padGridInternal, otherGridInternal);

                            if(parentGrid == padGridInternal)
                                master = true;
                            else
                                logic.master = true;

                            break;
                        }
                    }

                    if(otherPad == null)
                        return;
                }
                else
                {
                    otherPos = otherPad.WorldMatrix.Translation + (-otherPad.WorldMatrix.GetDirectionVector(DIR_FORWARD) * (otherPad.CubeGrid.GridSize / 2));
                    distSq = Vector3D.DistanceSquared(pos, otherPos);

                    if(distSq > otherPad.CubeGrid.GridSize * otherPad.CubeGrid.GridSize)
                    {
                        var logic = otherPad.GameLogic.GetAs<WeldPad>();
                        logic.otherPad = null;
                        logic.master = false;

                        otherPad = null;
                        master = false;
                        return;
                    }
                }

                var otherGrid = otherPad.CubeGrid;
                var axisDot = Math.Round(Vector3D.Dot(pad.WorldMatrix.Up, otherPad.WorldMatrix.Up), 2);
                var rollDot = Math.Round(Vector3D.Dot(pad.WorldMatrix.Right, otherPad.WorldMatrix.GetDirectionVector(otherPad.WorldMatrix.GetClosestDirection(pad.WorldMatrix.Right))), 2);
                var distReq = (padGrid.GridSizeEnum == MyCubeSize.Large ? 0.5 : 0.25);
                var dist = Math.Sqrt(distSq) - distReq;

                if(dist <= 0 && axisDot == -1.0 && rollDot == 1.0)
                {
                    if(!CanMergeCubes(pad, otherPad, CalculateOffset(pad, otherPad)))
                    {
                        SetToolStatus("WeldPad: Can't merge this way, blocks would overlap!", MyFontEnum.Red, 1000);
                        return;
                    }

                    //if(MyAPIGateway.GridGroups.HasConnection(pad.CubeGrid, otherPad.CubeGrid, GridLinkTypeEnum.Physical))
                    //{
                    //    SetToolStatus("WeldPad: Can't merge physically connected grids!", MyFontEnum.Red, 1000);
                    //    return;
                    //}

                    //SetToolStatus("WeldPad: Succesfully fake merged", MyFontEnum.Green, 1000);
                    //return;

                    if(!MyAPIGateway.Multiplayer.IsServer)
                    {
                        Log.Info("Merge checks out clientside, waiting for server...");
                    }
                    else
                    {
                        AdvancedWelding.Instance.ToMerge.Add(new MergeGrids(pad, otherPad));

                        padGrid.RemoveBlock(pad.SlimBlock);
                        otherGrid.RemoveBlock(otherPad.SlimBlock);

                        RemoveAllWeldPads(padGrid);
                        RemoveAllWeldPads(otherGrid);

                        Log.Info($"Queued for merge {padGrid} and {otherGrid}");
                    }

                    var logic = otherPad.GameLogic.GetAs<WeldPad>();
                    logic.otherPad = null;
                    logic.master = false;

                    otherPad = null;
                    master = false;

                    SetToolStatus("WeldPad: Succesfully merged", MyFontEnum.Green, 1000);
                }
                else
                {
                    // double dist = Math.Abs(((distSq - distReq) - pad.CubeGrid.GridSize) / pad.CubeGrid.GridSize) * 100;
                    double axisDist = 100 - ((Math.Abs(axisDot - -1.0) / 2) * 100);
                    double rollDist = 100 - ((Math.Abs(rollDot - 1.0) / 2) * 100);

                    SetToolStatus($"WeldPad: Distance = {Math.Max(dist, 0).ToString("0.00")}m, axis = {axisDist.ToString("0.00")}%, roll = {rollDist.ToString("0.00")}%", MyFontEnum.Blue);

                    //MyAPIGateway.Utilities.ShowNotification("[debug] offset="+AdvancedWelding.CalculateOffset(padGrid, otherGrid, pad)+"; offset2="+AdvancedWelding.CalculateOffset(otherGrid, padGrid, otherPad), 160, MyFontEnum.Blue);
                    //MyAPIGateway.Utilities.ShowNotification("[debug] offset="+AdvancedWelding.CalculateOffset(pad, otherPad)+"; offset2="+AdvancedWelding.CalculateOffset(otherPad, pad), 160, MyFontEnum.Blue);

                    //MyAPIGateway.Utilities.ShowNotification("[debug] dist="+Math.Round(Math.Sqrt(distSq), 3), 160, MyFontEnum.Blue);
                    //MyAPIGateway.Utilities.ShowNotification("[debug] distSq="+Math.Round(distSq, 2)+"; axis="+axisDot+"; rotation="+rotationDot, 160, MyFontEnum.Blue);

                    /*
                    Vector3D dir = (otherPos - pos);

                    if(!padGrid.IsStatic)
                        padGrid.Physics.AddForce(MyPhysicsForceType.APPLY_WORLD_FORCE, dir * 5 * otherPad.Mass, pos, null);

                    if(!otherGrid.IsStatic)
                        otherGrid.Physics.AddForce(MyPhysicsForceType.APPLY_WORLD_FORCE, -dir * 5 * pad.Mass, otherPos, null);
                     */
                }
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }

        private static void RemoveAllWeldPads(IMyCubeGrid grid)
        {
            var blocks = AdvancedWelding.Instance.TmpBlocks;
            blocks.Clear();

            var gridInternal = (MyCubeGrid)grid;

            foreach(var block in gridInternal.GetFatBlocks())
            {
                if(IsWeldPad(block.BlockDefinition.Id))
                    blocks.Add(block.SlimBlock);
            }

            foreach(var block in blocks)
            {
                grid.RemoveBlock(block);
            }

            blocks.Clear();
        }

        private static bool CanMergeCubes(IMyCubeBlock pad1, IMyCubeBlock pad2, Vector3I gridOffset)
        {
            try
            {
                var grid1 = pad1.CubeGrid;
                var grid2 = pad2.CubeGrid;

                var blocksGrid2 = AdvancedWelding.Instance.TmpBlocks;
                blocksGrid2.Clear();
                grid2.GetBlocks(blocksGrid2);

                MatrixI transform = grid1.CalculateMergeTransform(grid2, gridOffset);
                bool result = true;

                // check gridToMerge's blocks against grid's blocks
                foreach(var slimGrid2 in blocksGrid2)
                {
                    if(slimGrid2.FatBlock == pad2)
                        continue;

                    // ignore all pads
                    if(IsWeldPad(slimGrid2.BlockDefinition.Id))
                        continue;

                    var pos = Vector3I.Transform(slimGrid2.Position, transform);
                    var slimGrid1 = grid1.GetCubeBlock(pos);

                    if(slimGrid1 != null)
                    {
                        if(slimGrid1.FatBlock == pad1)
                            continue;

                        // ignore all pads
                        if(IsWeldPad(slimGrid1.BlockDefinition.Id))
                            continue;

                        //MyAPIGateway.Utilities.ShowNotification($"{(master ? "master" : "slavetest")} :: {slimGrid1.BlockDefinition.ToString()} OVERLAPS {slimGrid2.BlockDefinition.ToString()}", 16);
                        //DebugDraw(slimGrid1.CubeGrid, slimGrid1.Min, slimGrid1.Max, Color.Red, master);
                        //DebugDraw(slimGrid2.CubeGrid, slimGrid2.Min, slimGrid2.Max, Color.Yellow, master);
                        //result = false;
                        //continue;

                        result = false;
                        break;
                    }

                    //DebugDraw(grid1, pos, pos, Color.Blue, master);
                }

                blocksGrid2.Clear();
                return result;
            }
            catch(Exception e)
            {
                Log.Error(e);
            }

            return false;
        }

        private static bool IsWeldPad(MyDefinitionId defId)
        {
            if(defId.TypeId == typeof(MyObjectBuilder_TerminalBlock))
            {
                switch(defId.SubtypeName)
                {
                    case "SmallWeldPad":
                    case "LargeWeldPad":
                        return true;
                }
            }

            return false;
        }

        private static MyCubeGrid GetMergeParent(MyCubeGrid grid1, MyCubeGrid grid2)
        {
            bool g1rooted = IsRooted(grid1);
            bool g2rooted = IsRooted(grid2);

            if(g1rooted && !g2rooted)
                return grid1;

            if(g2rooted && !g1rooted)
                return grid2;

            if(grid1.BlocksCount > grid2.BlocksCount)
                return grid1;

            return grid2;
        }

        private static bool IsRooted(IMyCubeGrid grid)
        {
            if(grid.IsStatic)
                return true;

            List<IMyCubeGrid> grids = AdvancedWelding.Instance.TmpGrids;
            grids.Clear();

            try
            {
                MyAPIGateway.GridGroups.GetGroup(grid, GridLinkTypeEnum.Physical, grids);

                if(grids.Count == 1)
                    return false;

                foreach(IMyCubeGrid g in grids)
                {
                    if(g.IsStatic)
                        return true;
                }

                return false;
            }
            finally
            {
                grids.Clear();
            }
        }

        public static IMyCubeGrid MergeGrids(IMyCubeBlock pad1, IMyCubeBlock pad2, bool checkOrder)
        {
            var grid1 = (MyCubeGrid)pad1.CubeGrid;
            var grid2 = (MyCubeGrid)pad2.CubeGrid;
            var offset = CalculateOffset(pad1, pad2);
            return grid1.MergeGrid_MergeBlock(grid2, offset, checkOrder);
        }

        private static Vector3I CalculateOffset(IMyCubeBlock pad1, IMyCubeBlock pad2)
        {
            Vector3 pad1local = pad1.Position; // ConstraintPositionInGridSpace(pad1) / pad1.CubeGrid.GridSize;
            Vector3 pad2local = -pad2.Position; // -ConstraintPositionInGridSpace(pad2) / pad2.CubeGrid.GridSize;

            // I dunno why it works but it seems to do in the tests I made xD
            pad1local += Base6Directions.GetVector(pad1.Orientation.TransformDirection(Base6Directions.GetOppositeDirection(DIR_FORWARD)));
            //pad2local += Base6Directions.GetVector(pad2.Orientation.TransformDirection(Base6Directions.GetOppositeDirection(DIR_FORWARD)));

            Base6Directions.Direction direction = pad1.Orientation.TransformDirection(DIR_RIGHT);

            MatrixI matrix = MatrixI.CreateRotation(
                newB: pad1.Orientation.TransformDirection(DIR_FORWARD),
                oldB: Base6Directions.GetFlippedDirection(pad2.Orientation.TransformDirection(DIR_FORWARD)),
                oldA: pad2.CubeGrid.WorldMatrix.GetClosestDirection(pad1.CubeGrid.WorldMatrix.GetDirectionVector(direction)),
                newA: direction);

            Vector3 offset;
            Vector3.Transform(ref pad2local, ref matrix, out offset);
            return Vector3I.Round(pad1local + offset);
        }

        //private static Vector3 ConstraintPositionInGridSpace(IMyCubeBlock pad)
        //{
        //    return pad.Position * pad.CubeGrid.GridSize + pad.LocalMatrix.GetDirectionVector(DIR_FORWARD) * (pad.CubeGrid.GridSize * 0.5f);
        //}

        //public static void DebugDraw(IMyCubeGrid grid, Vector3I minPosition, Vector3I maxPosition, Color color, bool master)
        //{
        //    const float BOX_MODIFIER = 1.1f;
        //    float gridSize = grid.GridSize;
        //    Vector3 v1 = minPosition * gridSize - new Vector3(gridSize / 2f * BOX_MODIFIER);
        //    Vector3 v2 = maxPosition * gridSize + new Vector3(gridSize / 2f * BOX_MODIFIER);

        //    BoundingBoxD localbox = new BoundingBoxD(v1, v2);

        //    MatrixD worldMatrix = grid.WorldMatrix;

        //    color *= 0.75f;

        //    if(master)
        //    {
        //        var lineMaterial = MyStringId.GetOrCompute("Square");
        //        MySimpleObjectDraw.DrawTransparentBox(ref worldMatrix, ref localbox, ref color, MySimpleObjectRasterizer.Wireframe, 1, 0.1f, null, lineMaterial, false, -1, BlendTypeEnum.LDR);
        //    }
        //    else
        //    {
        //        var v1w = Vector3D.Transform(v1, worldMatrix);
        //        var v2w = Vector3D.Transform(v2, worldMatrix);

        //        MyTransparentGeometry.AddLineBillboard(MyStringId.GetOrCompute("WhiteDot"), color, v1w, (v2w - v1w), 1.05f, 0.25f, blendType: BlendTypeEnum.LDR);
        //    }
        //}
    }
}