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

        public readonly List<IMySlimBlock> BlocksInTheWay = new List<IMySlimBlock>(0);
        public readonly List<IMySlimBlock> DeleteWeldPads = new List<IMySlimBlock>(0);

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
                AdvancedWelding.Instance.PadDraw.Remove(this);
                otherPad = null;
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }

        public bool SeeWeldPadInfo()
        {
            if(!Networking.IsPlayer)
                return false;

            if(pad.GetPlayerRelationToOwner() == MyRelationsBetweenPlayerAndBlock.Enemies)
                return false;

            IMyShipController controlled = MyAPIGateway.Session.ControlledObject as IMyShipController;
            if(controlled != null)
            {
                if(!controlled.CanControlShip)
                    return false;

                if(!controlled.CubeGrid.IsSameConstructAs(pad.CubeGrid) && (otherPad == null || !otherPad.CubeGrid.IsSameConstructAs(pad.CubeGrid)))
                    return false;
            }
            else
            {
                Vector3D camPos = MyAPIGateway.Session.Camera.WorldMatrix.Translation;
                Vector3D padPos = pad.GetPosition();
                if(Vector3D.DistanceSquared(camPos, padPos) > NOTIFY_DISTANCE_SQ)
                    return false;
            }

            return true;
        }

        public void SetToolStatus(string text, string font, int aliveTime = TOOLSTATUS_TIMEOUT)
        {
            try
            {
                if(!SeeWeldPadInfo())
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
                BlocksInTheWay.Clear();
                DeleteWeldPads.Clear();

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

                IMyCubeGrid padGrid = pad.CubeGrid;

                MatrixD padMatrix = pad.WorldMatrix;
                Vector3D padForward = padMatrix.GetDirectionVector(DIR_FORWARD);
                Vector3D padPos = padMatrix.Translation + padForward * -(padGrid.GridSize / 2);

                //MyTransparentGeometry.AddPointBillboard(MyStringId.GetOrCompute("WhiteDot"), Color.Lime, padPos, 0.1f, 0, blendType: BlendTypeEnum.AdditiveTop);

                if(otherPad == null)
                {
                    double minDistance = (pad.CubeGrid.GridSize * pad.CubeGrid.GridSize);

                    IMyCubeBlock closestPad = null;
                    WeldPad closestLogic = null;
                    double closestDistSq = double.MaxValue;

                    foreach(IMyCubeBlock p in AdvancedWelding.Instance.Pads)
                    {
                        if(p.EntityId == pad.EntityId
                        || p.CubeGrid == pad.CubeGrid
                        || p.CubeGrid?.Physics == null
                        || p.CubeGrid.GridSizeEnum != padGrid.GridSizeEnum
                        || (padGrid.IsStatic && p.CubeGrid.IsStatic)
                        || !p.IsWorking)
                            continue;

                        Vector3D pForward = p.WorldMatrix.GetDirectionVector(DIR_FORWARD);

                        // not pointed at eachother, skip
                        if(Vector3D.Dot(pForward, padForward) > -0.5)
                            continue;

                        WeldPad otherLogic = p.GameLogic.GetAs<WeldPad>();
                        if(otherLogic == null)
                            continue;

                        Vector3D otherPos = p.WorldMatrix.Translation + (pForward * -(p.CubeGrid.GridSize / 2));
                        double distSq = Vector3D.DistanceSquared(padPos, otherPos);

                        //MyTransparentGeometry.AddPointBillboard(MyStringId.GetOrCompute("WhiteDot"), Color.Red, otherPos, 0.1f, 0, blendType: BlendTypeEnum.AdditiveTop);

                        if(distSq < closestDistSq && distSq <= minDistance)
                        {
                            closestDistSq = distSq;
                            closestPad = p;
                            closestLogic = otherLogic;
                        }
                    }

                    if(closestPad == null)
                        return;

                    otherPad = closestPad;
                    closestLogic.otherPad = pad;
                    closestLogic.master = false;
                    master = false;

                    MyCubeGrid padGridInternal = (MyCubeGrid)padGrid;
                    MyCubeGrid otherGridInternal = (MyCubeGrid)otherPad.CubeGrid;
                    MyCubeGrid parentGrid = GetMergeParent(padGridInternal, otherGridInternal);

                    if(parentGrid == padGridInternal)
                        master = true;
                    else
                        closestLogic.master = true;
                }

                Vector3D otherPadPos = otherPad.WorldMatrix.Translation + (-otherPad.WorldMatrix.GetDirectionVector(DIR_FORWARD) * (otherPad.CubeGrid.GridSize / 2));
                double distanceSquared = Vector3D.DistanceSquared(padPos, otherPadPos);
                if(distanceSquared > otherPad.CubeGrid.GridSize * otherPad.CubeGrid.GridSize)
                {
                    WeldPad otherLogic = otherPad.GameLogic.GetAs<WeldPad>();
                    otherLogic.otherPad = null;
                    otherLogic.master = false;

                    otherPad = null;
                    master = false;
                    return;
                }

                IMyCubeGrid otherGrid = otherPad.CubeGrid;
                double axisDot = Math.Round(Vector3D.Dot(pad.WorldMatrix.Up, otherPad.WorldMatrix.Up), 2);
                double rollDot = Math.Round(Vector3D.Dot(pad.WorldMatrix.Right, otherPad.WorldMatrix.GetDirectionVector(otherPad.WorldMatrix.GetClosestDirection(pad.WorldMatrix.Right))), 2);
                double distReq = (padGrid.GridSizeEnum == MyCubeSize.Large ? 0.5 : 0.25);
                double dist = Math.Sqrt(distanceSquared) - distReq;

                if(dist <= 0 && axisDot == -1.0 && rollDot == 1.0)
                {
                    if(!CanMergeCubes(pad, otherPad, CalculateOffset(pad, otherPad), BlocksInTheWay, DeleteWeldPads))
                    {
                        //bool otherWeldPadsInTheWay = false;
                        //foreach(IMySlimBlock block in DrawInTheWay)
                        //{
                        //    if(IsWeldPad(block.BlockDefinition.Id))
                        //    {
                        //        otherWeldPadsInTheWay = true;
                        //        break;
                        //    }
                        //}

                        //if(otherWeldPadsInTheWay)
                        //    SetToolStatus("WeldPad: Can't merge, other weld pads in the way, use only one pair!", MyFontEnum.Red, 1000);
                        //else

                        SetToolStatus("WeldPad: Can't merge, some blocks would overlap!", MyFontEnum.Red, 1000);

                        if(!AdvancedWelding.Instance.PadDraw.Contains(this))
                            AdvancedWelding.Instance.PadDraw.Add(this);

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
                        Log.Info($"Queued for merge {padGrid} and {otherGrid}");

                        AdvancedWelding.Instance.ToMerge.Add(new MergeGrids(pad, otherPad));

                        padGrid.RemoveBlock(pad.SlimBlock);
                        otherGrid.RemoveBlock(otherPad.SlimBlock);

                        // remove other weldpads that get in the way
                        foreach(IMySlimBlock block in DeleteWeldPads)
                        {
                            block.CubeGrid.RemoveBlock(block);
                            Log.Info($"   removed weldpad that was in the way at {block.Position.ToString()} on {block.CubeGrid.ToString()}");
                        }
                    }

                    WeldPad otherLogic = otherPad.GameLogic.GetAs<WeldPad>();
                    otherLogic.otherPad = null;
                    otherLogic.master = false;

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

        private static bool CanMergeCubes(IMyCubeBlock padA, IMyCubeBlock padB, Vector3I gridOffset, List<IMySlimBlock> blocksOverlapping, List<IMySlimBlock> weldPadsOverlapping)
        {
            blocksOverlapping.Clear();
            try
            {
                IMyCubeGrid gridA = padA.CubeGrid;
                IMyCubeGrid gridB = padB.CubeGrid;
                MyCubeGrid internalGridB = (MyCubeGrid)gridB;

                MatrixI transform = gridA.CalculateMergeTransform(gridB, gridOffset);

                bool result = true;

                // check gridToMerge's blocks against grid's blocks
                foreach(IMySlimBlock slimGridB in internalGridB.GetBlocks()) // optimized way to get blocks without intermediary lists
                {
                    if(slimGridB.FatBlock == padB)
                        continue;

                    // ignore all pads
                    //if(IsWeldPad(slimGridB.BlockDefinition.Id))
                    //    continue;

                    Vector3I pos = Vector3I.Transform(slimGridB.Position, transform);
                    IMySlimBlock slimGridA = gridA.GetCubeBlock(pos);

                    if(slimGridA != null)
                    {
                        if(slimGridA.FatBlock == padA)
                            continue;

                        // ignore all pads
                        //if(IsWeldPad(slimGridA.BlockDefinition.Id))
                        //    continue;

                        //MyAPIGateway.Utilities.ShowNotification($"{(master ? "master" : "slavetest")} :: {slimGrid1.BlockDefinition.ToString()} OVERLAPS {slimGrid2.BlockDefinition.ToString()}", 16);
                        //DebugDraw(slimGrid1.CubeGrid, slimGrid1.Min, slimGrid1.Max, Color.Red, master);
                        //DebugDraw(slimGrid2.CubeGrid, slimGrid2.Min, slimGrid2.Max, Color.Yellow, master);
                        //result = false;
                        //continue;

                        bool isWeldPadA = IsWeldPad(slimGridA.BlockDefinition.Id);
                        bool isWeldPadB = IsWeldPad(slimGridB.BlockDefinition.Id);

                        if(isWeldPadA || isWeldPadB)
                        {
                            if(isWeldPadA)
                                weldPadsOverlapping.Add(slimGridA);

                            if(isWeldPadB)
                                weldPadsOverlapping.Add(slimGridB);
                        }
                        else
                        {
                            blocksOverlapping.Add(slimGridA);
                            blocksOverlapping.Add(slimGridB);
                            result = false;
                        }

                        // allow more blocks to be found
                    }

                    //DebugDraw(grid1, pos, pos, Color.Blue, master);
                }

                return result;
            }
            catch(Exception e)
            {
                Log.Error(e);
            }

            return false;
        }

        public static bool IsWeldPad(MyDefinitionId defId)
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