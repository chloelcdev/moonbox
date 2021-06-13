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
using MLAPI.Transports.Tasks;
using MLAPI.Messaging;
using System.IO;
using MLAPI.Connection;
using ProtoBuf;
using MLAPI.Serialization;
using BitStream = MLAPI.Serialization.BitStream;
using MLAPI.Serialization.Pooled;
using System.Text;
using TMPro;

/// <summary>
/// This should always be sticking around (DontDestroyOnLoad) 
/// we always need the information the lobby has (the "scoreboard"/user-list can probably just show info from here)
/// </summary>

public class LobbyManager : MonoBehaviour
{
    public static LobbyManager Instance;
    public static UnetTransport Transport;

    public GameObject Lobby;
    public InputField createServerName;
    public InputField joinAddress;

    public TMP_InputField playerNameField;

    void Start()
    {
        Instance = this;

        NetworkingManager.Singleton.OnClientConnectedCallback += ML_OnClientConnected;
        NetworkingManager.Singleton.OnClientDisconnectCallback += ML_OnClientDisconnect;

        CustomMessagingManager.RegisterNamedMessageHandler("ClientConnected", ClientConnected);
        CustomMessagingManager.RegisterNamedMessageHandler("ClientDisconnected", ClientDisconnected);


    }

    Transform GetSpawnPosition()
    {
        Debug.LogWarning("Not implemented");
        return transform;
    }

    void CloseLobbyScreen()
    {
        Lobby.transform.DOScale(Vector3.zero, 0.4f).onComplete += () =>
        {
            Lobby.SetActive(false);
        };
    }

    public void HostServer()
    {
        NetworkingManager.Singleton.ConnectionApprovalCallback += ApprovalCheck;
        NetworkingManager.Singleton.StartServer();

        StartListenForDownloadRequests();

        NetworkSceneManager.SwitchScene("Game");

        CloseLobbyScreen();
    }

    public void HostAndPlay()
    {
        NetworkingManager.Singleton.ConnectionApprovalCallback += ApprovalCheck;
        NetworkingManager.Singleton.StartHost();

        StartListenForDownloadRequests();

        NetworkSceneManager.SwitchScene("Game");

        CloseLobbyScreen();

    }

    public void JoinServer()
    {
        if (!joinAddress.text.Contains(":"))
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

        Debug.Log("Connecting to " + _ip + ":" + _port);

        ulong? hsh = SpawnManager.GetPrefabHashFromGenerator("Player");
        Debug.Log(hsh);
        ConnectionApprovalData connectionData = new ConnectionApprovalData((ulong)hsh, playerNameField.text, _password);

        NetworkingManager.Singleton.NetworkConfig.ConnectionData = connectionData.GetSerializedAndCompressed();

        NetworkingManager.Singleton.StartClient();

        Debug.Log("registered JoinConnectionAccepted");
        CustomMessagingManager.RegisterNamedMessageHandler("JoinConnectionAccepted", OnJoinConnectionAccepted);

    }

    [System.Serializable]
    public class ConnectionApprovalData
    {
        public ConnectionApprovalData(ulong _hash, string _playerName = "Unnamed Player", string _password = "")
        {
            playerPrefabHash = _hash;

            playerName = _playerName;
            roomPassword = _password;
        }

        [SerializeField]
        public ulong playerPrefabHash { get; set; }

        [SerializeField]
        public string playerName { get; set; }
        [SerializeField]
        public string roomPassword { get; set; }
        
        
    }


    /// <summary>
    /// Called on the client after the server confirms we passed the approval check
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="payload"></param>
    private void OnJoinConnectionAccepted(ulong sender, Stream payload)
    {
        Debug.Log("Connection was accepted");
        RequestDownloads();
    }

    void RequestDownloads()
    {

        ProgressUI.SetMessage("Downloading files", "Waiting for headers...");
        ProgressUI.Show();

        Debug.Log("Requesting downloads");
        LargeRPC download = new LargeRPC("InitialGameDownload");
        download.ListenForDownload();
        download.OnDownloadComplete += FinishedDownloadingFromHost;
        download.OnProgressUpdated += UpdateProgressBar;
        
        CustomMessagingManager.SendNamedMessage("DownloadFilesRequest", NetworkingManager.Singleton.ServerClientId, Stream.Null);
    }

    void UpdateProgressBar(float _progress, string _info)
    {
        ProgressUI.SetProgess(_progress);
        ProgressUI.SetInfo(_info);
    }

    void FinishedDownloadingFromHost(SendOrReceiveFlag _flag, ulong _serverClientID)
    {
        Debug.Log("Finished downloading files");

        ProgressUI.Hide();

        CloseLobbyScreen();

        Debug.Log("this is where we load the world :)");

        Debug.Log("this is where we load lua :)");

        Debug.Log("this is where we spawn the player object :)");
    }

    void StartListenForDownloadRequests()
    {
        Debug.Log("Listening for download requests");
        if (!NetworkingManager.Singleton.IsServer)
        {
            Debug.LogWarning("Only run ListenForDownloadRequests() on the server");
            return;
        }

        CustomMessagingManager.RegisterNamedMessageHandler("DownloadFilesRequest", DownloadRequestReceived);
    }

    Dictionary<ulong, LargeRPC> inProgressDownloads = new Dictionary<ulong, LargeRPC>();
    

    void DownloadRequestReceived(ulong _requesterClientID, Stream _data)
    {
        Debug.Log("Received download request from sender " + _requesterClientID);

        if (!inProgressDownloads.ContainsKey(_requesterClientID))
        {
            LargeRPC clientDownload = new LargeRPC("InitialGameDownload");
            inProgressDownloads.Add(_requesterClientID, clientDownload);
            clientDownload.OnDownloadComplete += ClientFinishedDownload;

            // TODO: Actually send files
            clientDownload.SendFolder(@"C:\Users\Richard\Pictures\moonbox_files_testing", _requesterClientID);
        }
        else
        {
            Debug.LogWarning("User " + _requesterClientID + " is already downloading, ignoring request");
        }
    }

    void ClientFinishedDownload(SendOrReceiveFlag _flag, ulong _clientID) {
        Debug.Log("client " + _clientID + " has finished downloading files");
        inProgressDownloads.Remove(_clientID);
    }

    void StopListenForDownloadRequests()
    {
        CustomMessagingManager.UnregisterNamedMessageHandler("DownloadFilesRequest");
    }

    public void StopHosting()
    {
        StopListenForDownloadRequests();
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

        StopListenForDownloadRequests();
    }

    public void LoadMainMenu()
    {
        UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenu");
    }

    Dictionary<ulong, ConnectionApprovalData> connectionDataCache = new Dictionary<ulong, ConnectionApprovalData>();

    void ApprovalCheck(byte[] _compressedSerializedData, ulong _clientId, NetworkingManager.ConnectionApprovedDelegate _callback)
    {
        Debug.Log("Client " + _clientId + " is being approved.");

        bool approve = true;
        bool createPlayerObject = true;

        


        ConnectionApprovalData connectionData = _compressedSerializedData.GetDecompressedAndDeserialized<ConnectionApprovalData>();

        // I don't actually know that this is going to do us much good, but it seems like if they hack their game this might catch it and I already coded it :p
        approve = true || (connectionData.playerPrefabHash == SpawnManager.GetPrefabHashFromGenerator("Player"));


        if (!connectionDataCache.ContainsKey(_clientId)) {
            connectionDataCache.Add(_clientId, connectionData);
        }

        //If approve is true, the connection gets added. If it's false. The client gets disconnected
        _callback(createPlayerObject, null, approve, null, null);
        Debug.Log("Client " + _clientId + " approved, spawning player object");

        // set a reference to the NetworkedClient on the Player class for later networking

        
        CustomMessagingManager.SendNamedMessage("JoinConnectionAccepted", _clientId, Stream.Null);


    }

    // this happens after approval (96% sure :p)
    private void ML_OnClientConnected(ulong _clientId)
    {
        if (!NetworkingManager.Singleton.IsHost && !NetworkingManager.Singleton.IsServer) return;

        Debug.Log("Client " + _clientId + " connected");

        NetworkedClient client = NetworkingManager.Singleton.ConnectedClients[_clientId];
        Player player = client.GetPlayer();

        

        player.ClientId.Value = _clientId;

        player.Name.Value = connectionDataCache[_clientId].playerName;

        connectionDataCache.Remove(_clientId);

        using (PooledBitStream stream = PooledBitStream.Get())
        {
            using (PooledBitWriter w = PooledBitWriter.Get(stream))
            {
                w.WriteUInt64(player.NetworkedObject.NetworkId);
                CustomMessagingManager.SendNamedMessage("ClientConnected", AllClientIDs(), stream);
            }
        }

    }

    /// <summary>
    /// Server only
    /// </summary>
    public static List<ulong> AllClientIDs(bool _excludeLocal = false)
    {
        List<ulong> clients = new List<ulong>();
        
        foreach (var client in NetworkingManager.Singleton.ConnectedClientsList)
        {
            if (_excludeLocal && client.ClientId == NetworkingManager.Singleton.LocalClientId) continue;

            clients.Add(client.ClientId);
        }

        return clients;
    }

    private void ML_OnClientDisconnect(ulong _clientId)
    {
        if (!NetworkingManager.Singleton.IsHost && !NetworkingManager.Singleton.IsServer) return;

        Debug.Log("Client " + _clientId + " connected: ");

        using (PooledBitStream stream = PooledBitStream.Get())
        {
            using (PooledBitWriter writer = PooledBitWriter.Get(stream))
            {
                CustomMessagingManager.SendNamedMessage("ClientDisconnected", AllClientIDs(), stream);
            }
        }
    }

    private void ClientConnected(ulong _sender, Stream _stream)
    {
        ulong objectID;
        using (_stream)
        {
            using (PooledBitReader reader = PooledBitReader.Get(_stream))
            {
                objectID = reader.ReadUInt64();
            }
        }

        Player player = null;

        if (objectID != 0 && SpawnManager.SpawnedObjects.ContainsKey(objectID))
        {
            player = SpawnManager.SpawnedObjects[objectID].GetComponent<Player>();
        }

        if (player != null)
        {
            Debug.Log("Server says client " + player.Name.Value + " - " + player.ClientId.Value + " connected: ");
        }
    }


    private void ClientDisconnected(ulong _sender, Stream _stream)
    {
        PlayerInfo info;
        using (_stream)
        {
            info = _stream.DeserializeNetworkCompressedStream<PlayerInfo>();
        }

        Debug.Log("Server says client " + info.Name + " - " + info.ClientId + " disconnected: ");

    }

}

[Serializable]
public class PlayerInfo
{
    [SerializeField]
    public ulong ClientId { get; set; }

    [SerializeField]
    public string Name { get; set; } = "Unnamed Player";
}