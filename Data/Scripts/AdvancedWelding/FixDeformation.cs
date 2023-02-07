using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;

namespace Digi.AdvancedWelding
{
    public class FixDeformation : ComponentBase
    {
        public FixDeformation(AdvancedWeldingMod main) : base(main)
        {
        }

        public override void Register()
        {
            DoFix();
        }

        public override void Dispose()
        {
        }

        void DoFix()
        {
            const string Key = "AdvWeld-FixDeform";

            bool did;
            if(MyAPIGateway.Utilities.GetVariable<bool>(Key, out did) && did)
                return; // already fixed in this world, moving on

            Log.Info($"[FixDeformation] Checking for detached grids...");

            // fix deformation on all non-armor detached blocks only once per world
            foreach(MyEntity ent in MyEntities.GetEntities())
            {
                MyCubeGrid grid = ent as MyCubeGrid;
                if(grid != null && grid.BlocksCount <= 2 && grid.DisplayName != null && grid.DisplayName.StartsWith("(Detached "))
                {
                    bool didFix = false;

                    foreach(IMySlimBlock block in grid.GetBlocks())
                    {
                        //if(!block.HasDeformation)
                        //    continue;

                        var def = (MyCubeBlockDefinition)block.BlockDefinition;
                        if(def.BlockTopology == MyBlockTopology.TriangleMesh) // non-deformable
                        {
                            grid.ResetBlockSkeleton(grid.GetCubeBlock(block.Min));
                            didFix = true;
                        }
                    }

                    if(didFix)
                    {
                        Log.Info($"[FixDeformation] Reset bones on '{grid.DisplayName}' for its single non-deformable block.");
                    }
                }
            }

            MyAPIGateway.Utilities.SetVariable<bool>(Key, true);

            Log.Info($"[FixDeformation] Done, tagged world so that this process never executes again.");
        }
    }
}