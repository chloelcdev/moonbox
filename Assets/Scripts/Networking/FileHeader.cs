using System.Text;

public class FileHeader
{
    public FileHeader(int _id, string _path, byte[] _hash, long _size)
    {
        id = _id;
        path = _path;
        hash = _hash;
        fileSize = _size;
    }

    public int id;
    public string path;
    public byte[] hash;

    /// <summary>
    /// length of the file in bytes
    /// </summary>
    public long fileSize;

    /// <summary>
    /// 
    /// int fileID 4 bytes - for each header packet
    /// string filename varsize - for each header packet
    /// byte[256] hash 256 bytes - for each header packet
    /// long fileLength 8 bytes - for each header packet
    /// bool isLastInPacket byte - for each header packet
    /// 
    /// int fileID 4 bytes - for each file packet
    /// byte[var] filedata - for each file packet
    /// bool isLastInPacket byte - for each file packet
    /// 
    /// </summary>
    /// <returns></returns>
    public long CalculateDownloadSize()
    {
        return HeaderPacketBytes() + fileSize;
    }

    public long HeaderPacketBytes()
    {
        return sizeof(int) + Encoding.Unicode.GetByteCount(path) + hash.Length + sizeof(bool);
    }
}