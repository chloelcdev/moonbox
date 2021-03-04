using MLAPI;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

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
        if (Input.GetKeyDown(KeyCode.T))
        {
            Test();
        }
        if (Input.GetKeyDown(KeyCode.R))
        {
            TestReceive();
        }
    }


    public void Test()
    {
#if UNITY_EDITOR
        string path = UnityEditor.EditorUtility.OpenFilePanel("Overwrite with png", "", "png");
        if (path.Length != 0)
        {
            LargeRPC download = new LargeRPC("gameDownload");
            download.SendFiles(new List<string>() { path }, NetworkingManager.Singleton.LocalClientId);
        }
#endif
    }

    public void TestReceive()
    {
        LargeRPC download = new LargeRPC("gameDownload");
        download.ListenForDownload();
    }

}
