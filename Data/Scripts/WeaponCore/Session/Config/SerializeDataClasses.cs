﻿using ProtoBuf;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game.Entity;
using VRageMath;
using WeaponCore.Support;
using static WeaponCore.Support.GridAi;
using static WeaponCore.Support.Target;

namespace WeaponCore
{

    public enum PacketType
    {
        CompStateUpdate,
        CompSettingsUpdate,
        TargetUpdate,
        FakeTargetUpdate,
        ClientMouseEvent,
        ActiveControlUpdate
    }

    [ProtoContract]
    [ProtoInclude(4, typeof(StatePacket))]
    [ProtoInclude(5, typeof(SettingPacket))]
    [ProtoInclude(6, typeof(TargetPacket))]
    [ProtoInclude(7, typeof(MouseInputPacket))]
    [ProtoInclude(8, typeof(LookupUpdatePacket))]
    [ProtoInclude(9, typeof(FakeTargetPacket))] 
    public class Packet
    {
        [ProtoMember(1)] internal long EntityId;
        [ProtoMember(2)] internal ulong SenderId;
        [ProtoMember(3)] internal PacketType PType;        
    }

    [ProtoContract]
    public class StatePacket : Packet
    {
        [ProtoMember(1)] internal CompStateValues Data = null;

        public StatePacket() { }
    }

    [ProtoContract]
    public class SettingPacket : Packet
    {
        [ProtoMember(1)] internal CompSettingsValues Data = null;
        public SettingPacket() { }
    }

    [ProtoContract]
    public class TargetPacket : Packet
    {
        [ProtoMember(1)] internal TransferTarget TargetData = null;
        [ProtoMember(2)] internal WeaponSyncValues WeaponData;
        [ProtoMember(3)] internal WeaponTimings Timmings = null;
        public TargetPacket() { }
    }

    [ProtoContract]
    public class FakeTargetPacket : Packet
    {
        [ProtoMember(1)] internal FakeTarget Data = null;
        public FakeTargetPacket() { }
    }

    [ProtoContract]
    public class MouseInputPacket : Packet
    {
        [ProtoMember(1)] internal MouseState Data = null;
        public MouseInputPacket() { }
    }

    [ProtoContract]
    public class LookupUpdatePacket : Packet
    {
        [ProtoMember(1)] internal bool Data;
        public LookupUpdatePacket() { }
    }

    [ProtoContract]
    internal class MouseState
    {
        [ProtoMember(1)] internal bool MouseButtonLeft;
        [ProtoMember(2)] internal bool MouseButtonMiddle;
        [ProtoMember(3)] internal bool MouseButtonRight;
    }

    [ProtoContract]
    public class TransferTarget
    {
        [ProtoMember(1)] internal long EntityId;
        [ProtoMember(2)] internal bool IsProjectile;
        [ProtoMember(3)] internal bool IsFakeTarget;
        [ProtoMember(4)] internal Vector3D TargetPos;
        [ProtoMember(5)] internal double HitShortDist;
        [ProtoMember(6)] internal double OrigDistance;
        [ProtoMember(7)] internal long TopEntityId;
        [ProtoMember(8)] internal Targets State = Targets.Expired;
        [ProtoMember(9)] internal int WeaponId;

        internal void SyncTarget(Target target)
        {
            var entity = MyEntities.GetEntityByIdOrDefault(EntityId);
            target.Entity = entity;
            target.IsProjectile = IsProjectile;
            target.IsFakeTarget = IsFakeTarget;
            target.TargetPos = TargetPos;
            target.HitShortDist = HitShortDist;
            target.OrigDistance = OrigDistance;
            target.TopEntityId = TopEntityId;
            target.State = State;
        }

        public TransferTarget()
        {
        }
    }

   

    /*[ProtoInclude(3, typeof(DataCompState))]
    [ProtoContract]
    public abstract class PacketBase
    {
        [ProtoMember(1)] public ulong SenderId;

        [ProtoMember(2)] public long EntityId;

        private MyEntity _ent;

        public MyEntity Entity
        {
            get
            {
                if (EntityId == 0) return null;

                if (_ent == null) _ent = MyEntities.GetEntityById(EntityId, true);

                if (_ent == null || _ent.MarkedForClose) return null;
                return _ent;
            }
        }

        public PacketBase(long entityId = 0)
        {
            SenderId = MyAPIGateway.Multiplayer.MyId;
            EntityId = entityId;
        }

        public abstract bool Received(bool isServer);
    }
    
    [ProtoContract]
    public class DataCompState : PacketBase
    {
        public DataCompState()
        {
        } // Empty constructor required for deserialization

        [ProtoMember(1)] public CompStateValues State = null;

        public DataCompState(long entityId, CompStateValues state) : base(entityId)
        {
            State = state;
        }

        public override bool Received(bool isServer)
        {
            if (!isServer)
            {
                if (Entity == null) return false;
                var comp = Entity.Components.Get<WeaponComponent>();
                comp?.UpdateState(State);
                return false;
            }
            return true;
        }
    }

    [ProtoContract]
    public class DataCompSettings : PacketBase
    {
        public DataCompSettings()
        {
        } // Empty constructor required for deserialization

        [ProtoMember(1)] public CompSettingsValues Settings = null;

        public DataCompSettings(long entityId, CompSettingsValues settings) : base(entityId)
        {
            Settings = settings;
        }

        public override bool Received(bool isServer)
        {
            if (Entity == null) return false;
            var comp = Entity.Components.Get<WeaponComponent>();
            comp?.UpdateSettings(Settings);
            return isServer;
        }
    }*/
}
