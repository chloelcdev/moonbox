using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ProtoBuf;
using System.IO;

[ProtoContract]
public class ProtoFile
{
    [ProtoMember(1)]
    public string path { get; set; }

    [ProtoMember(2)]
    public byte[] hash { get; set; }
}


public class ProtoFileMap
{
    public static List<ProtoFile> Files { get; set; }

    public static void SaveFileMap(ProtoFileMap _fileMap)
    {
        using (var file = File.Create("filemap.bin"))
        {
            Serializer.Serialize<ProtoFileMap>(file, _fileMap) ;
        }
    }

    public static void LoadFileMap()
    {
        ProtoFileMap Files;
        using (var file = File.OpenRead("filemap.bin"))
        {
            Files = Serializer.Deserialize<ProtoFileMap>(file);
        }
    }
}
