using System;
using Sandbox.Game;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Weapons;
using VRageMath;

namespace Digi.AdvancedWelding
{
    // Player side only
    public class ChatCommands : ComponentBase
    {
        const string HelpText = "This mod adds a few features to Hand Grinder as well as a Weld Pad block (for both small and large)." +
            "\n" +
            "\nTo detach a block, equip a Hand Grinder and hold CTRL or LeftBumper then grind a block (does not grind off components)." +
            "\n" +
            "\nTo grind only the aimed block without accidentally grinding other blocks after it's done, hold SecondaryAction while grinding." +
            "\n" +
            "\nThe weld pads are single-use slim blocks that allow merging of 2 surfaces without needing space between, useful for re-attaching detached blocks elsewhere.";

        bool InformedAboutFeature = false;

        public ChatCommands(AdvancedWeldingMod main) : base(main)
        {
        }

        public override void Register()
        {
            Main.GrinderHandler.GrinderChanged += EquippedGrinderChanged;
            MyAPIGateway.Utilities.MessageEntered += MessageEntered;
        }

        public override void Dispose()
        {
            MyAPIGateway.Utilities.MessageEntered -= MessageEntered;
        }

        void EquippedGrinderChanged(IMyAngleGrinder grinder)
        {
            if(grinder != null && !InformedAboutFeature && MyAPIGateway.Session?.Player != null)
            {
                InformedAboutFeature = true;
                MyVisualScriptLogicProvider.SendChatMessageColored("Type /detach in chat to see grinder features.", Color.Green, Log.ModName, MyAPIGateway.Session.Player.IdentityId);
            }
        }

        void MessageEntered(string msg, ref bool send)
        {
            if(msg.StartsWith("/detach", StringComparison.OrdinalIgnoreCase))
            {
                send = false;
                MyAPIGateway.Utilities.ShowMissionScreen(Log.ModName, "", "", HelpText);
            }
        }
    }
}