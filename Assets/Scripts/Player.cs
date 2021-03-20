using MLAPI;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using MLAPI.Connection;
using MLAPI.NetworkedVar;

public class Player : NetworkedBehaviour
{
    public NetworkedVar<int> clientId = new NetworkedVar<int>( new NetworkedVarSettings { WritePermission = NetworkedVarPermission.ServerOnly, SendTickrate = -1}, -1);

    public PlayerInfo info;
}
