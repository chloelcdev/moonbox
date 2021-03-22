using MLAPI;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using MLAPI.Connection;
using MLAPI.NetworkedVar;
using System.IO;

public class Player : NetworkedBehaviour
{
    public NetworkedVar<ulong> ClientId = new NetworkedVar<ulong>( new NetworkedVarSettings { WritePermission = NetworkedVarPermission.ServerOnly, SendTickrate = -1}, 0);
    //public string Name { get; set; } = "Unnamed Player";
    public NetworkedVar<string> Name = new NetworkedVar<string>(new NetworkedVarSettings { WritePermission = NetworkedVarPermission.ServerOnly, SendTickrate = -1 }, "Unnamed Player");
    //public NetworkedVar<PlayerInfo> Info = new NetworkedVar<PlayerInfo>(new NetworkedVarSettings { WritePermission = NetworkedVarPermission.ServerOnly, SendTickrate = -1 }, new PlayerInfo());

    public List<PlayerCharacter> Characters;
    public PlayerCharacter Character => Characters[0];

    public void Spawn(Stream spawnPayload = null, bool destroyWithScene = false)
    {

    }
}
