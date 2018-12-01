﻿using System;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;

public class P2PController : MonoBehaviour, INetworkController
{
	[SerializeField]
	Player playerPrefab;

	public LatencyProfile latencyProfile;
	public CheaterProfile cheaterProfile;
	public bool consensusAlgorithm = true;
	public float requestTimeoutTimeMs = 500; //500 ms
	public float connectionRequestTimeoutTimeS = 2;

	[HideInInspector]
	public int myPort;

	[HideInInspector]
	public int myLane;
	
	GameController gameController;

	string targetIp; //only when joining
	int targetPort; //only when joining
	bool initialized = false;
	List<Player> players = new List<Player>();

	public static int bufferLength = 1024;
	public static byte error;

	//temp
	float lastSendPosition = 0;
	float cooldownSendPosition = 100;

	void Start()
	{
		gameController = GameObject.FindObjectOfType<GameController>();
	}

    public int AskForConsent(ConsentAction consentAction, int myResult, int[] parameters)
	{
		AskConsentMessage message = new AskConsentMessage();
		message.consentId = P2PConsentManager.GetNextConsentIdAndIncrement();
		message.consentAction = consentAction;
		message.result = myResult;
		message.parameters.AddRange(parameters);
		if(consensusAlgorithm && P2PConnectionManager.SuccessfulConnectionsCount() > 0)
		{
			Debug.Log("P2P: Asking consent for: " + consentAction);
			P2PConsentManager.AddPendingConsent(message);
			P2PSender.SendToAll(P2PChannels.ReliableChannelId, message, MessageTypes.AskConsent);
		}
		else
		{
			Debug.Log("P2P: Imposing consent for: " + consentAction);
			P2PConsentManager.ApplyAndSpreadConsentResult(OnAskForConsentMsg(-1, -1, message));
		}

		return -1;
	}
	
	public AnswerConsentMessage OnAskForConsentMsg(int hostId, int connectionId, AskConsentMessage message)
    {
		Debug.Log("P2P: OnAskForConsentMsg for: " + message.consentAction + ", parameter0: " + message.parameters[0]);
		AnswerConsentMessage answerMessage = new AnswerConsentMessage();
		answerMessage.consentId = message.consentId;
		answerMessage.consentAction = message.consentAction;

		if(message.consentAction == ConsentAction.SpawnRocket)
		{
			answerMessage.result = gameController.lanes[message.parameters[1]].spawnManager.GetRandomSpawnerIndex();
			answerMessage.parameters = message.parameters;
			
			if(hostId != -1 && connectionId != -1) //not imposing consent (see AskForConsent)
				P2PSender.Send(hostId, connectionId, P2PChannels.ReliableChannelId, answerMessage, MessageTypes.AnswerConsent);
		}
		if(message.consentAction == ConsentAction.JoinGame)
		{
			answerMessage.parameters = message.parameters;

			Lane freeLane = gameController.GetFirstUnoccupiedLane();
			answerMessage.result = 10;
			if(freeLane != null)
				answerMessage.result = freeLane.id;

			if(hostId != -1 && connectionId != -1) //not imposing consent (see AskForConsent)
				P2PSender.Send(hostId, connectionId, P2PChannels.ReliableChannelId, answerMessage, MessageTypes.AnswerConsent);
		}
		return answerMessage;
    }

    public void OnApplyConsentMsg(ApplyConsentMessage message)
	{
		Debug.Log("P2P: Applying consent for: " + message.consentAction);
		if(message.consentAction == ConsentAction.SpawnRocket)
		{
			gameController.lanes[message.parameters[1]].spawnManager.Spawn(message.result);
		}
		else if(message.consentAction == ConsentAction.JoinGame)
		{
			P2PConnection connection = P2PConnectionManager.GetConnection(message.parameters[0], message.parameters[1]);
			if(connection != null)
			{
				JoinAnswerMessage answerMessage = new JoinAnswerMessage();
				answerMessage.lane = message.result;
				answerMessage.successfulConnections = P2PConnectionManager.GetSuccessfulConnections();
				P2PSender.Send(message.parameters[0], message.parameters[1], P2PChannels.ReliableChannelId, answerMessage, MessageTypes.JoinAnswer);
			
				connection.SuccessfullyConnect();

				Debug.Log("Sending JoinAnswer with lane: " + answerMessage.lane + "and " + answerMessage.successfulConnections.Count + " connections");
			}
		}
	}

    public void ApplyConsent(ConsentAction consentAction, int consentResult, int[] parameters)
	{
		/*Debug.Log("P2P: Applying consent for: " + consentAction);
		if(consentAction == ConsentAction.SpawnRocket)
		{
			gameController.lanes[parameters[1]].spawnManager.Spawn(consentResult);
		}*/
	}

	public void NewGame()
	{
		Debug.Log("Starting New P2P Game... port: " + myPort);
		Initialize();

		StartNewGame();
	}

	public void JoinGame()
	{
		Debug.Log("Joining P2P Game " + targetIp + ":" + targetPort + "... (my port: " + myPort + ")");
		Initialize();

		NetworkTransport.Connect(P2PConnectionManager.myHostId, targetIp, targetPort, 0, out error);
		CheckError("Connect");
		P2PConnectionManager.connectionRequestSentTime = Time.time;
	}

	void Update()
	{
		if(initialized && !GameStarted())
		{
			float currentTime = Time.time;
			if(currentTime - P2PConnectionManager.connectionRequestSentTime > connectionRequestTimeoutTimeS)
			{
				Debug.Log("Connection request timeout");
				myLane = -1;
				DisplayError("Connection request timeout");
			}
		}
		
		if(initialized)
		{
			SendPositionInformation();
			P2PConsentManager.CheckForTimeoutPendingConsents();
			P2PListener.Listen();
		}
	}

	public void Initialize()
	{
		P2PConnectionManager.p2PController = this;
		P2PListener.p2PController = this;
		P2PConsentManager.p2PController = this;

		//https://docs.unity3d.com/Manual/UNetUsingTransport.html
		NetworkTransport.Init();

		ConnectionConfig config = new ConnectionConfig();
		P2PChannels.ReliableChannelId = config.AddChannel(QosType.Reliable);
		P2PChannels.UnreliableChannelId = config.AddChannel(QosType.Unreliable);

		HostTopology topology = new HostTopology(config, 10);

		P2PConnectionManager.myHostId = NetworkTransport.AddHost(topology, myPort);

		initialized = true;
	}

	public void StartNewGame()
	{
		myLane = 0;
		StartGame();
	}

	public void StartGame()
	{
		P2PConnectionManager.JoinRequestSend = true;
		P2PConnectionManager.JoinAnswerReceived = true;
		
		gameController.StartGame();

		Player player1 = SpawnPlayer(myLane);
		player1.gameObject.GetComponent<PlayerController>().enabled = true;
		gameController.player = player1;
	}

	public void Quit()
	{
		LeaveGame();
	}

	public void LeaveGame()
	{
		initialized = false;
		P2PConnectionManager.DisconnectAll();
		NetworkTransport.Shutdown();

		P2PConnectionManager.Reset();
		players.Clear();
		
		//destroy rockets and players
		Rocket[] rockets = FindObjectsOfType<Rocket>();
		foreach(Rocket rocket in rockets)
			Destroy(rocket.gameObject);
			
		Player[] ps = FindObjectsOfType<Player>();
		foreach(Player p in ps)
			Destroy(p.gameObject);
	}

	public void DisplayError(string er)
	{
		gameController.LeaveGame(false);
		FindObjectOfType<UIController>().DisplayError(er);
	}

	public Player SpawnPlayer(int lane)
	{
		//Debug.Log("Spawn player on lane " + lane);
		Player player = Instantiate(playerPrefab, gameController.lanes[lane].startPosition.transform.position, Quaternion.identity);
		players.Add(player);
		return player;
	}

	public void DespawnPlayer(int lane)
	{
		Player player = players.FirstOrDefault(p => p.lane != null && p.lane.id == lane);
		if(player != null)
			Destroy(player.gameObject);
		players.Remove(player);
	}

	void SendPositionInformation()
	{
		float currentTime = Time.time * 1000;
		if(GameStarted() && currentTime - lastSendPosition > cooldownSendPosition)
		{
			lastSendPosition = Time.time * 1000;
			PositionMessage message = new PositionMessage();
			message.lane = System.Convert.ToUInt32(myLane);
			message.position = gameController.player.transform.position;
			message.hp = System.Convert.ToUInt32(gameController.player.Health);

			P2PSender.SendToAll(P2PChannels.UnreliableChannelId, message, MessageTypes.Position);
		}
	}

	public void ReceivePositionInformation(int hostId, int connectionId, PositionMessage message)
	{
		if(!GameStarted())
			return;
		
		int lane = System.Convert.ToInt32(message.lane);
		Player player = players.FirstOrDefault(p => p.lane != null && p.lane.id == lane);
		if(player != null)
		{
			player.transform.position = message.position;
			player.SetHealth(System.Convert.ToInt32(message.hp));
		}
		else SpawnPlayer(lane);

		P2PConnection connection = P2PConnectionManager.GetConnection(hostId, connectionId);
		if(connection != null)
			connection.lane = lane;
	}

	void OnApplicationQuit()
    {
		LeaveGame();
    }

	public static void CheckError(string label)
	{
        if ((NetworkError)error != NetworkError.Ok)
            Debug.Log(label + " error: " + (NetworkError)error);
        //else Debug.Log( label + ": " + (NetworkError)error);
	}

	public GameController GetGameController()
	{
		return gameController;
	}

	public bool GameStarted()
	{
		return GameController.gameStarted;
	}

	public void SetMyPort(int port)
	{
		myPort = port;
	}

	public void SetTargetPort(int port)
	{
		this.targetPort = port;
	}

	public void SetTargetIp(string ip)
	{
		this.targetIp = ip;
	}


	public bool HandleCollisions(Lane lane)
	{
		//only if its own lane
		return gameController.player.lane.id == lane.id;
	}
}
