﻿using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Game.ModAPI.Interfaces;
using VRageMath;
using WeaponCore.Projectiles;
using WeaponCore.Support;

namespace WeaponCore
{
    public struct RadiatedBlock
    {
        public Vector3I Center;
        public IMySlimBlock Slim;
        public Vector3I Position;
    }


    public partial class Session
    {
        internal void ProcessHits()
        {
            for (int x = 0; x < Hits.Count; x++)
            {
                var p = Hits[x];
                var info = p.Info;
                var maxObjects = info.System.MaxObjectsHit;
                var phantom = info.System.Values.Ammo.BaseDamage <= 0;
                var pInvalid = (int) p.State > 3;
                var tInvalid = info.Target.IsProjectile && (int)info.Target.Projectile.State > 1;
                if (tInvalid) info.Target.Reset();
                var skip = pInvalid || tInvalid;
                for (int i = 0; i < info.HitList.Count; i++)
                {
                    var hitEnt = info.HitList[i];
                    var hitMax = info.ObjectsHit >= maxObjects;
                    var outOfPew = info.BaseDamagePool <= 0 && !(phantom && hitEnt.EventType == HitEntity.Type.Effect);

                    if (skip || hitMax || outOfPew)
                    {
                        if (hitMax || outOfPew || pInvalid)
                        {
                            p.State = Projectile.ProjectileState.Depleted;
                        }
                        Projectiles.HitEntityPool.Return(hitEnt);
                        continue;
                    }
                    switch (hitEnt.EventType)
                    {
                        case HitEntity.Type.Shield:
                            DamageShield(hitEnt, info);
                            continue;
                        case HitEntity.Type.Grid:
                            DamageGrid(hitEnt, info);
                            continue;
                        case HitEntity.Type.Destroyable:
                            DamageDestObj(hitEnt, info);
                            continue;
                        case HitEntity.Type.Voxel:
                            DamageVoxel(hitEnt, info);
                            continue;
                        case HitEntity.Type.Projectile:
                            DamageProjectile(hitEnt, info);
                            continue;
                        case HitEntity.Type.Field:
                            UpdateField(hitEnt, info);
                            continue;
                        case HitEntity.Type.Effect:
                            UpdateEffect(hitEnt, info);
                            continue;
                    }
                    Projectiles.HitEntityPool.Return(hitEnt);
                }

                if (info.BaseDamagePool <= 0)
                    p.State = Projectile.ProjectileState.Depleted;

                info.HitList.Clear();
            }
            Hits.Clear();
        }

        private void DamageShield(HitEntity hitEnt, ProInfo info)
        {
            var shield = hitEnt.Entity as IMyTerminalBlock;
            var system = info.System;
            if (shield == null || !hitEnt.HitPos.HasValue) return;
            info.ObjectsHit++;

            var damageScale = 1;
            if (system.VirtualBeams) damageScale *= info.WeaponCache.Hits;
            var damageType = info.System.Values.DamageScales.Shields.Type;
            var energy = damageType == ShieldDefinition.ShieldType.Energy;
            var heal = damageType == ShieldDefinition.ShieldType.Heal;
            var areaEffect = info.System.Values.Ammo.AreaEffect;
            var detonateOnEnd = system.Values.Ammo.AreaEffect.Detonation.DetonateOnEnd;

            var scaledDamage = ((info.BaseDamagePool * damageScale) + areaEffect.AreaEffectDamage * (areaEffect.AreaEffectRadius * 0.5f)) * system.ShieldModifier;
            var detonateDamage = detonateOnEnd ? (areaEffect.Detonation.DetonationDamage * (areaEffect.Detonation.DetonationRadius * 0.5f)) * system.ShieldModifier : 0;

            var combinedDamage = (float) (scaledDamage + detonateDamage);
            if (heal) combinedDamage *= -1;
            var hit = SApi.PointAttackShieldExt(shield, hitEnt.HitPos.Value, info.Target.FiringCube.EntityId, combinedDamage, energy, info.System.Values.Graphics.ShieldHitDraw);
            if (hit.HasValue)
            {
                if (heal)
                {
                    info.BaseDamagePool = 0;
                    return;
                }
                var objHp = hit.Value;
                if (scaledDamage < objHp) info.BaseDamagePool = 0;
                else if (objHp > 0) info.BaseDamagePool -= (float)scaledDamage - objHp;
                else info.BaseDamagePool -= ((float)scaledDamage - (objHp * -1));

                if (system.Values.Ammo.Mass <= 0) return;

                var speed = system.Values.Ammo.Trajectory.DesiredSpeed > 0 ? system.Values.Ammo.Trajectory.DesiredSpeed : 1;
                ApplyProjectileForce((MyEntity)shield.CubeGrid, hitEnt.HitPos.Value, hitEnt.Intersection.Direction, system.Values.Ammo.Mass * speed);
            }
        }

        private void DamageGrid(HitEntity hitEnt, ProInfo t)
        {
            var grid = hitEnt.Entity as MyCubeGrid;
            var system = t.System;
            if (grid == null || grid.MarkedForClose || !hitEnt.HitPos.HasValue || hitEnt.Blocks == null)
            {
                hitEnt.Blocks?.Clear();
                return;
            }
            if (system.Values.DamageScales.Shields.Type == ShieldDefinition.ShieldType.Heal)
            {
                t.BaseDamagePool = 0;
                return;
            }

            _destroyedSlims.Clear();
            //grid.Physics.Gravity = (Vector3D.Normalize(hitEnt.Beam.From - grid.Physics.CenterOfMassWorld) * 10);
            var largeGrid = grid.GridSizeEnum == MyCubeSize.Large;
            var areaRadius = largeGrid ? system.AreaRadiusLarge : system.AreaRadiusSmall;
            var detonateRadius = largeGrid ? system.DetonateRadiusLarge : system.DetonateRadiusSmall;
            var maxObjects = t.System.MaxObjectsHit;
            var areaEffect = system.Values.Ammo.AreaEffect.AreaEffect;
            var explosive = areaEffect == AreaDamage.AreaEffectType.Explosive;
            var radiant = areaEffect == AreaDamage.AreaEffectType.Radiant;
            var detonateOnEnd = system.Values.Ammo.AreaEffect.Detonation.DetonateOnEnd;
            var detonateDmg = t.DetonationDamage;
            var shieldBypass = system.Values.DamageScales.Shields.Type == ShieldDefinition.ShieldType.Bypass;
            var attackerId = shieldBypass ? grid.EntityId : t.Target.FiringCube.EntityId;
            var attacker = shieldBypass ? (MyEntity)grid : t.Target.FiringCube;
            var areaEffectDmg = t.AreaEffectDamage;
            var hitMass = system.Values.Ammo.Mass;

            if (t.IsShrapnel)
            {
                var shrapnel = system.Values.Ammo.Shrapnel;
                areaEffectDmg = areaEffectDmg > 0 ? areaEffectDmg / shrapnel.Fragments : 0;
                detonateDmg = detonateDmg > 0 ? detonateDmg / shrapnel.Fragments : 0;
                hitMass = hitMass > 0 ? hitMass / shrapnel.Fragments : 0;
                areaRadius = ModRadius(areaRadius, largeGrid);
                detonateRadius = ModRadius(detonateRadius, largeGrid);
            }

            var hasAreaDmg = areaEffectDmg > 0;
            var radiantCascade = radiant && !detonateOnEnd;
            var primeDamage = !radiantCascade || !hasAreaDmg;
            var radiantBomb = radiant && detonateOnEnd;
            var damageType = explosive || radiant ? MyDamageType.Explosion : MyDamageType.Bullet;

            var damagePool = t.BaseDamagePool;
            if (system.VirtualBeams)
            {
                var hits = t.WeaponCache.Hits;
                damagePool *= hits;
                areaEffectDmg *= hits;
            }
            var objectsHit = t.ObjectsHit;
            var countBlocksAsObjects = system.Values.Ammo.ObjectsHit.CountBlocks;

            List<Vector3I> radiatedBlocks = null;
            if (radiant) GetBlockSphereDb(grid, areaRadius, out radiatedBlocks);

            var done = false;
            var nova = false;
            var outOfPew = false;
            for (int i = 0; i < hitEnt.Blocks.Count; i++)
            {
                if (done || outOfPew && !nova) break;

                var rootBlock = hitEnt.Blocks[i];

                if (!nova)
                {
                    if (_destroyedSlims.Contains(rootBlock)) continue;
                    if (rootBlock.IsDestroyed)
                    {
                        _destroyedSlims.Add(rootBlock);
                        continue;
                    }
                }
                var radiate = radiantCascade || nova;
                var dmgCount = 1;
                if (radiate)
                {
                    if (nova) GetBlockSphereDb(grid, detonateRadius, out radiatedBlocks);
                    if (radiatedBlocks != null) ShiftAndPruneBlockSphere(grid, rootBlock.Position, radiatedBlocks, _slimsSortedList);

                    done = nova;
                    dmgCount = _slimsSortedList.Count;
                }

                for (int j = 0; j < dmgCount; j++)
                {
                    var block = radiate ? _slimsSortedList[j].Slim : rootBlock;
                    var blockHp = block.Integrity;
                    float damageScale = 1;

                    if (system.DamageScaling)
                    {
                        var d = system.Values.DamageScales;
                        if (d.MaxIntegrity > 0 && blockHp > d.MaxIntegrity)
                        {
                            outOfPew = true;
                            damagePool = 0;
                            continue;
                        }

                        if (d.Grids.Large >= 0 && largeGrid) damageScale *= d.Grids.Large;
                        else if (d.Grids.Small >= 0 && !largeGrid) damageScale *= d.Grids.Small;

                        MyDefinitionBase blockDef = null;
                        if (system.ArmorScaling)
                        {
                            blockDef = block.BlockDefinition;
                            var isArmor = AllArmorBaseDefinitions.Contains(blockDef);
                            if (isArmor && d.Armor.Armor >= 0) damageScale *= d.Armor.Armor;
                            else if (!isArmor && d.Armor.NonArmor >= 0) damageScale *= d.Armor.NonArmor;

                            if (isArmor && (d.Armor.Light >= 0 || d.Armor.Heavy >= 0))
                            {
                                var isHeavy = HeavyArmorBaseDefinitions.Contains(blockDef);
                                if (isHeavy && d.Armor.Heavy >= 0) damageScale *= d.Armor.Heavy;
                                else if (!isHeavy && d.Armor.Light >= 0) damageScale *= d.Armor.Light;
                            }
                        }
                        if (system.CustomDamageScales)
                        {
                            if (blockDef == null) blockDef = block.BlockDefinition;
                            float modifier;
                            var found = system.CustomBlockDefinitionBasesToScales.TryGetValue(blockDef, out modifier);

                            if (found) damageScale *= modifier;
                            else if (system.Values.DamageScales.Custom.IgnoreAllOthers) continue;
                        }
                    }

                    var blockIsRoot = block == rootBlock;
                    var primaryDamage = primeDamage || blockIsRoot;

                    if (damagePool <= 0 && primaryDamage || objectsHit >= maxObjects) break;

                    var scaledDamage = damagePool * damageScale;
                    if (primaryDamage)
                    {
                        if (countBlocksAsObjects) objectsHit++;

                        if (scaledDamage <= blockHp)
                        {
                            outOfPew = true;
                            damagePool = 0;
                        }
                        else
                        {
                            _destroyedSlims.Add(block);
                            damagePool -= blockHp;
                        }
                    }
                    else
                    {
                        scaledDamage = areaEffectDmg * damageScale;
                        if (scaledDamage >= blockHp) _destroyedSlims.Add(block);
                    }

                    block.DoDamage(scaledDamage, damageType, true, null, attackerId);
                    var theEnd = damagePool <= 0 || objectsHit >= maxObjects;

                    if (explosive && (!detonateOnEnd && blockIsRoot || detonateOnEnd && theEnd))
                    {
                        var rootPos = grid.GridIntegerToWorld(rootBlock.Position);
                        if (areaEffectDmg > 0) SUtils.CreateMissileExplosion(this, areaEffectDmg, areaRadius, rootPos, hitEnt.Intersection.Direction, attacker, grid, system, true);
                        if (detonateOnEnd && theEnd) SUtils.CreateMissileExplosion(this, detonateDmg, detonateRadius, rootPos, hitEnt.Intersection.Direction, attacker, grid, system, true);
                    }
                    else if (!nova)
                    {
                        if (hitMass > 0 && blockIsRoot)
                        {
                            var speed = system.Values.Ammo.Trajectory.DesiredSpeed > 0 ? system.Values.Ammo.Trajectory.DesiredSpeed : 1;
                            ApplyProjectileForce(grid, grid.GridIntegerToWorld(rootBlock.Position), hitEnt.Intersection.Direction, (hitMass * speed));
                        }

                        if (radiantBomb && theEnd)
                        {
                            nova = true;
                            i--;
                            t.BaseDamagePool = 0;
                            t.ObjectsHit = maxObjects;
                            objectsHit = int.MinValue;
                            var aInfo = system.Values.Ammo.AreaEffect;
                            var dInfo = aInfo.Detonation;

                            if (dInfo.DetonationDamage > 0) damagePool = detonateDmg;
                            else if (aInfo.AreaEffectDamage > 0) damagePool = areaEffectDmg;
                            else damagePool = scaledDamage;
                            break;
                        }
                    }
                }
            }
            if (!countBlocksAsObjects) t.ObjectsHit += 1;
            if (!nova)
            {
                t.BaseDamagePool = damagePool;
                t.ObjectsHit = objectsHit;
            }
            if (radiantCascade || nova) _slimsSortedList.Clear();
            hitEnt.Blocks.Clear();
        }

        private void DamageDestObj(HitEntity hitEnt, ProInfo info)
        {
            var entity = hitEnt.Entity;
            var destObj = hitEnt.Entity as IMyDestroyableObject;
            var system = info.System;

            if (destObj == null || entity == null) return;
            var shieldHeal = system.Values.DamageScales.Shields.Type == ShieldDefinition.ShieldType.Heal;
            var shieldByPass = system.Values.DamageScales.Shields.Type == ShieldDefinition.ShieldType.Bypass;

            //projectile.ObjectsHit++;
            var attackerId = info.Target.FiringCube.EntityId;

            var objHp = destObj.Integrity;
            var integrityCheck = system.Values.DamageScales.MaxIntegrity > 0;
            if (integrityCheck && objHp > system.Values.DamageScales.MaxIntegrity || shieldHeal)
            {
                info.BaseDamagePool = 0;
                return;
            }

            var character = hitEnt.Entity as IMyCharacter;
            float damageScale = 1;
            if (system.VirtualBeams) damageScale *= info.WeaponCache.Hits;
            if (character != null && system.Values.DamageScales.Characters >= 0)
                damageScale *= system.Values.DamageScales.Characters;

            var scaledDamage = info.BaseDamagePool * damageScale;
            if (scaledDamage < objHp) info.BaseDamagePool = 0;
            else info.BaseDamagePool -= objHp;

            destObj.DoDamage(scaledDamage, !shieldByPass ? MyDamageType.Bullet : MyDamageType.Drill, true, null, attackerId);
            if (system.Values.Ammo.Mass > 0)
            {
                var speed = system.Values.Ammo.Trajectory.DesiredSpeed > 0 ? system.Values.Ammo.Trajectory.DesiredSpeed : 1;
                ApplyProjectileForce(entity, entity.PositionComp.WorldAABB.Center, hitEnt.Intersection.Direction, (system.Values.Ammo.Mass * speed));
            }
        }

        private void DamageProjectile(HitEntity hitEnt, ProInfo attacker)
        {
            var pTarget = hitEnt.Projectile;
            var system = attacker.System;
            if (pTarget == null) return;

            attacker.ObjectsHit++;
            var objHp = pTarget.Info.BaseHealthPool;
            var integrityCheck = system.Values.DamageScales.MaxIntegrity > 0;
            if (integrityCheck && objHp > system.Values.DamageScales.MaxIntegrity) return;

            float damageScale = 1;
            if (system.VirtualBeams) damageScale *= attacker.WeaponCache.Hits;

            var scaledDamage = attacker.BaseDamagePool * damageScale;

            if (scaledDamage >= objHp)
            {
                attacker.BaseDamagePool -= objHp;
                pTarget.Info.BaseHealthPool = 0;
                pTarget.State = Projectile.ProjectileState.Destroy;
            }
            else 
            {
                attacker.BaseDamagePool = 0;
                pTarget.Info.BaseHealthPool -= scaledDamage;

                if (attacker.DetonationDamage > 0 && system.Values.Ammo.AreaEffect.Detonation.DetonateOnEnd)
                {
                    var areaSphere = new BoundingSphereD(pTarget.Position, attacker.System.Values.Ammo.AreaEffect.Detonation.DetonationRadius);
                    foreach (var sTarget in attacker.Ai.LiveProjectile)
                    {
                        if (areaSphere.Contains(sTarget.Position) != ContainmentType.Disjoint)
                        {
                            if (attacker.DetonationDamage >= sTarget.Info.BaseHealthPool)
                            {
                                sTarget.Info.BaseHealthPool = 0;
                                sTarget.State = Projectile.ProjectileState.Destroy;
                            }
                            else sTarget.Info.BaseHealthPool -= attacker.DetonationDamage;
                        }
                    }
                }
            }
        }

        private void DamageVoxel(HitEntity hitEnt, ProInfo info)
        {
            var entity = hitEnt.Entity;
            var destObj = hitEnt.Entity as MyVoxelBase;
            var system = info.System;
            if (destObj == null || entity == null || !hitEnt.HitPos.HasValue) return;
            var shieldHeal = system.Values.DamageScales.Shields.Type == ShieldDefinition.ShieldType.Heal;
            if (!system.VoxelDamage || shieldHeal)
            {
                info.BaseDamagePool = 0;
                return;
            }

            using (destObj.Pin())
            {
                var detonateOnEnd = system.AmmoAreaEffect && system.Values.Ammo.AreaEffect.Detonation.DetonateOnEnd && system.Values.Ammo.AreaEffect.AreaEffect != AreaDamage.AreaEffectType.Radiant;

                info.ObjectsHit++;
                float damageScale = 1;
                if (system.VirtualBeams) damageScale *= info.WeaponCache.Hits;

                var scaledDamage = info.BaseDamagePool * damageScale;
                var oRadius = system.Values.Ammo.AreaEffect.AreaEffectRadius;
                var minTestRadius = info.DistanceTraveled - info.PrevDistanceTraveled;
                var tRadius = oRadius < minTestRadius ? minTestRadius : oRadius;
                var objHp = (int)MathHelper.Clamp(MathFuncs.VolumeCube(MathFuncs.LargestCubeInSphere(tRadius)), 1, double.MaxValue);
                if (tRadius > 5) objHp *= 5;
                if (scaledDamage < objHp)
                {
                    var reduceBy = objHp / scaledDamage;
                    oRadius /= reduceBy;
                    if (oRadius < 1) oRadius = 1;

                    info.BaseDamagePool = 0;
                }
                else
                {
                    info.BaseDamagePool -= objHp;
                    if (oRadius < minTestRadius) oRadius = minTestRadius;
                }
                destObj.PerformCutOutSphereFast(hitEnt.HitPos.Value, (float)oRadius, true);

                if (detonateOnEnd)
                {
                    var det = system.Values.Ammo.AreaEffect.Detonation;
                    var dRadius = det.DetonationRadius;
                    var dObjHp = (int)MathHelper.Clamp(MathFuncs.VolumeCube(MathFuncs.LargestCubeInSphere(dRadius)), 1, double.MaxValue);
                    if (dRadius > 5) dObjHp *= 5;
                    dObjHp *= 5;
                    var dDamage = det.DetonationDamage;
                    var reduceBy = dObjHp / dDamage;

                    dRadius /= reduceBy;
                    if (dRadius < 1.5) dRadius = 1.5f;
                    destObj.PerformCutOutSphereFast(hitEnt.HitPos.Value, dRadius, true);
                }
            }
        }

        public static void ApplyProjectileForce(MyEntity entity, Vector3D intersectionPosition, Vector3 normalizedDirection, float impulse)
        {
            if (entity.Physics == null || !entity.Physics.Enabled || entity.Physics.IsStatic || entity.Physics.Mass / impulse > 500)
                return;
            entity.Physics.AddForce(MyPhysicsForceType.APPLY_WORLD_IMPULSE_AND_WORLD_ANGULAR_IMPULSE, normalizedDirection * impulse, intersectionPosition, Vector3.Zero);
        }

        public void GetBlockSphereDb(MyCubeGrid grid, double areaRadius, out List<Vector3I> radiatedBlocks)
        {
            areaRadius = Math.Ceiling(areaRadius);

            if (grid.GridSizeEnum == MyCubeSize.Large)
            {
                if (areaRadius < 3) areaRadius = 3;
                LargeBlockSphereDb.TryGetValue(areaRadius, out radiatedBlocks);
            }
            else SmallBlockSphereDb.TryGetValue(areaRadius, out radiatedBlocks);
        }

        private void GenerateBlockSphere(MyCubeSize gridSizeEnum, double radiusInMeters)
        {
            var gridSizeInv = 2.0; // Assume small grid (1 / 0.5)
            if (gridSizeEnum == MyCubeSize.Large)
                gridSizeInv = 0.4; // Large grid (1 / 2.5)

            var radiusInBlocks = radiusInMeters * gridSizeInv;
            var radiusSq = radiusInBlocks * radiusInBlocks;
            var radiusCeil = (int)Math.Ceiling(radiusInBlocks);
            int i, j, k;
            var max = Vector3I.One * radiusCeil;
            var min = Vector3I.One * -radiusCeil;

            var blockSphereLst = _blockSpherePool.Get();
            for (i = min.X; i <= max.X; ++i)
                for (j = min.Y; j <= max.Y; ++j)
                    for (k = min.Z; k <= max.Z; ++k)
                        if (i * i + j * j + k * k < radiusSq)
                            blockSphereLst.Add(new Vector3I(i, j, k));

            blockSphereLst.Sort((a, b) => Vector3I.Dot(a, a).CompareTo(Vector3I.Dot(b, b)));
            if (gridSizeEnum == MyCubeSize.Large)
                LargeBlockSphereDb.Add(radiusInMeters, blockSphereLst);
            else
                SmallBlockSphereDb.Add(radiusInMeters, blockSphereLst);
        }

        private void ShiftAndPruneBlockSphere(MyCubeGrid grid, Vector3I center, List<Vector3I> sphereOfCubes, List<RadiatedBlock> slims)
        {
            slims.Clear(); // Ugly but super inlined V3I check
            var gMinX = grid.Min.X;
            var gMinY = grid.Min.Y;
            var gMinZ = grid.Min.Z;
            var gMaxX = grid.Max.X;
            var gMaxY = grid.Max.Y;
            var gMaxZ = grid.Max.Z;

            for (int i = 0; i < sphereOfCubes.Count; i++)
            {
                var v3ICheck = center + sphereOfCubes[i];
                var contained = gMinX <= v3ICheck.X && v3ICheck.X <= gMaxX && (gMinY <= v3ICheck.Y && v3ICheck.Y <= gMaxY) && (gMinZ <= v3ICheck.Z && v3ICheck.Z <= gMaxZ);
                if (!contained) continue;

                MyCube cube;
                if (grid.TryGetCube(v3ICheck, out cube))
                {
                    IMySlimBlock slim = cube.CubeBlock;
                    if (slim.Position == v3ICheck)
                        slims.Add(new RadiatedBlock { Center = center, Slim = slim, Position = v3ICheck });
                }
            }
        }

        static void GetIntVectorsInSphere(MyCubeGrid grid, Vector3I center, double radius, List<RadiatedBlock> points)
        {
            points.Clear();
            radius *= grid.GridSizeR;
            double radiusSq = radius * radius;
            int radiusCeil = (int)Math.Ceiling(radius);
            int i, j, k;
            for (i = -radiusCeil; i <= radiusCeil; ++i)
            {
                for (j = -radiusCeil; j <= radiusCeil; ++j)
                {
                    for (k = -radiusCeil; k <= radiusCeil; ++k)
                    {
                        if (i * i + j * j + k * k < radiusSq)
                        {
                            var vector3I = center + new Vector3I(i, j, k);
                            IMySlimBlock slim = grid.GetCubeBlock(vector3I);

                            if (slim != null)
                            {
                                var radiatedBlock = new RadiatedBlock
                                {
                                    Center = center, Slim = slim, Position = vector3I
                                };
                                points.Add(radiatedBlock);
                            }
                        }
                    }
                }
            }
        }

        private void GetIntVectorsInSphere2(MyCubeGrid grid, Vector3I center, double radius)
        {
            _slimsSortedList.Clear();
            radius *= grid.GridSizeR;
            var gridMin = grid.Min;
            var gridMax = grid.Max;
            double radiusSq = radius * radius;
            int radiusCeil = (int)Math.Ceiling(radius);
            int i, j, k;
            Vector3I max = Vector3I.Min(Vector3I.One * radiusCeil, gridMax - center);
            Vector3I min = Vector3I.Max(Vector3I.One * -radiusCeil, gridMin - center);

            for (i = min.X; i <= max.X; ++i)
            {
                for (j = min.Y; j <= max.Y; ++j)
                {
                    for (k = min.Z; k <= max.Z; ++k)
                    {
                        if (i * i + j * j + k * k < radiusSq)
                        {
                            var vector3I = center + new Vector3I(i, j, k);
                            IMySlimBlock slim = grid.GetCubeBlock(vector3I);

                            if (slim != null && slim.Position == vector3I)
                            {
                                var radiatedBlock = new RadiatedBlock
                                {
                                    Center = center, Slim = slim, Position = vector3I
                                };
                                _slimsSortedList.Add(radiatedBlock);
                            }
                        }
                    }
                }
            }
            _slimsSortedList.Sort((a, b) => Vector3I.Dot(a.Position, a.Position).CompareTo(Vector3I.Dot(b.Position, b.Position)));
        }

        public void GetBlocksInsideSphere(MyCubeGrid grid, Dictionary<Vector3I, IMySlimBlock> cubes, ref BoundingSphereD sphere, bool sorted, Vector3I center, bool checkTriangles = false)
        {
            if (grid.PositionComp == null) return;

            if (sorted) _slimsSortedList.Clear();
            else _slimsSet.Clear();

            var matrixNormalizedInv = grid.PositionComp.WorldMatrixNormalizedInv;
            Vector3D result;
            Vector3D.Transform(ref sphere.Center, ref matrixNormalizedInv, out result);
            var localSphere = new BoundingSphere(result, (float)sphere.Radius);
            var fromSphere2 = BoundingBox.CreateFromSphere(localSphere);
            var min = (Vector3D)fromSphere2.Min;
            var max = (Vector3D)fromSphere2.Max;
            var vector3I1 = new Vector3I((int)Math.Round(min.X * grid.GridSizeR), (int)Math.Round(min.Y * grid.GridSizeR), (int)Math.Round(min.Z * grid.GridSizeR));
            var vector3I2 = new Vector3I((int)Math.Round(max.X * grid.GridSizeR), (int)Math.Round(max.Y * grid.GridSizeR), (int)Math.Round(max.Z * grid.GridSizeR));
            var start = Vector3I.Min(vector3I1, vector3I2);
            var end = Vector3I.Max(vector3I1, vector3I2);
            if ((end - start).Volume() < cubes.Count)
            {
                var vector3IRangeIterator = new Vector3I_RangeIterator(ref start, ref end);
                var next = vector3IRangeIterator.Current;
                while (vector3IRangeIterator.IsValid())
                {
                    IMySlimBlock cube;
                    if (cubes.TryGetValue(next, out cube))
                    {
                        if (new BoundingBox(cube.Min * grid.GridSize - grid.GridSizeHalf, cube.Max * grid.GridSize + grid.GridSizeHalf).Intersects(localSphere))
                        {
                            var radiatedBlock = new RadiatedBlock
                            {
                                Center = center,
                                Slim = cube,
                                Position = cube.Position,
                            };
                            if (sorted) _slimsSortedList.Add(radiatedBlock);
                            else _slimsSet.Add(cube);
                        }
                    }
                    vector3IRangeIterator.GetNext(out next);
                }
            }
            else
            {
                foreach (var cube in cubes.Values)
                {
                    if (new BoundingBox(cube.Min * grid.GridSize - grid.GridSizeHalf, cube.Max * grid.GridSize + grid.GridSizeHalf).Intersects(localSphere))
                    {
                        var radiatedBlock = new RadiatedBlock
                        {
                            Center = center,
                            Slim = cube,
                            Position = cube.Position,
                        };
                        if (sorted) _slimsSortedList.Add(radiatedBlock);
                        else _slimsSet.Add(cube);
                    }
                }
            }
            if (sorted)
                _slimsSortedList.Sort((x, y) => Vector3I.DistanceManhattan(x.Position, x.Slim.Position).CompareTo(Vector3I.DistanceManhattan(y.Position, y.Slim.Position)));
        }

        public void GetBlocksInsideSphereBrute(MyCubeGrid grid, Vector3I center, ref BoundingSphereD sphere, bool sorted)
        {
            if (grid.PositionComp == null) return;

            if (sorted) _slimsSortedList.Clear();
            else _slimsSet.Clear();

            var matrixNormalizedInv = grid.PositionComp.WorldMatrixNormalizedInv;
            Vector3D result;
            Vector3D.Transform(ref sphere.Center, ref matrixNormalizedInv, out result);
            var localSphere = new BoundingSphere(result, (float)sphere.Radius);
            foreach (IMySlimBlock cube in grid.CubeBlocks)
            {
                if (new BoundingBox(cube.Min * grid.GridSize - grid.GridSizeHalf, cube.Max * grid.GridSize + grid.GridSizeHalf).Intersects(localSphere))
                {
                    var radiatedBlock = new RadiatedBlock
                    {
                        Center = center,
                        Slim = cube,
                        Position = cube.Position,
                    };
                    if (sorted) _slimsSortedList.Add(radiatedBlock);
                    else _slimsSet.Add(cube);
                }
            }
            if (sorted)
                _slimsSortedList.Sort((x, y) => Vector3I.DistanceManhattan(x.Position, x.Slim.Position).CompareTo(Vector3I.DistanceManhattan(y.Position, y.Slim.Position)));
        }
    }
}
