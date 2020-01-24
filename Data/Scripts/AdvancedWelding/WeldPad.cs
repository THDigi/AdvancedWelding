using System;
using Digi.AdvancedWelding.MP;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRageMath;

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

                            if(logic.master == false)
                                master = true;

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

                var grid2blocks = AdvancedWelding.Instance.TmpBlocks;
                grid2.GetBlocks(grid2blocks);

                MatrixI transform = grid1.CalculateMergeTransform(grid2, gridOffset);
                bool result = true;

                // check gridToMerge's blocks against grid's blocks
                foreach(var grid2slim in grid2blocks)
                {
                    if(grid2slim.FatBlock == pad2)
                        continue;

                    //switch(grid2slim.BlockDefinition.Id.SubtypeName)
                    //{
                    //    case "SmallWeldPad":
                    //    case "LargeWeldPad":
                    //        continue; // ignore weld pads' existence
                    //}

                    var pos = Vector3I.Transform(grid2slim.Position, transform);
                    var grid1slim = grid1.GetCubeBlock(pos);

                    if(grid1slim != null)
                    {
                        if(grid1slim.FatBlock == pad1)
                            continue;

                        //switch(grid1slim.BlockDefinition.Id.SubtypeName)
                        //{
                        //    case "SmallWeldPad":
                        //    case "LargeWeldPad":
                        //        continue; // ignore weld pads' existence
                        //}

                        result = false;
                        break;
                    }
                }

                grid2blocks.Clear();
                return result;
            }
            catch(Exception e)
            {
                Log.Error(e);
            }

            return false;
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
    }
}