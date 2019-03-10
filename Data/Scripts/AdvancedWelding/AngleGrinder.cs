using System;
using Digi.AdvancedWelding.MP;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.Game.EntityComponents;
using Sandbox.Game.Weapons;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRageMath;

namespace Digi.AdvancedWelding
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_AngleGrinder), false)]
    public class AngleGrinder : MyGameLogicComponent
    {
        private const int TOOLSTATUS_TIMEOUT = 200;
        private const string DETACH_MODE_PREFIX = "DETACH MODE\n";

        public bool thisLocallyEquipped = false;

        public static bool IsEquipped = false;
        public static bool DetachMode = false;
        public static bool Notified = false;
        private static IMyHudNotification toolStatus;

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            NeedsUpdate = MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
        }

        public override void UpdateOnceBeforeFrame()
        {
            try
            {
                var character = MyAPIGateway.Session.ControlledObject as IMyCharacter;

                if(character != null)
                {
                    if(character.EquippedTool != null && character.EquippedTool.EntityId == Entity.EntityId)
                    {
                        thisLocallyEquipped = true;
                        IsEquipped = true;
                        NeedsUpdate |= MyEntityUpdateEnum.EACH_10TH_FRAME;

                        if(!Notified)
                        {
                            Notified = true;
                            SetToolStatus("Type [/detach] to detach blocks with angle grinders.", MyFontEnum.White, 5000);
                        }
                    }
                }
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }

        public override void Close()
        {
            try
            {
                if(thisLocallyEquipped)
                {
                    thisLocallyEquipped = false;
                    IsEquipped = false;

                    if(DetachMode)
                    {
                        SetToolStatus("Detach mode cancelled.", MyFontEnum.White, 1500);
                        DetachMode = false;
                    }
                }
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }

        public override void UpdateAfterSimulation10()
        {
            try
            {
                if(!thisLocallyEquipped || !DetachMode)
                    return;

                var tool = (IMyGunObject<MyDeviceBase>)Entity;
                var casterComp = Entity.Components.Get<MyCasterComponent>();

                if(casterComp == null)
                {
                    SetToolStatus($"This grinder is modded to not be able to aim, can't be used for detaching.", MyFontEnum.Red, 3000);
                    DetachMode = false;
                    return;
                }

                var slimBlock = casterComp.HitBlock as IMySlimBlock;

                if(slimBlock == null || !(slimBlock.FatBlock is IMyTerminalBlock))
                {
                    SetToolStatus($"{DETACH_MODE_PREFIX}Aim at a terminal block.");
                    return;
                }

                var blocksCount = ((MyCubeGrid)slimBlock.CubeGrid).BlocksCount;

                if(blocksCount == 1)
                {
                    SetToolStatus($"{DETACH_MODE_PREFIX}This is the only block on the ship, nothing to detach from.");
                    return;
                }

                var blockDef = (MyCubeBlockDefinition)slimBlock.BlockDefinition;
                var buildRatio = slimBlock.BuildLevelRatio;
                var criticalRatio = Math.Min(blockDef.CriticalIntegrityRatio + 0.1f, 1f); // +10% above critical integrity, capped to 100%.

                // check shoot too because the block could already be under this build stage
                if(!tool.IsShooting || buildRatio >= criticalRatio)
                {
                    SetToolStatus($"{DETACH_MODE_PREFIX}Grind below {(int)(criticalRatio * 100)}% to detach.", MyFontEnum.Blue);
                    return;
                }

                var packet = new DetachPacket(slimBlock.CubeGrid.EntityId, slimBlock.Position);
                AdvancedWelding.Instance.Networking.SendToServer(packet);

                SetToolStatus($"{blockDef.DisplayNameText} detached!\nDetach mode turned off.", MyFontEnum.Green, 3000);
                DetachMode = false;
                PlayDetachSound(slimBlock);
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }

        public static void PlayDetachSound(IMySlimBlock block)
        {
            Vector3D position;
            block.ComputeWorldCenter(out position);

            var camPos = MyAPIGateway.Session.Camera.WorldMatrix.Translation;

            if(Vector3D.DistanceSquared(camPos, position) > 100 * 100)
                return;

            var emitter = new MyEntity3DSoundEmitter(null);
            emitter.CustomVolume = 0.4f;
            emitter.SetPosition(position);
            emitter.PlaySingleSound(new MySoundPair("PrgDeconstrPh01Start"));
        }

        public static void SetToolStatus(string text, string font = MyFontEnum.White, int aliveTime = TOOLSTATUS_TIMEOUT)
        {
            try
            {
                if(toolStatus == null)
                    toolStatus = MyAPIGateway.Utilities.CreateNotification("");

                toolStatus.Font = font;
                toolStatus.Text = text;
                toolStatus.AliveTime = aliveTime;
                toolStatus.Show();
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }
    }
}