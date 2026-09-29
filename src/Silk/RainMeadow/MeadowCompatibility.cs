#if RAINMEADOW
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using RainMeadow;
using Tinker.Silk.Bridge;
using UnityEngine;
using static Tinker.Silk.Bridge.BridgeModeState;

namespace tinker.Silk.RainMeadow
{
    // Every entry called by the optional facade is non-inlined. No RM assembly is
    // resolved by ordinary offline input, physics, or Plugin.OnModsInit methods.
    internal static class MeadowCompatibility
    {
        private static int nextBridgeNumber;
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static bool IsOnline() => OnlineManager.lobby != null;

        private static OnlinePhysicalObject OnlineObject(PhysicalObject obj)
        {
            if (obj?.abstractPhysicalObject == null) return null;
            OnlinePhysicalObject.map.TryGetValue(obj.abstractPhysicalObject, out var online);
            return online;
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static bool CanSimulate(PhysicalObject obj) => !IsOnline() || OnlineObject(obj)?.isMine == true;

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static bool CanMoveObject(PhysicalObject obj)
        {
            if (!IsOnline()) return true;
            var online = OnlineObject(obj);
            if (online == null) return false;
            if (online.isMine) return true;
            // Use Meadow's ownership arbitration; do not write a remote object's chunks.
            if (online.isTransferable && !online.isPending && obj.grabbedBy.Count == 0) online.Request();
            return false;
        }

        internal static OnlineEntity.EntityId ObjectId(PhysicalObject obj) => OnlineObject(obj)?.id;
        internal static PhysicalObject ResolveObject(OnlineEntity.EntityId id, Room room)
        {
            var obj = (id?.FindEntity(true) as OnlinePhysicalObject)?.apo.realizedObject;
            return obj?.room == room ? obj : null;
        }
        internal static OnlineEntity.EntityId BridgeId(SilkBridge bridge) => bridge == null || bridge.NetworkNumber == 0 ? null :
            new OnlineEntity.EntityId(bridge.NetworkOwner, OnlineEntity.EntityId.IdType.custom, bridge.NetworkNumber);
        internal static bool Matches(SilkBridge bridge, OnlineEntity.EntityId id) => id != null &&
            bridge.NetworkNumber == id.id && bridge.NetworkOwner == id.originalOwner;

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void AttachSilkData(Player player)
        {
            if (!IsOnline() || !tinkerSilkData.IsTinkerPlayer(player)) return;
            var online = OnlineObject(player);
            if (online?.isMine == true && !online.TryGetData<TinkerSilkEntityData>(out _))
                online.AddData(new TinkerSilkEntityData());
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static bool HasSilkData(Player player) => IsOnline() && OnlineObject(player) is OnlinePhysicalObject online &&
            online.TryGetData<TinkerSilkEntityData>(out _);

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void PushSilkState(Player player, SilkPhysics silk) => AttachSilkData(player);

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static bool PullSilkState(Player player, SilkPhysics silk)
        {
            if (!IsOnline()) return false;
            var online = OnlineObject(player);
            if (online == null || online.isMine || !online.TryGetData<TinkerSilkEntityData>(out var data) ||
                data.Snapshot == null || data.Snapshot.room != player.room?.abstractRoom.name)
            {
                if (online != null && online.TryGetData<TinkerSilkEntityData>(out var stale)) stale.AppliedPlayer = null;
                silk.ResetNetworkSilk();
                SilkBridgeManager.GetBridgeModeState(player).ApplyNetworkPreview(false, false, Vector2.zero, Vector2.zero);
                return false;
            }
            if (data.AppliedPlayer == player && data.AppliedRevision == data.Revision)
            {
                silk.attachedObject = ResolveObject(data.Snapshot.attachedObject, player.room);
                return true;
            }
            var state = data.Snapshot;
            if (state.mode < 0 || state.mode > (int)SilkMode.Retracting || !SilkNetworkCurve.Finite(state.tip) ||
                !SilkNetworkCurve.Finite(state.terrain) || !SilkNetworkCurve.Finite(state.ropeLength) ||
                !SilkNetworkCurve.Finite(state.requestedLength) || !SilkNetworkCurve.Finite(state.energy) ||
                !SilkNetworkCurve.Finite(state.bridgeStart) || !SilkNetworkCurve.Finite(state.bridgeTip) ||
                state.bends == null || state.bends.Length > 50 || state.bends.Any(p => !SilkNetworkCurve.Finite(p))) return false;
            data.AppliedPlayer = player;
            data.AppliedRevision = data.Revision;
            silk.ApplyNetworkSilk((SilkMode)state.mode, state.tip, state.terrain,
                Mathf.Clamp(state.ropeLength, 0f, 1200f), Mathf.Clamp(state.requestedLength, 0f, 1200f),
                state.pullingObject, Mathf.Clamp(state.superJumpTimer, 0, 40), state.instantDisappear,
                state.shotSequence, state.bends, ResolveObject(state.attachedObject, player.room));
            tinkerSilkData.ApplyNetworkEnergy(player, state.energy, state.exhausted);
            SilkBridgeManager.GetBridgeModeState(player).ApplyNetworkPreview(state.building, state.firingBridge,
                state.bridgeStart, state.bridgeTip);
            return true;
        }

        private static RoomSession Session(Room room)
        {
            if (!IsOnline() || room?.abstractRoom == null) return null;
            RoomSession.map.TryGetValue(room.abstractRoom, out var session);
            return session;
        }
        private static SilkRoomData Data(RoomSession session)
        {
            if (session.TryGetData<SilkRoomData>(out var data)) return data;
            return session.isOwner ? session.AddData(new SilkRoomData()) : null;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static bool UpdateRoom(Room room)
        {
            if (!IsOnline()) return false;
            var session = Session(room);
            if (session == null || !session.isAvailable) return true;
            var data = Data(session);
            if (data == null) return true; // wait for the first authoritative room snapshot
            var bridges = SilkBridgeManager.EnsureBridgesInRoom(room);
            bool newRoom = data.AppliedRoom != room;
            if (newRoom || ((!session.isOwner || !data.WasOwner) && data.Applied != data.Snapshot))
            {
                ApplyRoomSnapshot(room, bridges, data.Snapshot, !newRoom);
                data.Applied = data.Snapshot;
            }
            data.AppliedRoom = room;
            if (data.Snapshot?.bridges?.list != null)
                foreach (var state in data.Snapshot.bridges.list)
                {
                    var bridge = bridges.Find(b => Matches(b, state.id));
                    if (bridge == null) continue;
                    if (bridge.startAnchor.type == BridgeAnchor.AnchorType.PhysicalObject)
                        bridge.startAnchor.attachedObject = ResolveObject(state.start.target, room);
                    if (bridge.endAnchor.type == BridgeAnchor.AnchorType.PhysicalObject)
                        bridge.endAnchor.attachedObject = ResolveObject(state.end.target, room);
                }
            if (session.isOwner)
            {
                foreach (var bridge in bridges) bridge.BecomeNetworkOwner();
                for (int i = data.Loads.Count - 1; i >= 0; i--)
                {
                    var load = data.Loads[i];
                    var bridge = bridges.Find(b => Matches(b, load.Bridge));
                    if (--load.Frames < 0 || bridge == null || !session.participants.Contains(load.Sender))
                    { data.Loads.RemoveAt(i); continue; }
                    bridge.ApplyForceAt(load.Point, load.Force, 40f);
                }
                data.WasOwner = true;
                SilkBridge.UpdateNetwork(bridges);
            }
            else
            {
                data.WasOwner = false;
                data.Loads.Clear();
                foreach (var bridge in bridges) bridge.UpdateNetworkReplica();
                if (room.game.clock % 4 == 0)
                {
                    foreach (var pair in data.Forces)
                    {
                        if (!pair.Key.IsActive) continue;
                        var load = pair.Value;
                        session.owner.InvokeRPC(BridgeForce, session, BridgeId(pair.Key),
                            load.Point / Math.Max(1, load.Samples), Vector2.ClampMagnitude(load.Force / 4f, 30f));
                    }
                    data.Forces.Clear();
                }
            }
            return true;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void UnloadRoom(Room room)
        {
            var session = Session(room);
            if (session == null || !session.TryGetData<SilkRoomData>(out var data)) return;
            if (session.isOwner && data.AppliedRoom == room)
                data.Snapshot = new SilkRoomData.SilkRoomState(SilkBridgeManager.GetBridgesInRoom(room));
            data.AppliedRoom = null;
            data.Applied = null;
            data.WasOwner = false;
            data.Forces.Clear();
            data.Loads.Clear();
        }

        private static void ApplyRoomSnapshot(Room room, List<SilkBridge> bridges, SilkRoomData.SilkRoomState snapshot, bool animate)
        {
            if (snapshot?.bridges?.list == null) return;
            var states = snapshot.bridges.list.Where(s => s != null && s.Valid).Take(SilkRoomData.MaxBridges).ToList();
            for (int i = bridges.Count - 1; i >= 0; i--)
            {
                if (states.Any(s => Matches(bridges[i], s.id))) continue;
                bridges[i].RemoveNetworkBridge(animate);
                bridges.RemoveAt(i);
            }
            // Parents normally precede children; repeat to handle reordered delta additions.
            var pending = new List<SilkBridgeState>(states.Where(s => !bridges.Any(b => Matches(b, s.id))));
            for (int pass = 0; pass < states.Count && pending.Count > 0; pass++)
            {
                for (int i = pending.Count - 1; i >= 0; i--)
                {
                    var state = pending[i];
                    var start = state.start.Resolve(room, bridges);
                    var end = state.end.Resolve(room, bridges);
                    if ((start.type == BridgeAnchor.AnchorType.BridgeSegment && start.attachedBridge == null) ||
                        (end.type == BridgeAnchor.AnchorType.BridgeSegment && end.attachedBridge == null)) continue;
                    var bridge = new SilkBridge(start, end, room, state.length, state.segments + 1)
                    { NetworkOwner = state.id.originalOwner, NetworkNumber = state.id.id };
                    bridges.Add(bridge);
                    pending.RemoveAt(i);
                }
            }
            foreach (var state in states)
            {
                var bridge = bridges.Find(b => Matches(b, state.id));
                if (bridge == null || bridge.SegmentCount != state.segments) continue;
                // A physical object may be realized after the room snapshot arrives.
                bridge.startAnchor = state.start.Resolve(room, bridges);
                bridge.endAnchor = state.end.Resolve(room, bridges);
                bridge.health = state.health;
                bridge.ReceiveNetworkGeometry(state.coordinates, state.basis, state.visual);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static bool CanCreateBridge(Player player)
        {
            if (!IsOnline()) return true;
            var session = Session(player?.room);
            return OnlineObject(player)?.isMine == true && session != null && session.isAvailable && session.owner != null &&
                SilkBridgeManager.GetBridgesInRoom(player.room).Count < SilkRoomData.MaxBridges;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static bool SubmitBridge(Player player, SilkBridge bridge, Action onRejected)
        {
            if (!IsOnline()) return false;
            var session = Session(player.room);
            bridge.NetworkOwner = OnlineManager.mePlayer.inLobbyId;
            bridge.NetworkNumber = ++nextBridgeNumber;
            if (session.isOwner)
            {
                Data(session);
                SilkBridgeManager.EnsureBridgesInRoom(player.room).Add(bridge);
            }
            else
            {
                var state = new SilkBridgeState(bridge);
                // This provisional strand is not part of the authoritative room yet.
                bridge.RemoveNetworkBridge(false);
                session.owner.InvokeRPC(CreateBridge, session, OnlineObject(player), state).Then(result =>
                {
                    if (result is GenericResult.Ok) return;
                    onRejected?.Invoke();
                    Debug.LogWarning("[Tinker] Meadow bridge creation was not accepted; its energy payment was refunded.");
                });
            }
            return true;
        }

        [RPCMethod]
        public static GenericResult CreateBridge(RPCEvent rpc, RoomSession session, OnlinePhysicalObject creator, SilkBridgeState state)
        {
            if (session == null || !session.isOwner || !session.isAvailable || creator == null || creator.owner != rpc.from ||
                !session.participants.Contains(rpc.from) || state == null || !state.Valid ||
                state.id.originalOwner != rpc.from.inLobbyId || session.absroom.realizedRoom is not Room room ||
                creator.apo.realizedObject is not Player player || player.room != room || !tinkerSilkData.IsTinkerPlayer(player)) return new GenericResult.Fail(rpc);
            var bridges = SilkBridgeManager.EnsureBridgesInRoom(room);
            if (bridges.Any(b => Matches(b, state.id))) return new GenericResult.Ok(rpc);
            if (bridges.Count >= SilkRoomData.MaxBridges) return new GenericResult.Fail(rpc);
            if (Vector2.Distance(state.start.position, state.end.position) > 1200f ||
                Mathf.Min(Vector2.Distance(player.mainBodyChunk.pos, state.start.position),
                    Vector2.Distance(player.mainBodyChunk.pos, state.end.position)) > 1300f) return new GenericResult.Fail(rpc);
            var start = state.start.Resolve(room, bridges);
            var end = state.end.Resolve(room, bridges);
            if (!start.IsValid(room) || !end.IsValid(room)) return new GenericResult.Fail(rpc);
            var bridge = new SilkBridge(start, end, room, state.length, state.segments + 1)
            { NetworkOwner = state.id.originalOwner, NetworkNumber = state.id.id };
            Data(session);
            bridges.Add(bridge);
            return new GenericResult.Ok(rpc);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static bool RelayForce(SilkBridge bridge, Vector2 point, Vector2 force)
        {
            if (!IsOnline()) return false;
            var session = Session(bridge.room);
            if (session?.isOwner == true) return false;
            if (session == null || !session.isAvailable || !SilkNetworkCurve.Finite(point) || !SilkNetworkCurve.Finite(force)) return true;
            var data = Data(session);
            if (data == null) return true;
            if (!data.Forces.TryGetValue(bridge, out var load)) data.Forces.Add(bridge, load = new SilkRoomData.PendingForce());
            load.Point += point; load.Force += Vector2.ClampMagnitude(force, 30f); load.Samples++;
            return true;
        }

        [RPCMethod]
        public static void BridgeForce(RPCEvent rpc, RoomSession session, OnlineEntity.EntityId id, Vector2 point, Vector2 force)
        {
            if (!ValidRoomRPC(rpc, session) || !SilkNetworkCurve.Finite(point) || !SilkNetworkCurve.Finite(force)) return;
            var bridge = SilkBridgeManager.GetBridgesInRoom(session.absroom.realizedRoom).Find(b => Matches(b, id));
            if (bridge == null) return;
            var data = Data(session);
            var load = data.Loads.Find(l => l.Sender == rpc.from && l.Bridge == id);
            if (load == null)
                data.Loads.Add(load = new SilkRoomData.RemoteLoad { Sender = rpc.from, Bridge = id });
            load.Point = point;
            load.Force = Vector2.ClampMagnitude(force, 30f);
            load.Frames = 4;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static bool RelayDamage(SilkBridge bridge, float amount, Vector2 point)
        {
            if (!IsOnline()) return false;
            var session = Session(bridge.room);
            if (session?.isOwner == true) return false;
            if (session?.isAvailable == true && session.owner != null)
                session.owner.InvokeRPC(BridgeDamage, session, BridgeId(bridge), amount, point);
            return true;
        }

        private static bool ValidRoomRPC(RPCEvent rpc, RoomSession session) => session != null && session.isOwner &&
            session.isAvailable && session.absroom.realizedRoom != null && session.participants.Contains(rpc.from);

        [RPCMethod]
        public static void BridgeDamage(RPCEvent rpc, RoomSession session, OnlineEntity.EntityId id, float amount, Vector2 point)
        {
            if (!ValidRoomRPC(rpc, session) || !SilkNetworkCurve.Finite(amount) || amount <= 0f || !SilkNetworkCurve.Finite(point)) return;
            var bridge = SilkBridgeManager.GetBridgesInRoom(session.absroom.realizedRoom).Find(b => Matches(b, id));
            if (bridge != null && Vector2.Distance(point, bridge.GetClosestPoint(point, out _, out _)) < 50f)
                bridge.TakeDamage(Mathf.Min(amount, 121f), point);
        }
    }
}
#endif
