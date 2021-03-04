using MLAPI;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class Game : MonoBehaviour
{
    public static Game Instance;

    void Awake()
    {
        Instance = this;
    }

    // Start is called before the first frame update
    void Start()
    {
        DontDestroyOnLoad(gameObject);


    }

    // Update is called once per frame
    void Update()
    {
        if (Keyboard.current.tKey.wasPressedThisFrame) {
            Debug.Log("sending");
            Test();
        }
        if (Keyboard.current.rKey.wasPressedThisFrame)
        {
            Debug.Log("listening");
            TestReceive();
        }
        if (Keyboard.current.rKey.wasPressedThisFrame)
        {
            Debug.Log(NetworkingManager.Singleton.LocalClientId);
        }
    }


    public void Test()
    {
#if UNITY_EDITOR
        string path = UnityEditor.EditorUtility.OpenFilePanel("Overwrite with png", "", "png");
        if (path.Length != 0)
        {
            LargeRPC download = new LargeRPC("gameDownload");
            download.SendFiles(new List<string>() { path }, NetworkingManager.Singleton.ConnectedClients[2].ClientId);
        }
#endif
    }

    public void TestReceive()
    {
        LargeRPC download = new LargeRPC("gameDownload");
        download.ListenForDownload();
    }

}
