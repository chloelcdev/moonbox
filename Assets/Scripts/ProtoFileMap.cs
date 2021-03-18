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


public static class ProtoFileMap
{
    public static List<ProtoFile> Files { get; set; }

    public static void LoadFileMap()
    {
        using (var file = File.Create("filemap.bin"))
        {
            Serializer.Serialize<ProtoFile>(file, person);
        }
    }

    public static void SaveFileMap()
    {

    }
}
