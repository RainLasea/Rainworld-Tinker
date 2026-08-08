using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using RWCustom;
using UnityEngine;

namespace RainMeadow;

public class Serializer
{
	public interface ICustomSerializable
	{
		void CustomSerialize(Serializer serializer);
	}

	public struct TypeInfo(Type fieldType, bool nullable, bool polymorphic, bool longList) : IEqualityComparer<TypeInfo>
	{
		public Type fieldType = fieldType;

		public bool nullable = nullable;

		public bool polymorphic = polymorphic;

		public bool longList = longList;

		public override string ToString()
		{
			return $"{fieldType.FullName}{nullable}{polymorphic}{longList}";
		}

		public bool Equals(TypeInfo b1, TypeInfo b2)
		{
			return b1.fieldType.FullName == b2.fieldType.FullName && b1.nullable == b2.nullable && b1.polymorphic == b2.polymorphic && b1.longList == b2.longList;
		}

		public int GetHashCode(TypeInfo obj)
		{
			return obj.ToString().GetHashCode();
		}
	}

	public readonly byte[] buffer;

	private readonly long capacity;

	private long margin;

	public MemoryStream stream;

	public BinaryWriter writer;

	public BinaryReader reader;

	public OnlinePlayer currPlayer;

	private uint eventCount;

	private long eventHeader;

	private uint stateCount;

	private long stateHeader;

	public int zipTreshold = 4000;

	private static Serializer scratchpad;

	public bool IsDelta;

	internal static Dictionary<TypeInfo, MethodInfo> serializerMethods = new Dictionary<TypeInfo, MethodInfo>();

	public long Position => stream.Position;

	public bool IsWriting { get; set; }

	public bool IsReading { get; set; }

	private bool Aborted { get; set; }

	public void SerializeExtEnum<T>(ref T extEnum) where T : ExtEnum<T>
	{
		if (IsWriting)
		{
			writer.Write((byte)((ExtEnumBase)extEnum).Index);
		}
		if (IsReading)
		{
			extEnum = (T)Activator.CreateInstance(typeof(T), ExtEnum<T>.values.GetEntry((int)reader.ReadByte()), false);
		}
	}

	public void SerializeNullableExtEnum<T>(ref T extEnum) where T : ExtEnum<T>
	{
		if (IsWriting)
		{
			writer.Write((ExtEnum<T>)(object)extEnum != (ExtEnum<T>)null);
			if ((ExtEnum<T>)(object)extEnum != (ExtEnum<T>)null)
			{
				this.SerializeExtEnum<T>(ref extEnum);
			}
		}
		if (IsReading && reader.ReadBoolean())
		{
			this.SerializeExtEnum<T>(ref extEnum);
		}
	}

	public void SerializeExtEnums<T>(ref T[] extEnum) where T : ExtEnum<T>
	{
		if (IsWriting)
		{
			writer.Write((byte)extEnum.Length);
			for (int i = 0; i < extEnum.Length; i++)
			{
				writer.Write((byte)((ExtEnumBase)(object)extEnum[i]).Index);
			}
		}
		if (IsReading)
		{
			extEnum = new T[reader.ReadByte()];
			for (int j = 0; j < extEnum.Length; j++)
			{
				extEnum[j] = (T)Activator.CreateInstance(typeof(T), ExtEnum<T>.values.GetEntry((int)reader.ReadByte()), false);
			}
		}
	}

	public void SerializeExtEnums<T>(ref List<T> extEnum) where T : ExtEnum<T>
	{
		if (IsWriting)
		{
			writer.Write((byte)extEnum.Count);
			for (int i = 0; i < extEnum.Count; i++)
			{
				writer.Write((byte)((ExtEnumBase)(object)extEnum[i]).Index);
			}
		}
		if (IsReading)
		{
			byte b = reader.ReadByte();
			extEnum = new List<T>(b);
			for (int j = 0; j < b; j++)
			{
				extEnum.Add((T)Activator.CreateInstance(typeof(T), ExtEnum<T>.values.GetEntry((int)reader.ReadByte()), false));
			}
		}
	}

	public Serializer(long bufferCapacity, bool scratch = false)
	{
		capacity = bufferCapacity;
		margin = 16L;
		buffer = new byte[bufferCapacity];
		stream = new MemoryStream(buffer);
		writer = new BinaryWriter(stream);
		reader = new BinaryReader(stream);
		if (!scratch)
		{
			scratchpad = new Serializer(3 * capacity, scratch: true);
		}
	}

	private void PlayerHeaders()
	{
		if (IsWriting)
		{
			DebugOverlay.playersWritten.addPlayer(currPlayer);
			writer.Write(OnlineManager.mePlayer.tick);
			writer.Write(currPlayer.lastEventFromRemote);
			writer.Write(currPlayer.tick);
			writer.Write(currPlayer.recentTicksToAckBitpack);
		}
		if (IsReading)
		{
			DebugOverlay.playersRead.addPlayer(currPlayer);
			uint num = reader.ReadUInt32();
			if (!EventMath.IsNewer(num, currPlayer.tick))
			{
				AbortRead();
				return;
			}
			currPlayer.NewTick(num);
			currPlayer.EventAckFromRemote(reader.ReadUInt16());
			currPlayer.TickAckFromRemote(reader.ReadUInt32(), reader.ReadUInt16());
		}
	}

	private void AbortRead()
	{
		RainMeadow.Debug("aborted read", "/Online/Serialization/Serializer.cs", "AbortRead");
		currPlayer = null;
		IsReading = false;
		IsDelta = false;
		Aborted = true;
		scratchpad.currPlayer = null;
		scratchpad.IsReading = false;
	}

	public void BeginWrite(OnlinePlayer toPlayer)
	{
		currPlayer = toPlayer;
		if (IsWriting || IsReading)
		{
			throw new InvalidOperationException("not done with previous operation");
		}
		IsWriting = true;
		IsDelta = false;
		Aborted = false;
		stream.Seek(0L, SeekOrigin.Begin);
		scratchpad.currPlayer = toPlayer;
		scratchpad.IsWriting = true;
	}

	private void BeginWriteEvents()
	{
		eventCount = 0u;
		eventHeader = stream.Position;
		writer.Write(eventCount);
	}

	private bool WriteEvent(OnlineEvent playerEvent)
	{
		scratchpad.stream.Seek(0L, SeekOrigin.Begin);
		playerEvent.CustomSerialize(scratchpad);
		if (scratchpad.Position < capacity - Position - margin)
		{
			writer.Write((byte)playerEvent.eventType);
			writer.Write(scratchpad.buffer, 0, (int)scratchpad.Position);
			eventCount++;
			return true;
		}
		return false;
	}

	private void EndWriteEvents()
	{
		long position = stream.Position;
		stream.Position = eventHeader;
		writer.Write(eventCount);
		stream.Position = position;
	}

	private void BeginWriteStates()
	{
		stateCount = 0u;
		stateHeader = stream.Position;
		writer.Write(stateCount);
	}

	private bool WriteZippedState(OnlineState state)
	{
		int num = (int)scratchpad.Position;
		scratchpad.stream.Seek(0L, SeekOrigin.Begin);
		DeflateState deflateState = new DeflateState(scratchpad.stream, num);
		RainMeadow.Debug($"zipping state {state}, was {num} became ~{deflateState.bytes.Length}", "/Online/Serialization/Serializer.cs", "WriteZippedState");
		return WriteState(deflateState);
	}

	private bool WriteState(OnlineState state)
	{
		scratchpad.stream.Seek(0L, SeekOrigin.Begin);
		state.WritePolymorph(scratchpad);
		scratchpad.WrappedSerialize(state);
		bool flag = scratchpad.Position < capacity - Position - margin;
		if ((scratchpad.Position > zipTreshold || !flag) && !(state is DeflateState))
		{
			return WriteZippedState(state);
		}
		if (flag)
		{
			writer.Write(scratchpad.buffer, 0, (int)scratchpad.Position);
			stateCount++;
			return true;
		}
		return false;
	}

	private void EndWriteStates()
	{
		long position = stream.Position;
		stream.Position = stateHeader;
		writer.Write(stateCount);
		stream.Position = position;
	}

	public void EndWrite()
	{
		currPlayer = null;
		IsWriting = false;
		writer.Flush();
		scratchpad.currPlayer = null;
		scratchpad.IsWriting = false;
	}

	private void BeginRead(OnlinePlayer fromPlayer)
	{
		currPlayer = fromPlayer;
		if (IsWriting || IsReading)
		{
			throw new InvalidOperationException("not done with previous operation");
		}
		IsReading = true;
		Aborted = false;
		stream.Seek(0L, SeekOrigin.Begin);
		scratchpad.currPlayer = fromPlayer;
		scratchpad.IsReading = true;
	}

	private uint BeginReadEvents()
	{
		return reader.ReadUInt32();
	}

	private OnlineEvent ReadEvent()
	{
		OnlineEvent onlineEvent = OnlineEvent.NewFromType((OnlineEvent.EventTypeId)reader.ReadByte());
		onlineEvent.from = currPlayer;
		onlineEvent.to = OnlineManager.mePlayer;
		onlineEvent.CustomSerialize(this);
		return onlineEvent;
	}

	private uint BeginReadStates()
	{
		return reader.ReadUInt32();
	}

	private OnlineState ReadState()
	{
		OnlineState onlineState = OnlineState.ParsePolymorph(this);
		if (onlineState is RootDeltaState rootDeltaState)
		{
			rootDeltaState.from = currPlayer;
			rootDeltaState.tick = currPlayer.tick;
		}
		WrappedSerialize(onlineState);
		if (onlineState is DeflateState deflateState)
		{
			scratchpad.stream.Seek(0L, SeekOrigin.Begin);
			deflateState.Decompress(scratchpad.stream);
			scratchpad.stream.Seek(0L, SeekOrigin.Begin);
			onlineState = scratchpad.ReadState();
		}
		return onlineState;
	}

	public void EndRead()
	{
		currPlayer = null;
		IsReading = false;
		scratchpad.currPlayer = null;
		scratchpad.IsReading = false;
	}

	public void ReadData(OnlinePlayer fromPlayer, long size)
	{
		fromPlayer.bytesIn[fromPlayer.bytesSnapIndex] = (int)size;
		BeginRead(fromPlayer);
		PlayerHeaders();
		if (Aborted)
		{
			RainMeadow.Debug("skipped packet", "/Online/Serialization/Serializer.cs", "ReadData");
			return;
		}
		uint num = BeginReadEvents();
		fromPlayer.eventsRead = num != 0;
		for (uint num2 = 0u; num2 < num; num2++)
		{
			OnlineManager.ProcessIncomingEvent(ReadEvent());
		}
		uint num3 = BeginReadStates();
		fromPlayer.statesRead = num3 != 0;
		for (uint num4 = 0u; num4 < num3; num4++)
		{
			OnlineManager.ProcessIncomingState(ReadState());
		}
		EndRead();
	}

	public long WriteData(OnlinePlayer toPlayer)
	{
		BeginWrite(toPlayer);
		PlayerHeaders();
		BeginWriteEvents();
		toPlayer.eventsWritten = toPlayer.OutgoingEvents.Count > 0;
		foreach (OnlineEvent outgoingEvent in toPlayer.OutgoingEvents)
		{
			if (WriteEvent(outgoingEvent))
			{
				continue;
			}
			RainMeadow.Error($"WriteEvent failed for {outgoingEvent}", "/Online/Serialization/Serializer.cs", "WriteData");
			RainMeadow.Error("no space for events", "/Online/Serialization/Serializer.cs", "WriteData");
			break;
		}
		EndWriteEvents();
		BeginWriteStates();
		toPlayer.statesWritten = toPlayer.OutgoingStates.Count > 0;
		while (toPlayer.OutgoingStates.Count > 0)
		{
			OnlineStateMessage onlineStateMessage = toPlayer.OutgoingStates.Dequeue();
			StateProfiler.Instance?.Push(onlineStateMessage.state.GetType());
			if (WriteState(onlineStateMessage.state))
			{
				onlineStateMessage.Sent();
			}
			else
			{
				RainMeadow.Error($"State overflow writing to player {toPlayer}, {onlineStateMessage.state} not sent", "/Online/Serialization/Serializer.cs", "WriteData");
				onlineStateMessage.Failed();
			}
			StateProfiler.Instance?.Pop(onlineStateMessage.state.GetType());
		}
		EndWriteStates();
		EndWrite();
		toPlayer.bytesOut[toPlayer.bytesSnapIndex] = (int)Position;
		return Position;
	}

	public void SerializeResourceByReference<T>(ref T onlineResource) where T : OnlineResource
	{
		if (IsWriting)
		{
			writer.Write(onlineResource.Id());
		}
		if (IsReading)
		{
			string rid = reader.ReadString();
			onlineResource = (T)OnlineManager.ResourceFromIdentifier(rid);
		}
	}

	public void SerializeEntityById<T>(ref T onlineEntity) where T : OnlineEntity
	{
		if (IsWriting)
		{
			onlineEntity.id.CustomSerialize(this);
		}
		if (IsReading)
		{
			OnlineEntity.EntityId entityId = new OnlineEntity.EntityId();
			entityId.CustomSerialize(this);
			onlineEntity = (T)entityId.FindEntity();
		}
	}

	public void SerializeNullableEntityById<T>(ref T onlineEntity) where T : OnlineEntity
	{
		if (IsWriting)
		{
			writer.Write(onlineEntity != null);
			if (onlineEntity != null)
			{
				onlineEntity.id.CustomSerialize(this);
			}
		}
		if (IsReading && reader.ReadBoolean())
		{
			OnlineEntity.EntityId entityId = new OnlineEntity.EntityId();
			entityId.CustomSerialize(this);
			onlineEntity = (T)entityId.FindEntity();
		}
	}

	private void WrappedSerialize(OnlineState state)
	{
		bool isDelta = IsDelta;
		state.CustomSerialize(this);
		IsDelta = isDelta;
	}

	public void SerializePolyState<T>(ref T state) where T : OnlineState
	{
		if (IsWriting)
		{
			state.WritePolymorph(this);
			WrappedSerialize(state);
		}
		if (IsReading)
		{
			state = (T)OnlineState.ParsePolymorph(this);
			if (state is RootDeltaState rootDeltaState)
			{
				rootDeltaState.from = currPlayer;
				rootDeltaState.tick = currPlayer.tick;
			}
			WrappedSerialize(state);
		}
	}

	public void SerializeNullablePolyState<T>(ref T nullableState) where T : OnlineState
	{
		if (IsWriting)
		{
			writer.Write(nullableState != null);
			if (nullableState != null)
			{
				SerializePolyState(ref nullableState);
			}
		}
		if (IsReading && reader.ReadBoolean())
		{
			SerializePolyState(ref nullableState);
		}
	}

	public void SerializePolyStatesByte<T>(ref T[] states) where T : OnlineState
	{
		if (IsWriting)
		{
			if (states.Length > 255)
			{
				throw new OverflowException("too many states");
			}
			writer.Write((byte)states.Length);
			T[] array = states;
			foreach (T val in array)
			{
				val.WritePolymorph(this);
				WrappedSerialize(val);
			}
		}
		if (!IsReading)
		{
			return;
		}
		byte b = reader.ReadByte();
		states = new T[b];
		for (int j = 0; j < b; j++)
		{
			T val2 = (T)OnlineState.ParsePolymorph(this);
			if (val2 is RootDeltaState rootDeltaState)
			{
				rootDeltaState.from = currPlayer;
				rootDeltaState.tick = currPlayer.tick;
			}
			WrappedSerialize(val2);
			states[j] = val2;
		}
	}

	public void SerializePolyStatesByte<T>(ref List<T> states) where T : OnlineState
	{
		if (IsWriting)
		{
			if (states.Count > 255)
			{
				throw new OverflowException("too many states");
			}
			writer.Write((byte)states.Count);
			foreach (T state in states)
			{
				state.WritePolymorph(this);
				WrappedSerialize(state);
			}
		}
		if (!IsReading)
		{
			return;
		}
		byte b = reader.ReadByte();
		states = new List<T>(b);
		for (int i = 0; i < b; i++)
		{
			T val = (T)OnlineState.ParsePolymorph(this);
			if (val is RootDeltaState rootDeltaState)
			{
				rootDeltaState.from = currPlayer;
				rootDeltaState.tick = currPlayer.tick;
			}
			WrappedSerialize(val);
			states.Add(val);
		}
	}

	public void SerializePolyStatesShort<T>(ref T[] states) where T : OnlineState
	{
		if (IsWriting)
		{
			if (states.Length > 65535)
			{
				throw new OverflowException("too many states");
			}
			writer.Write((ushort)states.Length);
			T[] array = states;
			foreach (T val in array)
			{
				val.WritePolymorph(this);
				WrappedSerialize(val);
			}
		}
		if (!IsReading)
		{
			return;
		}
		ushort num = reader.ReadUInt16();
		states = new T[num];
		for (int j = 0; j < num; j++)
		{
			T val2 = (T)OnlineState.ParsePolymorph(this);
			if (val2 is RootDeltaState rootDeltaState)
			{
				rootDeltaState.from = currPlayer;
				rootDeltaState.tick = currPlayer.tick;
			}
			WrappedSerialize(val2);
			states[j] = val2;
		}
	}

	public void SerializePolyStatesShort<T>(ref List<T> states) where T : OnlineState
	{
		if (IsWriting)
		{
			if (states.Count > 65535)
			{
				throw new OverflowException("too many states");
			}
			writer.Write((ushort)states.Count);
			foreach (T state in states)
			{
				state.WritePolymorph(this);
				WrappedSerialize(state);
			}
		}
		if (!IsReading)
		{
			return;
		}
		ushort num = reader.ReadUInt16();
		states = new List<T>(num);
		for (int i = 0; i < num; i++)
		{
			T val = (T)OnlineState.ParsePolymorph(this);
			if (val is RootDeltaState rootDeltaState)
			{
				rootDeltaState.from = currPlayer;
				rootDeltaState.tick = currPlayer.tick;
			}
			WrappedSerialize(val);
			states.Add(val);
		}
	}

	public void SerializeStaticState<T>(ref T state) where T : OnlineState, new()
	{
		if (IsWriting)
		{
			WrappedSerialize(state);
		}
		if (IsReading)
		{
			state = new T();
			if (state is RootDeltaState rootDeltaState)
			{
				rootDeltaState.from = currPlayer;
				rootDeltaState.tick = currPlayer.tick;
			}
			WrappedSerialize(state);
		}
	}

	public void SerializeNullableStaticState<T>(ref T nullableState) where T : OnlineState, new()
	{
		if (IsWriting)
		{
			writer.Write(nullableState != null);
			if (nullableState != null)
			{
				SerializeStaticState(ref nullableState);
			}
		}
		if (IsReading && reader.ReadBoolean())
		{
			SerializeStaticState(ref nullableState);
		}
	}

	public void SerializeStaticStatesByte<T>(ref T[] states) where T : OnlineState, new()
	{
		if (IsWriting)
		{
			if (states.Length > 255)
			{
				throw new OverflowException("too many states");
			}
			writer.Write((byte)states.Length);
			T[] array = states;
			foreach (T state in array)
			{
				WrappedSerialize(state);
			}
		}
		if (!IsReading)
		{
			return;
		}
		byte b = reader.ReadByte();
		states = new T[b];
		for (int j = 0; j < b; j++)
		{
			T val = new T();
			if (val is RootDeltaState rootDeltaState)
			{
				rootDeltaState.from = currPlayer;
				rootDeltaState.tick = currPlayer.tick;
			}
			WrappedSerialize(val);
			states[j] = val;
		}
	}

	public void SerializeStaticStatesShort<T>(ref T[] states) where T : OnlineState, new()
	{
		if (IsWriting)
		{
			if (states.Length > 65535)
			{
				throw new OverflowException("too many states");
			}
			writer.Write((ushort)states.Length);
			T[] array = states;
			foreach (T state in array)
			{
				WrappedSerialize(state);
			}
		}
		if (!IsReading)
		{
			return;
		}
		ushort num = reader.ReadUInt16();
		states = new T[num];
		for (int j = 0; j < num; j++)
		{
			T val = new T();
			if (val is RootDeltaState rootDeltaState)
			{
				rootDeltaState.from = currPlayer;
				rootDeltaState.tick = currPlayer.tick;
			}
			WrappedSerialize(val);
			states[j] = val;
		}
	}

	public void SerializePlayerIds(ref List<MeadowPlayerId> ids)
	{
		if (IsWriting)
		{
			writer.Write((byte)ids.Count);
			foreach (MeadowPlayerId id in ids)
			{
				id.CustomSerialize(this);
			}
		}
		if (IsReading)
		{
			byte b = reader.ReadByte();
			ids = new List<MeadowPlayerId>(b);
			for (int i = 0; i < b; i++)
			{
				MeadowPlayerId emptyId = MatchmakingManager.currentInstance.GetEmptyId();
				emptyId.CustomSerialize(this);
				ids.Add(emptyId);
			}
		}
	}

	public void SerializePlayerInLobby(ref OnlinePlayer player)
	{
		if (IsWriting)
		{
			writer.Write(player.inLobbyId);
		}
		if (IsReading)
		{
			ushort id = reader.ReadUInt16();
			player = OnlineManager.lobby?.PlayerFromId(id);
			if (player == null)
			{
				RainMeadow.Error("Player not found! " + id, "/Online/Serialization/Serializer.cs", "SerializePlayerInLobby");
			}
		}
	}

	public void SerializeReferencedEvent(ref OnlineEvent referencedEvent)
	{
		if (IsWriting)
		{
			writer.Write(referencedEvent.eventId);
		}
		if (IsReading)
		{
			referencedEvent = currPlayer.GetRecentEvent(reader.ReadUInt16());
		}
	}

	public void SerializeEvent<T>(ref T playerEvent) where T : OnlineEvent
	{
		if (IsWriting)
		{
			writer.Write((byte)playerEvent.eventType);
			playerEvent.CustomSerialize(this);
		}
		if (IsReading)
		{
			playerEvent = (T)OnlineEvent.NewFromType((OnlineEvent.EventTypeId)reader.ReadByte());
			playerEvent.from = currPlayer;
			playerEvent.to = OnlineManager.mePlayer;
			playerEvent.CustomSerialize(this);
		}
	}

	public void SerializeEvents<T>(ref List<T> events) where T : OnlineEvent
	{
		if (IsWriting)
		{
			if (events.Count > 255)
			{
				throw new OverflowException("too many events");
			}
			writer.Write((byte)events.Count);
			foreach (T @event in events)
			{
				writer.Write((byte)@event.eventType);
				@event.CustomSerialize(this);
			}
		}
		if (IsReading)
		{
			byte b = reader.ReadByte();
			events = new List<T>(b);
			for (int i = 0; i < b; i++)
			{
				T val = (T)OnlineEvent.NewFromType((OnlineEvent.EventTypeId)reader.ReadByte());
				val.from = currPlayer;
				val.to = OnlineManager.mePlayer;
				val.CustomSerialize(this);
				events.Add(val);
			}
		}
	}

	public void Serialize(ref byte data)
	{
		if (IsWriting)
		{
			writer.Write(data);
		}
		if (IsReading)
		{
			data = reader.ReadByte();
		}
	}

	public void Serialize(ref byte[] data)
	{
		if (IsWriting)
		{
			writer.Write((byte)data.Length);
			writer.Write(data);
		}
		if (IsReading)
		{
			data = reader.ReadBytes(reader.ReadByte());
		}
	}

	public void SerializeLongArray(ref byte[] data)
	{
		if (IsWriting)
		{
			writer.Write((ushort)data.Length);
			writer.Write(data);
		}
		if (IsReading)
		{
			data = reader.ReadBytes(reader.ReadUInt16());
		}
	}

	public void Serialize(ref List<byte> data)
	{
		if (IsWriting)
		{
			writer.Write((byte)data.Count);
			for (int i = 0; i < data.Count; i++)
			{
				writer.Write(data[i]);
			}
		}
		if (IsReading)
		{
			byte b = reader.ReadByte();
			data = new List<byte>(b);
			for (int j = 0; j < b; j++)
			{
				data.Add(reader.ReadByte());
			}
		}
	}

	public void Serialize(ref sbyte data)
	{
		if (IsWriting)
		{
			writer.Write(data);
		}
		if (IsReading)
		{
			data = reader.ReadSByte();
		}
	}

	public void Serialize(ref sbyte[] data)
	{
		if (IsWriting)
		{
			writer.Write((byte)data.Length);
			for (int i = 0; i < data.Length; i++)
			{
				writer.Write(data[i]);
			}
		}
		if (IsReading)
		{
			data = new sbyte[reader.ReadByte()];
			for (int j = 0; j < data.Length; j++)
			{
				data[j] = reader.ReadSByte();
			}
		}
	}

	public void Serialize(ref List<sbyte> data)
	{
		if (IsWriting)
		{
			writer.Write((byte)data.Count);
			for (int i = 0; i < data.Count; i++)
			{
				writer.Write(data[i]);
			}
		}
		if (IsReading)
		{
			byte b = reader.ReadByte();
			data = new List<sbyte>(b);
			for (int j = 0; j < b; j++)
			{
				data.Add(reader.ReadSByte());
			}
		}
	}

	public void Serialize(ref ushort data)
	{
		if (IsWriting)
		{
			writer.Write(data);
		}
		if (IsReading)
		{
			data = reader.ReadUInt16();
		}
	}

	public void Serialize(ref ushort[] data)
	{
		if (IsWriting)
		{
			writer.Write((byte)data.Length);
			for (int i = 0; i < data.Length; i++)
			{
				writer.Write(data[i]);
			}
		}
		if (IsReading)
		{
			data = new ushort[reader.ReadByte()];
			for (int j = 0; j < data.Length; j++)
			{
				data[j] = reader.ReadUInt16();
			}
		}
	}

	public void Serialize(ref List<ushort> data)
	{
		if (IsWriting)
		{
			writer.Write((byte)data.Count);
			for (int i = 0; i < data.Count; i++)
			{
				writer.Write(data[i]);
			}
		}
		if (IsReading)
		{
			byte b = reader.ReadByte();
			data = new List<ushort>(b);
			for (int j = 0; j < b; j++)
			{
				data.Add(reader.ReadUInt16());
			}
		}
	}

	public void Serialize(ref short data)
	{
		if (IsWriting)
		{
			writer.Write(data);
		}
		if (IsReading)
		{
			data = reader.ReadInt16();
		}
	}

	public void Serialize(ref short[] data)
	{
		if (IsWriting)
		{
			writer.Write((byte)data.Length);
			for (int i = 0; i < data.Length; i++)
			{
				writer.Write(data[i]);
			}
		}
		if (IsReading)
		{
			data = new short[reader.ReadByte()];
			for (int j = 0; j < data.Length; j++)
			{
				data[j] = reader.ReadInt16();
			}
		}
	}

	public void Serialize(ref List<short> data)
	{
		if (IsWriting)
		{
			writer.Write((byte)data.Count);
			for (int i = 0; i < data.Count; i++)
			{
				writer.Write(data[i]);
			}
		}
		if (IsReading)
		{
			byte b = reader.ReadByte();
			data = new List<short>(b);
			for (int j = 0; j < b; j++)
			{
				data.Add(reader.ReadInt16());
			}
		}
	}

	public void Serialize(ref int data)
	{
		if (IsWriting)
		{
			writer.Write(data);
		}
		if (IsReading)
		{
			data = reader.ReadInt32();
		}
	}

	public void Serialize(ref int[] data)
	{
		if (IsWriting)
		{
			writer.Write((byte)data.Length);
			for (int i = 0; i < data.Length; i++)
			{
				writer.Write(data[i]);
			}
		}
		if (IsReading)
		{
			data = new int[reader.ReadByte()];
			for (int j = 0; j < data.Length; j++)
			{
				data[j] = reader.ReadInt32();
			}
		}
	}

	public void Serialize(ref List<int> data)
	{
		if (IsWriting)
		{
			writer.Write((byte)data.Count);
			for (int i = 0; i < data.Count; i++)
			{
				writer.Write(data[i]);
			}
		}
		if (IsReading)
		{
			byte b = reader.ReadByte();
			data = new List<int>(b);
			for (int j = 0; j < b; j++)
			{
				data.Add(reader.ReadInt32());
			}
		}
	}

	public void Serialize(ref uint data)
	{
		if (IsWriting)
		{
			writer.Write(data);
		}
		if (IsReading)
		{
			data = reader.ReadUInt32();
		}
	}

	public void Serialize(ref uint[] data)
	{
		if (IsWriting)
		{
			writer.Write((byte)data.Length);
			for (int i = 0; i < data.Length; i++)
			{
				writer.Write(data[i]);
			}
		}
		if (IsReading)
		{
			data = new uint[reader.ReadByte()];
			for (int j = 0; j < data.Length; j++)
			{
				data[j] = reader.ReadUInt32();
			}
		}
	}

	public void Serialize(ref List<uint> data)
	{
		if (IsWriting)
		{
			writer.Write((byte)data.Count);
			for (int i = 0; i < data.Count; i++)
			{
				writer.Write(data[i]);
			}
		}
		if (IsReading)
		{
			byte b = reader.ReadByte();
			data = new List<uint>(b);
			for (int j = 0; j < b; j++)
			{
				data.Add(reader.ReadUInt32());
			}
		}
	}

	public void Serialize(ref bool data)
	{
		if (IsWriting)
		{
			writer.Write(data);
		}
		if (IsReading)
		{
			data = reader.ReadBoolean();
		}
	}

	public void Serialize(ref bool[] data)
	{
		if (IsWriting)
		{
			writer.Write((byte)data.Length);
			byte b = 0;
			for (int i = 0; i < data.Length; i++)
			{
				if (data[i])
				{
					b |= (byte)(1 << i % 1);
				}
				if ((i + 1) % 1 == 0)
				{
					writer.Write(b);
					b = 0;
				}
			}
			if (data.Length % 1 != 0)
			{
				writer.Write(b);
			}
		}
		if (!IsReading)
		{
			return;
		}
		data = new bool[reader.ReadByte()];
		byte b2 = 0;
		for (int j = 0; j < data.Length; j++)
		{
			if (j % 1 == 0)
			{
				b2 = reader.ReadByte();
			}
			data[j] = (b2 & (byte)(1 << j % 1)) != 0;
		}
	}

	public void Serialize(ref List<bool> data)
	{
		if (IsWriting)
		{
			writer.Write((byte)data.Count);
			for (int i = 0; i < data.Count; i++)
			{
				writer.Write(data[i]);
			}
		}
		if (IsReading)
		{
			byte b = reader.ReadByte();
			data = new List<bool>(b);
			for (int j = 0; j < b; j++)
			{
				data.Add(reader.ReadBoolean());
			}
		}
	}

	public void Serialize(ref ulong data)
	{
		if (IsWriting)
		{
			writer.Write(data);
		}
		if (IsReading)
		{
			data = reader.ReadUInt64();
		}
	}

	public void Serialize(ref ulong[] data)
	{
		if (IsWriting)
		{
			writer.Write((byte)data.Length);
			for (int i = 0; i < data.Length; i++)
			{
				writer.Write(data[i]);
			}
		}
		if (IsReading)
		{
			data = new ulong[reader.ReadByte()];
			for (int j = 0; j < data.Length; j++)
			{
				data[j] = reader.ReadUInt64();
			}
		}
	}

	public void Serialize(ref List<ulong> data)
	{
		if (IsWriting)
		{
			writer.Write((byte)data.Count);
			for (int i = 0; i < data.Count; i++)
			{
				writer.Write(data[i]);
			}
		}
		if (IsReading)
		{
			byte b = reader.ReadByte();
			data = new List<ulong>(b);
			for (int j = 0; j < b; j++)
			{
				data.Add(reader.ReadUInt64());
			}
		}
	}

	public void Serialize(ref float data)
	{
		if (IsWriting)
		{
			writer.Write(data);
		}
		if (IsReading)
		{
			data = reader.ReadSingle();
		}
	}

	public void Serialize(ref float[] data)
	{
		if (IsWriting)
		{
			writer.Write((byte)data.Length);
			for (int i = 0; i < data.Length; i++)
			{
				writer.Write(data[i]);
			}
		}
		if (IsReading)
		{
			data = new float[reader.ReadByte()];
			for (int j = 0; j < data.Length; j++)
			{
				data[j] = reader.ReadSingle();
			}
		}
	}

	public void Serialize(ref List<float> data)
	{
		if (IsWriting)
		{
			writer.Write((byte)data.Count);
			for (int i = 0; i < data.Count; i++)
			{
				writer.Write(data[i]);
			}
		}
		if (IsReading)
		{
			byte b = reader.ReadByte();
			data = new List<float>(b);
			for (int j = 0; j < b; j++)
			{
				data.Add(reader.ReadSingle());
			}
		}
	}

	public void SerializeHalf(ref float data)
	{
		if (IsWriting)
		{
			writer.Write(Mathf.FloatToHalf(data));
		}
		if (IsReading)
		{
			data = Mathf.HalfToFloat(reader.ReadUInt16());
		}
	}

	public void SerializeHalf(ref float[] data)
	{
		if (IsWriting)
		{
			writer.Write((byte)data.Length);
			for (int i = 0; i < data.Length; i++)
			{
				writer.Write(Mathf.FloatToHalf(data[i]));
			}
		}
		if (IsReading)
		{
			data = new float[reader.ReadByte()];
			for (int j = 0; j < data.Length; j++)
			{
				data[j] = Mathf.HalfToFloat(reader.ReadUInt16());
			}
		}
	}

	public void SerializeHalf(ref List<float> data)
	{
		if (IsWriting)
		{
			writer.Write((byte)data.Count);
			for (int i = 0; i < data.Count; i++)
			{
				writer.Write(Mathf.FloatToHalf(data[i]));
			}
		}
		if (IsReading)
		{
			byte b = reader.ReadByte();
			data = new List<float>(b);
			for (int j = 0; j < b; j++)
			{
				data.Add(Mathf.HalfToFloat(reader.ReadUInt16()));
			}
		}
	}

	public void Serialize(ref string data)
	{
		if (IsWriting)
		{
			writer.Write(data);
		}
		if (IsReading)
		{
			data = reader.ReadString();
		}
	}

	public void SerializeNullable(ref string data)
	{
		if (IsWriting)
		{
			writer.Write(data != null);
			if (data != null)
			{
				writer.Write(data);
			}
		}
		if (IsReading && reader.ReadBoolean())
		{
			data = reader.ReadString();
		}
	}

	public void Serialize(ref string[] data)
	{
		if (IsWriting)
		{
			writer.Write((byte)data.Length);
			for (int i = 0; i < data.Length; i++)
			{
				writer.Write(data[i]);
			}
		}
		if (IsReading)
		{
			data = new string[reader.ReadByte()];
			for (int j = 0; j < data.Length; j++)
			{
				data[j] = reader.ReadString();
			}
		}
	}

	public void Serialize(ref List<string> data)
	{
		if (IsWriting)
		{
			writer.Write((byte)data.Count);
			for (int i = 0; i < data.Count; i++)
			{
				writer.Write(data[i]);
			}
		}
		if (IsReading)
		{
			byte b = reader.ReadByte();
			data = new List<string>(b);
			for (int j = 0; j < b; j++)
			{
				data.Add(reader.ReadString());
			}
		}
	}

	public void SerializeNullable(ref List<string> data)
	{
		if (IsWriting)
		{
			writer.Write(data != null);
			if (data != null)
			{
				writer.Write((byte)data.Count);
				for (int i = 0; i < data.Count; i++)
				{
					writer.Write(data[i]);
				}
			}
		}
		if (IsReading && reader.ReadBoolean())
		{
			byte b = reader.ReadByte();
			data = new List<string>(b);
			for (int j = 0; j < b; j++)
			{
				data.Add(reader.ReadString());
			}
		}
	}

	public void Serialize(ref Vector2 data)
	{
		if (IsWriting)
		{
			writer.Write(data.x);
			writer.Write(data.y);
		}
		if (IsReading)
		{
			data.x = reader.ReadSingle();
			data.y = reader.ReadSingle();
		}
	}

	public void SerializeNullable(ref Vector2? data)
	{
		//IL_0030: Unknown result type (might be due to invalid IL or missing references)
		//IL_0047: Unknown result type (might be due to invalid IL or missing references)
		//IL_008b: Unknown result type (might be due to invalid IL or missing references)
		if (IsWriting)
		{
			writer.Write(data.HasValue);
			if (data.HasValue)
			{
				writer.Write(data.Value.x);
				writer.Write(data.Value.y);
			}
		}
		if (IsReading && reader.ReadBoolean())
		{
			data = new Vector2(reader.ReadSingle(), reader.ReadSingle());
		}
	}

	public void Serialize(ref List<Vector2> data)
	{
		//IL_002e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0047: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b2: Unknown result type (might be due to invalid IL or missing references)
		if (IsWriting)
		{
			writer.Write((byte)data.Count);
			for (int i = 0; i < data.Count; i++)
			{
				writer.Write(data[i].x);
				writer.Write(data[i].y);
			}
		}
		if (IsReading)
		{
			byte b = reader.ReadByte();
			data = new List<Vector2>(b);
			for (int j = 0; j < b; j++)
			{
				float num = reader.ReadSingle();
				float num2 = reader.ReadSingle();
				data.Add(new Vector2(num, num2));
			}
		}
	}

	public void Serialize(ref Vector2[] data)
	{
		//IL_00ae: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b3: Unknown result type (might be due to invalid IL or missing references)
		if (IsWriting)
		{
			writer.Write((byte)data.Length);
			for (int i = 0; i < data.Length; i++)
			{
				writer.Write(data[i].x);
				writer.Write(data[i].y);
			}
		}
		if (IsReading)
		{
			byte b = reader.ReadByte();
			data = (Vector2[])(object)new Vector2[b];
			for (int j = 0; j < b; j++)
			{
				float num = reader.ReadSingle();
				float num2 = reader.ReadSingle();
				data[j] = new Vector2(num, num2);
			}
		}
	}

	public void SerializeHalf(ref Vector2 data)
	{
		if (IsWriting)
		{
			writer.Write(Mathf.FloatToHalf(data.x));
			writer.Write(Mathf.FloatToHalf(data.y));
		}
		if (IsReading)
		{
			data.x = Mathf.HalfToFloat(reader.ReadUInt16());
			data.y = Mathf.HalfToFloat(reader.ReadUInt16());
		}
	}

	public void SerializeHalfNullable(ref Vector2? data)
	{
		//IL_0030: Unknown result type (might be due to invalid IL or missing references)
		//IL_004c: Unknown result type (might be due to invalid IL or missing references)
		//IL_009f: Unknown result type (might be due to invalid IL or missing references)
		if (IsWriting)
		{
			writer.Write(data.HasValue);
			if (data.HasValue)
			{
				writer.Write(Mathf.FloatToHalf(data.Value.x));
				writer.Write(Mathf.FloatToHalf(data.Value.y));
			}
		}
		if (IsReading && reader.ReadBoolean())
		{
			data = new Vector2(Mathf.HalfToFloat(reader.ReadUInt16()), Mathf.HalfToFloat(reader.ReadUInt16()));
		}
	}

	public void SerializeHalf(ref List<Vector2> data)
	{
		//IL_002e: Unknown result type (might be due to invalid IL or missing references)
		//IL_004c: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c6: Unknown result type (might be due to invalid IL or missing references)
		if (IsWriting)
		{
			writer.Write((byte)data.Count);
			for (int i = 0; i < data.Count; i++)
			{
				writer.Write(Mathf.FloatToHalf(data[i].x));
				writer.Write(Mathf.FloatToHalf(data[i].y));
			}
		}
		if (IsReading)
		{
			byte b = reader.ReadByte();
			data = new List<Vector2>(b);
			for (int j = 0; j < b; j++)
			{
				float num = Mathf.HalfToFloat(reader.ReadUInt16());
				float num2 = Mathf.HalfToFloat(reader.ReadUInt16());
				data.Add(new Vector2(num, num2));
			}
		}
	}

	public void SerializeHalf(ref Vector2[] data)
	{
		//IL_00c2: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c7: Unknown result type (might be due to invalid IL or missing references)
		if (IsWriting)
		{
			writer.Write((byte)data.Length);
			for (int i = 0; i < data.Length; i++)
			{
				writer.Write(Mathf.FloatToHalf(data[i].x));
				writer.Write(Mathf.FloatToHalf(data[i].y));
			}
		}
		if (IsReading)
		{
			byte b = reader.ReadByte();
			data = (Vector2[])(object)new Vector2[b];
			for (int j = 0; j < b; j++)
			{
				float num = Mathf.HalfToFloat(reader.ReadUInt16());
				float num2 = Mathf.HalfToFloat(reader.ReadUInt16());
				data[j] = new Vector2(num, num2);
			}
		}
	}

	public void Serialize(ref IntVector2 data)
	{
		if (IsWriting)
		{
			writer.Write((short)data.x);
			writer.Write((short)data.y);
		}
		if (IsReading)
		{
			data.x = reader.ReadInt16();
			data.y = reader.ReadInt16();
		}
	}

	public void Serialize(ref List<IntVector2> data)
	{
		//IL_002e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0048: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b4: Unknown result type (might be due to invalid IL or missing references)
		if (IsWriting)
		{
			writer.Write((byte)data.Count);
			for (int i = 0; i < data.Count; i++)
			{
				writer.Write((short)data[i].x);
				writer.Write((short)data[i].y);
			}
		}
		if (IsReading)
		{
			byte b = reader.ReadByte();
			data = new List<IntVector2>(b);
			for (int j = 0; j < b; j++)
			{
				short num = reader.ReadInt16();
				short num2 = reader.ReadInt16();
				data.Add(new IntVector2((int)num, (int)num2));
			}
		}
	}

	public void SerializeNullable(ref IntVector2? data)
	{
		//IL_0030: Unknown result type (might be due to invalid IL or missing references)
		//IL_0048: Unknown result type (might be due to invalid IL or missing references)
		//IL_008d: Unknown result type (might be due to invalid IL or missing references)
		if (IsWriting)
		{
			writer.Write(data.HasValue);
			if (data.HasValue)
			{
				writer.Write((short)data.Value.x);
				writer.Write((short)data.Value.y);
			}
		}
		if (IsReading && reader.ReadBoolean())
		{
			data = new IntVector2((int)reader.ReadInt16(), (int)reader.ReadInt16());
		}
	}

	public void SerializeRGB(ref Color data)
	{
		if (IsWriting)
		{
			writer.Write((byte)(data.r * 255f));
			writer.Write((byte)(data.g * 255f));
			writer.Write((byte)(data.b * 255f));
		}
		if (IsReading)
		{
			data.r = (float)(int)reader.ReadByte() / 255f;
			data.g = (float)(int)reader.ReadByte() / 255f;
			data.b = (float)(int)reader.ReadByte() / 255f;
			data.a = 1f;
		}
	}

	public void SerializeRGBNullable(ref Color? data)
	{
		//IL_0030: Unknown result type (might be due to invalid IL or missing references)
		//IL_004e: Unknown result type (might be due to invalid IL or missing references)
		//IL_006c: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d7: Unknown result type (might be due to invalid IL or missing references)
		if (IsWriting)
		{
			writer.Write(data.HasValue);
			if (data.HasValue)
			{
				writer.Write((byte)(data.Value.r * 255f));
				writer.Write((byte)(data.Value.g * 255f));
				writer.Write((byte)(data.Value.b * 255f));
			}
		}
		if (IsReading && reader.ReadBoolean())
		{
			data = new Color((float)(int)reader.ReadByte() / 255f, (float)(int)reader.ReadByte() / 255f, (float)(int)reader.ReadByte() / 255f);
		}
	}

	public void SerializeRGB(ref Color[] data)
	{
		if (IsWriting)
		{
			writer.Write((byte)data.Length);
			for (int i = 0; i < data.Length; i++)
			{
				writer.Write((byte)(data[i].r * 255f));
				writer.Write((byte)(data[i].g * 255f));
				writer.Write((byte)(data[i].b * 255f));
			}
		}
		if (IsReading)
		{
			data = (Color[])(object)new Color[reader.ReadByte()];
			for (int j = 0; j < data.Length; j++)
			{
				data[j].r = (float)(int)reader.ReadByte() / 255f;
				data[j].g = (float)(int)reader.ReadByte() / 255f;
				data[j].b = (float)(int)reader.ReadByte() / 255f;
				data[j].a = 1f;
			}
		}
	}

	public void Serialize(ref WorldCoordinate pos)
	{
		//IL_0067: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b5: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b6: Unknown result type (might be due to invalid IL or missing references)
		if (IsWriting)
		{
			writer.Write((short)pos.room);
			writer.Write((short)pos.x);
			writer.Write((short)pos.y);
			writer.Write((short)pos.abstractNode);
		}
		if (IsReading)
		{
			pos = new WorldCoordinate
			{
				room = reader.ReadInt16(),
				x = reader.ReadInt16(),
				y = reader.ReadInt16(),
				abstractNode = reader.ReadInt16()
			};
		}
	}

	public void SerializeNullable(ref WorldCoordinate? pos)
	{
		//IL_0033: Unknown result type (might be due to invalid IL or missing references)
		//IL_004b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0063: Unknown result type (might be due to invalid IL or missing references)
		//IL_007b: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ac: Unknown result type (might be due to invalid IL or missing references)
		//IL_00fa: Unknown result type (might be due to invalid IL or missing references)
		if (IsWriting)
		{
			writer.Write(pos.HasValue);
			if (pos.HasValue)
			{
				writer.Write((short)pos.Value.room);
				writer.Write((short)pos.Value.x);
				writer.Write((short)pos.Value.y);
				writer.Write((short)pos.Value.abstractNode);
			}
		}
		if (IsReading && reader.ReadBoolean())
		{
			pos = new WorldCoordinate
			{
				room = reader.ReadInt16(),
				x = reader.ReadInt16(),
				y = reader.ReadInt16(),
				abstractNode = reader.ReadInt16()
			};
		}
	}

	public void Serialize(ref Dictionary<string, bool> data)
	{
		if (IsWriting)
		{
			if (data == null)
			{
				writer.Write((byte)0);
			}
			else
			{
				writer.Write((byte)data.Count);
				foreach (KeyValuePair<string, bool> datum in data)
				{
					writer.Write(datum.Key);
					writer.Write(datum.Value);
				}
			}
		}
		if (IsReading)
		{
			byte b = reader.ReadByte();
			data = new Dictionary<string, bool>(b);
			for (int i = 0; i < b; i++)
			{
				string key = reader.ReadString();
				bool value = reader.ReadBoolean();
				data.Add(key, value);
			}
		}
	}

	public void Serialize(ref Dictionary<string, float> data)
	{
		if (IsWriting)
		{
			if (data == null)
			{
				writer.Write((byte)0);
			}
			else
			{
				writer.Write((byte)data.Count);
				foreach (KeyValuePair<string, float> datum in data)
				{
					writer.Write(datum.Key);
					writer.Write(datum.Value);
				}
			}
		}
		if (IsReading)
		{
			byte b = reader.ReadByte();
			data = new Dictionary<string, float>(b);
			for (int i = 0; i < b; i++)
			{
				string key = reader.ReadString();
				float value = reader.ReadSingle();
				data.Add(key, value);
			}
		}
	}

	public void Serialize(ref Dictionary<string, int> data)
	{
		if (IsWriting)
		{
			if (data == null)
			{
				writer.Write((byte)0);
			}
			else
			{
				writer.Write((byte)data.Count);
				foreach (KeyValuePair<string, int> datum in data)
				{
					writer.Write(datum.Key);
					writer.Write(datum.Value);
				}
			}
		}
		if (IsReading)
		{
			byte b = reader.ReadByte();
			data = new Dictionary<string, int>(b);
			for (int i = 0; i < b; i++)
			{
				string key = reader.ReadString();
				int value = reader.ReadInt32();
				data.Add(key, value);
			}
		}
	}

	public void Serialize(ref Dictionary<int, List<string>> data)
	{
		if (IsWriting)
		{
			if (data == null)
			{
				writer.Write((byte)0);
			}
			else
			{
				writer.Write((byte)data.Count);
				foreach (KeyValuePair<int, List<string>> datum in data)
				{
					writer.Write(datum.Key);
					writer.Write((byte)datum.Value.Count);
					for (int i = 0; i < datum.Value.Count; i++)
					{
						writer.Write(datum.Value[i].ToString());
					}
				}
			}
		}
		if (!IsReading)
		{
			return;
		}
		byte b = reader.ReadByte();
		if (b == 0)
		{
			data = new Dictionary<int, List<string>>();
			return;
		}
		data = new Dictionary<int, List<string>>(b);
		for (int j = 0; j < b; j++)
		{
			int key = reader.ReadInt32();
			byte b2 = reader.ReadByte();
			List<string> list = new List<string>(b2);
			for (int k = 0; k < b2; k++)
			{
				list.Add(reader.ReadString());
			}
			data.Add(key, new List<string>(list));
		}
	}

	public void Serialize(ref Dictionary<int, int> data)
	{
		if (IsWriting)
		{
			if (data == null)
			{
				writer.Write((byte)0);
			}
			else
			{
				writer.Write((byte)data.Count);
				foreach (KeyValuePair<int, int> datum in data)
				{
					writer.Write(datum.Key);
					writer.Write(datum.Value);
				}
			}
		}
		if (IsReading)
		{
			byte b = reader.ReadByte();
			data = new Dictionary<int, int>(b);
			for (int i = 0; i < b; i++)
			{
				int key = reader.ReadInt32();
				int value = reader.ReadInt32();
				data.Add(key, value);
			}
		}
	}

	public void Serialize(ref Color data)
	{
		if (IsWriting)
		{
			writer.Write(data.r);
			writer.Write(data.g);
			writer.Write(data.b);
			writer.Write(data.a);
		}
		if (IsReading)
		{
			data.r = reader.ReadSingle();
			data.g = reader.ReadSingle();
			data.b = reader.ReadSingle();
			data.a = reader.ReadSingle();
		}
	}

	public void Serialize(ref Dictionary<ushort, ushort[]> data)
	{
		if (IsWriting)
		{
			if (data == null)
			{
				writer.Write((byte)0);
			}
			else
			{
				writer.Write((byte)data.Count);
				foreach (KeyValuePair<ushort, ushort[]> datum in data)
				{
					writer.Write(datum.Key);
					writer.Write((byte)datum.Value.Length);
					for (int i = 0; i < datum.Value.Length; i++)
					{
						writer.Write(datum.Value[i]);
					}
				}
			}
		}
		if (!IsReading)
		{
			return;
		}
		byte b = reader.ReadByte();
		data = new Dictionary<ushort, ushort[]>(b);
		for (int j = 0; j < b; j++)
		{
			ushort key = reader.ReadUInt16();
			ushort[] array = new ushort[reader.ReadByte()];
			for (int k = 0; k < array.Length; k++)
			{
				array[k] = reader.ReadUInt16();
			}
			data.Add(key, array);
		}
	}

	public void Serialize(ref Dictionary<ushort, int> data)
	{
		if (IsWriting)
		{
			if (data == null)
			{
				writer.Write((byte)0);
			}
			else
			{
				writer.Write((byte)data.Count);
				foreach (KeyValuePair<ushort, int> datum in data)
				{
					writer.Write(datum.Key);
					writer.Write(datum.Value);
				}
			}
		}
		if (IsReading)
		{
			byte b = reader.ReadByte();
			data = new Dictionary<ushort, int>(b);
			for (int i = 0; i < b; i++)
			{
				ushort key = reader.ReadUInt16();
				int value = reader.ReadInt32();
				data.Add(key, value);
			}
		}
	}

	public void Serialize(ref List<KeyValuePair<ushort, byte>> data)
	{
		if (IsWriting)
		{
			writer.Write((byte)data.Count);
			foreach (KeyValuePair<ushort, byte> datum in data)
			{
				writer.Write(datum.Key);
				writer.Write(datum.Value);
			}
		}
		if (IsReading)
		{
			byte b = reader.ReadByte();
			data = new List<KeyValuePair<ushort, byte>>(b);
			for (int i = 0; i < b; i++)
			{
				ushort key = reader.ReadUInt16();
				byte value = reader.ReadByte();
				data.Add(new KeyValuePair<ushort, byte>(key, value));
			}
		}
	}

	public void Serialize(ref List<KeyValuePair<byte, ushort>> data)
	{
		if (IsWriting)
		{
			writer.Write((byte)data.Count);
			foreach (KeyValuePair<byte, ushort> datum in data)
			{
				writer.Write(datum.Key);
				writer.Write(datum.Value);
			}
		}
		if (IsReading)
		{
			byte b = reader.ReadByte();
			data = new List<KeyValuePair<byte, ushort>>(b);
			for (int i = 0; i < b; i++)
			{
				byte key = reader.ReadByte();
				ushort value = reader.ReadUInt16();
				data.Add(new KeyValuePair<byte, ushort>(key, value));
			}
		}
	}

	public void Serialize(ref Counter counter)
	{
		if (IsWriting)
		{
			writer.Write(counter.min);
			writer.Write(counter.max);
			writer.Write(counter.counter);
			writer.Write(counter.countsUp);
			writer.Write(counter.needReset);
		}
		if (IsReading)
		{
			counter.min = reader.ReadInt32();
			counter.max = reader.ReadInt32();
			counter.counter = reader.ReadInt32();
			counter.countsUp = reader.ReadBoolean();
			counter.needReset = reader.ReadBoolean();
		}
	}

	public void Serialize<T>(ref T customSerializable) where T : ICustomSerializable, new()
	{
		if (IsReading)
		{
			customSerializable = new T();
		}
		customSerializable.CustomSerialize(this);
	}

	public void SerializeNullable<T>(ref T customSerializable) where T : ICustomSerializable, new()
	{
		if (IsWriting)
		{
			writer.Write(customSerializable != null);
			if (customSerializable != null)
			{
				customSerializable.CustomSerialize(this);
			}
		}
		if (IsReading && reader.ReadBoolean())
		{
			customSerializable = new T();
			customSerializable.CustomSerialize(this);
		}
	}

	public void SerializeNullableDelta<T>(ref T customSerializable) where T : ICustomSerializable, new()
	{
		if (IsDelta)
		{
			SerializeNullable(ref customSerializable);
		}
		else
		{
			Serialize(ref customSerializable);
		}
	}

	public void SerializeByte<T>(ref List<T> listOfSerializables) where T : ICustomSerializable, new()
	{
		if (IsWriting)
		{
			if (listOfSerializables.Count > 255)
			{
				throw new OverflowException("too many elements");
			}
			writer.Write((byte)listOfSerializables.Count);
			for (int i = 0; i < listOfSerializables.Count; i++)
			{
				listOfSerializables[i].CustomSerialize(this);
			}
		}
		if (IsReading)
		{
			byte b = reader.ReadByte();
			listOfSerializables = new List<T>(b);
			for (int j = 0; j < b; j++)
			{
				T item = new T();
				item.CustomSerialize(this);
				listOfSerializables.Add(item);
			}
		}
	}

	public void SerializeByte<T>(ref T[] arrayOfSerializables) where T : ICustomSerializable, new()
	{
		if (IsWriting)
		{
			if (arrayOfSerializables.Length > 255)
			{
				throw new OverflowException("too many elements");
			}
			writer.Write((byte)arrayOfSerializables.Length);
			for (int i = 0; i < arrayOfSerializables.Length; i++)
			{
				arrayOfSerializables[i].CustomSerialize(this);
			}
		}
		if (IsReading)
		{
			byte b = reader.ReadByte();
			arrayOfSerializables = new T[b];
			for (int j = 0; j < b; j++)
			{
				T val = new T();
				val.CustomSerialize(this);
				arrayOfSerializables[j] = val;
			}
		}
	}

	public void SerializeShort<T>(ref List<T> listOfSerializables) where T : ICustomSerializable, new()
	{
		if (IsWriting)
		{
			if (listOfSerializables.Count > 65535)
			{
				throw new OverflowException("too many elements");
			}
			writer.Write((ushort)listOfSerializables.Count);
			for (int i = 0; i < listOfSerializables.Count; i++)
			{
				listOfSerializables[i].CustomSerialize(this);
			}
		}
		if (IsReading)
		{
			ushort num = reader.ReadUInt16();
			listOfSerializables = new List<T>(num);
			for (int j = 0; j < num; j++)
			{
				T item = new T();
				item.CustomSerialize(this);
				listOfSerializables.Add(item);
			}
		}
	}

	public void SerializeShort<T>(ref T[] arrayOfSerializables) where T : ICustomSerializable, new()
	{
		if (IsWriting)
		{
			if (arrayOfSerializables.Length > 65535)
			{
				throw new OverflowException("too many elements");
			}
			writer.Write((ushort)arrayOfSerializables.Length);
			for (int i = 0; i < arrayOfSerializables.Length; i++)
			{
				arrayOfSerializables[i].CustomSerialize(this);
			}
		}
		if (IsReading)
		{
			ushort num = reader.ReadUInt16();
			arrayOfSerializables = new T[num];
			for (int j = 0; j < num; j++)
			{
				T val = new T();
				val.CustomSerialize(this);
				arrayOfSerializables[j] = val;
			}
		}
	}

	internal static MethodInfo GetSerializationMethod(Type fieldType, bool nullable, bool polymorphic, bool longList)
	{
		TypeInfo typeInfo = new TypeInfo(fieldType, nullable, polymorphic, longList);
		MethodInfo value = null;
		if (serializerMethods.TryGetValue(typeInfo, out value))
		{
			RainMeadow.Debug($"Using cached method for {typeInfo}", "/Online/Serialization/Serializer.ICustomSerializable.cs", "GetSerializationMethod");
			return value;
		}
		RainMeadow.Debug($"Adding cached method for {typeInfo}", "/Online/Serialization/Serializer.ICustomSerializable.cs", "GetSerializationMethod");
		MethodInfo methodInfo = MakeSerializationMethod(fieldType, nullable, polymorphic, longList);
		serializerMethods.Add(typeInfo, methodInfo);
		return methodInfo;
	}

	internal static MethodInfo MakeSerializationMethod(Type fieldType, bool nullable, bool polymorphic, bool longList)
	{
		var arguments = new { nullable, polymorphic, longList };
		if (typeof(OnlineState).IsAssignableFrom(fieldType))
		{
			return typeof(Serializer).GetMethods().Single(delegate(MethodInfo m)
			{
				string name = m.Name;
				if (1 == 0)
				{
				}
				string text = default(string);
				if (arguments != null)
				{
					text = ((!arguments.nullable) ? (arguments.polymorphic ? "SerializePolyState" : "SerializeStaticState") : (arguments.polymorphic ? "SerializeNullablePolyState" : "SerializeNullableStaticState"));
				}
				else
				{
					if (1 == 0)
					{
					}
					global::<PrivateImplementationDetails>.ThrowInvalidOperationException();
				}
				if (1 == 0)
				{
				}
				return name == text && m.IsGenericMethod;
			}).MakeGenericMethod(fieldType);
		}
		if (typeof(OnlineState[]).IsAssignableFrom(fieldType) || (fieldType.IsGenericType && fieldType.GetGenericTypeDefinition() == typeof(List<>) && typeof(OnlineState).IsAssignableFrom(fieldType.GetGenericArguments()[0])))
		{
			return typeof(Serializer).GetMethods().Single(delegate(MethodInfo m)
			{
				string name = m.Name;
				if (1 == 0)
				{
				}
				string text = default(string);
				if (arguments != null)
				{
					text = ((!arguments.nullable) ? ((!arguments.polymorphic) ? (arguments.longList ? "SerializeStaticStatesShort" : "SerializeStaticStatesByte") : (arguments.longList ? "SerializePolyStatesShort" : "SerializePolyStatesByte")) : ((!arguments.polymorphic) ? (arguments.longList ? "SerializeNullableStaticStatesShort" : "SerializeNullableStaticStatesByte") : (arguments.longList ? "SerializeNullablePolyStatesShort" : "SerializeNullablePolyStatesByte")));
				}
				else
				{
					if (1 == 0)
					{
					}
					global::<PrivateImplementationDetails>.ThrowInvalidOperationException();
				}
				if (1 == 0)
				{
				}
				return name == text && m.IsGenericMethod && m.GetParameters()[0].ParameterType.GetElementType().IsArray == fieldType.IsArray;
			}).MakeGenericMethod(fieldType.IsArray ? fieldType.GetElementType() : fieldType.GetGenericArguments()[0]);
		}
		if (typeof(ICustomSerializable).IsAssignableFrom(fieldType))
		{
			return typeof(Serializer).GetMethods().Single(delegate(MethodInfo m)
			{
				string name = m.Name;
				if (1 == 0)
				{
				}
				string text = default(string);
				if (arguments != null)
				{
					text = (arguments.nullable ? "SerializeNullable" : "Serialize");
				}
				else
				{
					if (1 == 0)
					{
					}
					global::<PrivateImplementationDetails>.ThrowInvalidOperationException();
				}
				if (1 == 0)
				{
				}
				return name == text && m.IsGenericMethod && m.GetGenericMethodDefinition().GetGenericArguments().Any((Type ga) => ga.GetGenericParameterConstraints().Any((Type t) => t == typeof(ICustomSerializable))) && m.GetParameters().Any((ParameterInfo p) => p.ParameterType.IsByRef && (!p.ParameterType.GetElementType().IsGenericType || p.ParameterType.GetElementType().GetGenericTypeDefinition() != typeof(List<>)) && !p.ParameterType.GetElementType().IsArray);
			}).MakeGenericMethod(fieldType);
		}
		if (typeof(ICustomSerializable[]).IsAssignableFrom(fieldType) || (fieldType.IsGenericType && fieldType.GetGenericTypeDefinition() == typeof(List<>) && typeof(ICustomSerializable).IsAssignableFrom(fieldType.GetGenericArguments()[0])))
		{
			return typeof(Serializer).GetMethods().Single(delegate(MethodInfo m)
			{
				string name = m.Name;
				if (1 == 0)
				{
				}
				string text = default(string);
				if (arguments != null)
				{
					text = ((!arguments.nullable) ? (arguments.longList ? "SerializeShort" : "SerializeByte") : (arguments.longList ? "SerializeNullableShort" : "SerializeNullableByte"));
				}
				else
				{
					if (1 == 0)
					{
					}
					global::<PrivateImplementationDetails>.ThrowInvalidOperationException();
				}
				if (1 == 0)
				{
				}
				return name == text && m.IsGenericMethod && m.GetGenericMethodDefinition().GetGenericArguments().Any((Type ga) => ga.GetGenericParameterConstraints().Any((Type t) => t == typeof(ICustomSerializable))) && m.GetParameters().Any((ParameterInfo p) => p.ParameterType.IsByRef && (p.ParameterType.GetElementType().IsGenericType && p.ParameterType.GetElementType().GetGenericTypeDefinition() == typeof(List<>)) != fieldType.IsArray && p.ParameterType.GetElementType().IsArray == fieldType.IsArray);
			}).MakeGenericMethod(fieldType.IsArray ? fieldType.GetElementType() : fieldType.GetGenericArguments()[0]);
		}
		if (typeof(OnlineResource).IsAssignableFrom(fieldType))
		{
			return typeof(Serializer).GetMethod("SerializeResourceByReference").MakeGenericMethod(fieldType);
		}
		if (typeof(OnlineEntity).IsAssignableFrom(fieldType))
		{
			return typeof(Serializer).GetMethod(nullable ? "SerializeNullableEntityById" : "SerializeEntityById").MakeGenericMethod(fieldType);
		}
		if (typeof(OnlinePlayer).IsAssignableFrom(fieldType))
		{
			return typeof(Serializer).GetMethod("SerializePlayerInLobby");
		}
		if (typeof(OnlineEvent).IsAssignableFrom(fieldType))
		{
			return typeof(Serializer).GetMethods().Single((MethodInfo m) => m.Name == "SerializeEvent" && m.IsGenericMethod).MakeGenericMethod(fieldType);
		}
		Type baseType = fieldType.BaseType;
		if ((object)baseType != null && baseType.IsGenericType && typeof(ExtEnum<>).IsAssignableFrom(fieldType.BaseType.GetGenericTypeDefinition()))
		{
			return typeof(Serializer).GetMethods().Single((MethodInfo m) => m.Name == (arguments.nullable ? "SerializeNullableExtEnum" : "SerializeExtEnum") && m.IsGenericMethod).MakeGenericMethod(fieldType);
		}
		if (!fieldType.IsValueType && (!fieldType.IsArray || !fieldType.GetElementType().IsValueType) && fieldType != typeof(string))
		{
			RainMeadow.Debug($"{fieldType} not handled by SerializerCallMethod", "/Online/Serialization/Serializer.ICustomSerializable.cs", "MakeSerializationMethod");
		}
		MethodInfo method = typeof(Serializer).GetMethod(nullable ? "SerializeNullable" : "Serialize", new Type[1] { fieldType.MakeByRefType() });
		if ((object)method != null)
		{
			return method;
		}
		Type underlyingType = Nullable.GetUnderlyingType(fieldType);
		if ((object)underlyingType != null)
		{
			MethodInfo method2 = typeof(Serializer).GetMethod("SerializeNullable", new Type[1] { fieldType.MakeByRefType() });
			if ((object)method2 != null)
			{
				return method2;
			}
			FieldInfo field = typeof(Serializer).GetField("writer");
			FieldInfo field2 = typeof(Serializer).GetField("reader");
			MethodInfo getMethod = fieldType.GetProperty("HasValue").GetGetMethod();
			MethodInfo getMethod2 = fieldType.GetProperty("Value").GetGetMethod();
			ConstructorInfo constructor = fieldType.GetConstructor(new Type[1] { underlyingType });
			MethodInfo serializationMethod = GetSerializationMethod(underlyingType, nullable: false, polymorphic: true, longList: true);
			if (serializationMethod == null)
			{
				throw new InvalidOperationException("No matching serialization method found for type " + underlyingType.FullName);
			}
			DynamicMethod dynamicMethod = new DynamicMethod("SerializeNullable" + underlyingType.Name, null, new Type[2]
			{
				typeof(Serializer),
				fieldType.MakeByRefType()
			});
			ILGenerator iLGenerator = dynamicMethod.GetILGenerator();
			LocalBuilder local = iLGenerator.DeclareLocal(underlyingType, pinned: true);
			Label label = iLGenerator.DefineLabel();
			Label label2 = iLGenerator.DefineLabel();
			Label label3 = iLGenerator.DefineLabel();
			iLGenerator.Emit(OpCodes.Ldloca, local);
			iLGenerator.Emit(OpCodes.Initobj, underlyingType);
			iLGenerator.Emit(OpCodes.Stloc, local);
			iLGenerator.Emit(OpCodes.Ldarg_0);
			iLGenerator.Emit(OpCodes.Call, typeof(Serializer).GetProperty("IsWriting").GetGetMethod());
			iLGenerator.Emit(OpCodes.Brfalse_S, label);
			iLGenerator.Emit(OpCodes.Ldarg_0);
			iLGenerator.Emit(OpCodes.Ldfld, field);
			iLGenerator.Emit(OpCodes.Ldarg_1);
			iLGenerator.Emit(OpCodes.Call, getMethod);
			iLGenerator.Emit(OpCodes.Callvirt, typeof(BinaryWriter).GetMethod("Write", new Type[1] { typeof(bool) }));
			iLGenerator.Emit(OpCodes.Ldarg_1);
			iLGenerator.Emit(OpCodes.Call, getMethod);
			iLGenerator.Emit(OpCodes.Brfalse_S, label);
			iLGenerator.Emit(OpCodes.Ldarg_1);
			iLGenerator.Emit(OpCodes.Call, getMethod2);
			iLGenerator.Emit(OpCodes.Stloc, local);
			iLGenerator.Emit(OpCodes.Br, label3);
			iLGenerator.MarkLabel(label);
			iLGenerator.Emit(OpCodes.Ldarg_0);
			iLGenerator.Emit(OpCodes.Call, typeof(Serializer).GetProperty("IsReading").GetGetMethod());
			iLGenerator.Emit(OpCodes.Brfalse_S, label2);
			iLGenerator.Emit(OpCodes.Ldarg_0);
			iLGenerator.Emit(OpCodes.Ldfld, field2);
			iLGenerator.Emit(OpCodes.Callvirt, typeof(BinaryReader).GetMethod("ReadBoolean"));
			iLGenerator.Emit(OpCodes.Brfalse_S, label2);
			iLGenerator.Emit(OpCodes.Br, label3);
			iLGenerator.MarkLabel(label2);
			iLGenerator.Emit(OpCodes.Ret);
			iLGenerator.MarkLabel(label3);
			iLGenerator.Emit(OpCodes.Ldarg_0);
			iLGenerator.Emit(OpCodes.Ldloca, local);
			iLGenerator.Emit(OpCodes.Call, serializationMethod);
			iLGenerator.Emit(OpCodes.Ldarga, 1);
			iLGenerator.Emit(OpCodes.Ldloc, local);
			iLGenerator.Emit(OpCodes.Newobj, constructor);
			iLGenerator.Emit(OpCodes.Stobj, fieldType);
			iLGenerator.Emit(OpCodes.Ret);
			return dynamicMethod;
		}
		if (fieldType.GetCustomAttribute(typeof(SerializableAttribute)) is SerializableAttribute && !fieldType.IsPrimitive && !fieldType.IsEnum && !fieldType.IsArray && fieldType != typeof(string))
		{
			RainMeadow.Debug($"Generating function for [Serializable] {fieldType}", "/Online/Serialization/Serializer.ICustomSerializable.cs", "MakeSerializationMethod");
			DynamicMethod dynamicMethod2 = new DynamicMethod("SerializeSystemSerializable" + fieldType.Name, null, new Type[2]
			{
				typeof(Serializer),
				fieldType.MakeByRefType()
			}, restrictedSkipVisibility: true);
			ILGenerator iLGenerator2 = dynamicMethod2.GetILGenerator();
			FieldInfo[] array = (from x in fieldType.GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
				where x.GetCustomAttribute(typeof(NonSerializedAttribute)) == null
				select x).OfType<FieldInfo>().ToArray();
			FieldInfo[] array2 = array;
			foreach (FieldInfo fieldInfo in array2)
			{
				RainMeadow.Debug($"{fieldInfo}", "/Online/Serialization/Serializer.ICustomSerializable.cs", "MakeSerializationMethod");
				MethodInfo serializationMethod2 = GetSerializationMethod(fieldInfo.FieldType, nullable: false, polymorphic: false, longList: false);
				iLGenerator2.Emit(OpCodes.Ldarg, 0);
				iLGenerator2.Emit(OpCodes.Ldarga, 1);
				iLGenerator2.Emit(OpCodes.Ldflda, fieldInfo);
				iLGenerator2.Emit(OpCodes.Call, serializationMethod2);
			}
			iLGenerator2.Emit(OpCodes.Ret);
			return dynamicMethod2;
		}
		throw new KeyNotFoundException("Could not find a valid serializable function");
	}
}
