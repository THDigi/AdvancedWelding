using System;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Weapons;
using VRage.Game;
using VRage.Game.ModAPI;

namespace Digi.AdvancedWelding
{
    // Server-side only
    public class GrindDamageHandler : ComponentBase
    {
        public event GrindDel GrindingBlock;
        public delegate void GrindDel(IMySlimBlock block, ref MyDamageInformation info, IMyAngleGrinder grinder, ulong attackerSteamId);

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
                if(block == null)
                    return;

                IMyAngleGrinder grinder = MyEntities.GetEntityById(info.AttackerId) as IMyAngleGrinder;
                if(grinder == null)
                    return;

                ulong attackerSteamId = MyAPIGateway.Players.TryGetSteamId(grinder.OwnerIdentityId);
                GrindingBlock?.Invoke(block, ref info, grinder, attackerSteamId);
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }
    }
}