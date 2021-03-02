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
    public LargeRPC(string _messageName)
    {
        messageName = _messageName;
    }

    string messageName = "";

    public event Action<float> OnProgressUpdated;
    public event Action<byte[]> OnDownloadComplete;

    public LargeRPCState State = LargeRPCState.Idle;

    public TransmissionState transmissionState {
        get {

            if (State.ToString().Contains("Send_"))
            {
                return TransmissionState.Send;
            }
            else if (State.ToString().Contains("Receive_"))
            {
                return TransmissionState.Receive;
            }
            
            return TransmissionState.Idle;
        }
    }

    // 64k cap on Unet packets, trying to stick around 32k to be safe

    // headers are about 300b/packet depending on file path length
    public static int headersPerPacket { get => 32000 / 300; }

    // file IDs are int32s, they're 4 bytes
    public static int fileIDsPerPacket { get => 32000 / 4; }

    // the size of the pieces are that we actually send over the network
    int netChunkSize { get => 1024 * 32; }

    // the size of the pieces of file we read into memory at a time when sending are
    int fileChunkSize { get => 1024 * 1024 * 50; }


    public List<FileHeader> headers = new List<FileHeader>();


    public void StopListening()
    {
        CustomMessagingManager.UnregisterNamedMessageHandler(messageName);
    }

    public static void Test()
    {
        LargeRPC download = new LargeRPC("gameDownload");
        download.SendFiles(new List<string>() { "C:/NVIDIA/file.txt" }, NetworkingManager.Singleton.LocalClientId);
    }

    public void Clear()
    {
        if (transmissionState != TransmissionState.Idle)
        {
            Debug.Log("Cannot clear LargeRPC until it has finished the current job.");
            return;
        }

        receptionFileStream.Dispose();
        filesNeededByRecipient.Clear();
        headers.Clear();

        numFilesNeeded = 0;
        numHeadersReceived = 0;
        downloadSize = 0;
        previousFileID = -1;
    }



    #region Sender

    List<int> filesNeededByRecipient = new List<int>();

    public void SendFile(string _path, ulong _clientID)
    {
        SendFiles(new List<string>() { _path }, _clientID);
    }
    public void SendFiles(List<string> _paths, ulong _clientID)
    {
        Game.Instance.StartCoroutine(SendFilesDownloadRoutine(_paths, _clientID));
    }

    /// <summary>
    /// this function splits FILES into MEMORY SAFE sized chunks and safely sends one before starting another
    /// 
    /// files receipient needs to receive the same number of headers with each header packet (packet 1 counts as a header packet)
    /// </summary>
    public IEnumerator SendFilesDownloadRoutine(List<string> _paths, ulong _clientID)
    {
        if (State != LargeRPCState.Idle)
        {
            Debug.LogWarning("Cannot start sending files while files are being sent, waiting for Idle state to begin");
            yield break;
        }

        State = LargeRPCState.Send_SendingHeaders;

        // packet 1: 
        // int32 fileCount | uint64 downloadSize | <start headers>


        // header packets: 
        // int32 fileID | string filename | byte[256] hash | uint32 fileLength | bool isLastInPacket


        // subsequent packets: 
        // int32 fileID | byte[] filedata | bool isLastInPacket

        long fullDownloadSize = 0;


        // grab info for headers
        foreach (var header in headers)
        {
            if (File.Exists(header.path))
            {
                using (FileStream fs = File.Open(header.path, FileMode.Open))
                {

                    int id = headers.Count - 1;
                    byte[] fileHash = fs.sha256();

                    headers.Add(new FileHeader(id, header.path, fileHash, (long)fs.Length));


                    fullDownloadSize += fs.Length;
                }
            }
            else
            {
                Debug.LogError("File not found, skipping: " + header.path);
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

            if (headersThisPacket >= headersPerPacket || id == headers.Count - 1)
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

        State = LargeRPCState.Send_AwaitingFilesNeededList;
        ListenForFilesNeededList();

        while (State == LargeRPCState.Send_AwaitingFilesNeededList)
        {
            yield return new WaitForSeconds(0.5f);
        }

        StopListening();

        bitStream = PooledBitStream.Get();
        writer = PooledBitWriter.Get(bitStream);

        foreach (var header in headers)
        {
            if (File.Exists(header.path) && filesNeededByRecipient.Contains(header.id))
            {
                using (FileStream fs = File.Open(header.path, FileMode.Open))
                {

                    // while loop pulled from fs.Read docs from microsoft, a little confusing to the glance but works and will be fast

                    int numBytesToRead = (int)fs.Length;
                    int numBytesRead = 0;
                    while (numBytesToRead > 0)
                    {

                        byte[] fileChunk = new byte[fileChunkSize];
                        // Read may return anything from 0 to numBytesToRead.
                        int n = fs.Read(fileChunk, numBytesRead, fileChunkSize);



                        foreach (byte[] netChunk in fileChunk.Split(netChunkSize))
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

        State = LargeRPCState.Send_AwaitingResponse;
        yield break;
    }


    public void ListenForFilesNeededList()
    {
        CustomMessagingManager.RegisterNamedMessageHandler(messageName, ReceiveFilesNeededListFromReceiver);
    }

    private void ReceiveFilesNeededListFromReceiver(ulong sender, Stream _stream)
    {
        using (PooledBitReader reader = PooledBitReader.Get(_stream))
        {
            filesNeededByRecipient.AddRange(reader.ReadIntArray());

            bool isLastFilesNeededPacket = reader.ReadBit();
            if (isLastFilesNeededPacket)
            {
                State = LargeRPCState.Send_SendingFiles;
            }

        }
    }


    #endregion


    #region Receiver

    ulong senderID = 0;

    public LargeRPCState receptionState = LargeRPCState.Idle;

    public int numFilesNeeded = 0;
    public int numHeadersReceived = 0;
    public int numFilesReceived = 0;

    public long downloadSize = 0;

    /// <summary>
    /// The last fileID received
    /// </summary>
    int previousFileID = -1;

    public FileStream receptionFileStream;

    public void ListenForDownload()
    {
        CustomMessagingManager.RegisterNamedMessageHandler(messageName, ReceiveFilesDownloadPieceFromSender);
    }

    public void ReceiveFilesDownloadPieceFromSender(ulong _senderClientID, Stream _stream)
    {

        switch (receptionState)
        {
            case LargeRPCState.Idle:

                // allow packets from everyone if this is the server
                // only allow packets from server otherwise
                if (!NetworkingManager.Singleton.IsHost && !NetworkingManager.Singleton.IsServer && _senderClientID != NetworkingManager.Singleton.ServerClientId) return;

                // receive first packet
                using (PooledBitReader reader = PooledBitReader.Get(_stream))
                {
                    senderID = _senderClientID;
                    numFilesNeeded = reader.ReadInt32();
                    downloadSize = reader.ReadInt64();

                    PullHeadersFromPacket(reader);
                }

                break;
            case LargeRPCState.Receive_AwaitingAllHeaders:

                // receive header packet
                using (PooledBitReader reader = PooledBitReader.Get(_stream))
                {
                    PullHeadersFromPacket(reader);
                    SendNeededFilesListToSender();
                }

                break;
            case LargeRPCState.Receive_AwaitingAllFileData:

                // receive file data packet
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


                        if (id != previousFileID)
                        {

                            receptionFileStream.Dispose();

                            if (!File.Exists(headers[id].path))
                            {
                                File.Create(headers[id].path, (int)headers[id].fileSize);
                            }

                            receptionFileStream = File.Open(headers[id].path, FileMode.Append);

                            receptionFileStream.Write(data, 0, data.Length);
                        }


                        if (isLastFile)
                        {
                            receptionState = LargeRPCState.Idle;
                            break;
                        }


                        previousFileID = id;
                    }
                }
                break;
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


            headers.Add(new FileHeader(id, filename, hash, fileLength));

            // if it's the last in the packet stop early
            if (isLastInPacket)
            {
                receptionState = LargeRPCState.Receive_AwaitingAllFileData;
                break;
            }
        }
    }

    void SendNeededFilesListToSender()
    {
        List<int> fileIDs = new List<int>();
        int totalFilesToBeRequested = 0;



        // TODO: compare hashes using binarys system and only add ones that we need, for now just put in everything
        foreach (var header in headers)
        {

            // TODO: use header.hash to figure out if we need each file. This allows us to dump a ton of downloaded files together (maybe separating by lua, model, material, for convenience) and get them no matter

            // we're just adding all of them without question for now
            fileIDs.Add(header.id);
            totalFilesToBeRequested++;


            bool isLastPacket = (totalFilesToBeRequested >= headers.Count);

            if (fileIDs.Count >= fileIDsPerPacket || isLastPacket)
            {
                using (PooledBitStream bitStream = PooledBitStream.Get())
                {
                    using (PooledBitWriter writer = PooledBitWriter.Get(bitStream))
                    {
                        writer.WriteIntArray(fileIDs.ToArray());
                        writer.WriteBit(isLastPacket);

                        CustomMessagingManager.SendNamedMessage(messageName, senderID, bitStream, "MLAPI_INTERNAL");

                    }
                }
            }
        }

    }

    #endregion
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

public enum TransmissionState { Idle, Send, Receive };

public enum LargeRPCState
{
    Idle,
    Send_SendingHeaders,
    Send_AwaitingFilesNeededList,
    Send_SendingFiles,
    Send_AwaitingResponse,
    Receive_AwaitingAllHeaders,
    Receive_AwaitingAllFileData
}