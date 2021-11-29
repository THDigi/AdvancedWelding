using System;
using System.Collections.Generic;
using Digi.Sync;
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
        public const Base6Directions.Direction DirForward = Base6Directions.Direction.Up;
        public const Base6Directions.Direction DirRight = Base6Directions.Direction.Right;

        const int NotifyMaxDistanceSq = 20 * 20;

        IMyCubeBlock ThisPad;
        IMyCubeBlock OtherPad;
        IMyHudNotification Notification;
        WeldPadHandler Handler;

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
                ThisPad = (IMyCubeBlock)Entity;
                if(ThisPad.CubeGrid?.Physics == null)
                    return;

                Handler = AdvancedWeldingMod.Instance.WeldPadHandler;
                Handler.Pads.Add(ThisPad);

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
                if(Handler != null)
                {
                    Handler.Pads.Remove(ThisPad);
                    Handler.PadDraw.Remove(this);
                }

                OtherPad = null;
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

            if(ThisPad.GetPlayerRelationToOwner() == MyRelationsBetweenPlayerAndBlock.Enemies)
                return false;

            // doesn't quite work for ship with LG carrying a weldpad'd block
            //IMyShipController controlled = MyAPIGateway.Session.ControlledObject as IMyShipController;
            //if(controlled != null)
            //{
            //    if(!controlled.CanControlShip)
            //        return false;

            //    if(!MyAPIGateway.GridGroups.HasConnection(controlled.CubeGrid, ThisPad.CubeGrid, GridLinkTypeEnum.Mechanical))
            //        return false;

            //    if(OtherPad != null && !MyAPIGateway.GridGroups.HasConnection(controlled.CubeGrid, OtherPad.CubeGrid, GridLinkTypeEnum.Mechanical))
            //        return false;
            //}
            //else
            {
                Vector3D camPos = MyAPIGateway.Session.Camera.WorldMatrix.Translation;
                Vector3D padPos = ThisPad.GetPosition();
                if(Vector3D.DistanceSquared(camPos, padPos) > NotifyMaxDistanceSq)
                    return false;
            }

            return true;
        }

        void SetPadStatus(string text, string font, int aliveTime = 200)
        {
            try
            {
                if(!SeeWeldPadInfo())
                    return;

                if(Notification == null)
                {
                    Notification = MyAPIGateway.Utilities.CreateNotification(text, aliveTime, font);
                }
                else
                {
                    Notification.Hide(); // required since SE v1.194
                    Notification.Font = font;
                    Notification.Text = text;
                    Notification.AliveTime = aliveTime;
                }

                Notification.Show();
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

                if(Handler.Pads.Count <= 1)
                    return;

                if(!ThisPad.IsWorking)
                    return;

                if(OtherPad != null && (OtherPad.Closed || OtherPad.MarkedForClose || OtherPad.CubeGrid?.Physics == null || !OtherPad.IsWorking))
                {
                    OtherPad = null;
                    return;
                }

                bool isMaster = (OtherPad == null ? true : ThisPad.EntityId > OtherPad.EntityId);
                if(!isMaster)
                    return; // only one pad 'thinks'

                IMyCubeGrid padGrid = ThisPad.CubeGrid;

                MatrixD padMatrix = ThisPad.WorldMatrix;
                Vector3D padForward = padMatrix.GetDirectionVector(DirForward);
                Vector3D padPos = padMatrix.Translation + padForward * -(padGrid.GridSize / 2);

                //MyTransparentGeometry.AddPointBillboard(MyStringId.GetOrCompute("WhiteDot"), Color.Lime, padPos, 0.1f, 0, blendType: BlendTypeEnum.AdditiveTop);

                if(OtherPad == null)
                {
                    double minDistance = (ThisPad.CubeGrid.GridSize * ThisPad.CubeGrid.GridSize);

                    IMyCubeBlock closestPad = null;
                    WeldPad closestLogic = null;
                    double closestDistSq = double.MaxValue;

                    foreach(IMyCubeBlock p in Handler.Pads)
                    {
                        if(p.EntityId == ThisPad.EntityId
                        || p.CubeGrid == ThisPad.CubeGrid
                        || p.CubeGrid?.Physics == null
                        || p.CubeGrid.GridSizeEnum != padGrid.GridSizeEnum
                        || (padGrid.IsStatic && p.CubeGrid.IsStatic)
                        || !p.IsWorking)
                            continue;

                        Vector3D pForward = p.WorldMatrix.GetDirectionVector(DirForward);

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

                    OtherPad = closestPad;
                    closestLogic.OtherPad = ThisPad;
                }

                Vector3D otherPadPos = OtherPad.WorldMatrix.Translation + (-OtherPad.WorldMatrix.GetDirectionVector(DirForward) * (OtherPad.CubeGrid.GridSize / 2));
                double distanceSquared = Vector3D.DistanceSquared(padPos, otherPadPos);
                if(distanceSquared > OtherPad.CubeGrid.GridSize * OtherPad.CubeGrid.GridSize)
                {
                    WeldPad otherLogic = OtherPad.GameLogic.GetAs<WeldPad>();
                    otherLogic.OtherPad = null;

                    OtherPad = null;
                    return;
                }

                IMyCubeGrid otherGrid = OtherPad.CubeGrid;
                double axisDot = Math.Round(Vector3D.Dot(ThisPad.WorldMatrix.Up, OtherPad.WorldMatrix.Up), 2);
                double rollDot = Math.Round(Vector3D.Dot(ThisPad.WorldMatrix.Right, OtherPad.WorldMatrix.GetDirectionVector(OtherPad.WorldMatrix.GetClosestDirection(ThisPad.WorldMatrix.Right))), 2);

                double distReq = (padGrid.GridSizeEnum == MyCubeSize.Large ? 0.5 : 0.25);
                double dist = Math.Sqrt(distanceSquared) - distReq;

                if(dist <= 0 && axisDot == -1.0 && rollDot == 1.0)
                {
                    if(!CanMergeCubes(ThisPad, OtherPad, MergeHandler.CalculateOffset(ThisPad, OtherPad), BlocksInTheWay, DeleteWeldPads))
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

                        SetPadStatus("WeldPad: Can't merge, some blocks would overlap!", MyFontEnum.Red, 1000);

                        if(!Handler.PadDraw.Contains(this))
                            Handler.PadDraw.Add(this);

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

                        AdvancedWeldingMod.Instance.MergeHandler.ScheduleMerge(ThisPad, OtherPad);

                        padGrid.RemoveBlock(ThisPad.SlimBlock);
                        otherGrid.RemoveBlock(OtherPad.SlimBlock);

                        // remove other weldpads that get in the way
                        foreach(IMySlimBlock block in DeleteWeldPads)
                        {
                            block.CubeGrid.RemoveBlock(block);
                            Log.Info($"   removed weldpad that was in the way at {block.Position.ToString()} on {block.CubeGrid.ToString()}");
                        }
                    }

                    WeldPad otherLogic = OtherPad.GameLogic.GetAs<WeldPad>();
                    otherLogic.OtherPad = null;

                    OtherPad = null;

                    SetPadStatus("WeldPad: Succesfully merged", MyFontEnum.Green, 1000);
                }
                else
                {
                    // double dist = Math.Abs(((distSq - distReq) - pad.CubeGrid.GridSize) / pad.CubeGrid.GridSize) * 100;
                    double axisDist = 100 - ((Math.Abs(axisDot - -1.0) / 2) * 100);
                    double rollDist = 100 - ((Math.Abs(rollDot - 1.0) / 2) * 100);

                    SetPadStatus($"WeldPad: Distance = {Math.Max(dist, 0).ToString("0.00")}m, axis = {axisDist.ToString("0.00")}%, roll = {rollDist.ToString("0.00")}%", MyFontEnum.Blue);

                    //MyAPIGateway.Utilities.ShowNotification("[debug] offset="+AdvancedWelding.CalculateOffset(padGrid, otherGrid, pad)+"; offset2="+AdvancedWelding.CalculateOffset(otherGrid, padGrid, otherPad), 160, MyFontEnum.Blue);
                    //MyAPIGateway.Utilities.ShowNotification("[debug] offset="+AdvancedWelding.CalculateOffset(pad, otherPad)+"; offset2="+AdvancedWelding.CalculateOffset(otherPad, pad), 160, MyFontEnum.Blue);

                    //MyAPIGateway.Utilities.ShowNotification("[debug] dist="+Math.Round(Math.Sqrt(distSq), 3), 160, MyFontEnum.Blue);
                    //MyAPIGateway.Utilities.ShowNotification("[debug] distSq="+Math.Round(distSq, 2)+"; axis="+axisDot+"; rotation="+rotationDot, 160, MyFontEnum.Blue);

                    // magnetism...
                    //Vector3D dir = (otherPos - pos);

                    //if(!padGrid.IsStatic)
                    //    padGrid.Physics.AddForce(MyPhysicsForceType.APPLY_WORLD_FORCE, dir * 5 * otherPad.Mass, pos, null);

                    //if(!otherGrid.IsStatic)
                    //    otherGrid.Physics.AddForce(MyPhysicsForceType.APPLY_WORLD_FORCE, -dir * 5 * pad.Mass, otherPos, null);
                }
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }

        static bool CanMergeCubes(IMyCubeBlock padA, IMyCubeBlock padB, Vector3I gridOffset, List<IMySlimBlock> blocksOverlapping, List<IMySlimBlock> weldPadsOverlapping)
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
                foreach(IMySlimBlock slimGridB in internalGridB.GetBlocks()) // get blocks without intermediary lists
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

                        bool isWeldPadA = (slimGridA?.FatBlock?.GameLogic?.GetAs<WeldPad>() != null);
                        bool isWeldPadB = (slimGridB?.FatBlock?.GameLogic?.GetAs<WeldPad>() != null);

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
    }
}