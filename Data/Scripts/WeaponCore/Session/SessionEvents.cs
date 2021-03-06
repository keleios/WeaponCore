﻿using System;
using System.Collections.Generic;
using System.Text;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Weapons;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;
using WeaponCore.Support;
using static WeaponCore.Support.GridAi;
using static WeaponCore.Support.WeaponDefinition.HardPointDef.HardwareDef;

namespace WeaponCore
{
    public partial class Session
    {
        internal void OnEntityCreate(MyEntity myEntity)
        {
            try
            {
                if (!Inited) lock (InitObj) Init();
                var grid = myEntity as MyCubeGrid;
                if (grid != null) grid.AddedToScene += GridAddedToScene;
                if (!PbApiInited && myEntity is IMyProgrammableBlock) PbActivate = true;

                var placer = myEntity as IMyBlockPlacerBase;
                if (placer != null && Placer == null) Placer = placer;

                var cube = myEntity as MyCubeBlock;
                var sorter = cube as MyConveyorSorter;
                var turret = cube as IMyLargeTurretBase;
                var controllableGun = cube as IMyUserControllableGun;

                if (sorter != null || turret != null || controllableGun != null)
                {
                    if (!(ReplaceVanilla && VanillaIds.ContainsKey(cube.BlockDefinition.Id)) && !WeaponPlatforms.ContainsKey(cube.BlockDefinition.Id)) return;

                    lock (InitObj)
                    {
                        if (!SorterControls && myEntity is MyConveyorSorter) {
                            MyAPIGateway.Utilities.InvokeOnGameThread(() => CreateTerminalUi<IMyConveyorSorter>(this));
                            SorterControls = true;
                        }
                        else if (!TurretControls && turret != null) {
                            MyAPIGateway.Utilities.InvokeOnGameThread(() => CreateTerminalUi<IMyLargeTurretBase>(this));
                            TurretControls = true;
                        }
                        else if (!FixedMissileReloadControls && controllableGun is IMySmallMissileLauncherReload) {
                            MyAPIGateway.Utilities.InvokeOnGameThread(() => CreateTerminalUi<IMySmallMissileLauncherReload>(this));
                            FixedMissileReloadControls = true;
                        }
                        else if (!FixedMissileControls && controllableGun is IMySmallMissileLauncher) {
                            MyAPIGateway.Utilities.InvokeOnGameThread(() => CreateTerminalUi<IMySmallMissileLauncher>(this));
                            FixedMissileControls = true;
                        }
                        else if (!FixedGunControls && controllableGun is IMySmallGatlingGun) {
                            MyAPIGateway.Utilities.InvokeOnGameThread(() => CreateTerminalUi<IMySmallGatlingGun>(this));
                            FixedGunControls = true;
                        }
                    }
                    InitComp(cube);
                }
            }
            catch (Exception ex) { Log.Line($"Exception in OnEntityCreate: {ex}"); }
        }

        private void GridAddedToScene(MyEntity myEntity)
        {
            try
            {
                NewGrids.Enqueue(myEntity as MyCubeGrid);
            }
            catch (Exception ex) { Log.Line($"Exception in GridAddedToScene: {ex}"); }
        }

        private void AddGridToMap()
        {
            MyCubeGrid grid;
            while (NewGrids.TryDequeue(out grid))
            {
                var allFat = ConcurrentListPool.Get();
                var gridFat = grid.GetFatBlocks();
                for (int i = 0; i < gridFat.Count; i++) allFat.Add(gridFat[i]);
                allFat.ApplyAdditions();

                var fatMap = FatMapPool.Get();

                if (grid.Components.TryGet(out fatMap.Targeting))
                    fatMap.Targeting.AllowScanning = false;
                fatMap.Trash = true;

                fatMap.MyCubeBocks = allFat;
                GridToFatMap.TryAdd(grid, fatMap);
                grid.OnFatBlockAdded += ToFatMap;
                grid.OnFatBlockRemoved += FromFatMap;
                grid.OnClose += RemoveGridFromMap;
                DirtyGrids.Add(grid);
            }
        }

        private void RemoveGridFromMap(MyEntity myEntity)
        {
            var grid = (MyCubeGrid)myEntity;
            FatMap fatMap;
            if (GridToFatMap.TryRemove(grid, out fatMap))
            {
                ConcurrentListPool.Return(fatMap.MyCubeBocks);
                fatMap.Trash = true;
                FatMapPool.Return(fatMap);
                grid.OnFatBlockAdded -= ToFatMap;
                grid.OnFatBlockRemoved -= FromFatMap;
                grid.OnClose -= RemoveGridFromMap;
                grid.AddedToScene -= GridAddedToScene;
                DirtyGrids.Add(grid);
            }
            else Log.Line($"grid not removed and list not cleaned: marked:{grid.MarkedForClose}({grid.Closed}) - inScene:{grid.InScene}");
        }

        private void ToFatMap(MyCubeBlock myCubeBlock)
        {
            try
            {
                GridToFatMap[myCubeBlock.CubeGrid].MyCubeBocks.Add(myCubeBlock);
                GridToFatMap[myCubeBlock.CubeGrid].MyCubeBocks.ApplyAdditions();
                DirtyGrids.Add(myCubeBlock.CubeGrid);
            }
            catch (Exception ex) { Log.Line($"Exception in ToFatMap: {ex} - marked:{myCubeBlock.MarkedForClose}"); }
        }

        private void FromFatMap(MyCubeBlock myCubeBlock)
        {
            try
            {
                GridToFatMap[myCubeBlock.CubeGrid].MyCubeBocks.Remove(myCubeBlock, true);
                DirtyGrids.Add(myCubeBlock.CubeGrid);
            }
            catch (Exception ex) { Log.Line($"Exception in FromFatMap: {ex} - marked:{myCubeBlock.MarkedForClose}"); }
        }

        internal void BeforeDamageHandler(object o, ref MyDamageInformation info)
        {
            var slim = o as IMySlimBlock;

            if (slim != null) {

                var cube = slim.FatBlock as MyCubeBlock;
                var grid = (MyCubeGrid)slim.CubeGrid;

                if (info.IsDeformation && info.AttackerId > 0 && DeformProtection.Contains(grid)) {
                    Log.Line($"BeforeDamageHandler1");
                    info.Amount = 0f;
                    return;
                }

                WeaponComponent comp;
                if (cube != null && ArmorCubes.TryGetValue(cube, out comp)) {

                    Log.Line($"BeforeDamageHandler2");
                    info.Amount = 0f;
                    if (info.IsDeformation && info.AttackerId > 0) {
                        DeformProtection.Add(cube.CubeGrid);
                        LastDeform = Tick;
                    }
                }
            }
        }

        private void MenuOpened(object obj)
        {
            try
            {
                InMenu = true;
                GridAi ai;
                if (ActiveControlBlock != null && GridToMasterAi.TryGetValue(ActiveControlBlock.CubeGrid, out ai))  {
                    //Send updates?
                }
            }
            catch (Exception ex) { Log.Line($"Exception in MenuOpened: {ex}"); }
        }

        private void MenuClosed(object obj)
        {
            try
            {
                InMenu = false;
                HudUi.NeedsUpdate = true;
                GridAi ai;
                if (ActiveControlBlock != null && GridToMasterAi.TryGetValue(ActiveControlBlock.CubeGrid, out ai))  {
                    //Send updates?
                }
            }
            catch (Exception ex) { Log.Line($"Exception in MenuClosed: {ex}"); }
        }

        private void PlayerControlAcquired(MyEntity lastEnt)
        {
            var cube = lastEnt as MyCubeBlock;
            GridAi gridAi;
            if (cube != null && GridTargetingAIs.TryGetValue(cube.CubeGrid, out gridAi)) {

                WeaponComponent comp;
                if (gridAi.WeaponBase.TryGetValue(cube, out comp))
                    comp.RequestShootUpdate(WeaponComponent.ShootActions.ShootOff, comp.Session.DedicatedServer ? 0 : -1);
            }
        }

        private void PlayerConnected(long id)
        {
            try
            {
                if (Players.ContainsKey(id)) return;
                MyAPIGateway.Multiplayer.Players.GetPlayers(null, myPlayer => FindPlayer(myPlayer, id));
            }
            catch (Exception ex) { Log.Line($"Exception in PlayerConnected: {ex}"); }
        }

        private void PlayerDisconnected(long l)
        {
            try
            {
                PlayerEventId++;
                IMyPlayer removedPlayer;
                if (Players.TryRemove(l, out removedPlayer))
                {
                    long playerId;

                    SteamToPlayer.TryRemove(removedPlayer.SteamUserId, out playerId);
                    PlayerEntityIdInRange.Remove(removedPlayer.SteamUserId);
                    PlayerMouseStates.Remove(playerId);
                    PlayerDummyTargets.Remove(playerId);
                    PlayerMIds.Remove(removedPlayer.SteamUserId);

                    if (IsServer && MpActive)
                        SendPlayerConnectionUpdate(l, false);

                    if (AuthorIds.Contains(removedPlayer.SteamUserId))
                        ConnectedAuthors.Remove(playerId);
                }
            }
            catch (Exception ex) { Log.Line($"Exception in PlayerDisconnected: {ex}"); }
        }


        private bool FindPlayer(IMyPlayer player, long id)
        {
            if (player.IdentityId == id)
            {
                Players[id] = player;
                SteamToPlayer[player.SteamUserId] = id;
                PlayerMouseStates[id] = new InputStateData();
                PlayerDummyTargets[id] = new FakeTarget();
                PlayerEntityIdInRange[player.SteamUserId] = new HashSet<long>();
                PlayerMIds[player.SteamUserId] = new uint[Enum.GetValues(typeof(PacketType)).Length];

                PlayerEventId++;
                if (AuthorIds.Contains(player.SteamUserId)) 
                    ConnectedAuthors.Add(id, player.SteamUserId);

                if (IsServer && MpActive)  {
                    SendPlayerConnectionUpdate(id, true);
                    SendServerStartup(player.SteamUserId);
                }
            }
            return false;
        }

        private void WApiReceiveData()
        {
            if (WApi.Registered) {
                WaterMap.Clear();
                MaxWaterHeightSqr.Clear();
                for (int i = 0; i < WApi.Waters.Count; i++) {
                    
                    var water = WApi.Waters[i];
                    if (water.planet != null) {

                        WaterMap[water.planet] = water;
                        var maxWaterHeight = water.radius;
                        Log.Line($"{water.radius}");
                        var maxWaterHeightSqr = maxWaterHeight * maxWaterHeight;
                        MaxWaterHeightSqr[water.planet] = maxWaterHeightSqr;
                    }
                }
            }
        }
    }
}
