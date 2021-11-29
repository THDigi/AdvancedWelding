using System;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Weapons;
using VRage.Game.ModAPI;

namespace Digi.AdvancedWelding
{
    // Player side only
    public class GrinderHandler : ComponentBase, IUpdatable
    {
        public IMyAngleGrinder EquippedGrinder { get; private set; }

        public event Action<IMyAngleGrinder> GrinderChanged;

        public GrinderHandler(AdvancedWeldingMod main) : base(main)
        {
            SetUpdate(this, true);
        }

        public override void Register()
        {
        }

        public override void Dispose()
        {
        }

        void IUpdatable.Update()
        {
            IMyCharacter chr = MyAPIGateway.Session?.ControlledObject as IMyCharacter;
            IMyAngleGrinder grinder = chr?.EquippedTool as IMyAngleGrinder;

            if(EquippedGrinder != grinder)
            {
                EquippedGrinder = grinder;
                GrinderChanged?.Invoke(grinder);
            }
        }
    }
}