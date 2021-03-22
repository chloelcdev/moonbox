using ProtoBuf;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

[ProtoContract]
public class FileInfo
{
    [ProtoMember(1)]
    public byte[] hash { get; set; }

    [ProtoMember(2)]
    public string fileName { get; set; }

    [ProtoMember(3)]
    public long fileSize { get; set; }

    [ProtoMember(4)]
    public DateTime lastModified { get; set; }
}

[ProtoContract]
public class FileInfoMap
{
    [ProtoMember(1)]
    public Dictionary<string, FileInfo> Files { get; set; } = new Dictionary<string, FileInfo>();
    public Dictionary<byte[], FileInfo> FilesByHash { get; set; } = new Dictionary<byte[], FileInfo>();
    public static FileInfoMap Map { get; private set; }

    public static void ReadOrCreateEmptyMap()
    {

        if (File.Exists("filemap.bin"))
        {
            using (var fs = File.Open("filemap.bin", FileMode.Open))
            {
                Map = Serializer.Deserialize<FileInfoMap>(fs);
            }
        }
        else
        {
            File.Create("filemap.bin");
            Map = new FileInfoMap();
        }
    }


    public static void UpdateFileMap()
    {
        //foreach (var dirPath in new List<string>() { "/game", "/downloadCache" })
       
        foreach (var filePath in MoonboxExtensions.GetFiles(Application.dataPath + "/game", MainController.FileTypeWhitelist, SearchOption.AllDirectories))
        {
            using (FileStream fs = new FileStream(filePath, FileMode.Open))
            {
                DateTime lastMod = File.GetLastWriteTime(filePath);

                if (!Map.Files.ContainsKey(filePath) || Map.Files[filePath].lastModified != lastMod)
                {
                    FileInfo info = new FileInfo();
                    info.lastModified = File.GetLastWriteTime(filePath);
                    info.hash = fs.sha256();
                    info.fileName = Path.GetFileName(filePath);
                    info.fileSize = fs.Length;

                    Map.Files.Add(filePath, info);
                    Map.FilesByHash.Add(info.hash, info);
                }
            }
        }

        using (var fs = File.OpenWrite("filemap.bin"))
        {
            Serializer.Serialize(fs, Map);
        }
    }
}


[ProtoContract]
public class DownloadCacheFileInfo
{
    [ProtoMember(1)]
    public byte[] hash { get; set; }

    [ProtoMember(2)]
    public string virtualPath { get; set; }

    [ProtoMember(3)]
    public long fileSize { get; set; }

    [ProtoMember(4)]
    public DateTime lastModified { get; set; }
}


[ProtoContract]
public class DownloadCacheFileInfoMap
{
    [ProtoMember(1)]
    public Dictionary<byte[], DownloadCacheFileInfo> Files { get; set; } = new Dictionary<byte[], DownloadCacheFileInfo>();

    public static DownloadCacheFileInfoMap Map { get; private set; }

    public static void ReadOrCreateEmptyMap()
    {

        if (File.Exists("downloadmap.bin"))
        {
            using (var fs = File.Open("downloadmap.bin", FileMode.Open))
            {
                Map = Serializer.Deserialize<DownloadCacheFileInfoMap>(fs);
            }
        }
        else
        {
            File.Create("downloadmap.bin");
            Map = new DownloadCacheFileInfoMap();
        }
    }

    public static void AddMapping(string realPath, string virtualPath)
    {
        // generate unique name 

        if (File.Exists(realPath))
        {
           
            using (var fs = File.Open(realPath, FileMode.Open))
            {
                FileInfo fileInfo = FileInfoMap.Map.Files[realPath];

                DownloadCacheFileInfo dlInfo = new DownloadCacheFileInfo();
                dlInfo.virtualPath = virtualPath;
                dlInfo.lastModified = File.GetLastWriteTime(realPath);
                dlInfo.hash = fs.sha256();
                dlInfo.fileSize = fs.Length;

                Map.Files.Add(fileInfo.hash, dlInfo);

            }
        }
    }

    public static void SaveMap()
    {
        using (var fs = File.OpenWrite("downloadmap.bin"))
        {
            Serializer.Serialize(fs, Map);
        }
    }
}