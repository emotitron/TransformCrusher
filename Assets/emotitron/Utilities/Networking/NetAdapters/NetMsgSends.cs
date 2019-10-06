using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using emotitron.Compression;


#if PUN_2_OR_NEWER
using Photon;
using Photon.Pun;
using Photon.Realtime;
using ExitGames.Client.Photon;
#elif MIRROR
using Mirror;
#else
using UnityEngine.Networking;
#endif

#pragma warning disable CS0618 // UNET obsolete

namespace emotitron.Utilities.Networking
{
	public enum ReceiveGroup { Others, All, Master }

	/// <summary>
	/// Unified code for sending network messages across different Network Libraries.
	/// </summary>
	public static class NetMsgSends
	{
		public static byte[] reusableIncomingBuffer = new byte[4000];
		public static byte[] reusableOutgoingBuffer = new byte[4000];

#if PUN_2_OR_NEWER

		public static bool ReadyToSend { get { return PhotonNetwork.NetworkClientState == ClientState.Joined; } }
		public static bool AmActiveServer { get { return false; } }

		private static RaiseEventOptions[] opts = new RaiseEventOptions[3]
		{
			new RaiseEventOptions() { Receivers = ReceiverGroup.Others },
			new RaiseEventOptions() { Receivers = ReceiverGroup.All },
			new RaiseEventOptions() { Receivers = ReceiverGroup.MasterClient }
		};

		private static SendOptions sendOptsUnreliable = new SendOptions() { DeliveryMode = DeliveryMode.UnreliableUnsequenced };
		private static SendOptions sendOptsReliable = new SendOptions() { DeliveryMode = DeliveryMode.Reliable };

		public static void Send(this byte[] buffer, int bitposition, GameObject refObj, ReceiveGroup rcvGrp, bool sendReliable = false, bool flush = false)
		{
			int bytecount = (bitposition + 7) >> 3;

			System.ArraySegment<byte> byteseg = new System.ArraySegment<byte>(buffer, 0, bytecount);

			PhotonNetwork.NetworkingClient.OpRaiseEvent(NetMsgCallbacks.DEF_MSG_ID, byteseg, opts[(int)rcvGrp], (sendReliable) ? sendOptsReliable : sendOptsUnreliable);

			if (flush)
				PhotonNetwork.NetworkingClient.Service();
		}

#else
		public static bool ReadyToSend { get { return NetworkServer.active || ClientScene.readyConnection != null; } }
		public static bool AmActiveServer { get { return NetworkServer.active; } }

		public static readonly BytesMessageNonalloc bytesmsg = new BytesMessageNonalloc();

		public static void Send(this byte[] buffer, int bitcount, GameObject refObj, ReceiveGroup rcvGrp, bool sendReliable = false, bool flush = false)
		{
			BytesMessageNonalloc.outgoingbuffer = buffer;
			BytesMessageNonalloc.length = (ushort)((bitcount + 7) >> 3);
			Send(refObj, bytesmsg, NetMsgCallbacks.DEF_MSG_ID, rcvGrp, sendReliable ? 0 : 1, flush);
		}

#if BANDWIDTH_MONITOR
		static int monitorTime;
		static int byteCountForTime;
#endif
		/// <summary>
		/// Sends byte[] to each client, making any needed per client alterations, such as changing the frame offset value in the first byte.
		/// </summary>
		public static void Send(GameObject refObj, BytesMessageNonalloc msg, short msgId, ReceiveGroup rcvGrp, int channel = Channels.DefaultUnreliable, bool flush = false)
		{

#if BANDWIDTH_MONITOR

			if (monitorTime != (int)Time.time)
			{
				/// Print last total
				Debug.Log("Sent Bytes/Sec: " + byteCountForTime);
				byteCountForTime = 0;
				monitorTime = (int)Time.time;
			}

			byteCountForTime += BytesMessageNonalloc.length;
#endif
			/////TEST
			//rcvGrp = ReceiveGroup.All;

			/// Client's cant send to all, so we will just send to server to make 'others' always work.
			if (!NetworkServer.active)
			{
				var conn = ClientScene.readyConnection;
				if (conn != null)
				{
#if MIRROR
					conn.Send<BytesMessageNonalloc>(msg, channel);
#else
					conn.SendByChannel(msgId, msg, channel);

					if (flush)
						conn.FlushChannels();
#endif
				}
			}
			/// Server send to all. Owner client send to server.
			else if (rcvGrp == ReceiveGroup.All)
			{
#if MIRROR
				var ni = (refObj) ? refObj.GetComponent<NetworkIdentity>() : null;
				NetworkServer.SendToReady(ni, msg, channel);
#else
				NetworkServer.SendByChannelToReady(refObj, msgId, msg, channel);
#endif

			}

			/// Send To Others
			else
			{
				var ni = (refObj) ? refObj.GetComponent<NetworkIdentity>() : null;
				var observers = ni ? ni.observers : null;
#if MIRROR
				foreach (NetworkConnection conn in NetworkServer.connections.Values)
#else
				foreach (NetworkConnection conn in NetworkServer.connections)
#endif
				{
					if (conn == null)
						continue;

					/// Don't send to self if Host
					if (conn.connectionId == 0)
						continue;

#if MIRROR
					if (conn.isReady && (observers != null && observers.ContainsKey(conn.connectionId)))
					{
						conn.Send<BytesMessageNonalloc>(msg, channel);
					}
#else
					if (conn.isReady && (observers == null || observers.Contains(conn)))
					{
						conn.SendByChannel(msgId, msg, channel);
						if (flush)
							conn.FlushChannels();
					}
#endif
				}
			}
		}
#endif
	}

}
#pragma warning restore CS0618 // UNET obsolete
