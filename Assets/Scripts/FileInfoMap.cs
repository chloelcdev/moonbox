using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ProtoBuf;
using System.IO;
using Unity.IO.LowLevel.Unsafe;
using System;

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
    public Dictionary<string, FileInfo> Files { get; set; }

    public static FileInfoMap Main { get; private set; }

    public const string FileTypeWhitelist = "*.fbx|*.png|*.bmp|*.txt|*.lua|*.mp4";

    public static void ReadOrCreateEmptyMap()
    {

        if (File.Exists("filemap.bin"))
        {
            using (var file = File.Open("filemap.bin", FileMode.Open))
            {
                Main = Serializer.Deserialize<FileInfoMap>(file);
            }
        }
        else
        {
            File.Create("filemap.bin");
            Main = new FileInfoMap();
        }
    }

    // https://social.msdn.microsoft.com/Forums/vstudio/en-US/b0c31115-f6f0-4de5-a62d-d766a855d4d1/directorygetfiles-with-searchpattern-to-get-all-dll-and-exe-files-in-one-call?forum=netfxbcl
    public static string[] GetFiles(string path, string searchPattern, SearchOption searchOption)
    {
        string[] searchPatterns = searchPattern.Split('|');
        List<string> files = new List<string>();
        foreach (string sp in searchPatterns)
            files.AddRange(System.IO.Directory.GetFiles(path, sp, searchOption));
        files.Sort();
        return files.ToArray();
    }

    public static void UpdateFileMap()
    {
        
        foreach(var filePath in GetFiles(Application.dataPath + "/game", FileTypeWhitelist, SearchOption.AllDirectories))
        {
            using (FileStream fs = new FileStream(filePath, FileMode.Open))
            {
                DateTime lastMod = File.GetLastWriteTime(filePath);

                if (!Main.Files.ContainsKey(filePath) || Main.Files[filePath].lastModified != lastMod)
                {
                    FileInfo info = new FileInfo();
                    info.lastModified = File.GetLastWriteTime(filePath);
                    info.hash = fs.sha256();
                    info.fileName = Path.GetFileName(filePath);
                    info.fileSize = fs.Length;

                    Main.Files.Add(filePath, info);
                }
            }
        }

        using (var file = File.OpenWrite("filemap.bin"))
        {
            Serializer.Serialize(file, Main);
        }
    }
}


[ProtoContract]
public class DownloadCacheFileInfo
{
    [ProtoMember(1)]
    public byte[] hash { get; set; }

    [ProtoMember(2)]
    public string filePath { get; set; }

    [ProtoMember(3)]
    public long fileSize { get; set; }

    [ProtoMember(4)]
    public DateTime lastModified { get; set; }
}
