﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class GameController : MonoBehaviour
{
	[SerializeField]
	GameObject InGameUI;

	[HideInInspector]
	public Player player;

	[HideInInspector]
	public bool gameStarted = false;

	private List<Lane> lanes;
	private INetworkController networkController;

	void Awake()
	{
		FindLanes();
	}

	public void FindLanes()
	{
		lanes = new List<Lane>();
		lanes.Add(GameObject.FindGameObjectWithTag("Lane1").GetComponent<Lane>());
		lanes.Add(GameObject.FindGameObjectWithTag("Lane2").GetComponent<Lane>());
		lanes.Add(GameObject.FindGameObjectWithTag("Lane3").GetComponent<Lane>());
		lanes.Add(GameObject.FindGameObjectWithTag("Lane4").GetComponent<Lane>());
	}

	public void StartGame()
	{
		if(InGameUI != null)
			InGameUI.SetActive(true);
		
		gameStarted = true;
	}

	public void LeaveGame(bool enterUI = true)
	{
		gameStarted = false;
		networkController.Quit();
		
		player = null;

		if(enterUI)
        	SceneManager.LoadScene("Main Menu");
	}

	public int GetNextOccupiedLaneId(Player p)
	{
		int laneId = p.lane.id;
		for(int i = 0; i < 3; i ++)
		{
			laneId ++;
			if(laneId >= 4)
				laneId = 0;
			
			if(lanes[laneId].IsOccupied())
				return laneId;
		}
		
		//if no other player, just send to next lane
		laneId = p.lane.id;
		laneId ++;
		if(laneId >= 4)
			laneId = 0;
		
		return laneId;
	}

	public Lane GetFirstUnoccupiedLane()
	{
		foreach(Lane lane in lanes)
		{
			if(!lane.IsOccupied())
				return lane;
		}
		return null;
	}

	public Lane GetLane(int index)
	{
		return lanes[index];
	}

	public void SetNetworkController(INetworkController n)
	{
		networkController = n;
	}

	public void SendRocket() { SendRocket(player.lane.id, GetNextOccupiedLaneId(player)); }
	public void SendRocket(int playerId, int neighbourPlayerId)
	{
		List<int> parameters = new List<int>();
		parameters.Add(playerId);
		parameters.Add(neighbourPlayerId);
		int[] parametersInt = parameters.ToArray();
		int consentResult = networkController.AskForConsent(ConsentAction.SpawnRocket, parametersInt);
		Debug.Log("Asking for consent " + ConsentAction.SpawnRocket + ", result: " + consentResult);
		if(consentResult != -1) //for p2p and singleplayer. Server-Client uses another callback (OnAskForConsentMsg)
		{
			networkController.ApplyConsent(ConsentAction.SpawnRocket, parametersInt, consentResult);
		}
	}

	public bool HandleCollisions(Lane lane)
	{
		if(lane == null)
			return false;
		bool result = networkController.HandleCollisions(lane);
		Debug.Log("Handle Collisions for lane " + lane.id + ": " + result);
		return result;
	}
}
