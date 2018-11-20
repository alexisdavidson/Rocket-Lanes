﻿using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;

public class P2PConnections
{
	public static List<P2PConnection> connections = new List<P2PConnection>();
	public static P2PController p2PController;
	public static int myHostId;

	public static bool requestPlayersInfoSent = false;
	public static bool playersInfoReceived = false;

	public static void ConnectEvent(int hostId, int connectionId)
	{
		P2PConnection connection = connections.FirstOrDefault(c => 
								c.hostId == hostId && 
								c.connectionId == connectionId);
		if(connection == null)
		{
			//new connection from targeted ip or new player
			connection = new P2PConnection(hostId, connectionId);
			connection.SuccessfullyConnect();

			if(p2PController.GameStarted())
			{
				//todo
				//here we would ask consent to others if the newcomer has the right to join the game
				//and compute his lane Id?
			}
			connections.Add(connection);
			Debug.Log("New connection with " + connection);
		}
		else if(!connection.ConnectionSuccessful())
		{
			//successfully connect to an existing player. Connection requested thanks to PlayersInfo
			connection.SuccessfullyConnect();
		}

		//on connecting to an existing game, request infos of other players
		if(!p2PController.GameStarted())
		{
			if(!requestPlayersInfoSent)
				P2PConnections.RequestPlayersInfo(hostId, connectionId);
			if(playersInfoReceived)
				CheckConnectionsStatus();
		}
		
	}

	public static void SharePlayersInfo(int hostId, int connectionId)
	{
		PlayersInfoMessage message = new PlayersInfoMessage();

		message.freeLane = p2PController.GetGameController().GetFirstUnoccupiedLane().id;

		message.connections = new List<P2PConnection>();
		foreach(P2PConnection connection in connections)
		{
			//send all connections except the one with the requester
			if(!(connection.hostId == hostId && connection.connectionId == connectionId))
				message.connections.Add(connection);
		}

		Debug.Log("Sharing players info, count: " + message.connections.Count);
		P2PSender.Send(hostId, connectionId, P2PChannels.ReliableChannelId, message, MessageTypes.PlayersInfo);
	}

	public static void FetchPlayersInfo(PlayersInfoMessage message)
	{
		Debug.Log("Fetching players infos..." + message.connections.Count + " my lane: " + message.freeLane);
		
		p2PController.myLane = message.freeLane;

		if(connections.Count > 1)
		{
			Debug.Log("Warning! Already have players infos");
		}
		else
		{
			foreach(P2PConnection connection in message.connections)
			{
				Debug.Log("Fetched and request connection with " + connection);

				//send connection requests
				NetworkTransport.Connect(myHostId, connection.ip, connection.port, 0, out P2PController.error);
				P2PController.CheckError("Connect");
			}
		}
		playersInfoReceived = true;
		CheckConnectionsStatus();
	}

	public static void RequestPlayersInfo(int hostId, int connectionId)
	{
		requestPlayersInfoSent = true;
		RequestPlayersInfoMessage message = new RequestPlayersInfoMessage();
		P2PSender.Send(hostId, connectionId, P2PChannels.ReliableChannelId, message, MessageTypes.RequestPlayersInfo);
	}

	static void CheckConnectionsStatus()
	{
		bool connectedToAll = true;
		foreach(P2PConnection connection in connections)
		{
			if(!connection.ConnectionSuccessful())
				connectedToAll = false;
		}

		if(connectedToAll)
			p2PController.StartGame();
	}

	public static void RemoveConnection(int recHostId, int connectionId)
	{
		P2PConnection connection = connections.FirstOrDefault(c => 
								c.hostId == recHostId && 
								c.connectionId == connectionId);
		if(connection == null)
			Debug.Log("Warning! Connection with " + connection + " doesn't exist");
		else
		{
			//Remove connection
			connections.Remove(connection);
			Debug.Log("Remove connection with " + connection);
		}
	}

	public static void DisconnectAll()
	{
		foreach(P2PConnection connection in connections)
			connection.Disconnect();
		connections.Clear();
	}
}
