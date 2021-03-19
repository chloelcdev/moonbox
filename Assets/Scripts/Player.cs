using MLAPI;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using MLAPI.Connection;
using MLAPI.NetworkedVar;

public class Player : NetworkedBehaviour
{
    [SyncedVar] public NetworkedClient client;
    
    public void SetConnection(NetworkedClient _client)
    {
        client = _client;
    }


}
