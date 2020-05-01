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
                var merge = ToMerge[i];

                if(merge.Pad1.CubeGrid == merge.Pad2.CubeGrid) // already got merged by other means
                {
                    ToMerge.RemoveAtFast(i);
                    continue;
                }

                if(++merge.WaitedTicks < WAIT_UNTIL_MERGE)
                    continue;

                Matrix matrixForEffects = merge.Pad1LocalMatrix;
                var newGrid = WeldPad.MergeGrids(merge.Pad1, merge.Pad2, true);

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

                    var effectPos = matrixForEffects.Translation;
                    var effectOrientation = Quaternion.CreateFromRotationMatrix(matrixForEffects);
                    var packet = new WeldEffectsPacket(newGrid.EntityId, effectPos, effectOrientation);

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