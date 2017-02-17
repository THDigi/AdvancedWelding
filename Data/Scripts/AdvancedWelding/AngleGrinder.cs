using System;
using System.Collections.Generic;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ObjectBuilders;
using VRage.Game.Components;
using VRage.ModAPI;

namespace Digi.AdvancedWelding
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_AngleGrinder), true)]
    public class AngleGrinder : MyGameLogicComponent
    {
        public bool isYours = false;

        public static bool detach = false;
        public static bool isHolding = false;

        private static IMyHudNotification toolStatus;
        public static bool notified = false;

        private const string DETACH_MODE_PREFIX = "DETACH MODE\n";
        private const int TOOLSTATUS_TIMEOUT = 200;

        private static List<IMySlimBlock> blocks = new List<IMySlimBlock>();

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            Entity.NeedsUpdate |= MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
        }

        public static void SetToolStatus(string text, string font, int aliveTime = TOOLSTATUS_TIMEOUT)
        {
            try
            {
                if(toolStatus == null)
                {
                    toolStatus = MyAPIGateway.Utilities.CreateNotification(text, aliveTime, font);
                }
                else
                {
                    toolStatus.Font = font;
                    toolStatus.Text = text;
                    toolStatus.AliveTime = aliveTime;
                }

                toolStatus.Show();
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }

        public override void UpdateOnceBeforeFrame()
        {
            try
            {
                isYours = false;

                if(MyAPIGateway.Session.ControlledObject is IMyCharacter)
                {
                    var playerEnt = MyAPIGateway.Session.ControlledObject.Entity;
                    var charObj = playerEnt.GetObjectBuilder(false) as MyObjectBuilder_Character;

                    if(charObj.HandWeapon != null && charObj.HandWeapon.EntityId == Entity.EntityId)
                    {
                        isYours = true;
                        isHolding = true;
                        Entity.NeedsUpdate |= MyEntityUpdateEnum.EACH_10TH_FRAME;

                        if(!notified)
                        {
                            notified = true;
                            SetToolStatus("Type /detach to detach blocks with angle grinders.", MyFontEnum.DarkBlue, 3000);
                        }
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
                if(!isYours || !detach)
                    return;

                var grid = MyAPIGateway.CubeBuilder.FindClosestGrid();

                if(grid == null)
                {
                    SetToolStatus(DETACH_MODE_PREFIX + "Aim at a terminal block.", MyFontEnum.LoadingScreen);
                    return;
                }

                var player = MyAPIGateway.Session.ControlledObject.Entity;
                var playerPos = player.WorldMatrix.Translation + player.WorldMatrix.Up * 1.6;
                var blockPos = grid.RayCastBlocks(playerPos, playerPos + player.WorldMatrix.Forward * 2.0);

                if(!blockPos.HasValue)
                {
                    SetToolStatus(DETACH_MODE_PREFIX + "Aim at a terminal block.", MyFontEnum.LoadingScreen);
                    return;
                }

                blocks.Clear();
                grid.GetBlocks(blocks);
                int blocksCount = blocks.Count;
                blocks.Clear();

                if(blocksCount == 1)
                {
                    SetToolStatus(DETACH_MODE_PREFIX + "That's the only block on the ship!", MyFontEnum.Red);
                    return;
                }

                var slimBlock = grid.GetCubeBlock(blockPos.Value);

                if(slimBlock == null)
                {
                    Log.Error("Unexpected empty block slot at " + blockPos.Value);
                    return;
                }

                if(slimBlock.FatBlock == null || slimBlock.FatBlock.BlockDefinition.TypeIdString.EndsWith("CubeBlock"))
                {
                    SetToolStatus(DETACH_MODE_PREFIX + "Aim at a terminal block.\n(No armor, no catwalks, no blast doors, etc)", MyFontEnum.Red);
                    return;
                }

                var blockName = ((MyCubeBlockDefinition)slimBlock.BlockDefinition).DisplayNameText;
                var blockObj = slimBlock.GetObjectBuilder();
                var defId = new MyDefinitionId(blockObj.TypeId, blockObj.SubtypeId);
                MyCubeBlockDefinition blockDef;

                if(!MyDefinitionManager.Static.TryGetCubeBlockDefinition(defId, out blockDef))
                {
                    Log.Error("Unable to get block definition for " + defId.ToString());
                    return;
                }

                var tool = Entity as Sandbox.Game.Entities.IMyGunObject<Sandbox.Game.Weapons.MyToolBase>;
                int buildRatio = (int)(Math.Round(slimBlock.BuildLevelRatio, 2) * 100);
                int criticalRatio = (int)(Math.Round(blockDef.CriticalIntegrityRatio, 2) * 100);

                // ShakeAmount is used to determine wether you're actually hitting a block
                if(!tool.IsShooting || tool.ShakeAmount <= 1 || buildRatio > criticalRatio)
                {
                    SetToolStatus(DETACH_MODE_PREFIX + "Grind it below the red line.", MyFontEnum.Blue);
                    return;
                }

                SetToolStatus(blockName + " detached!\nDetach mode now off.", MyFontEnum.Green, 3000);
                detach = false;

                var bytes = new List<byte>();
                bytes.AddRange(BitConverter.GetBytes(grid.EntityId));
                bytes.AddRange(BitConverter.GetBytes(slimBlock.Position.X));
                bytes.AddRange(BitConverter.GetBytes(slimBlock.Position.Y));
                bytes.AddRange(BitConverter.GetBytes(slimBlock.Position.Z));
                // TODO add 4th dimmension when compound blocks are a thing

                MyAPIGateway.Multiplayer.SendMessageToServer(AdvancedWelding.PACKET, bytes.ToArray(), true);
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
                if(isYours)
                {
                    isYours = false;
                    isHolding = false;

                    if(detach)
                    {
                        SetToolStatus("Detach mode cancelled.", MyFontEnum.Red, 1500);
                        detach = false;
                    }
                }
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }
    }
}