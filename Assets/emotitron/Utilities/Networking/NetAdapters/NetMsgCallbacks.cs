﻿//Copyright 2018, Davin Carten, All rights reserved

using System.Collections.Generic;
using UnityEngine;
using emotitron.Debugging;
using emotitron.Compression;

#if PUN_2_OR_NEWER
using Photon.Pun;
using Photon.Realtime;
using ExitGames.Client.Photon;
#elif MIRROR
using Mirror;
#else
using UnityEngine.Networking;
#endif

#pragma warning disable CS0618 // UNET obsolete

/// <summary>
/// Generic handelers for routing incoming network messages as byte[] arrays to registered handlers. This abstracts the various netlibs into a
/// standard byte[] format.
/// </summary>
namespace emotitron.Utilities.Networking
{

#if !PUN_2_OR_NEWER
	/// <summary>
	///  Nonalloc message for Mirror, since we can't directly send writers with Mirror. Set the buffer and length values prior to sending/rcving.
	/// </summary>
	public class BytesMessageNonalloc : MessageBase
	{
		public static byte[] incomingbuffer = NetMsgSends.reusableByteArray;
		public byte[] buffer = NetMsgSends.reusableByteArray;
		public ushort length;

		public BytesMessageNonalloc() { }

		public BytesMessageNonalloc(byte[] nonalloc)
		{
			this.buffer = nonalloc;
		}

		public BytesMessageNonalloc(byte[] nonalloc, ushort length)
		{
			this.buffer = nonalloc;
			this.length = length;
		}

		public override void Serialize(NetworkWriter writer)
		{
			writer.Write(length);
			for (int i = 0; i < length; i++)
				writer.Write(buffer[i]);
		}

		public override void Deserialize(NetworkReader reader)
		{
			length = reader.ReadUInt16();
			for (int i = 0; i < length; i++)
				incomingbuffer[i] = reader.ReadByte();
		}
	}

#endif

	public static class NetMsgCallbacks
	{
		public delegate void ByteBufferCallback(byte[] buffer);

		private static Dictionary<int, CallbackLists> callbacks = new Dictionary<int, CallbackLists>();

		private class CallbackLists
		{
			public List<ByteBufferCallback> bufferCallbacks;
		}


#if PUN_2_OR_NEWER

		#region PUN2

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
		private static void RegisterOnEventListener()
		{
			PhotonNetwork.NetworkingClient.EventReceived += OnEvent;
		}

		/// <summary>
		/// Capture incoming Photon messages here.
		/// </summary>
		public static void OnEvent(EventData photonEvent)
		{
			byte msgId = photonEvent.Code;

			if (!callbacks.ContainsKey(msgId))
				return;

			// ignore messages from self.
			if (PhotonNetwork.IsMasterClient && PhotonNetwork.MasterClient.ActorNumber == photonEvent.Sender())
			{
				Debug.Log("Master Client talking to self? Normal occurance for a few seconds after Master leaves the game and a new master is selected.");
				return;
			}

			byte[] buffer = (photonEvent.CustomData() as byte[]);

			var cbs = callbacks[msgId];
			if (cbs.bufferCallbacks != null && cbs.bufferCallbacks.Count > 0)
			{
				foreach (var cb in cbs.bufferCallbacks)
					cb(buffer);
			}

		}

		#region Handler Registration

		[System.Obsolete("Removed the asServer from UNET side, killing it here as well.")]
		public static void RegisterHandler(byte msgid, ByteBufferCallback callback, bool asServer)
		{
			if (!callbacks.ContainsKey(msgid))
				callbacks.Add(msgid, new CallbackLists());

			if (callbacks[msgid].bufferCallbacks == null)
				callbacks[msgid].bufferCallbacks = new List<ByteBufferCallback>();

			var cbs = callbacks[msgid].bufferCallbacks;

			if (!cbs.Contains(callback))
				cbs.Add(callback);
		}

		public static void RegisterHandler(byte msgid, ByteBufferCallback callback)
		{
			if (!callbacks.ContainsKey(msgid))
				callbacks.Add(msgid, new CallbackLists());

			if (callbacks[msgid].bufferCallbacks == null)
				callbacks[msgid].bufferCallbacks = new List<ByteBufferCallback>();

			var cbs = callbacks[msgid].bufferCallbacks;

			if (!cbs.Contains(callback))
				cbs.Add(callback);
		}

		[System.Obsolete("Removed the asServer from UNET side, killing it here as well.")]
		public static void UnregisterHandler(byte msgid, ByteBufferCallback callback, bool asServer)
		{
			if (callbacks.ContainsKey(msgid))
			{
				var cbs = callbacks[msgid];
				cbs.bufferCallbacks.Remove(callback);

				if (cbs.bufferCallbacks.Count == 0 )
					callbacks.Remove(msgid);
			}
		}

		public static void UnregisterHandler(byte msgid, ByteBufferCallback callback)
		{
			if (callbacks.ContainsKey(msgid))
			{
				var cbs = callbacks[msgid];
				cbs.bufferCallbacks.Remove(callback);

				if (cbs.bufferCallbacks.Count == 0)
					callbacks.Remove(msgid);
			}
		}

		#endregion  // END HANDLERS

		#endregion  // END PUN2
#else


		private static bool RegisterMessageId(short msgId)
		{
			/// Make sure network is active, or registering handlers will fail, or they will just be forgotten
			if (NetworkServer.active)
			{
#if MIRROR
				NetworkClient.UnregisterHandler<BytesMessageNonalloc>();
				NetworkServer.RegisterHandler<BytesMessageNonalloc>(OnMessage);
#else
				NetworkServer.RegisterHandler(msgId, OnMessage);
				if (!ReferenceEquals(NetworkManager.singleton.client, null))
					NetworkManager.singleton.client.UnregisterHandler(msgId);
#endif
			}
			else if (NetworkClient.active)
			{
#if MIRROR
				NetworkServer.UnregisterHandler<BytesMessageNonalloc>();
				NetworkClient.RegisterHandler<BytesMessageNonalloc>(OnMessage);
#else
				NetworkServer.UnregisterHandler(msgId);
				NetworkManager.singleton.client.RegisterHandler(msgId, OnMessage);
#endif
			}
			else
				return false;

			if (!callbacks.ContainsKey(msgId))
				callbacks.Add(msgId, new CallbackLists());

			return true;
		}

		[System.Obsolete("Moving to make the asServer handling not needed, to make send more generic.")]
		private static bool RegisterMessageId(short msgId, bool asServer)
		{
			/// Make sure network is active, or registering handlers will fail, or they will just be forgotten
			if (asServer)
			{
				if (NetworkServer.active)
#if MIRROR
					NetworkServer.RegisterHandler<BytesMessageNonalloc>(OnMessage);
#else
					NetworkServer.RegisterHandler(msgId, OnMessage);
#endif
				else
					return false;
			}
			else
			{
				if (NetworkClient.active)
#if MIRROR
					NetworkClient.RegisterHandler<BytesMessageNonalloc>(OnMessage);
#else
					NetworkManager.singleton.client.RegisterHandler(msgId, OnMessage);
#endif

				else
					return false;
			}

			if (!callbacks.ContainsKey(msgId))
				callbacks.Add(msgId, new CallbackLists());

			return true;
		}


		public static void RegisterHandler(short msgId, ByteBufferCallback callback)
		{

#if MIRROR
			msgId = 0;
#endif

			if (callback == null)
				return;

			if (!RegisterMessageId(msgId))
				return;

			/// make a new list if this is the first item
			if (callbacks[msgId].bufferCallbacks == null)
				callbacks[msgId].bufferCallbacks = new List<ByteBufferCallback>();

			/// don't register the same callback twice
			if (callbacks[msgId].bufferCallbacks.Contains(callback))
				return;

			callbacks[msgId].bufferCallbacks.Add(callback);
		}

		[System.Obsolete("Moving to make the asServer handling not needed, to make send more generic.")]
		public static void RegisterHandler(short msgId, ByteBufferCallback callback, bool asServer)
		{
			if (callback == null)
				return;

#if MIRROR
			msgId = 0;
#endif

			if (!RegisterMessageId(msgId, asServer))
				return;

			/// make a new list if this is the first item
			if (callbacks[msgId].bufferCallbacks == null)
				callbacks[msgId].bufferCallbacks = new List<ByteBufferCallback>();

			/// don't register the same callback twice
			if (callbacks[msgId].bufferCallbacks.Contains(callback))
				return;

			callbacks[msgId].bufferCallbacks.Add(callback);
		}

		[System.Obsolete("Moving to make the asServer handling not needed, to make send more generic.")]
		private static void UnregisterMessageId(int msgId, bool asServer)
		{
			if (asServer)
				NetworkServer.UnregisterHandler((short)msgId);
			else
#if MIRROR
				NetworkClient.UnregisterHandler((short)msgId);
#else
						if (NetworkManager.singleton.client != null)
				NetworkManager.singleton.client.UnregisterHandler((short)msgId);
#endif
		}

		private static void UnregisterMessageId(int msgId)
		{
			if (NetworkServer.active)
				NetworkServer.UnregisterHandler((short)msgId);
			if (NetworkClient.active)
#if MIRROR
				NetworkClient.UnregisterHandler((short)msgId);
#else
				if (NetworkManager.singleton.client != null)
					NetworkManager.singleton.client.UnregisterHandler((short)msgId);
#endif
		}


		public static void UnregisterHandler(short msgId, ByteBufferCallback callback)
		{
#if MIRROR
			msgId = 0;
#endif

			if (!callbacks.ContainsKey(msgId) || callbacks[msgId].bufferCallbacks == null)
				return;

			var cbs = callbacks[msgId];

			// Remove callback method from list for this msgid
			cbs.bufferCallbacks.Remove(callback);

			/// Remove the dictionary entry entirely if we no longer have any callbacks
			if (cbs.bufferCallbacks.Count == 0)
			{
				UnregisterMessageId(msgId);
				callbacks.Remove(msgId);
			}
		}

		[System.Obsolete("Moving to make the asServer handling not needed, to make send more generic.")]
		public static void UnregisterHandler(short msgId, ByteBufferCallback callback, bool asServer)
		{
			if (!callbacks.ContainsKey(msgId) || callbacks[msgId].bufferCallbacks == null)
				return;

			var cbs = callbacks[msgId];

			// Remove callback method from list for this msgid
			cbs.bufferCallbacks.Remove(callback);

			/// Remove the dictionary entry entirely if we no longer have any callbacks
			if (cbs.bufferCallbacks.Count == 0/* && (cbs.bitstreamCallbacks == null || cbs.bitstreamCallbacks.Count == 0)*/)
			{
				UnregisterMessageId(msgId, asServer);
				callbacks.Remove(msgId);
			}
		}

		/// <summary>
		/// All of our registered UNET msgId msgs get routed this method, which reads them into a byte[] form before passing them to the callbacks
		/// </summary>
		/// <param name="msg"></param>
#if MIRROR
		public static void OnMessage(NetworkConnection conn, BytesMessageNonalloc bmsg)
		{
			if (!callbacks.ContainsKey(0))
				return;

			var cbs = callbacks[0];


			/// Send to all byte[] buffer callbacks
			var bufferCBList = cbs.bufferCallbacks;

			if (bufferCBList != null)
			{
				var buffer = BytesMessageNonalloc.incomingbuffer;

				int cnt = bufferCBList.Count;
				for (int i = 0; i < cnt; ++i)
					bufferCBList[i](buffer);
			}
		}
#else
		public static void OnMessage(NetworkMessage msg)
		{
			var bmsg = NetMsgSends.bytesmsg;

			bmsg.Deserialize(msg.reader);

			var msgId = msg.msgType;

			if (!callbacks.ContainsKey(msgId))
				return;

			var cbs = callbacks[msgId];

			/// Send to all byte[] buffer callbacks
			var bufferCBList = cbs.bufferCallbacks;

			if (bufferCBList != null)
			{
				int cnt = bufferCBList.Count;
				for (int i = 0; i < cnt; ++i)
					bufferCBList[i](BytesMessageNonalloc.incomingbuffer);
			}
		}
#endif

	}

#endif

}
#pragma warning restore CS0618 // UNET obsolete
