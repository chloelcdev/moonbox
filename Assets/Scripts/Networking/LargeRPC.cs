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
    string messageName = "";

    public LargeRPC(string _messageName)
    {
        messageName = _messageName;
    }

    
    public event Action<float> OnProgressUpdated;
    public event Action<TransmissionState> OnDownloadComplete;

    public List<FileHeader> headers = new List<FileHeader>();


    /// <summary>
    /// What exactly this RPC is currently doing, see transmissionState for just Sender/Receiver/Idle
    /// </summary>
    public LargeRPCState State = LargeRPCState.Idle;

    /// <summary>
    /// Tells you whether this RPC is Idle, Sending, or Receiving based on the State
    /// </summary>
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


    #region read-only info

    // 64k cap on Unet packets, trying to stick around 32k to be safe

    // headers are about 300b/packet depending on file path length
    public static int headersPerPacket { get => 32000 / 300; }

    // file IDs are int32s, they're 4 bytes
    public static int fileIDsPerPacket { get => 32000 / 4; }

    // the size of the pieces are that we actually send over the network
    int netChunkSize { get => 1024 * 32; }

    // the size of the pieces of file we read into memory at a time when sending are
    int fileChunkSize { get => 1024 * 1024 * 50; }

    #endregion

    


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
        downloadSize = 0;
        previousFileID = -1;
        senderID = 0;
        receiverID = 0;
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

        // Make sure this is ALWAYS matching in the fullDownloadSize. If we just add an int or something it's only going to be off by 4 bytes and that's fine, but it's obviously best if it's accurate

        // packet 1: 
        // int fileCount 4b | long downloadSize 8b | <start headers>


        // header packets: 
        // int fileID 4b | string filename varsize | byte[256] hash 256b | long fileLength 8b | bool isLastInPacket 1bit


        // subsequent packets: 
        // int fileID 4b | byte[var] filedata | bool isLastInPacket 1bit


        // filecount and download size are be accounted for here
        long fullDownloadSize = sizeof(int) + sizeof(long);

        // grab info for headers
        foreach (var header in headers)
        {
            if (File.Exists(header.path))
            {
                using (FileStream fs = File.Open(header.path, FileMode.Open))
                {

                    int id = headers.Count - 1;
                    byte[] fileHash = fs.sha256();

                    headers.Add(new FileHeader(id, header.path, fileHash, fs.Length));

                    fullDownloadSize += header.CalculateDownloadSize();
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
        var packetsSent = 0;

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


            // send it off if we've filled up a packet
            if (headersThisPacket >= headersPerPacket || id >= headers.Count - 1)
            {
                // isLastInPacket
                writer.WriteBit(true);

                headersThisPacket = 0;

                CustomMessagingManager.SendNamedMessage(messageName, _clientID, bitStream, "MLAPI_INTERNAL");

                /* headers are pretty small, they really don't need the receiver to check in here unless it becomes a problem
                
                // if we haven't sent any packets yet when we get here, wait for an okay from the receiver
                if (packetsSent == 0)
                {
                    State = LargeRPCState.Send_AwaitingOkayToSend;
                    
                    while (State == LargeRPCState.Send_AwaitingOkayToSend)
                    {
                        yield return new WaitForSeconds(0.5f);
                    }
                }*/

                packetsSent++;
                
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

        // just failsafing these, should be disposed of already
        writer.Dispose();
        bitStream.Dispose();

        // now just wait for them to tell us they're good

        State = LargeRPCState.Send_AwaitingClientCompletionMessage;

        while (State == LargeRPCState.Send_AwaitingClientCompletionMessage)
        {
            yield return new WaitForSeconds(0.5f);
        }

        StopListening();
        OnDownloadComplete.Invoke(TransmissionState.Send);

        State = LargeRPCState.Idle;

        yield break;
    }


    public void ListenForFilesNeededList()
    {
        CustomMessagingManager.RegisterNamedMessageHandler(messageName, ReceiveFilesNeededListFromReceiver);
    }

    public void ListenForReceiverCompletion()
    {
        CustomMessagingManager.RegisterNamedMessageHandler(messageName, ReceiveCompletionMessage);
    }

    private void ReceiveFilesNeededListFromReceiver(ulong sender, Stream _stream)
    {
        using (PooledBitReader reader = PooledBitReader.Get(_stream))
        {
            filesNeededByRecipient.AddRange(reader.ReadIntArray());

            bool isFinalPacket = reader.ReadBit();
            if (isFinalPacket)
            {
                State = LargeRPCState.Send_SendingFiles;
            }

        }
    }

    private void ReceiveCompletionMessage(ulong sender, Stream _stream)
    {
        State = LargeRPCState.Complete;
    }


    #endregion




    #region Receiver

    ulong senderID = 0;
    ulong receiverID = 0;

    public int numFilesNeeded = 0;
    public int numFilesReceived = 0;

    public long downloadSize = 0;
    public long bytesDownloaded = 0;

    /// <summary>
    /// The last fileID received
    /// </summary>
    int previousFileID = -1;

    public FileStream receptionFileStream;

    public void ListenForDownload()
    {
        State = LargeRPCState.Receive_AwaitingFirstPacket;
        CustomMessagingManager.RegisterNamedMessageHandler(messageName, ReceiveFilesDownloadPieceFromSender);
    }

    public void ReceiveFilesDownloadPieceFromSender(ulong _senderClientID, Stream _stream)
    {
        switch (State)
        {
            case LargeRPCState.Receive_AwaitingFirstPacket:

                // allow packets from everyone if this is the server
                // only allow packets from server otherwise
                if (!NetworkingManager.Singleton.IsHost && !NetworkingManager.Singleton.IsServer && _senderClientID != NetworkingManager.Singleton.ServerClientId) return;

                // receive first packet
                using (PooledBitReader reader = PooledBitReader.Get(_stream))
                {
                    senderID = _senderClientID;

                    // grab out these two before we send the packet to the header grabbing loop
                    numFilesNeeded = reader.ReadInt32();
                    downloadSize = reader.ReadInt64();

                    // account for the downloadSize and fileCount
                    bytesDownloaded += sizeof(int) + sizeof(long);
                }

                State = LargeRPCState.Receive_AwaitingAllHeaders;
                goto case LargeRPCState.Receive_AwaitingAllHeaders;

            case LargeRPCState.Receive_AwaitingAllHeaders:

                // receive header packet
                using (PooledBitReader reader = PooledBitReader.Get(_stream))
                {
                    if (PullHeadersFromPacket(reader))
                    {
                        State = LargeRPCState.Receive_AwaitingAllFileData;
                        SendNeededFilesListToSender();
                    }
                }

                break;
            case LargeRPCState.Receive_AwaitingAllFileData:

                // receive file data packet
                using (PooledBitReader reader = PooledBitReader.Get(_stream))
                {
                    if (PullFilesFromPacket(reader))
                    {
                        // tell the server we're good to go (later on we'll need a recursive hash check so we make sure we get all the files properly
                        using (PooledBitStream okay = PooledBitStream.Get())
                        {
                            using (PooledBitWriter okayWriter = PooledBitWriter.Get(okay))
                            {
                                okayWriter.WriteBool(true);
                                CustomMessagingManager.SendNamedMessage(messageName, senderID, okay, "MLAPI_INTERNAL");
                            }
                        }
                        

                        OnDownloadComplete.Invoke(TransmissionState.Receive);
                        StopListening();
                        State = LargeRPCState.Idle;
                    }
                }
                break;
        }
    }

    /// <summary>
    /// 
    /// </summary>
    /// <returns>bool shouldMoveToNextState</returns>
    public bool PullHeadersFromPacket(PooledBitReader reader)
    {
        for (int i = 0; i < headersPerPacket - 1; i++)
        {
            int id = reader.ReadInt32();
            string filename = reader.ReadString().ToString();
            byte[] hash = reader.ReadByteArray(null, 256);
            long fileLength = reader.ReadInt64();

            bool isLastInPacket = reader.ReadBool();

            FileHeader header = new FileHeader(id, filename, hash, fileLength);
            bytesDownloaded += header.HeaderPacketBytes();
            OnProgressUpdated.Invoke(bytesDownloaded / downloadSize);
            headers.Add(header);

            // if we have a header for every file, move to waiting for file data
            if (headers.Count >= numFilesNeeded)
            {
                return true;
            }

            // if it's the last in the packet stop early (if it's the absolute last header, we already stopped)
            if (isLastInPacket)
            {
                return false;
            }

        }
        return false;
    }

    public bool PullFilesFromPacket(PooledBitReader reader)
    {
        bool packetProcessed = false;

        while (!packetProcessed)
        {
            int id = reader.ReadInt32();
            byte[] data = reader.ReadByteArray(null);
            packetProcessed = reader.ReadBit();

            bytesDownloaded += headers[id].FilePacketBytes();
            OnProgressUpdated.Invoke(bytesDownloaded / downloadSize);

            numFilesReceived++;
            bool allFilesProcessed = bytesDownloaded >= downloadSize;

            if (id != previousFileID)
            {

                receptionFileStream.Dispose();

                if (!File.Exists(headers[id].path))
                {
                    File.Create(headers[id].path, (int)headers[id].fileSize);
                }

                receptionFileStream = File.Open(headers[id].path, FileMode.Append);
            }

            using (StreamWriter sw = new StreamWriter(receptionFileStream))
            {
                sw.Write(data);
            }


            if (allFilesProcessed)
            {
                receptionFileStream.Dispose();
                return true;
            }


            previousFileID = id;
        }

        return false;
    }

    void SendNeededFilesListToSender()
    {
        List<int> fileIDs = new List<int>();

        int i = 0;
        // TODO: compare hashes using binarys system and only add ones that we need, for now just put in everything
        foreach (var header in headers)
        {
            i++;
            // TODO: use header.hash to figure out if we need each file. This allows us to dump a ton of downloaded files together (maybe separating by lua, model, material, for convenience) and get them no matter

            // we're just adding all of them without question for now
            fileIDs.Add(header.id);


            bool isFinalPacket = (i >= headers.Count);

            if (fileIDs.Count >= fileIDsPerPacket || isFinalPacket)
            {
                using (PooledBitStream bitStream = PooledBitStream.Get())
                {
                    using (PooledBitWriter writer = PooledBitWriter.Get(bitStream))
                    {
                        writer.WriteIntArray(fileIDs.ToArray());
                        writer.WriteBit(isFinalPacket);

                        CustomMessagingManager.SendNamedMessage(messageName, senderID, bitStream, "MLAPI_INTERNAL");

                    }
                }

                fileIDs.Clear();
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
        return HeaderPacketBytes() + FilePacketBytes();
    }

    public long HeaderPacketBytes()
    {
        return sizeof(int) + Encoding.Unicode.GetByteCount(path) + hash.Length + sizeof(bool);
    }

    public long FilePacketBytes()
    {
        return sizeof(long) + fileSize + sizeof(bool);
    }
}

public enum TransmissionState { Idle, Send, Receive };

public enum LargeRPCState
{
    Idle,
    Send_AwaitingOkayToSend,
    Send_SendingHeaders,
    Send_AwaitingFilesNeededList,
    Send_SendingFiles,
    Send_AwaitingClientCompletionMessage,
    Receive_AwaitingFirstPacket,
    Receive_AwaitingAllHeaders,
    Receive_AwaitingAllFileData,
    Complete
}