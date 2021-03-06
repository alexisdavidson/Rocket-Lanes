﻿using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class MyPortField : MonoBehaviour
{
	void OnEnable()
	{
		UpdateField();
	}
	
	public void UpdateField()
	{
		int port = 0;

		P2PController c = GameObject.FindObjectOfType<P2PController>();
		if(c != null && Int32.TryParse(GetComponent<InputField>().text, out port))
			c.SetMyPort(port);

		MyNetworkManager m = GameObject.FindObjectOfType<MyNetworkManager>();
		if (m != null && Int32.TryParse(GetComponent<InputField>().text, out port))
			m.networkPort = port;
	}
}
