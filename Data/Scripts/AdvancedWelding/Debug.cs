using System;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Weapons;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;

namespace Digi.AdvancedWelding
{
    public class Debug : ComponentBase, IUpdatable
    {
        public Debug(AdvancedWeldingMod main) : base(main)
        {
        }

        public override void Register()
        {
            SetUpdate(this, true);
        }

        public override void Dispose()
        {
        }

        public void Update()
        {
            var chr = MyAPIGateway.Session.ControlledObject as IMyCharacter;
            var block = chr?.EquippedTool?.Components?.Get<MyCasterComponent>()?.HitBlock as IMySlimBlock;
            if(block == null)
                return;

            MyAPIGateway.Utilities.ShowNotification($"Def={block.BlockDefinition}", 16);
            MyAPIGateway.Utilities.ShowNotification($"AccumulatedDamage={block.AccumulatedDamage}; CurrentDamage={block.CurrentDamage}; DamageRatio={block.DamageRatio}", 16);
            MyAPIGateway.Utilities.ShowNotification($"Integrity={block.Integrity}; BuildIntegrity={block.BuildIntegrity}; BuildLevelRatio={block.BuildLevelRatio}", 16);
            MyAPIGateway.Utilities.ShowNotification($"HasDeformation={block.HasDeformation}; MaxDeformation={block.MaxDeformation}", 16);
        }
    }
}