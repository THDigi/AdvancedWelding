using System;
using System.Collections.Generic;
using Digi.AdvancedWelding.MP;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRageMath;

namespace Digi.AdvancedWelding
{
    [MySessionComponentDescriptor(MyUpdateOrder.NoUpdate)]
    public class AdvancedWelding : MySessionComponentBase
    {
        private const int WAIT_UNTIL_MERGE = 10;

        public static AdvancedWelding Instance;

        private bool init = false;
        private bool detachCancelNotified = false;

        public bool GrinderEquipped = false;
        public bool DetachMode = false;
        public bool Notified = false;

        public IMyHudNotification ToolStatus;

        public readonly Networking Networking = new Networking(9472);

        public readonly List<IMyCubeBlock> Pads = new List<IMyCubeBlock>();
        public readonly List<MergeGrids> ToMerge = new List<MergeGrids>();

        public readonly List<IMySlimBlock> TmpBlocks = new List<IMySlimBlock>();
        public readonly List<IMyCubeGrid> TmpGrids = new List<IMyCubeGrid>();

        public MySoundPair DETACH_SOUND = new MySoundPair("PrgDeconstrPh01Start");

        public override void LoadData()
        {
            Instance = this;
            Log.ModName = "Advanced Welding";
            Log.AutoClose = false;
        }

        public override void BeforeStart()
        {
            init = true;

            Networking.Register();

            Notified = false;
            DetachMode = false;

            if(Networking.IsPlayer)
            {
                MyAPIGateway.Utilities.MessageEntered += MessageEntered;
            }

            if(Networking.IsServer)
            {
                SetUpdateOrder(MyUpdateOrder.AfterSimulation);
                CleanupNoPhysGrids();
            }
        }

        protected override void UnloadData()
        {
            try
            {
                init = false;

                MyAPIGateway.Utilities.MessageEntered -= MessageEntered;

                Networking.Unregister();

                Pads.Clear();
                ToMerge.Clear();
            }
            catch(Exception e)
            {
                Log.Error(e);
            }

            Instance = null;
            Log.Close();
        }

        private void CleanupNoPhysGrids()
        {
            foreach(var ent in MyEntities.GetEntities())
            {
                MyCubeGrid grid = ent as MyCubeGrid;
                if(grid == null || grid.IsPreview || grid.BlocksCount > 1) // no 'grid.Physics == null' check because it IS null for this kind of grid
                    continue;

                string customName = ((IMyCubeGrid)grid).CustomName;
                if(!customName.StartsWith(DetachPacket.NAME_PREFIX))
                    continue; // only care about this mods' mistakes xD

                foreach(var block in grid.GetFatBlocks())
                {
                    if(!block.BlockDefinition.HasPhysics || !block.BlockDefinition.IsStandAlone)
                    {
                        Log.Info($"{customName} ({grid.EntityId.ToString()}) was removed because it only had 1 no-phys/no-standalone block and its grid name started with '{DetachPacket.NAME_PREFIX}'.");
                        grid.Close();
                        break;
                    }
                }
            }
        }

        public override void UpdateAfterSimulation()
        {
            try
            {
                if(!init)
                    return;

                ProcessMergeQueue();
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }

        private void ProcessMergeQueue()
        {
            if(ToMerge.Count == 0 || !Networking.IsServer)
                return;

            for(int i = ToMerge.Count - 1; i >= 0; --i)
            {
                MergeGrids merge = ToMerge[i];

                if(merge.Pad1.CubeGrid == merge.Pad2.CubeGrid) // already got merged by other means
                {
                    ToMerge.RemoveAtFast(i);
                    continue;
                }

                if(++merge.WaitedTicks < WAIT_UNTIL_MERGE)
                    continue;

                Matrix matrixForEffects = merge.Pad1LocalMatrix;
                IMyCubeGrid newGrid = WeldPad.MergeGrids(merge.Pad1, merge.Pad2, true);

                if(newGrid == null)
                {
                    newGrid = WeldPad.MergeGrids(merge.Pad2, merge.Pad1, false);
                    matrixForEffects = merge.Pad2LocalMatrix;
                }

                if(newGrid == null)
                {
                    Log.Error("Unable to merge!");
                }
                else
                {
                    Log.Info($"Merged to {newGrid}");

                    Vector3 effectPos = matrixForEffects.Translation;
                    Quaternion effectOrientation = Quaternion.CreateFromRotationMatrix(matrixForEffects);
                    WeldEffectsPacket packet = new WeldEffectsPacket(newGrid.EntityId, effectPos, effectOrientation);

                    Networking.RelayToClients(packet, true);
                }

                ToMerge.RemoveAtFast(i);
            }
        }

        private void MessageEntered(string msg, ref bool send)
        {
            if(msg.StartsWith("/detach", StringComparison.OrdinalIgnoreCase))
            {
                send = false;

                if(msg.StartsWith("/detach cancel", StringComparison.OrdinalIgnoreCase))
                {
                    if(DetachMode)
                    {
                        DetachMode = false;
                        MyAPIGateway.Utilities.ShowMessage(Log.ModName, "Block detach mode turned off.");
                    }
                }
                else
                {
                    DetachMode = true;

                    if(!GrinderEquipped)
                    {
                        AngleGrinder.SetToolStatus("Detach mode enabled, switch to your [Angle Grinder] to begin...", MyFontEnum.Blue, 5000);
                    }

                    if(!detachCancelNotified)
                    {
                        detachCancelNotified = true;
                        MyAPIGateway.Utilities.ShowMessage(Log.ModName, "To cancel this mode just holster your grinder or type /detach cancel");
                    }
                }
            }
        }
    }
}