﻿using System.Collections.Generic;
using System.Xml.Serialization;
using Sandbox.Game.Entities;
using VRage.Game.Entity;
using VRage.Utils;
using VRageMath;
using static WeaponCore.Support.WeaponDefinition;

namespace WeaponCore.Support
{
    public class DefinitionSet
    {
        [XmlElement(nameof(Block), typeof(Block))]
        public List<Block> Beams = new List<Block>();
    }

    public struct SerializedTurretDef
    {
        public List<KeyValuePair<string, List<KeyValuePair<string, TurretParts>>>> TurretMap;
    }

    public struct TurretDefinition
    {
        public readonly Dictionary<string, TurretParts> TurretMap;

        public TurretDefinition(IEnumerable<KeyValuePair<string, List<KeyValuePair<string, TurretParts>>>> mapList)
        {
            TurretMap = new Dictionary<string, TurretParts>();
            foreach (var id in mapList)
            {
                foreach (var tPart in id.Value)
                {
                    TurretMap.Add(tPart.Key, new TurretParts(tPart.Key, tPart.Value.BarrelGroup));
                }
            }
        }
    }

    public struct TurretParts
    {
        public readonly string WeaponType;
        public readonly string BarrelGroup;

        internal TurretParts(string weaponType, string barrelGroup)
        {
            WeaponType = weaponType;
            BarrelGroup = barrelGroup;
        }
    }
    
    public struct BarrelGroup
    {
        public List<string> Barrels;
    }

    public struct WeaponDefinition
    {
        public enum EffectType
        {
            Spark,
            Lance,
            Orb,
            Custom
        }

        internal enum GuidanceType
        {
            None,
            Remote,
            Seeking,
            Lock,
            Smart
        }

        internal enum ShieldType
        {
            Bypass,
            Emp,
            Energy,
            Kinetic
        }

        internal bool TurretMode;
        internal bool TrackTarget;
        internal bool HasAreaEffect;
        internal bool HasThermalEffect;
        internal bool HasKineticEffect;
        internal bool SkipAcceleration;
        internal bool UseRandomizedRange;
        internal bool ShieldHitDraw;
        internal bool RealisticDamage;
        internal bool LineTrail;
        internal bool ParticleTrail;
        internal int RotateBarrelAxis; 
        internal int ReloadTime;
        internal int RateOfFire;
        internal int BarrelsPerShot;
        internal int SkipBarrels;
        internal int ShotsPerBarrel;
        internal int HeatPerRoF;
        internal int MaxHeat;
        internal int HeatSinkRate;
        internal int MuzzleFlashLifeSpan;
        internal float Mass;
        internal float Health;
        internal float LineLength;
        internal float LineWidth;
        internal float InitalSpeed;
        internal float AccelPerSec;
        internal float DesiredSpeed;
        internal float RotateSpeed;
        internal float SpeedVariance;
        internal float MaxTrajectory;
        internal float BackkickForce;
        internal float DeviateShotAngle;
        internal float ReleaseTimeAfterFire;
        internal float RangeMultiplier;
        internal float ThermalDamage;
        internal float KeenScaler;
        internal float AreaEffectYield;
        internal float AreaEffectRadius;
        internal float ShieldDmgMultiplier;
        internal float DefaultDamage;
        internal float ComputedBaseDamage;
        internal float VisualProbability;
        internal float ParticleRadiusMultiplier;
        internal float AmmoTravelSoundRange;
        internal float AmmoTravelSoundVolume;
        internal float AmmoHitSoundRange;
        internal float AmmoHitSoundVolume;
        internal float ReloadSoundRange;
        internal float ReloadSoundVolume;
        internal float FiringSoundRange;
        internal float FiringSoundVolume;
        internal MyStringId PhysicalMaterial;
        internal MyStringId ModelName;
        internal Vector4 TrailColor;
        internal Vector4 ParticleColor;
        internal ShieldType ShieldDamage;
        internal EffectType Effect;
        internal GuidanceType Guidance;
        internal MySoundPair AmmoTravelSound;
        internal MySoundPair AmmoHitSound;
        internal MySoundPair ReloadSound;
        internal MySoundPair FiringSound;
        internal string CustomEffect;
    }
 
    public struct WeaponSystem
    {
        public readonly MyStringHash PartName;
        public readonly WeaponDefinition WeaponType;
        public readonly string WeaponName;
        public readonly string[] Barrels;

        public WeaponSystem(MyStringHash partName, WeaponDefinition weaponType, string weaponName, string[] barrels)
        {
            PartName = partName;
            WeaponType = weaponType;
            WeaponName = weaponName;
            Barrels = barrels;
        }
    }

    public struct WeaponStructure
    {
        public readonly Dictionary<MyStringHash, WeaponSystem> WeaponSystems;
        public readonly MyStringHash[] PartNames;
        public readonly bool MultiParts;

        public WeaponStructure(KeyValuePair<string, TurretDefinition> tDef, Dictionary<string, WeaponDefinition> wDef, Dictionary<string, BarrelGroup> bDef)
        {
            var map = tDef.Value.TurretMap;
            var numOfParts = map.Count;
            MultiParts = numOfParts > 1;

            var names = new MyStringHash[numOfParts];
            var mapIndex = 0;
            WeaponSystems = new Dictionary<MyStringHash, WeaponSystem>(MyStringHash.Comparer);
            foreach (var w in map)
            {
                var myNameHash = MyStringHash.GetOrCompute(w.Key);
                names[mapIndex] = myNameHash;
                var myBarrels = bDef[w.Value.BarrelGroup].Barrels;
                var barrelStrings = new string[myBarrels.Count];
                for (int i = 0; i < myBarrels.Count; i++)
                    barrelStrings[i] = myBarrels[i];
                var weaponTypeName = w.Value.WeaponType;

                var weaponDef = wDef[weaponTypeName];

                weaponDef.DeviateShotAngle = MathHelper.ToRadians(weaponDef.DeviateShotAngle);
                weaponDef.HasAreaEffect = weaponDef.AreaEffectYield > 0 && weaponDef.AreaEffectRadius > 0;
                weaponDef.SkipAcceleration = weaponDef.AccelPerSec > 0;
                if (weaponDef.RealisticDamage)
                {
                    weaponDef.HasKineticEffect = weaponDef.Mass > 0 && weaponDef.DesiredSpeed > 0;
                    weaponDef.HasThermalEffect = weaponDef.ThermalDamage > 0;
                    var kinetic = ((weaponDef.Mass / 2) * (weaponDef.DesiredSpeed * weaponDef.DesiredSpeed) / 1000) * weaponDef.KeenScaler;
                    weaponDef.ComputedBaseDamage = kinetic + weaponDef.ThermalDamage;
                }
                else weaponDef.ComputedBaseDamage = weaponDef.DefaultDamage; // For the unbelievers. 

                WeaponSystems.Add(myNameHash, new WeaponSystem(myNameHash, weaponDef, weaponTypeName, barrelStrings));

                mapIndex++;
            }
            PartNames = names;
        }
    }

    public class Shrinking
    {
        internal WeaponDefinition WepDef;
        internal Vector3D Position;
        internal Vector3D Direction;
        internal double Length;
        internal int ReSizeSteps;
        internal double LineReSizeLen;
        internal int ShrinkStep;

        internal void Init(WeaponDefinition wepDef, LineD line, int reSizeSteps, double lineReSizeLen)
        {
            WepDef = wepDef;
            Position = line.To;
            Direction = line.Direction;
            Length = line.Length;
            ReSizeSteps = reSizeSteps;
            LineReSizeLen = lineReSizeLen;
            ShrinkStep = reSizeSteps;
        }

        internal LineD? GetLine()
        {
            if (ShrinkStep-- <= 0) return null;
            return new LineD(Position + -(Direction * (ShrinkStep * LineReSizeLen)), Position);
        }
    }

    public struct WeaponHit
    {
        public readonly Logic Logic;
        public readonly Vector3D HitPos;
        public readonly float Size;
        public readonly EffectType Effect;

        public WeaponHit(Logic logic, Vector3D hitPos, float size, EffectType effect)
        {
            Logic = logic;
            HitPos = hitPos;
            Size = size;
            Effect = effect;
        }
    }

    public struct TargetInfo
    {
        public enum TargetType
        {
            Player,
            Grid,
            Other
        }

        public readonly MyEntity Entity;
        public readonly double Distance;
        public readonly float Size;
        public readonly TargetType Type;
        public TargetInfo(MyEntity entity, double distance, float size, TargetType type)
        {
            Entity = entity;
            Distance = distance;
            Size = size;
            Type = type;
        }
    }

    public struct BlockInfo
    {
        public enum BlockType
        {
            Player,
            Grid,
            Other
        }

        public readonly MyEntity Entity;
        public readonly double Distance;
        public readonly float Size;
        public readonly BlockType Type;
        public BlockInfo(MyEntity entity, double distance, float size, BlockType type)
        {
            Entity = entity;
            Distance = distance;
            Size = size;
            Type = type;
        }
    }
}
