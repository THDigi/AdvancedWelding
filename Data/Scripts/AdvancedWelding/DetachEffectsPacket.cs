using Digi.Sync;
using ProtoBuf;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ObjectBuilders;
using VRageMath;

namespace Digi.AdvancedWelding
{
    [ProtoContract]
    public class DetachEffectsPacket : PacketBase
    {
        const float MaxDistanceSq = 500 * 500;

        public DetachEffectsPacket() { } // Empty constructor required for deserialization

        [ProtoMember(1)]
        Vector3D Position;

        [ProtoMember(2)]
        QuaternionD Orientation;

        [ProtoMember(3)]
        Vector3 Velocity;

        [ProtoMember(10)]
        BoundingBox? ModelBB;

        [ProtoMember(20)]
        SerializableDefinitionId BlockDefId;

        public DetachEffectsPacket(IMySlimBlock block)
        {
            Matrix localMatrix;
            block.Orientation.GetMatrix(out localMatrix);
            block.ComputeWorldCenter(out Position);

            MatrixD wm = localMatrix * block.CubeGrid.WorldMatrix;
            Orientation = QuaternionD.CreateFromRotationMatrix(wm);

            Velocity = block.CubeGrid.Physics.LinearVelocity;

            ModelBB = block.FatBlock?.LocalAABB;

            BlockDefId = block.BlockDefinition.Id;
        }

        public override void Received(ref RelayMode relay, ulong senderSteamId)
        {
            relay = RelayMode.RelayOriginal;

            if(!Networking.IsPlayer)
                return;

            Vector3D camPos = MyAPIGateway.Session.Camera.WorldMatrix.Translation;
            if(Vector3D.DistanceSquared(camPos, Position) > MaxDistanceSq)
                return;

            MatrixD worldMatrix = MatrixD.CreateFromQuaternion(Orientation);
            worldMatrix.Translation = Position;

            MyEntity3DSoundEmitter emitter = new MyEntity3DSoundEmitter(null);
            emitter.CustomVolume = 0.9f;
            emitter.SetPosition(Position);
            emitter.PlaySingleSound(new MySoundPair("PrgDeconstrPh03Fin"));

            // TODO: spawn smoke or something only from where the block was mounted
            CreateConstructionSmokes(worldMatrix);
        }

        // cloned MySlimBlock.CreateConstructionSmokes()
        void CreateConstructionSmokes(MatrixD worldMatrix)
        {
            MyCubeBlockDefinition blockDef = MyDefinitionManager.Static.GetCubeBlockDefinition(BlockDefId);
            if(blockDef == null)
                return;

            float gridSize = MyDefinitionManager.Static.GetCubeSize(blockDef.CubeSize);

            Vector3I min = Vector3I.Zero;
            Vector3I max = blockDef.Size - Vector3I.One;

            Vector3 halfGridSizeVec = new Vector3(gridSize) / 2f;
            BoundingBox boundingBox = new BoundingBox(min * gridSize - halfGridSizeVec, max * gridSize + halfGridSizeVec);

            if(ModelBB.HasValue)
            {
                boundingBox = ModelBB.Value;

                //BoundingBox bb = BoundingBox.CreateInvalid();
                //Vector3[] modelCorners = ModelBB.Value.GetCorners();
                //foreach(Vector3 position in modelCorners)
                //{
                //    bb = bb.Include(Vector3.Transform(position);
                //}

                //boundingBox = new BoundingBox(bb.Min + boundingBox.Center, bb.Max + boundingBox.Center);
            }

            boundingBox.Inflate(-0.3f);

            Vector3[] corners = boundingBox.GetCorners();
            float step = 0.25f;

            for(int v = 0; v < MyOrientedBoundingBox.StartVertices.Length; v++)
            {
                Vector3 vec = corners[MyOrientedBoundingBox.StartVertices[v]];
                float n1 = 0f;
                float n2 = Vector3.Distance(vec, corners[MyOrientedBoundingBox.EndVertices[v]]);
                Vector3 vec2 = step * Vector3.Normalize(corners[MyOrientedBoundingBox.EndVertices[v]] - corners[MyOrientedBoundingBox.StartVertices[v]]);

                while(n1 < n2)
                {
                    Vector3D pos = Vector3D.Transform(vec, worldMatrix);
                    MatrixD wm = MatrixD.CreateTranslation(pos);

                    MyParticleEffect effect;
                    if(MyParticlesManager.TryCreateParticleEffect("AdvancedWelding_Detach", ref wm, ref pos, uint.MaxValue, out effect))
                    {
                        effect.Velocity = Velocity;
                    }

                    n1 += step;
                    vec += vec2;
                }
            }
        }
    }
}
