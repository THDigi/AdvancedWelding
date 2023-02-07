using System;
using System.Collections.Generic;
using Digi.Sync;
using Sandbox.ModAPI;
using VRage.Collections;
using VRage.Game.Components;

namespace Digi.AdvancedWelding
{
    [MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation)]
    public class AdvancedWeldingMod : MySessionComponentBase
    {
        public static AdvancedWeldingMod Instance;

        public Networking Networking = new Networking(9472); // id must be unique because it collides with other mods
        public GrindDamageHandler GrindDamageHandler;
        public DetachHandler DetachHandler;
        public GrinderHandler GrinderHandler;
        public PrecisionHandler PrecisionHandler;
        public WeldPadHandler WeldPadHandler;
        public MergeHandler MergeHandler;
        public ChatCommands ChatCommands;
        public Notifications Notifications;

        internal readonly List<ComponentBase> Components = new List<ComponentBase>();
        internal readonly CachingHashSet<IUpdatable> UpdateObjects = new CachingHashSet<IUpdatable>();

        public override void LoadData()
        {
            Instance = this;
            Log.ModName = "Advanced Welding";
            Log.AutoClose = false;

            Networking.LogInfo = (msg) => Log.Info(msg, null);
            Networking.LogError = (err) => Log.Error(err);
            Networking.LogException = (ex) => Log.Error(ex);
            Networking.Register(ModContext);

            PrecisionHandler = new PrecisionHandler(this);
            DetachHandler = new DetachHandler(this);
            WeldPadHandler = new WeldPadHandler(this);
            MergeHandler = new MergeHandler(this);

            if(Networking.IsPlayer)
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
                Networking.Unregister();

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

        public override void UpdateAfterSimulation()
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