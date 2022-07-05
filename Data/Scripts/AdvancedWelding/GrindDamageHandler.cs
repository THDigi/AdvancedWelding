using System;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Weapons;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;

namespace Digi.AdvancedWelding
{
    // Server-side only
    public class GrindDamageHandler : ComponentBase
    {
        public event GrindBlockDel GrindingBlock;
        public delegate void GrindBlockDel(IMySlimBlock block, ref MyDamageInformation info, IMyAngleGrinder grinder, ulong attackerSteamId);

        public event GrindFloatingObjectDel GrindingFloatingObject;
        public delegate void GrindFloatingObjectDel(IMyFloatingObject floatingObject, ref MyDamageInformation info, ulong attackerSteamId);

        public GrindDamageHandler(AdvancedWeldingMod main) : base(main)
        {
        }

        public override void Register()
        {
            // NOTE: this event does not trigger for MP clients (when grinding at least).
            MyAPIGateway.Session.DamageSystem.RegisterBeforeDamageHandler(10, BeforeDamage);
        }

        public override void Dispose()
        {
        }

        void BeforeDamage(object target, ref MyDamageInformation info)
        {
            try
            {
                if(info.IsDeformation || info.Amount <= 0 || info.Type != MyDamageType.Grind)
                    return;

                IMySlimBlock block = target as IMySlimBlock;
                IMyFloatingObject fo = (block == null ? target as IMyFloatingObject : null);

                if(block == null && fo == null)
                    return;

                MyEntity attacker = MyEntities.GetEntityById(info.AttackerId);
                IMyAngleGrinder grinder = attacker as IMyAngleGrinder;
                IMyCharacter attackerChar = (grinder == null ? attacker as IMyCharacter : null);
                if(grinder == null && attackerChar == null)
                    return;

                ulong attackerSteamId;
                if(grinder != null)
                    attackerSteamId = MyAPIGateway.Players.TryGetSteamId(grinder.OwnerIdentityId);
                else
                    attackerSteamId = MyAPIGateway.Players.TryGetSteamId(attackerChar.ControllerInfo.ControllingIdentityId);

                if(block != null)
                    GrindingBlock?.Invoke(block, ref info, grinder, attackerSteamId);
                else
                    GrindingFloatingObject?.Invoke(fo, ref info, attackerSteamId);
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }
    }
}