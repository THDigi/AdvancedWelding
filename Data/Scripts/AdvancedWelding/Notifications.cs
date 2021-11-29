using System;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;

namespace Digi.AdvancedWelding
{
    public enum Channel
    {
        Detach,
        Precision,
        Other,
    }

    // Player side only
    public class Notifications : ComponentBase
    {
        readonly IMyHudNotification[] Channels;

        public Notifications(AdvancedWeldingMod main) : base(main)
        {
            int enums = Enum.GetValues(typeof(Channel)).Length;
            Channels = new IMyHudNotification[enums];
        }

        public override void Register()
        {
        }

        public override void Dispose()
        {
        }

        public void Print(Channel channel, string text, string font = MyFontEnum.Debug, int aliveTime = 200)
        {
            IMyHudNotification notification = Channels[(int)channel];

            if(notification == null)
            {
                notification = MyAPIGateway.Utilities.CreateNotification(string.Empty);
                Channels[(int)channel] = notification;
            }

            notification.Hide(); // required since SE v1.194
            notification.Font = font;
            notification.Text = text;
            notification.AliveTime = aliveTime;
            notification.Show();
        }
    }
}