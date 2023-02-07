using System;
using System.Collections.Generic;
using Sandbox.Game.Entities;
using VRage.Game.ModAPI;
using VRageMath;

namespace Digi.AdvancedWelding
{
    public class MergeGrids
    {
        public readonly IMyCubeBlock Pad1;
        public readonly IMyCubeBlock Pad2;

        public readonly Matrix Pad1LocalMatrix;
        public readonly Matrix Pad2LocalMatrix;

        public int WaitedTicks = 0;

        public MergeGrids(IMyCubeBlock pad1, IMyCubeBlock pad2)
        {
            Pad1 = pad1;
            Pad2 = pad2;
            Pad1LocalMatrix = pad1.LocalMatrix;
            Pad2LocalMatrix = pad2.LocalMatrix;
        }
    }

    public struct MergedPair : IEquatable<MergedPair>
    {
        public readonly IMyCubeGrid GridA;
        public readonly IMyCubeGrid GridB;

        public MergedPair(IMyCubeGrid gridA, IMyCubeGrid gridB)
        {
            GridA = gridA;
            GridB = gridB;
        }

        public bool Equals(MergedPair other)
        {
            return (GridA == other.GridA || GridA == other.GridB) && (GridB == other.GridA || GridB == other.GridB);
        }
    }

    // Server and client side
    public class MergeHandler : ComponentBase, IUpdatable
    {
        const int WaitTicksUntilMerge = 10;

        readonly List<MergeGrids> ScheduledMerge = new List<MergeGrids>();
        readonly List<MergedPair> AlreadyMerged = new List<MergedPair>();

        public MergeHandler(AdvancedWeldingMod main) : base(main)
        {
        }

        public override void Register()
        {
        }

        public override void Dispose()
        {
        }

        public void ScheduleMerge(IMyCubeBlock padA, IMyCubeBlock padB)
        {
            ScheduledMerge.Add(new MergeGrids(padA, padB));
            SetUpdate(this, true);
        }

        void IUpdatable.Update()
        {
            if(ScheduledMerge.Count == 0)
            {
                AlreadyMerged.Clear();
                SetUpdate(this, false);
                return;
            }

            for(int i = ScheduledMerge.Count - 1; i >= 0; --i)
            {
                MergeGrids merge = ScheduledMerge[i];

                // already got merged by other means... but doesn't actually work properly, so that's why the next one exists.
                if(merge.Pad1.CubeGrid == merge.Pad2.CubeGrid)
                {
                    ScheduledMerge.RemoveAtFast(i);
                    //Log.Info("a pair of weldpads was already merged, skipping");
                    continue;
                }

                MergedPair pair = new MergedPair(merge.Pad1.CubeGrid, merge.Pad2.CubeGrid);
                if(AlreadyMerged.Contains(pair))
                {
                    ScheduledMerge.RemoveAtFast(i);
                    //Log.Info("a pair of weldpads was already merged (solution 2), skipping");
                    continue;
                }

                if(++merge.WaitedTicks < WaitTicksUntilMerge)
                    continue;

                Matrix matrixForEffects = merge.Pad1LocalMatrix;
                IMyCubeGrid newGrid = MergeGrids(merge.Pad1, merge.Pad2, true);
                if(newGrid == null)
                {
                    newGrid = MergeGrids(merge.Pad2, merge.Pad1, false);
                    matrixForEffects = merge.Pad2LocalMatrix;
                }

                if(newGrid == null)
                {
                    Log.Error($"Unable to merge {merge.Pad1} ({merge.Pad1.EntityId.ToString()}) with {merge.Pad2} ({merge.Pad2.EntityId.ToString()})");
                }
                else
                {
                    Log.Info($"Merged to {newGrid} ({newGrid.EntityId.ToString()})");

                    AlreadyMerged.Add(pair);

                    Vector3 effectLocalPos = matrixForEffects.Translation;
                    Quaternion effectLocalOrientation = Quaternion.CreateFromRotationMatrix(matrixForEffects);
                    WeldEffectsPacket packet = new WeldEffectsPacket(newGrid.EntityId, effectLocalPos, effectLocalOrientation);
                    Main.Networking.SendToServer(packet);
                }

                ScheduledMerge.RemoveAtFast(i);
            }
        }

        static IMyCubeGrid MergeGrids(IMyCubeBlock pad1, IMyCubeBlock pad2, bool checkOrder)
        {
            MyCubeGrid grid1 = (MyCubeGrid)pad1.CubeGrid;
            MyCubeGrid grid2 = (MyCubeGrid)pad2.CubeGrid;
            Vector3I offset = CalculateOffset(pad1, pad2);
            return grid1.MergeGrid_MergeBlock(grid2, offset, checkOrder);
        }

        public static Vector3I CalculateOffset(IMyCubeBlock pad1, IMyCubeBlock pad2)
        {
            Vector3 pad1local = pad1.Position; // ConstraintPositionInGridSpace(pad1) / pad1.CubeGrid.GridSize;
            Vector3 pad2local = -pad2.Position; // -ConstraintPositionInGridSpace(pad2) / pad2.CubeGrid.GridSize;

            // I dunno why it works but it seems to do in the tests I made xD
            pad1local += Base6Directions.GetVector(pad1.Orientation.TransformDirection(Base6Directions.GetOppositeDirection(WeldPad.DirForward)));
            //pad2local += Base6Directions.GetVector(pad2.Orientation.TransformDirection(Base6Directions.GetOppositeDirection(WeldPad.DIR_FORWARD)));

            Base6Directions.Direction direction = pad1.Orientation.TransformDirection(WeldPad.DirRight);

            MatrixI matrix = MatrixI.CreateRotation(
                newB: pad1.Orientation.TransformDirection(WeldPad.DirForward),
                oldB: Base6Directions.GetFlippedDirection(pad2.Orientation.TransformDirection(WeldPad.DirForward)),
                oldA: pad2.CubeGrid.WorldMatrix.GetClosestDirection(pad1.CubeGrid.WorldMatrix.GetDirectionVector(direction)),
                newA: direction);

            Vector3 offset;
            Vector3.Transform(ref pad2local, ref matrix, out offset);
            return Vector3I.Round(pad1local + offset);
        }

        //static Vector3 ConstraintPositionInGridSpace(IMyCubeBlock pad)
        //{
        //    return pad.Position * pad.CubeGrid.GridSize + pad.LocalMatrix.GetDirectionVector(DIR_FORWARD) * (pad.CubeGrid.GridSize * 0.5f);
        //}

        //static void DebugDraw(IMyCubeGrid grid, Vector3I minPosition, Vector3I maxPosition, Color color, bool master)
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

        //public static MyCubeGrid GetMergeParent(MyCubeGrid grid1, MyCubeGrid grid2)
        //{
        //    bool g1rooted = IsRooted(grid1);
        //    bool g2rooted = IsRooted(grid2);

        //    if(g1rooted && !g2rooted)
        //        return grid1;

        //    if(g2rooted && !g1rooted)
        //        return grid2;

        //    if(grid1.BlocksCount > grid2.BlocksCount)
        //        return grid1;

        //    return grid2;
        //}

        //readonly List<IMyCubeGrid> TmpGrids = new List<IMyCubeGrid>();
        //static bool IsRooted(IMyCubeGrid grid)
        //{
        //    if(grid.IsStatic)
        //        return true;

        //    List<IMyCubeGrid> grids = AdvancedWeldingMod.Instance.MergeHandler.TmpGrids;
        //    grids.Clear();

        //    try
        //    {
        //        MyAPIGateway.GridGroups.GetGroup(grid, GridLinkTypeEnum.Physical, grids);

        //        if(grids.Count == 1)
        //            return false;

        //        foreach(IMyCubeGrid g in grids)
        //        {
        //            if(g.IsStatic)
        //                return true;
        //        }

        //        return false;
        //    }
        //    finally
        //    {
        //        grids.Clear();
        //    }
        //}
    }
}
