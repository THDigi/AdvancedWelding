using System;
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
}
