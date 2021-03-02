using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using MLAPI;
using MLAPI.Messaging;
using System.IO;
using MLAPI.Serialization.Pooled;
using System.Text;

public class LargeRPC
{
    public event Action<float> OnProgressUpdated;
    public event Action<byte[]> OnDownloadComplete;

    int netChunkSize = 1024 * 16;
    int _fileChunkSize = 1024 * 1024 * 50;

    string messageName = "";

    public void ListenForDownload()
    {
        CustomMessagingManager.RegisterNamedMessageHandler(messageName, ReceiveFilesDownloadPieceFromServer);
    }

    public void ListenForFilesNeededList()
    {
        CustomMessagingManager.RegisterNamedMessageHandler(messageName, ReceiveFilesNeededListFromClient);
    }

    

    public void StopListening()
    {
        CustomMessagingManager.UnregisterNamedMessageHandler(messageName);
    }


    public LargeRPC(string _messageName) {
        ListenForDownload();
    }


    // this function chunks BYTES into NETWORK sized chunks
    // thanks https://stackoverflow.com/questions/50655268/c-sharp-split-byte-array-into-separate-chunks-and-get-number-of-chunks/50655347
    public static byte[][] Split(byte[] _data, int _chunkSize)
    {

        if (_data.Length <= _chunkSize)
        {
            byte[][] data = new byte[1][];
            data[0] = _data;
            return data;
        }

        int chunkCount = (int)Math.Ceiling(_data.Length / (float)_chunkSize);

        byte[][] chunks = new byte[chunkCount][];

        for (int i = 0; i < chunkCount; i++)
        {
            chunks[i] = new byte[Math.Min(_chunkSize, _data.Length - i * _chunkSize)];
            for (int j = 0; j < _chunkSize && i * chunkCount + j < _data.Length; j++)
            {
                chunks[i][j] = _data[i * chunkCount + j];
            }
        }

        return chunks;

    }

    public void SendFiles(List<string> _paths, ulong _clientID)
    {
        Game.Instance.StartCoroutine(SendFilesDownloadRoutine(_paths, _clientID));
    }
    public void SendFile(string _path, ulong _clientID)
    {
        SendFiles(new List<string>() { _path }, _clientID);
    }

    public static void Test()
    {
        LargeRPC download = new LargeRPC("gameDownload");
        download.SendFiles(new List<string>() { "C:/NVIDIA/file.txt" }, NetworkingManager.Singleton.LocalClientId);
    }
    

    //the cap is around 180 for header packets per send if you follow the 64k unet rule (I try to keep under it)
    public static int headersPerPacket
    {
        get
        {
            return 160;
        }
    }

    public static FilesDownloadSendState downloadSendState = FilesDownloadSendState.Idle;

    List<FileHeader> headers = new List<FileHeader>();
    Dictionary<string, int> fileIndex = new Dictionary<string, int>();
    List<int> filesNeeded = new List<int>();
    /// <summary>
    /// this function splits FILES into MEMORY SAFE sized chunks and safely sends one before starting another
    /// 
    /// files receipient needs to receive the same number of headers with each header packet (packet 1 counts as a header packet)
    /// </summary>
    public IEnumerator SendFilesDownloadRoutine(List<string> _paths, ulong _clientID)
    {
        if (downloadSendState != FilesDownloadSendState.Idle)
        {
            Debug.LogWarning("Cannot start sending files while files are being sent, waiting for Idle state to begin");
            yield break;
        }

        downloadSendState = FilesDownloadSendState.SendingHeaders;

        // packet 1: 
        // int32 fileCount | uint64 downloadSize | <start headers>


        // header packets: 
        // int32 fileID | string filename | byte[256] hash | uint32 fileLength | bool isLastInPacket


        // subsequent packets: 
        // int32 fileID | byte[] filedata | bool isLastInPacket

        long fullDownloadSize = 0;


        // grab info for headers
        foreach (var path in _paths)
        {
            if (File.Exists(path))
            {
                using (FileStream fs = File.Open(path, FileMode.Open))
                {

                    int id = headers.Count - 1;
                    byte[] fileHash = fs.sha256();

                    fileIndex.Add(path, id);
                    headers.Add(new FileHeader(id, path, fileHash, (long)fs.Length));
                    

                    fullDownloadSize += fs.Length;
                }
            }
            else
            {
                Debug.LogError("File not found, skipping: " + path);
            }
        }

        PooledBitStream bitStream = PooledBitStream.Get();
        PooledBitWriter writer = PooledBitWriter.Get(bitStream);

        // fileCount
        writer.WriteInt32(headers.Count);

        // downloadSize
        writer.WriteInt64(fullDownloadSize);


        var headersThisPacket = 0;

        foreach (var header in headers)
        {
            var path = header.path;
            var id = header.id;

            headersThisPacket++;

            // fileID
            writer.WriteInt32(header.id);
            
            // filename
            writer.WriteString(path);
            
            // hash
            writer.WriteByteArray(header.hash, 256);

            // fileLength
            writer.WriteInt64(header.fileSize);

            if (headersThisPacket >= headersPerPacket || id == headers.Count-1)
            {
                // isLastInPacket
                writer.WriteBit(true);

                headersThisPacket = 0;

                CustomMessagingManager.SendNamedMessage(messageName, _clientID, bitStream, "MLAPI_INTERNAL");

                writer.Dispose();
                bitStream.Dispose();

                bitStream = PooledBitStream.Get();
                writer = PooledBitWriter.Get(bitStream);

                yield return new WaitForSeconds(1 / 14);
            }
            else
            {
                writer.WriteBit(false);
            }

        }

        writer.Dispose();
        bitStream.Dispose();

        downloadSendState = FilesDownloadSendState.AwaitingFilesNeededList;
        ListenForFilesNeededList();

        while (downloadSendState == FilesDownloadSendState.AwaitingFilesNeededList)
        {
            yield return new WaitForSeconds(0.5f);
        }

        StopListening();

        bitStream = PooledBitStream.Get();
        writer = PooledBitWriter.Get(bitStream);

        foreach (var header in headers)
        {
            if (File.Exists(header.path) && filesNeeded.Contains(header.id))
            {
                using (FileStream fs = File.Open(header.path, FileMode.Open))
                {
                    
                    // while loop pulled from fs.Read docs from microsoft, a little confusing to the glance but works and will be fast

                    int numBytesToRead = (int)fs.Length;
                    int numBytesRead = 0;
                    while (numBytesToRead > 0)
                    {

                        byte[] fileChunk = new byte[_fileChunkSize];
                        // Read may return anything from 0 to numBytesToRead.
                        int n = fs.Read(fileChunk, numBytesRead, _fileChunkSize);

                        

                        foreach (byte[] netChunk in Split(fileChunk, netChunkSize))
                        {

                            // fileID
                            writer.WriteInt32(header.id);

                            // filedata
                            writer.WriteByteArray(netChunk, netChunk.Length);

                            // isLastInPacket
                            bool isLastInPacket = bitStream.Length >= netChunkSize;
                            writer.WriteBit(isLastInPacket);

                            if (isLastInPacket)
                            {

                                CustomMessagingManager.SendNamedMessage(messageName, _clientID, bitStream, "MLAPI_INTERNAL");

                                writer.Dispose();
                                bitStream.Dispose();

                                bitStream = PooledBitStream.Get();
                                writer = PooledBitWriter.Get(bitStream);

                                yield return new WaitForSeconds(1 / 14);
                            }

                        }

                        // Break when the end of the file is reached.
                        if (n == 0) break;

                        numBytesRead += n;
                        numBytesToRead -= n;
                    }
                }
            }


        }

        downloadSendState = FilesDownloadSendState.AwaitingResponse;
        yield break;
    }

    private void ReceiveFilesNeededListFromClient(ulong sender, Stream _stream)
    {
        using (PooledBitReader reader = PooledBitReader.Get(_stream))
        {
            filesNeeded.AddRange(reader.ReadIntArray());

            bool isLastFilesNeededPacket = reader.ReadBit();
            if (isLastFilesNeededPacket)
            {
                downloadSendState = FilesDownloadSendState.SendingFiles;
            }

        }
    }


    public FilesDownloadReceiveState receptionState = FilesDownloadReceiveState.Idle;

    
    public int numFilesNeeded = 0;
    public int numHeadersReceived = 0;
    public int numFilesReceived = 0;

    public long downloadSize = 0;

    public List<FileHeader> receivedHeaders = new List<FileHeader>();

    /// <summary>
    /// The last fileID received
    /// </summary>
    int previousFileID = -1;
    public FileStream receptionFileStream;

    int fileIDsPerPacket = 32000/4;

    public void ReceiveFilesDownloadPieceFromServer(ulong _senderClientID, Stream _stream)
    {
        
        switch(receptionState)
        {
            case FilesDownloadReceiveState.Idle:
                // receive first packet
                using (PooledBitReader reader = PooledBitReader.Get(_stream)) {
                    numFilesNeeded = reader.ReadInt32();
                    downloadSize = reader.ReadInt64();

                    PullHeadersFromPacket(reader);
                }
                
                break;
            case FilesDownloadReceiveState.AwaitingAllHeaders:
                // receive header packet
                using (PooledBitReader reader = PooledBitReader.Get(_stream))
                {
                    PullHeadersFromPacket(reader);
                    SendNeededFilesListToServer();
                }

                break;
            case FilesDownloadReceiveState.AwaitingAllFileData:
                using (PooledBitReader reader = PooledBitReader.Get(_stream))
                {
                    bool packetDataStored = false;
                    while (!packetDataStored)
                    {
                        int id = reader.ReadInt32();
                        byte[] data = reader.ReadByteArray(null);
                        packetDataStored = reader.ReadBit();

                        numFilesReceived++;
                        bool isLastFile = numFilesReceived >= numFilesNeeded;


                        if (id != previousFileID) {

                            receptionFileStream.Dispose();

                            if (!File.Exists(receivedHeaders[id].path))
                            {
                                File.Create(receivedHeaders[id].path, (int)receivedHeaders[id].fileSize);
                            }

                            receptionFileStream = File.Open(receivedHeaders[id].path, FileMode.Append);

                            receptionFileStream.Write(data, 0, data.Length);
                        }


                        if (isLastFile)
                        {
                            receptionState = FilesDownloadReceiveState.Idle;
                            break;
                        }


                        previousFileID = id;
                    }
                }
                break;
        }
    }

    void SendNeededFilesListToServer()
    {
        List<int> fileIDs = new List<int>();
        int totalFilesToBeRequested = 0;



        // TODO: compare hashes using binarys system and only add ones that we need, for now just put in everything
        foreach (var header in receivedHeaders)
        {
            
            // TODO: use header.hash to figure out if we need each file. This allows us to dump a ton of downloaded files together (maybe separating by lua, model, material, for convenience) and get them no matter

            // we're just adding all of them without question for now
            fileIDs.Add(header.id);
            totalFilesToBeRequested++;


            bool isLastPacket = (totalFilesToBeRequested >= receivedHeaders.Count);

            if (fileIDs.Count >= fileIDsPerPacket || isLastPacket)
            {
                using (PooledBitStream bitStream = PooledBitStream.Get())
                {
                    using (PooledBitWriter writer = PooledBitWriter.Get(bitStream))
                    {
                        writer.WriteIntArray(fileIDs.ToArray());
                        writer.WriteBit(isLastPacket);

                        CustomMessagingManager.SendNamedMessage(messageName, NetworkingManager.Singleton.ServerClientId, bitStream, "MLAPI_INTERNAL");

                    }
                }
            }
        }
        
    }

    public void PullHeadersFromPacket(PooledBitReader reader)
    {
        for (int i = 0; i < headersPerPacket - 1; i++)
        {
            int id = reader.ReadInt32();
            string filename = reader.ReadString().ToString();
            byte[] hash = reader.ReadByteArray(null, 256);
            long fileLength = reader.ReadInt64();

            bool isLastInPacket = reader.ReadBool();


            receivedHeaders.Add(new FileHeader(id, filename, hash, fileLength));

            // if it's the last in the packet stop early
            if (isLastInPacket)
            {
                receptionState = FilesDownloadReceiveState.AwaitingAllFileData;
                break;
            }
        }
    }
}





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


}

public enum FilesDownloadSendState
{
    Idle,
    SendingHeaders,
    AwaitingFilesNeededList,
    SendingFiles,
    AwaitingResponse
}
public enum FilesDownloadReceiveState
{
    Idle,
    AwaitingAllHeaders,
    AwaitingAllFileData
}

public static class Extensions
{
    public static byte[] sha256(this byte[] _bytes)
    {
        var crypt = new System.Security.Cryptography.SHA256Managed();
        byte[] crypto = crypt.ComputeHash(_bytes);
        return crypto;
    }

    public static byte[] sha256(this FileStream _stream)
    {
        var crypt = new System.Security.Cryptography.SHA256Managed();
        byte[] crypto = crypt.ComputeHash(_stream);
        return crypto;
    }
}