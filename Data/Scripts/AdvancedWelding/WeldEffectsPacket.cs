using Digi.Sync;
using ProtoBuf;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;
using VRageMath;

namespace Digi.AdvancedWelding
{
    [ProtoContract]
    public class WeldEffectsPacket : PacketBase
    {
        const float MaxDistanceSq = 500 * 500;

        public WeldEffectsPacket() { } // Empty constructor required for deserialization

        [ProtoMember(1)]
        readonly long GridEntId;

        [ProtoMember(2)]
        readonly Vector3 LocalPos;

        [ProtoMember(3)]
        readonly Quaternion LocalOrientation;

        public WeldEffectsPacket(long gridEntId, Vector3 localPos, Quaternion localOrientation)
        {
            GridEntId = gridEntId;
            LocalPos = localPos;
            LocalOrientation = localOrientation;
        }

        public override void Received(ref RelayMode relay, ulong senderSteamId)
        {
            relay = RelayMode.RelayOriginal;

            if(!Networking.IsPlayer)
                return;

            IMyCubeGrid grid = MyEntities.GetEntityById(GridEntId) as IMyCubeGrid;
            if(grid == null)
                return;

            Vector3D worldPos = Vector3.Transform(LocalPos, grid.WorldMatrix);
            Vector3D camPos = MyAPIGateway.Session.Camera.WorldMatrix.Translation;
            if(Vector3D.DistanceSquared(camPos, worldPos) > MaxDistanceSq)
                return;

            MatrixD worldMatrix = MatrixD.CreateFromQuaternion(LocalOrientation);
            worldMatrix.Translation = LocalPos;
            worldMatrix.Translation += worldMatrix.Down * (grid.GridSize * 0.5f);
            worldMatrix *= grid.WorldMatrix;

            float scale = grid.GridSize * 0.975f;

            SpawnWeldParticle(worldPos, worldMatrix, new Vector3(1, 0, 1), scale);
            SpawnWeldParticle(worldPos, worldMatrix, new Vector3(1, 0, -1), scale);
            SpawnWeldParticle(worldPos, worldMatrix, new Vector3(-1, 0, 1), scale);
            SpawnWeldParticle(worldPos, worldMatrix, new Vector3(-1, 0, -1), scale);

            MyEntity3DSoundEmitter emitter = new MyEntity3DSoundEmitter(null);
            emitter.CustomVolume = 0.9f;
            emitter.SetPosition(worldPos);
            emitter.PlaySingleSound(new MySoundPair("WeldPad_Weld"));
        }

        void SpawnWeldParticle(Vector3D worldPos, MatrixD worldMatrix, Vector3 offset, float scale)
        {
            worldMatrix.Translation += Vector3D.TransformNormal(offset * (scale * 0.5f), worldMatrix);

            MyParticleEffect effect;
            if(MyParticlesManager.TryCreateParticleEffect("AdvancedWelding_Weld", ref worldMatrix, ref worldPos, uint.MaxValue, out effect))
            {
                effect.UserScale = scale;
                effect.Autodelete = true;
            }
        }
    }
}
