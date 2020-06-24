using ProtoBuf;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace Digi.AdvancedWelding.MP
{
    [ProtoContract]
    public class WeldEffectsPacket : PacketBase
    {
        public WeldEffectsPacket() { } // Empty constructor required for deserialization

        [ProtoMember(1)]
        private readonly long gridEntId;

        [ProtoMember(2)]
        private readonly Vector3 effectPos;

        [ProtoMember(3)]
        private readonly Quaternion effectOrientation;

        public WeldEffectsPacket(long gridEntId, Vector3 effectPos, Quaternion effectOrientation)
        {
            this.gridEntId = gridEntId;
            this.effectPos = effectPos;
            this.effectOrientation = effectOrientation;
        }

        public override void Received(ref bool relay, ref bool includeSender)
        {
            relay = true;
            includeSender = true;

            if(!Networking.IsPlayer)
                return;

            IMyEntity ent;

            if(!MyAPIGateway.Entities.TryGetEntityById(gridEntId, out ent))
                return;

            var grid = ent as IMyCubeGrid;

            if(grid == null)
                return;

            var worldPos = Vector3.Transform(effectPos, grid.WorldMatrix);
            var camPos = MyAPIGateway.Session.Camera.WorldMatrix.Translation;

            if(Vector3D.DistanceSquared(camPos, worldPos) > 500 * 500)
                return;

            MatrixD worldMatrix = MatrixD.CreateFromQuaternion(effectOrientation);
            worldMatrix.Translation = effectPos;
            worldMatrix.Translation += worldMatrix.Down * (grid.GridSize * 0.5f);
            worldMatrix *= grid.WorldMatrix;

            float scale = grid.GridSize * 0.975f;

            SpawnWeldParticle(worldPos, worldMatrix, new Vector3(1, 0, 1), scale);
            SpawnWeldParticle(worldPos, worldMatrix, new Vector3(1, 0, -1), scale);
            SpawnWeldParticle(worldPos, worldMatrix, new Vector3(-1, 0, 1), scale);
            SpawnWeldParticle(worldPos, worldMatrix, new Vector3(-1, 0, -1), scale);

            var emitter = new MyEntity3DSoundEmitter(null);
            emitter.CustomVolume = 0.9f;
            emitter.SetPosition(worldPos);
            emitter.PlaySingleSound(new MySoundPair("WeldPad_Weld"));
        }

        private void SpawnWeldParticle(Vector3D worldPos, MatrixD worldMatrix, Vector3 offset, float scale)
        {
            worldMatrix.Translation += Vector3D.TransformNormal(offset * (scale * 0.5f), worldMatrix);

            MyParticleEffect effect;
            if(MyParticlesManager.TryCreateParticleEffect("AdvancedWelding_Weld", ref worldMatrix, ref worldPos, uint.MaxValue, out effect))
            {
                effect.UserScale = scale;
            }
        }
    }
}
