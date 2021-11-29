using System.Collections.Generic;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRageMath;
using BlendTypeEnum = VRageRender.MyBillboard.BlendTypeEnum;

namespace Digi.AdvancedWelding
{
    // Server and client side
    public class WeldPadHandler : ComponentBase
    {
        readonly MyStringId LineMaterial = MyStringId.GetOrCompute("WeaponLaser");

        public readonly List<IMyCubeBlock> Pads = new List<IMyCubeBlock>();
        public readonly List<WeldPad> PadDraw = new List<WeldPad>();

        public WeldPadHandler(AdvancedWeldingMod main) : base(main)
        {
        }

        public override void Register()
        {
        }

        public override void Dispose()
        {
            Pads.Clear();
        }

        public void Draw()
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
    }
}