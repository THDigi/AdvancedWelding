using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using Sandbox.Common;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.Engine;
using Sandbox.Engine.Physics;
using Sandbox.Engine.Multiplayer;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces;
using VRage.Common.Utils;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRageMath;
using VRage;
using VRage.ModAPI;
using VRage.Utils;
using Digi.Utils;

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
        public static bool init { get; private set; }
        
        public const string MOD_NAME = "AdvancedWelding";
        
        private static bool detachCancelNotified = false;
        public static List<IMyTerminalBlock> pads = new List<IMyTerminalBlock>();
        public static List<MergeGrids> mergeQueue = new List<MergeGrids>();
        
        public void Init()
        {
            Log.Init();
            Log.Info("Initialized.");
            init = true;
            AngleGrinder.notified = false;
            AngleGrinder.detach = false;
            
            if(MyAPIGateway.Multiplayer.IsServer && MyAPIGateway.Utilities.IsDedicated)
                return;
            
            MyAPIGateway.Utilities.MessageEntered += MessageEntered;
        }
        
        protected override void UnloadData()
        {
            try
            {
                init = false;
                pads.Clear();
                mergeQueue.Clear();
                
                MyAPIGateway.Utilities.MessageEntered -= MessageEntered;
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
            
            Log.Info("Mod unloaded");
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
                        {
                            newGrid = MergeGrids(merge.pad2, merge.pad1);
                        }
                        
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
                        MyAPIGateway.Utilities.ShowMessage(MOD_NAME, "Block detach mode turned off.");
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
                        MyAPIGateway.Utilities.ShowMessage(MOD_NAME, "To cancel this mode just holster your grinder or type /detach cancel.");
                    }
                }
            }
        }
    }
}