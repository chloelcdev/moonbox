using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using MLAPI;
using MLAPI.Spawning;
using MLAPI.Transports.UNET;
using UnityEngine.UI;
using UnityEngine.UIElements;
using MLAPI.SceneManagement;
using DG.Tweening;
using System;

public class LobbyManager : MonoBehaviour
{
    public static LobbyManager Instance;
    public static UnetTransport Transport;

    public GameObject Lobby;
    public InputField createServerName;
    public InputField joinAddress;

    void Start()
    {
        Instance = this;
    }

    Transform GetSpawnPosition()
    {
        Debug.LogError("Not implemented");
        return transform;
    }

    

    public void JoinServer()
    {
        print(joinAddress);
        print(joinAddress.text);
        if (joinAddress.text.Contains(":"))
        {
            JoinServer(joinAddress.text);
        }
        else
        {
            string[] ipPortSplit = joinAddress.text.Split(':');

            string ipAddress = ipPortSplit[0];
            int port = 20202;
            if (int.TryParse(ipPortSplit[1], out port))
            {
                JoinServer(ipAddress, port);
            }
        }
    }

    
    void JoinServer(string _ip, int _port = 20202, string _password = "")
    {
        if (Transport == null) {
            Transport = NetworkingManager.Singleton.GetComponent<UnetTransport>();
        }

        Transport.ConnectAddress = _ip; //takes string
        Transport.ConnectPort = _port;

        NetworkingManager.Singleton.NetworkConfig.ConnectionData = System.Text.Encoding.ASCII.GetBytes(_password);
        NetworkingManager.Singleton.StartClient();
        
    }
    
    public void HostServer()
    {
        NetworkingManager.Singleton.ConnectionApprovalCallback += ApprovalCheck;
        NetworkingManager.Singleton.StartServer();
    }

    public void HostAndPlay()
    {
        NetworkingManager.Singleton.ConnectionApprovalCallback += ApprovalCheck;
        NetworkingManager.Singleton.StartHost();

        NetworkSceneManager.SwitchScene("Game");

        Lobby.transform.DOScale(Vector3.zero, 0.4f).onComplete += () =>
        {
            Lobby.SetActive(false);
        };

    }

    public void StopHosting()
    {
        NetworkingManager.Singleton.ConnectionApprovalCallback -= ApprovalCheck;
        Disconnect();
        UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenu");

    }

    public void Disconnect()
    {
        if (NetworkingManager.Singleton.IsHost)
        {
            NetworkingManager.Singleton.StopHost();
        }
        else if (NetworkingManager.Singleton.IsClient)
        {
            NetworkingManager.Singleton.StopClient();
        }
        else if (NetworkingManager.Singleton.IsServer)
        {
            NetworkingManager.Singleton.StopServer();
        }
    }

    public void LoadMainMenu()
    {
        UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenu");
    }

    void ApprovalCheck(byte[] connectionData, ulong clientId, NetworkingManager.ConnectionApprovedDelegate callback)
    {
        //Your logic here
        bool approve = true;
        bool createPlayerObject = true;

        print("Client " + clientId + " is being approved.");

        // The prefab hash. Use null to use the default player prefab
        // If using this hash, replace "MyPrefabHashGenerator" with the name of a prefab added to the NetworkedPrefabs field of your NetworkingManager object in the scene
        ulong? prefabHash = SpawnManager.GetPrefabHashFromGenerator("GamePlayer");

        Transform spawn = GetSpawnPosition();

        // probably send lua here

        //If approve is true, the connection gets added. If it's false. The client gets disconnected
        callback(createPlayerObject, prefabHash, approve, spawn.position, spawn.rotation);
    }
}
