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
    public event Action<TransmissionSide> OnDownloadComplete;

    public List<FileHeader> headers = new List<FileHeader>();


    /// <summary>
    /// What exactly this RPC is currently doing, see transmissionState for just Sender/Receiver/Idle
    /// </summary>
    public LargeRPCState State = LargeRPCState.Idle;

    /// <summary>
    /// Tells you whether this RPC is Idle, Sending, or Receiving based on the State
    /// </summary>
    public TransmissionSide transmissionState {
        get {

            if (State.ToString().Contains("Send_"))
            {
                return TransmissionSide.Send;
            }
            else if (State.ToString().Contains("Receive_"))
            {
                return TransmissionSide.Receive;
            }
            
            return TransmissionSide.Idle;
        }
    }


    #region read-only info

    // 64k cap on Unet packets, trying to stick around 32k to be safe

    // headers are about 300b/packet depending on file path length
    public static int headersPerPacket { get => (1024*6) / 300; }

    // file IDs are int32s, they're 4 bytes
    public static int fileIDsPerPacket { get => (1024*6) / 4; }

    // the size of the pieces are that we actually send over the network
    public static int netChunkSize { get => 128; }
    // 1024 * 6 is old value

    // the size of the pieces of file we read into memory at a time when sending are
    int fileChunkSize { get => 1024 * 1024 * 50; }

    #endregion

    


    public void StopListening()
    {
        CustomMessagingManager.UnregisterNamedMessageHandler(messageName);
        Debug.LogError("Stopped Listening");
    }

    

    public void Clear()
    {
        if (transmissionState != TransmissionSide.Idle)
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

    public void ChangeState(LargeRPCState _state)
    {
        State =_state;
        Debug.LogError(_state.ToString());
    }

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
        Debug.Log("coroutine started");
        if (State != LargeRPCState.Idle)
        {
            Debug.LogWarning("Cannot start sending files while files are being sent, waiting for Idle state to begin");
            yield break;
        }

        receiverID = _clientID;

        ChangeState(LargeRPCState.Send_SendingHeaders);

        #region comment -- header sizes
        /* -- Header sizes --
        * Make sure this is ALWAYS matching in the fullDownloadSize. If we just add an int or something it's only going to be off by 4 bytes and that's fine, but it's obviously best if it's accurate
        * 
        * packet 1
        * int fileCount 4b | long downloadSize 8b | <start headers>
        * 
        * header packets
        * int fileID 4b | string filename varsize | byte[256] hash 32b | long fileLength 8b | bool isLastInPacket 1bit
        * 
        * subsequent packets
        * int fileID 4b | int filedata_length 4b | byte[var] filedata <=netChunkSize | bool isLastInPacket 1byte
        */
        #endregion

        #region Grab Download Information

        // filecount and download size are be accounted for here
        long fullDownloadSize = sizeof(int) + sizeof(long);
        
        // grab info for headers
        foreach (var path in _paths)
        {
            if (File.Exists(path))
            {
                using (FileStream fs = File.Open(path, FileMode.Open))
                {
                    Debug.Log(fs.Name);
                    int id = headers.Count;
                    byte[] fileHash = fs.sha256();

                    FileHeader header = new FileHeader(id, path, fileHash, fs.Length);
                    headers.Add(header);

                    fullDownloadSize += header.CalculateDownloadSize();
                    Debug.Log("headers were " + header.HeaderPacketBytes());
                }
            }
            else
            {
                Debug.LogError("File not found, skipping: " + path);
            }
        }
        
        #endregion

        #region send headers

        PooledBitStream bitStream = PooledBitStream.Get();
        PooledBitWriter writer = PooledBitWriter.Get(bitStream);

        // fileCount
        writer.WriteInt32(headers.Count);

        // downloadSize
        writer.WriteInt64(fullDownloadSize);

        
        var headersThisPacket = 0;
        var packetsSent = 0;
        Debug.Log("Sending headers");
        foreach (var header in headers)
        {
            Debug.Log(headersThisPacket + "          " + packetsSent);

            var path = header.path;
            var id = header.id;

            headersThisPacket++;

            // fileID
            writer.WriteInt32(header.id);

            // filename
            writer.WriteString(path);

            Debug.Log(Encoding.Unicode.GetString(header.hash));
            // hash
            writer.WriteByteArray(header.hash, 32);

            // fileLength
            writer.WriteInt64(header.fileSize);

            bool isLastPacket = id >= headers.Count - 1;

            // send it off if we've filled up a packet
            if (headersThisPacket >= headersPerPacket || isLastPacket)
            {
                Debug.Log("message going out");
                // isLastInPacket
                writer.WriteBit(true);

                

                CustomMessagingManager.SendNamedMessage(messageName, _clientID, bitStream, "MLAPI_INTERNAL");

                /* headers are pretty small, they really don't need the receiver to check in here unless it becomes a problem
                
                // if we haven't sent any packets yet when we get here, wait for an okay from the receiver
                if (packetsSent == 0)
                {
                    ChangeState(LargeRPCState.Send_AwaitingOkayToSend);
                    
                    while (State == LargeRPCState.Send_AwaitingOkayToSend)
                    {
                        yield return new WaitForSeconds(0.5f);
                    }
                }*/

                packetsSent++;
                Debug.Log("headers: "+headersThisPacket + "          packets: " + packetsSent);
                headersThisPacket = 0;

                writer.Dispose();
                bitStream.Dispose();

                bitStream = PooledBitStream.Get();
                writer = PooledBitWriter.Get(bitStream);

                // don't wait on the last one
                if (!isLastPacket)
                {
                    yield return new WaitForSeconds(1 / 14);
                }
            }
            else
            {
                writer.WriteBit(false);
            }
        }

        writer.Dispose();
        bitStream.Dispose();

        #endregion

        ChangeState(LargeRPCState.Send_AwaitingFilesNeededList);


        ListenForFilesNeededListOrCompletion();

        // loop start
        while (State != LargeRPCState.Complete)
        {
            Debug.Log("Not done, running not-complete loop");
            #region wait for needed files list

            while (State == LargeRPCState.Send_AwaitingFilesNeededList || State == LargeRPCState.Send_EnsuringIntegrity)
            {
                Debug.Log("waiting for list");
                yield return new WaitForSeconds(0.5f);
            }

            Debug.Log("No longer waiting for list");

            #endregion
            // runs ReceiveFilesNeededListFromReceiver, changes state to either Send_SendingFiles or Complete


            if (filesNeededByRecipient.Count > 0)
            {
                Debug.Log("client still needs more files, sending");
                #region send files

                bitStream = PooledBitStream.Get();
                writer = PooledBitWriter.Get(bitStream);

                foreach (var header in headers)
                {
                    Debug.Log("processing header");
                    if (File.Exists(header.path) && filesNeededByRecipient.Contains(header.id))
                    {
                        Debug.Log("file is needed");
                        using (FileStream fs = File.Open(header.path, FileMode.Open))
                        {

                            // while loop pulled from fs.Read docs from microsoft, a little confusing to the glance but works and will be fast

                            int numBytesToRead = (int)fs.Length;
                            int numBytesRead = 0;
                            while (numBytesToRead > 0)
                            {
                                Debug.Log("still bytes left");
                                int thisFileChunkSize = fileChunkSize;
                                thisFileChunkSize = Mathf.Min(thisFileChunkSize, numBytesToRead);

                                byte[] fileChunk = new byte[thisFileChunkSize];
                                
                                // Read may return anything from 0 to numBytesToRead.
                                int n = fs.Read(fileChunk, numBytesRead, thisFileChunkSize);



                                foreach (byte[] netChunk in fileChunk.Slices(netChunkSize, false))
                                {
                                    Debug.Log("processing next chunk");

                                    // fileID
                                    writer.WriteInt32(header.id);

                                    writer.WriteInt32(netChunk.Length);
                                    Debug.Log("netchunk len: " + netChunk.Length);
                                    // filedata
                                    writer.WriteByteArray(netChunk);

                                    // isLastInPacket
                                    bool isLastInPacket = bitStream.Length >= netChunkSize || netChunk.Length < netChunkSize;
                                    writer.WriteBit(isLastInPacket);

                                    if (isLastInPacket)
                                    {

                                        CustomMessagingManager.SendNamedMessage(messageName, _clientID, bitStream, "MLAPI_INTERNAL");
                                        Debug.Log("packet sent");

                                        yield return new WaitForSeconds(1 / 14);

                                        writer.Dispose();
                                        bitStream.Dispose();

                                        bitStream = PooledBitStream.Get();
                                        writer = PooledBitWriter.Get(bitStream);
                                    }

                                }

                                // Break when the end of the file is reached.
                                if (n == 0)
                                {
                                    Debug.Log("end of file reached");
                                    break;
                                }

                                numBytesRead += n;
                                numBytesToRead -= n;
                            }
                        }
                    }
                }

                Debug.Log("all headers processed");

                filesNeededByRecipient.Clear();

                // just failsafing these, should be disposed of already
                writer.Dispose();
                bitStream.Dispose();

                #endregion

                ChangeState(LargeRPCState.Send_EnsuringIntegrity);
            }

            Debug.Log("Waiting 2 seconds before checking completion again");
            yield return new WaitForSeconds(2f);
        
        }

        StopListening();

        Debug.Log("complete");
        if (OnDownloadComplete != null) OnDownloadComplete(TransmissionSide.Send);

        ChangeState(LargeRPCState.Idle);

        yield break;
    }


    public void ListenForFilesNeededListOrCompletion()
    {
        CustomMessagingManager.RegisterNamedMessageHandler(messageName, ReceiveFilesNeededListFromReceiver);
        Debug.LogError("Started Listening");
    }

    // this will pass nothing when they're done
    private void ReceiveFilesNeededListFromReceiver(ulong sender, Stream _stream)
    {
        Debug.Log("receiving needed files list");
        using (PooledBitReader reader = PooledBitReader.Get(_stream))
        {
            bool isFinalPacket = reader.ReadBit();
            filesNeededByRecipient.AddRange(reader.ReadIntArray());
            bool clientFinished = reader.ReadBit();

            Debug.Log("----------\n"+isFinalPacket + "\n" + filesNeededByRecipient.Count + "\n" + clientFinished + "\n");

            if (isFinalPacket && !clientFinished)
            {
                ChangeState(LargeRPCState.Send_SendingFiles);
            }

            if (clientFinished)
            {
                ChangeState(LargeRPCState.Complete);
                Debug.Log("complete");
                if (OnDownloadComplete != null) OnDownloadComplete(TransmissionSide.Send);
                StopListening();
            }

        }
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

    FileStream receptionFileStream;
    BinaryWriter receptionFileStreamWriter;

    public void ListenForDownload()
    {
        ChangeState(LargeRPCState.Receive_AwaitingFirstPacket);
        CustomMessagingManager.RegisterNamedMessageHandler(messageName, ReceiveFilesDownloadPieceFromSender);
        Debug.LogError("Started Listening");
    }

    public void ReceiveFilesDownloadPieceFromSender(ulong _senderClientID, Stream _stream)
    {
        Debug.LogError("packet");
        switch (State)
        {
            case LargeRPCState.Receive_AwaitingFirstPacket:

                // allow packets from everyone if this is the server
                // only allow packets from server otherwise
                // if it's not from the server and were not the server, bail
                Debug.Log(NetworkingManager.Singleton.IsHost);
                Debug.Log(NetworkingManager.Singleton.IsServer);
                Debug.Log(NetworkingManager.Singleton.ServerClientId);
                Debug.Log(_senderClientID);

                if (!NetworkingManager.Singleton.IsHost && !NetworkingManager.Singleton.IsServer && _senderClientID != NetworkingManager.Singleton.ServerClientId && _senderClientID != NetworkingManager.Singleton.LocalClientId) return;

                Debug.Log(1);
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

                ChangeState(LargeRPCState.Receive_AwaitingAllHeaders);
                goto case LargeRPCState.Receive_AwaitingAllHeaders;




            case LargeRPCState.Receive_AwaitingAllHeaders:

                // receive header packet
                using (PooledBitReader reader = PooledBitReader.Get(_stream))
                {
                    if (PullHeadersFromPacket(reader))
                    {
                        ChangeState(LargeRPCState.Receive_AwaitingAllFileData);
                        Game.Instance.StartCoroutine(SendNeededFilesListToSender(GetNeededFiles()));

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

                        Debug.Log("checking files");

                        List<int> filesNeeded = new List<int>();

                        foreach (var header in headers)
                        {
                            string filePath = testPath(header.path);

                            if (File.Exists(filePath))
                            {
                                using (FileStream fs = new FileStream(filePath, FileMode.Open))
                                {

                                    Debug.Log(Encoding.Unicode.GetString(header.hash) + "  --  Received hash:");
                                    Debug.Log(Encoding.Unicode.GetString(fs.sha256()) + "  --  File hash:");

                                    if (false && fs.sha256() != header.hash)
                                    {
                                        filesNeeded.Add(header.id);
                                    }
                                }
                            }
                            else
                            {
                                Debug.LogError("File not found, you messed up, holmes.");
                            }
                        }


                        Game.Instance.StartCoroutine(SendNeededFilesListToSender(filesNeeded));


                        
                    }
                }
                break;

        }
    }


    /// <returns>bool shouldMoveToNextState</returns>
    public bool PullHeadersFromPacket(PooledBitReader reader)
    {
        for (int i = 0; i < headersPerPacket - 1; i++)
        {
            int id = reader.ReadInt32();
            string filename = reader.ReadString().ToString();
            byte[] hash = reader.ReadByteArray(null, 32);
            long fileLength = reader.ReadInt64();

            bool isLastInPacket = reader.ReadBool();

            FileHeader header = new FileHeader(id, filename, hash, fileLength);
            Debug.LogError("header received: " + id + " " + filename + "    hash: " + hash.ToString() + "  " + fileLength.ToString());
            bytesDownloaded += header.HeaderPacketBytes();
            if (OnProgressUpdated!=null) OnProgressUpdated(bytesDownloaded / downloadSize);
            headers.Add(header);

            // if we have a header for every file, move to waiting for file data
            if (headers.Count >= numFilesNeeded)
            {
                Debug.Log("headers were " + (bytesDownloaded - sizeof(int) - sizeof(long)));
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

    public string testPath(string _path)
    {
        return _path.Insert(_path.Length - 4, "_test");
    }

    public bool PullFilesFromPacket(PooledBitReader reader)
    {
        bool packetEndHit = false;

        while (!packetEndHit)
        {
            
            int id = reader.ReadInt32();
            int dataLen = reader.ReadInt32();
            byte[] data = reader.ReadByteArray(null);
            packetEndHit = reader.ReadBit();

            Debug.LogError("file packet received: " + id + " " + data.Length + "    packet finished: " + packetEndHit.ToString());

            bytesDownloaded += sizeof(int) + sizeof(bool) + data.Length;
            if (OnProgressUpdated != null) OnProgressUpdated(bytesDownloaded / downloadSize);

            numFilesReceived++;
            Debug.Log(bytesDownloaded + "   /   " + downloadSize);
            bool allFilesProcessed = bytesDownloaded >= downloadSize;

            if (id != previousFileID)
            {

                if (receptionFileStream != null)
                {
                    receptionFileStreamWriter.Dispose();
                    receptionFileStream.Dispose();
                }

                string filePath = testPath(headers[id].path);

                receptionFileStream = File.Open(filePath, FileMode.Append);
                receptionFileStreamWriter = new BinaryWriter(receptionFileStream);
            }

            receptionFileStreamWriter.Write(data);


            if (allFilesProcessed)
            {
                receptionFileStreamWriter.Dispose();
                receptionFileStream.Dispose();
                return true;
            }


            previousFileID = id;
        }

        return false;
    }

    List<int> GetNeededFiles()
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
        }

        return fileIDs;
    }

    // Essentially, when this is passed an empty list, the okay will be sent to the server
    IEnumerator SendNeededFilesListToSender(List<int> _fileIDs)
    {
        bool allFilesReceived = _fileIDs.Count == 0;

        Debug.Log("sending needed files, files: " + _fileIDs.Count);

        PooledBitStream bitStream = PooledBitStream.Get();
        PooledBitWriter writer = PooledBitWriter.Get(bitStream);
        
        if (allFilesReceived)
        {
            writer.WriteBit(true);
            writer.WriteIntArray(_fileIDs.ToArray());
            writer.WriteBit(true);

            CustomMessagingManager.SendNamedMessage(messageName, senderID, bitStream, "MLAPI_INTERNAL");

            bitStream.Dispose();
            writer.Dispose();
            _fileIDs.Clear();

            if (OnDownloadComplete != null) OnDownloadComplete(TransmissionSide.Receive);

            StopListening();
            ChangeState(LargeRPCState.Idle);
        }
        else
        {
            var i = 0;
            List<int> ids = _fileIDs;
            foreach (var id in ids)
            {
                i++;

                bool isFinalPacket = i >= _fileIDs.Count;

                if (i >= _fileIDs.Count)
                {
                    writer.WriteBit(isFinalPacket);
                    writer.WriteIntArray(_fileIDs.ToArray());
                    writer.WriteBit(false);

                    CustomMessagingManager.SendNamedMessage(messageName, senderID, bitStream, "MLAPI_INTERNAL");

                    bitStream.Dispose();
                    writer.Dispose();

                    bitStream = PooledBitStream.Get();
                    writer = PooledBitWriter.Get(bitStream);

                    _fileIDs.Clear();

                    yield return new WaitForSeconds(1 / 8);

                    break;
                }
            }
        }

        yield break;

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
        return HeaderPacketBytes() + FilePacketsBytes();
    }

    public long HeaderPacketBytes()
    {
        return sizeof(int) + Encoding.Unicode.GetByteCount(path) + hash.Length + sizeof(bool);
    }

    public long FilePacketsBytes()
    {
        int numberOfChunks = Mathf.CeilToInt(fileSize / LargeRPC.netChunkSize);
        return (sizeof(long) + sizeof(bool)) * numberOfChunks + fileSize;
    }
}

public enum TransmissionSide { Idle, Send, Receive };

public enum LargeRPCState
{
    Idle,
    Send_AwaitingOkayToSend,
    Send_SendingHeaders,
    Send_AwaitingFilesNeededList,
    Send_SendingFiles,
    Send_EnsuringIntegrity,
    Receive_AwaitingFirstPacket,
    Receive_AwaitingAllHeaders,
    Receive_AwaitingAllFileData,
    Complete
}