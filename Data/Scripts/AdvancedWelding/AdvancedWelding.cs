using System;
using System.Collections.Generic;
using Digi.AdvancedWelding.MP;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRageMath;
using BlendTypeEnum = VRageRender.MyBillboard.BlendTypeEnum;

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
        public readonly List<WeldPad> PadDraw = new List<WeldPad>();

        public readonly List<MergeGrids> ToMerge = new List<MergeGrids>();
        private readonly List<MergedPair> Merged = new List<MergedPair>();

        public readonly List<IMyCubeGrid> TmpGrids = new List<IMyCubeGrid>();

        public readonly MySoundPair DetachSound = new MySoundPair("PrgDeconstrPh01Start");
        public readonly MyStringId LineMaterial = MyStringId.GetOrCompute("WeaponLaser");

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
            if(!Networking.IsServer)
                return;

            if(ToMerge.Count == 0)
            {
                Merged.Clear();
                return;
            }

            for(int i = ToMerge.Count - 1; i >= 0; --i)
            {
                MergeGrids merge = ToMerge[i];

                // already got merged by other means... but doesn't actually work properly, so that's why the next one exists.
                if(merge.Pad1.CubeGrid == merge.Pad2.CubeGrid)
                {
                    ToMerge.RemoveAtFast(i);
                    //Log.Info("a pair of weldpads was already merged, skipping");
                    continue;
                }

                MergedPair pair = new MergedPair(merge.Pad1.CubeGrid, merge.Pad2.CubeGrid);
                if(Merged.Contains(pair))
                {
                    ToMerge.RemoveAtFast(i);
                    //Log.Info("a pair of weldpads was already merged (solution 2), skipping");
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

                    Merged.Add(pair);

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

        public override void Draw()
        {
            try
            {
                if(PadDraw.Count == 0)
                    return;

                const float DepthRatio = 0.05f;
                const float LineWidth = 0.02f * DepthRatio;

                Color color = Color.Red * 0.5f;
                MatrixD camMatrix = MyAPIGateway.Session.Camera.WorldMatrix;

                for(int i = (PadDraw.Count - 1); i >= 0; i--)
                {
                    WeldPad pad = PadDraw[i];

                    if(pad.BlocksInTheWay.Count == 0)
                    {
                        PadDraw.RemoveAtFast(i);
                        return;
                    }

                    if(!pad.SeeWeldPadInfo())
                        continue;

                    foreach(IMySlimBlock block in pad.BlocksInTheWay)
                    {
                        MyCubeBlockDefinition def = (MyCubeBlockDefinition)block.BlockDefinition;

                        Matrix localMatrix;
                        block.Orientation.GetMatrix(out localMatrix);
                        localMatrix.Translation = new Vector3(block.Position) * block.CubeGrid.GridSize;
                        MatrixD worldMatrix = localMatrix * block.CubeGrid.WorldMatrix;

                        Vector3 halfSize = def.Size * (block.CubeGrid.GridSize / 2);
                        BoundingBoxD localBB = new BoundingBoxD(-halfSize, halfSize);

                        // for always-on-top draw
                        Vector3D center = camMatrix.Translation + ((worldMatrix.Translation - camMatrix.Translation) * DepthRatio);
                        MatrixD.Rescale(ref worldMatrix, DepthRatio);
                        worldMatrix.Translation = center;

                        MySimpleObjectDraw.DrawTransparentBox(ref worldMatrix, ref localBB, ref color, MySimpleObjectRasterizer.Wireframe, 1, LineWidth, lineMaterial: LineMaterial, blendType: BlendTypeEnum.PostPP);
                    }
                }
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }
    }
}