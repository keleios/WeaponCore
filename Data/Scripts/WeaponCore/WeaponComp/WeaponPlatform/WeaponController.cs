﻿using System;
using VRageMath;
using WeaponCore.Support;
using static WeaponCore.Support.WeaponComponent.TerminalControl;

namespace WeaponCore.Platform
{
    public partial class Weapon
    {
        public void AimBarrel(double azimuthChange, double elevationChange, bool moveAz = true, bool moveEl = true)
        {
            LastTrackedTick = Comp.Session.Tick;

            if (AiOnlyWeapon)
            {
                if (moveAz)
                {
                    bool rAz = false;
                    double absAzChange;
                    if (azimuthChange < 0)
                    {
                        absAzChange = azimuthChange * -1d;
                        rAz = true;
                    }
                    else
                        absAzChange = azimuthChange;

                    if (System.TurretMovement == WeaponSystem.TurretType.Full || System.TurretMovement == WeaponSystem.TurretType.AzimuthOnly)
                    {
                        if (absAzChange >= System.AzStep)
                        {
                            if (rAz)
                                AzimuthPart.Entity.PositionComp.LocalMatrix *= AzimuthPart.RevFullRotationStep;
                            else
                                AzimuthPart.Entity.PositionComp.LocalMatrix *= AzimuthPart.FullRotationStep;
                        }
                        else
                        {
                            AzimuthPart.Entity.PositionComp.LocalMatrix *= (AzimuthPart.ToTransformation * Matrix.CreateFromAxisAngle(AzimuthPart.RotationAxis, (float)-azimuthChange) * AzimuthPart.FromTransformation);
                        }
                    }
                }

                if (moveEl)
                {
                    bool rEl = false;
                    double absElChange;
                    if (elevationChange < 0)
                    {
                        absElChange = elevationChange * -1d;
                        rEl = true;
                    }
                    else
                        absElChange = elevationChange;

                    if (System.TurretMovement == WeaponSystem.TurretType.Full || System.TurretMovement == WeaponSystem.TurretType.ElevationOnly)
                    {
                        if (absElChange >= System.ElStep)
                        {
                            if (rEl)
                                ElevationPart.Entity.PositionComp.LocalMatrix *= ElevationPart.RevFullRotationStep;
                            else
                                ElevationPart.Entity.PositionComp.LocalMatrix *= ElevationPart.FullRotationStep;
                        }
                        else
                        {
                            ElevationPart.Entity.PositionComp.LocalMatrix *= (ElevationPart.ToTransformation * Matrix.CreateFromAxisAngle(ElevationPart.RotationAxis, (float)elevationChange) * ElevationPart.FromTransformation);
                        }
                    }
                }
            }
            else
            {   
                if (moveEl)
                    Comp.TurretBase.Elevation = (float)Elevation;

                if (moveAz)
                    Comp.TurretBase.Azimuth = (float)Azimuth;
            }

        }

        public void TurretHomePosition(object o = null)
        {
            if (Comp == null || Comp.Platform.State != MyWeaponPlatform.PlatformState.Ready || Comp.MyCube == null || Comp.MyCube.MarkedForClose || Comp.MyCube.Closed || Target == null) return;

            if (State.ManualShoot != TerminalActionState.ShootOff || Comp.UserControlled || Target.State == Target.Targets.Acquired)
            {
                ReturingHome = false;
                return;
            }
            Target.ExpiredTick = 0;

            var userControlled = o != null && (bool)o;
            if (userControlled && Comp.BaseType == WeaponComponent.BlockType.Turret)
            {
                Azimuth = Comp.TurretBase.Azimuth;
                Elevation = Comp.TurretBase.Elevation;
            }
            else if (!userControlled)
            {
                var azStep = System.AzStep;
                var elStep = System.ElStep;

                var oldAz = Azimuth;
                var oldEl = Elevation;

                if (oldAz > 0)
                    Azimuth = oldAz - azStep > 0 ? oldAz - azStep : 0;
                else if (oldAz < 0)
                    Azimuth = oldAz + azStep < 0 ? oldAz + azStep : 0;

                if (oldEl > 0)
                    Elevation = oldEl - elStep > 0 ? oldEl - elStep : 0;
                else if (oldEl < 0)
                    Elevation = oldEl + elStep < 0 ? oldEl + elStep : 0;


                AimBarrel(oldAz - Azimuth, oldEl - Elevation);
            }

            if (Azimuth > 0 || Azimuth < 0 || Elevation > 0 || Elevation < 0)
            {
                ReturingHome = true;
                IsHome = false;
                Comp.Session.FutureEvents.Schedule(TurretHomePosition, null, (userControlled ? 300u : 1u));
            }
            else
            {
                IsHome = true;
                ReturingHome = false;
            }
        }

        internal void UpdatePivotPos()
        {
            if (AzimuthPart?.Entity?.Parent == null || ElevationPart?.Entity == null || MuzzlePart?.Entity == null || Comp.Platform.State != MyWeaponPlatform.PlatformState.Ready) return;

            if (Comp.MatrixUpdateTick < Comp.Session.Tick && AzimuthOnBase)
            {
                Comp.MatrixUpdateTick = Comp.Session.Tick;
                Comp.CubeMatrix = Comp.MyCube.PositionComp.WorldMatrix;
            }

            var azimuthMatrix = AzimuthPart.Entity.PositionComp.WorldMatrix;
            var elevationMatrix = ElevationPart.Entity.PositionComp.WorldMatrix;
            var weaponCenter = MuzzlePart.Entity.PositionComp.WorldMatrix.Translation;
            var centerTestPos = azimuthMatrix.Translation + (azimuthMatrix.Down * 1);

            //Log.Line($"up: {AzimuthPart.RotationAxis} left: {ElevationPart.RotationAxis}");


            MyPivotUp = azimuthMatrix.Up;
            MyPivotDir = elevationMatrix.Forward;

            if (System.TurretMovement == WeaponSystem.TurretType.ElevationOnly)
            {
                var forward = Vector3D.Cross(elevationMatrix.Left, MyPivotUp);
                WeaponConstMatrix = new MatrixD { Forward = forward, Up = MyPivotUp, Left = elevationMatrix.Left };
            }
            else
            {
                var forward = AzimuthOnBase ? Comp.CubeMatrix.Forward : AzimuthPart.Entity.Parent.WorldMatrix.Forward;
                var left = Vector3D.Cross(MyPivotUp, forward);
                WeaponConstMatrix = new MatrixD { Forward = forward, Up = MyPivotUp, Left = left };
            }

            Vector3D axis = Vector3D.Cross(MyPivotUp, MyPivotDir);
            if (Vector3D.IsZero(axis))
                MyPivotPos = centerTestPos;
            else
            {
                Vector3D perpDir2 = Vector3D.Cross(MyPivotDir, axis);
                Vector3D point1To2 = weaponCenter - centerTestPos;
                MyPivotPos = centerTestPos + Vector3D.Dot(point1To2, perpDir2) / Vector3D.Dot(MyPivotUp, perpDir2) * MyPivotUp;
            }


            if (!Vector3D.IsZero(AimOffset))
                MyPivotPos += Vector3D.Rotate(AimOffset, new MatrixD { Forward = MyPivotDir, Left = elevationMatrix.Left, Up = elevationMatrix.Up });

            if (!Comp.Debug) return;
            MyCenterTestLine = new LineD(centerTestPos, centerTestPos + (MyPivotUp * 20));
            MyBarrelTestLine = new LineD(weaponCenter, weaponCenter + (MyPivotDir * 18));
            MyPivotTestLine = new LineD(MyPivotPos, MyPivotPos - (WeaponConstMatrix.Left * 10));
            MyAimTestLine = new LineD(MyPivotPos, MyPivotPos + (MyPivotDir * 20));
            if (Target.State == Target.Targets.Acquired)
                MyShootAlignmentLine = new LineD(MyPivotPos, Target.TargetPos);
        }

        internal void UpdateWeaponHeat(object o = null)
        {
            try
            {
                var currentHeat = State.Heat;
                currentHeat = currentHeat - ((float)HsRate / 3) > 0 ? currentHeat - ((float)HsRate / 3) : 0;
                var set = currentHeat - LastHeat > 0.001 || currentHeat - LastHeat < 0.001;

                Timings.LastHeatUpdateTick = Comp.Session.Tick;

                if (!Comp.Session.DedicatedServer)
                {
                    var heatPercent = currentHeat / System.MaxHeat;

                    if (set && heatPercent > .33)
                    {
                        if (heatPercent > 1) heatPercent = 1;

                        heatPercent -= .33f;

                        var intensity = .7f * heatPercent;

                        var color = Comp.Session.HeatEmissives[(int)(heatPercent * 100)];

                        for(int i = 0; i < HeatingParts.Count; i++)
                            HeatingParts[i]?.SetEmissiveParts("Heating", color, intensity);
                    }
                    else if (set)
                        for(int i = 0; i < HeatingParts.Count; i++)
                            HeatingParts[i]?.SetEmissiveParts("Heating", Color.Transparent, 0);

                    LastHeat = currentHeat;
                }

                if (set && System.DegRof && State.Heat >= (System.MaxHeat * .8))
                {
                    var systemRate = System.RateOfFire * Comp.Set.Value.RofModifier;
                    var barrelRate = System.BarrelSpinRate * Comp.Set.Value.RofModifier;
                    var heatModifier = MathHelper.Lerp(1f, .25f, State.Heat / System.MaxHeat);

                    systemRate *= heatModifier;

                    if (systemRate < 1)
                        systemRate = 1;

                    RateOfFire = (int)systemRate;
                    BarrelSpinRate = (int)barrelRate;
                    TicksPerShot = (uint)(3600f / RateOfFire);

                    if (System.HasBarrelRotation) UpdateBarrelRotation();
                    CurrentlyDegrading = true;
                }
                else if (set && CurrentlyDegrading)
                {
                    CurrentlyDegrading = false;
                    RateOfFire = (int)(System.RateOfFire * Comp.Set.Value.RofModifier);
                    BarrelSpinRate = (int)(System.BarrelSpinRate * Comp.Set.Value.RofModifier);
                    TicksPerShot = (uint)(3600f / RateOfFire);

                    if (System.HasBarrelRotation) UpdateBarrelRotation();
                }

                if (_fakeHeatTick * 30 == 60)
                {
                    Comp.CurrentHeat = Comp.CurrentHeat >= HsRate ? Comp.CurrentHeat - HsRate : 0;
                    State.Heat = State.Heat >= HsRate ? State.Heat - HsRate : 0;

                    if (State.Overheated && State.Heat <= (System.MaxHeat * System.WepCoolDown))
                    {
                        //ShootDelayTick = CurLgstAnimPlaying.Reverse ? (uint)CurLgstAnimPlaying.CurrentMove : (uint)((CurLgstAnimPlaying.NumberOfMoves - 1) - CurLgstAnimPlaying.CurrentMove);
                        if (CurLgstAnimPlaying != null)
                            Timings.ShootDelayTick = Comp.Session.Tick + (CurLgstAnimPlaying.Reverse ? (uint)CurLgstAnimPlaying.CurrentMove : (uint)((CurLgstAnimPlaying.NumberOfMoves - 1) - CurLgstAnimPlaying.CurrentMove));
                        
                        EventTriggerStateChanged(EventTriggers.Overheated, false);
                        State.Overheated = false;
                    }

                    _fakeHeatTick = -1;
                }

                //Log.Line($"currentHeat :{currentHeat} _fakeHeatTick: {_fakeHeatTick}");

                if (State.Heat > 0)
                {
                    _fakeHeatTick++;
                    Comp.Session.FutureEvents.Schedule(UpdateWeaponHeat, null, 20);
                }
                else
                {
                    _fakeHeatTick = 0;
                    HeatLoopRunning = false;
                    Timings.LastHeatUpdateTick = 0;
                }
            }
            catch (Exception ex) { Log.Line($"Exception in UpdateWeaponHeat: {ex} - {System == null}- Comp:{Comp == null} - State:{Comp?.State == null} - Set:{Comp?.Set == null} - Session:{Comp?.Session == null} - Value:{Comp?.State?.Value == null} - Weapons:{Comp?.State?.Value?.Weapons[WeaponId] == null}"); }
        }

        internal void SpinBarrel(bool spinDown = false)
        {
            MuzzlePart.Entity.PositionComp.LocalMatrix *= BarrelRotationPerShot[BarrelRate];

            if (PlayTurretAv && RotateEmitter != null && !RotateEmitter.IsPlaying)
                RotateEmitter.PlaySound(RotateSound, true, false, false, false, false, false);

            if (_spinUpTick <= Comp.Session.Tick && spinDown)
            {
                _spinUpTick = Comp.Session.Tick + _ticksBeforeSpinUp;
                BarrelRate--;
            }

            if (BarrelRate < 0)
            {
                BarrelRate = 0;
                BarrelSpinning = false;

                if (PlayTurretAv && RotateEmitter != null && RotateEmitter.IsPlaying)
                    RotateEmitter.StopSound(true);
            }
            else BarrelSpinning = true;
        }
    }
}
