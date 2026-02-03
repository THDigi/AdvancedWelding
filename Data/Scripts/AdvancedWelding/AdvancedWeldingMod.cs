using System;
using System.Collections.Generic;
using Digi.NetworkLib;
using Sandbox.ModAPI;
using VRage.Collections;
using VRage.Game.Components;
using VRage.Utils;

namespace Digi.AdvancedWelding
{
    [MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)]
    public class AdvancedWeldingMod : MySessionComponentBase
    {
        public static AdvancedWeldingMod Instance;

        public Network Networking;
        public GrindDamageHandler GrindDamageHandler;
        public DetachHandler DetachHandler;
        public GrinderHandler GrinderHandler;
        public PrecisionHandler PrecisionHandler;
        public WeldPadHandler WeldPadHandler;
        public MergeHandler MergeHandler;
        public ChatCommands ChatCommands;
        public Notifications Notifications;

        public static bool IsPlayer;

        internal readonly List<ComponentBase> Components = new List<ComponentBase>();
        internal readonly CachingHashSet<IUpdatable> UpdateObjects = new CachingHashSet<IUpdatable>();

        public override void LoadData()
        {
            Instance = this;
            Log.ModName = "Advanced Welding v1.1";
            Log.AutoClose = false;
            Log.Info(Log.ModName);
            MyLog.Default.WriteLineAndConsole($"### Initializing {Log.ModName}");

            bool isDS = MyAPIGateway.Session.IsServer && MyAPIGateway.Utilities.IsDedicated;
            IsPlayer = !isDS;

            // id must be unique because it collides with other mods
            Networking = new Network(9472, Log.ModName, registerListener: true);
            Networking.ErrorHandler = (err) => Log.Error(err);
            Networking.ExceptionHandler = (ex) => Log.Error(ex);

            PrecisionHandler = new PrecisionHandler(this);
            DetachHandler = new DetachHandler(this);
            WeldPadHandler = new WeldPadHandler(this);
            MergeHandler = new MergeHandler(this);

            if(IsPlayer)
            {
                GrinderHandler = new GrinderHandler(this);
                ChatCommands = new ChatCommands(this);
                Notifications = new Notifications(this);
            }

            if(MyAPIGateway.Session.IsServer)
            {
                GrindDamageHandler = new GrindDamageHandler(this);

                new FixDeformation(this);
            }

            //new Debug(this);
        }

        public override void BeforeStart()
        {
            try
            {
                foreach(ComponentBase comp in Components)
                {
                    comp.Register();
                }
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }

        protected override void UnloadData()
        {
            try
            {
                Networking?.Dispose();

                foreach(ComponentBase comp in Components)
                {
                    comp.Dispose();
                }

                Components.Clear();
            }
            catch(Exception e)
            {
                Log.Error(e);
            }

            Instance = null;
            Log.Close();
        }

        public override void UpdateBeforeSimulation()
        {
            try
            {
                UpdateObjects.ApplyChanges();

                foreach(IUpdatable obj in UpdateObjects)
                {
                    obj.Update();
                }
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }

        public override void Draw()
        {
            try
            {
                WeldPadHandler?.Draw();
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }
    }
}