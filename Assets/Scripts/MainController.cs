using MLAPI;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;

public class MainController : MonoBehaviour
{
    public const string FileTypeWhitelist = "*.fbx|*.png|*.bmp|*.txt|*.lua|*.mp4";

    public static MainController Instance;

    void Awake()
    {
        Instance = this;
    }

    

    // Start is called before the first frame update
    void Start()
    {
        DontDestroyOnLoad(gameObject);
        FileInfoMap.ReadOrCreateEmptyMap();
    }

    // Update is called once per frame
    void Update()
    {
        /* FILE SEND TESTING
         * 
        if (Keyboard.current.tKey.wasPressedThisFrame) {
            Debug.Log("sending");
            Test_SendFileToOpenReceiver(2);
        }
        if (Keyboard.current.rKey.wasPressedThisFrame)
        {
            Debug.Log("listening");
            Test_OpenToFileReception();
        }*/
        if (Keyboard.current.rKey.wasPressedThisFrame)
        {
            Debug.Log(NetworkingManager.Singleton.LocalClientId);
        }
    }


    public void Test_SendFileToOpenReceiver(ulong _connectedClient)
    {
#if UNITY_EDITOR
        string path = UnityEditor.EditorUtility.OpenFolderPanel("BE CAREFUL: Choose a folder to test sending with", "", "*");
        if (path.Length != 0)
        {
            LargeRPC download = new LargeRPC("gameDownload");
            download.SendFiles(new List<string>(Directory.GetFiles(path)), NetworkingManager.Singleton.ConnectedClients[_connectedClient].ClientId);
        }
#endif
    }

    public void Test_OpenToFileReception()
    {
        LargeRPC download = new LargeRPC("gameDownload");
        download.ListenForDownload();
    }



    public static Sprite LoadSprite(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        if (File.Exists(path))
        {
            byte[] bytes = System.IO.File.ReadAllBytes(path);
            Texture2D texture = new Texture2D(1, 1);
            texture.LoadImage(bytes);
            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
            return sprite;
        }
        return null;
    }
}
