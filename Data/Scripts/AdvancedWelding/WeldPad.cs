using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;
using VRageMath;
using VRage.ObjectBuilders;
using VRage.Game.Components;
using VRage.ModAPI;

namespace Digi.AdvancedWelding
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_TerminalBlock), "SmallWeldPad", "LargeWeldPad")]
    public class WeldPad : MyGameLogicComponent
    {
        private MyObjectBuilder_EntityBase objectBuilder;

        public static Base6Directions.Direction dirForward = Base6Directions.Direction.Up;
        public static Base6Directions.Direction dirRight = Base6Directions.Direction.Right;

        private bool master = false;
        private IMyTerminalBlock otherPad = null;
        private IMyHudNotification toolStatus;

        private const int TOOLSTATUS_TIMEOUT = 200;
        private const int NOTIFY_DISTANCE_SQ = 10 * 10;

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            this.objectBuilder = objectBuilder;

            Entity.NeedsUpdate |= MyEntityUpdateEnum.BEFORE_NEXT_FRAME | MyEntityUpdateEnum.EACH_10TH_FRAME;
        }

        public override void UpdateOnceBeforeFrame()
        {
            try
            {
                AdvancedWelding.pads.Add(Entity as IMyTerminalBlock);
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }

#if STABLE // HACK >>> STABLE condition
        public void SetToolStatus(string text, MyFontEnum font, int aliveTime = TOOLSTATUS_TIMEOUT)
#else
        public void SetToolStatus(string text, string font, int aliveTime = TOOLSTATUS_TIMEOUT)
#endif
        {
            try
            {
                if(!AdvancedWelding.init || (MyAPIGateway.Utilities.IsDedicated && MyAPIGateway.Multiplayer.IsServer))
                    return;

                var pad = Entity as IMyTerminalBlock;

                if(pad.GetPlayerRelationToOwner() == MyRelationsBetweenPlayerAndBlock.Enemies)
                    return;

                var playerPos = MyAPIGateway.Session.Player.GetPosition();
                var padPos = pad.GetPosition();

                if(Vector3D.DistanceSquared(playerPos, padPos) > NOTIFY_DISTANCE_SQ)
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
                if(!AdvancedWelding.init || AdvancedWelding.pads.Count <= 1)
                    return;

                var pad = Entity as IMyTerminalBlock;

                if(!pad.IsWorking)
                    return;

                if(otherPad != null && (otherPad.Closed || otherPad.MarkedForClose))
                {
                    otherPad = null;
                    master = false;
                    return;
                }

                if(otherPad != null && !master)
                    return; // only one pad 'thinks'

                IMyCubeGrid padGrid = pad.CubeGrid as IMyCubeGrid;
                Vector3D pos = pad.WorldMatrix.Translation + pad.WorldMatrix.Down * (padGrid.GridSize / 2);
                Vector3D otherPos = Vector3D.Zero;
                double distSq = 0;

                if(otherPad == null)
                {
                    foreach(var p in AdvancedWelding.pads)
                    {
                        if(!p.IsWorking)
                            continue;

                        if(p.EntityId == pad.EntityId || p.CubeGrid.GridSizeEnum != padGrid.GridSizeEnum || (padGrid.IsStatic && p.CubeGrid.IsStatic))
                            continue;

                        otherPos = p.WorldMatrix.Translation + (-p.WorldMatrix.GetDirectionVector(dirForward) * (p.CubeGrid.GridSize / 2));
                        distSq = Vector3D.DistanceSquared(pos, otherPos);

                        if(distSq <= p.CubeGrid.GridSize * p.CubeGrid.GridSize)
                        {
                            otherPad = p;

                            var logic = otherPad.GameLogic as WeldPad;
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
                    otherPos = otherPad.WorldMatrix.Translation + (-otherPad.WorldMatrix.GetDirectionVector(dirForward) * (otherPad.CubeGrid.GridSize / 2));
                    distSq = Vector3D.DistanceSquared(pos, otherPos);

                    if(distSq > otherPad.CubeGrid.GridSize * otherPad.CubeGrid.GridSize)
                    {
                        var logic = otherPad.GameLogic as WeldPad;
                        logic.otherPad = null;
                        logic.master = false;

                        otherPad = null;
                        master = false;
                        return;
                    }
                }

                var otherGrid = otherPad.CubeGrid as IMyCubeGrid;
                var axisDot = Math.Round(Vector3D.Dot(pad.WorldMatrix.Up, otherPad.WorldMatrix.Up), 2);
                var rollDot = Math.Round(Vector3D.Dot(pad.WorldMatrix.Right, otherPad.WorldMatrix.GetDirectionVector(otherPad.WorldMatrix.GetClosestDirection(pad.WorldMatrix.Right))), 2);
                var distReq = (padGrid.GridSizeEnum == MyCubeSize.Large ? 0.5 : 0.25);
                var dist = Math.Sqrt(distSq) - distReq;

                if(dist <= 0 && axisDot == -1.0 && rollDot == 1.0)
                {
                    if(!CanMergeCubes(padGrid, otherGrid, AdvancedWelding.CalculateOffset(pad, otherPad), pad, otherPad))
                    {
                        SetToolStatus("WeldPad: Can't merge this way as blocks would overlap!", MyFontEnum.Red, 1000);
                        return;
                    }

                    if(!MyAPIGateway.Multiplayer.IsServer)
                    {
                        Log.Info("Merge checks out, waiting for server...");
                    }
                    else
                    {
                        padGrid.RemoveBlock(padGrid.GetCubeBlock(pad.Position));
                        otherGrid.RemoveBlock(otherGrid.GetCubeBlock(otherPad.Position));

                        AdvancedWelding.mergeQueue.Add(new MergeGrids()
                        {
                            pad1 = pad,
                            pad2 = otherPad,
                        });

                        Log.Info("Queued for merge " + padGrid + " and " + otherGrid);
                    }

                    var logic = otherPad.GameLogic as WeldPad;
                    logic.otherPad = null;
                    logic.master = false;

                    otherPad = null;
                    master = false;

                    SetToolStatus("WeldPad: Succesfully merged", MyFontEnum.Green, 1000);
                }
                else
                {
                    // double dist = Math.Abs(((distSq - distReq) - pad.CubeGrid.GridSize) / pad.CubeGrid.GridSize) * 100;
                    int axisDist = 100 - (int)((Math.Abs(axisDot - -1.0) / 2) * 100);
                    int rollDist = 100 - (int)((Math.Abs(rollDot - 1.0) / 2) * 100);

                    SetToolStatus("WeldPad: Distance = " + Math.Round(dist, 2) + "m, axis = " + axisDist + "%, roll = " + rollDist + "%", MyFontEnum.Blue);

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

        public bool CanMergeCubes(IMyCubeGrid grid, IMyCubeGrid gridToMerge, Vector3I gridOffset, IMyTerminalBlock pad, IMyTerminalBlock otherPad)
        {
            try
            {
                MatrixI transform = grid.CalculateMergeTransform(gridToMerge, gridOffset);
                List<IMySlimBlock> blocks = new List<IMySlimBlock>();
                gridToMerge.GetBlocks(blocks, delegate (IMySlimBlock b)
                                      {
                                          if(b.FatBlock is IMyTerminalBlock)
                                          {
                                              switch(b.FatBlock.BlockDefinition.SubtypeId)
                                              {
                                                  case "SmallWeldPad":
                                                  case "LargeWeldPad":
                                                      return false; // skip the weld pads as they will get removed anyway
                                              }
                                          }

                                          return true;
                                      });

                foreach(var block in blocks)
                {
                    Vector3I position = Vector3I.Transform(block.Position, transform);

                    if(grid.CubeExists(position))
                    {
                        var cube = grid.GetCubeBlock(position);

                        if(cube.FatBlock is IMyTerminalBlock)
                        {
                            bool skip = false;

                            switch(cube.FatBlock.BlockDefinition.SubtypeId)
                            {
                                case "SmallWeldPad":
                                case "LargeWeldPad":
                                    skip = true;
                                    break;
                            }

                            if(skip)
                                continue;
                        }

                        // TODO check compound blocks when they're a thing, same as MyCubeGrid.CanMergeCubes() does

                        return false;
                    }
                }

                return true;
            }
            catch(Exception e)
            {
                Log.Error(e);
            }

            return false;
        }

        public override void Close()
        {
            try
            {
                otherPad = null;
                objectBuilder = null;
                AdvancedWelding.pads.Remove(Entity as IMyTerminalBlock);
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }

        public override MyObjectBuilder_EntityBase GetObjectBuilder(bool copy = false)
        {
            return copy ? (MyObjectBuilder_EntityBase)objectBuilder.Clone() : objectBuilder;
        }
    }
}