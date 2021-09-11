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

public class LargeRPC : IDisposable
{

    public LargeRPC(string _messageName)
    {
        MessageName = _messageName;
    }


    Coroutine sendFilesDownloadCoroutine;
    Coroutine sendNeededFilesListToSender;

    public void Dispose()
    {
        MainController.Instance.StopCoroutine(sendFilesDownloadCoroutine);
        MainController.Instance.StopCoroutine(sendNeededFilesListToSender);
        StopListening();
        Headers = null;
        OnProgressUpdated = null;
        OnDownloadComplete = null;
    }

    public event Action<float, string> OnProgressUpdated;
    public event Action<SendOrReceiveFlag, ulong> OnDownloadComplete;

    public List<FileHeader> Headers { get; private set; } = new List<FileHeader>();


    #region Constants

    // 64k cap on Unet packets, trying to stick around 32k to be safe

    // headers are about 300b/packet depending on file path length
    public const int headersPerPacket = (1024 * 6) / 300;

    // file IDs are int32s, they're 4 bytes
    public const int fileIDsPerPacket = (1024 * 6) / 4;

    // the size of the pieces are that we actually send over the network - 1024 * 6 is old value
    public const int netChunkSize = 1024;

    // the size of the pieces of file we read into memory at a time when sending
    public const int fileChunkSize = 1024 * 1024 * 50;

    #endregion


    #region Common Information


    /// <summary>
    /// What exactly this RPC is currently doing, see transmissionState for just Sender/Receiver/Idle
    /// </summary>
    public LargeRPCState State { get; private set; }

    /// <summary>
    /// Tells you whether this RPC is Idle, Sending, or Receiving based on the State
    /// </summary>
    public SendOrReceiveFlag TransmissionSide { get; private set; }


    public string MessageName { get; private set; } = "";
    public long DownloadSize { get; private set; }
    public ulong SenderID { get; private set; }
    public ulong ReceiverID { get; private set; }

    #endregion


    #region Common Methods

    public void StopListening()
    {
        CustomMessagingManager.UnregisterNamedMessageHandler(MessageName);
        Debug.Log("Stopped Listening");
    }

    public void ChangeState(LargeRPCState _state)
    {
        State = _state;

        if (State.ToString().Contains("Send_"))
        {
            TransmissionSide = SendOrReceiveFlag.Send;
        }
        else if (State.ToString().Contains("Receive_"))
        {
            TransmissionSide = SendOrReceiveFlag.Receive;
        }
        // if it's on compelte just don't change it, it will still have its transmission side set from the operation
        else if (State != LargeRPCState.Complete)
        {
            TransmissionSide = SendOrReceiveFlag.Idle;
        }


        if (TransmissionSide == SendOrReceiveFlag.Receive)
        {
            ReceiverID = NetworkingManager.Singleton.LocalClientId;
        }
        else if (TransmissionSide == SendOrReceiveFlag.Send)
        {
            SenderID = NetworkingManager.Singleton.LocalClientId;
        }

        Debug.Log("Changing LargeRPC state: " + _state.ToString());
    }

    public void Clear()
    {
        if (TransmissionSide != SendOrReceiveFlag.Idle)
        {
            Debug.Log("Cannot clear LargeRPC until it has finished the current job.");
            return;
        }

        receptionFileStream.Dispose();
        filesToSend.Clear();
        Headers.Clear();

        numFilesNeeded = 0;
        DownloadSize = 0;
        previousFileID = -1;
        SenderID = 0;
        ReceiverID = 0;
    }

    #endregion


    #region Sender

    /// <summary>
    /// The files the receipient confirmed they do not have
    /// </summary>
    List<int> filesToSend = new List<int>();

    public void SendFile(string _path, ulong _clientID)
    {
        SendFiles(new List<string>() { _path }, _clientID);
    }
    public void SendFiles(List<string> _paths, ulong _clientID)
    {
        SendFiles(_paths.ToArray(), _clientID);
    }
    public void SendFiles(string[] _paths, ulong _clientID)
    {
        sendFilesDownloadCoroutine = MainController.Instance.StartCoroutine(SendFilesDownloadRoutine(_paths, _clientID));
    }
    public void SendFolder(string _path, ulong _clientID)
    {
        if (Directory.Exists(_path))
        {
            SendFiles(Directory.GetFiles(_path), _clientID);
        }
        else
        {
            Debug.LogWarning("Folder not found");
        }
    }

    /// <summary>
    /// this function splits FILES into MEMORY SAFE sized chunks and safely sends one before starting another
    /// 
    /// files receipient needs to receive the same number of headers with each header packet (packet 1 counts as a header packet)
    /// </summary>
    public IEnumerator SendFilesDownloadRoutine(string[] _paths, ulong _clientID)
    {

        Debug.Log("coroutine started");
        if (State != LargeRPCState.Idle)
        {
            Debug.LogWarning("Cannot start sending files while files are being sent, waiting for Idle state to begin");
            yield break;
        }

        ReceiverID = _clientID;

        ChangeState(LargeRPCState.Send_SendingHeaders);

        #region comment -- header sizes
        /* -- Header sizes --
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
        
        // grab info for headers
        foreach (var path in _paths)
        {
            if (File.Exists(path))
            {
                using (FileStream fs = File.Open(path, FileMode.Open))
                {
                    Debug.Log(fs.Name);
                    int id = Headers.Count;
                    byte[] fileHash = fs.sha256();
                    yield return new WaitForEndOfFrame();

                    FileHeader header = new FileHeader(id, Path.GetFileName(path), fileHash, fs.Length);
                    Headers.Add(header);

                    DownloadSize += header.fileSize;
                }

                // let it exit if it needs to, giving StopRoutine() a chance
                yield return 0;
            }
            else
            {
                Debug.LogWarning("File not found, skipping: " + path);
            }
        }
        
        #endregion

        #region send headers

        PooledBitStream bitStream = PooledBitStream.Get();
        PooledBitWriter writer = PooledBitWriter.Get(bitStream);

        // fileCount
        writer.WriteInt32(Headers.Count);

        // downloadSize
        writer.WriteInt64(DownloadSize);

        
        var headersThisPacket = 0;
        var packetsSent = 0;
        Debug.Log("Sending headers");
        foreach (var header in Headers)
        {

            // let it exit if it needs to, giving StopRoutine() a chance
            yield return 0;

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

            bool isLastPacket = id >= Headers.Count - 1;

            // send it off if we've filled up a packet
            if (headersThisPacket >= headersPerPacket || isLastPacket)
            {
                Debug.Log("message going out");
                // isLastInPacket
                writer.WriteBit(true);

                

                CustomMessagingManager.SendNamedMessage(MessageName, _clientID, bitStream, "MLAPI_INTERNAL");

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
            // let it exit if it needs to, giving StopRoutine() a chance
            yield return 0;

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


            if (filesToSend.Count > 0)
            {
                Debug.Log("client still needs more files, sending");

                #region send files

                bitStream = PooledBitStream.Get();
                writer = PooledBitWriter.Get(bitStream);

                foreach (var header in Headers)
                {
                    // let it exit if it needs to, giving StopRoutine() a chance
                    yield return 0;

                    Debug.Log("processing header");
                    if (File.Exists(header.path) && filesToSend.Contains(header.id))
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

                                    //writer.WriteInt32(netChunk.Length);
                                    Debug.Log("netchunk len: " + netChunk.Length);
                                    // filedata
                                    writer.WriteByteArray(netChunk);

                                    // isLastInPacket, need to add in its own size
                                    bool isLastInPacket = bitStream.Length+1 >= netChunkSize || netChunk.Length < netChunkSize;
                                    writer.WriteBit(isLastInPacket);

                                    if (isLastInPacket)
                                    {

                                        CustomMessagingManager.SendNamedMessage(MessageName, _clientID, bitStream, "MLAPI_INTERNAL");
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
                                    Debug.Log("end of file reached, this is a failsafe");
                                    break;
                                }

                                numBytesRead += n;
                                numBytesToRead -= n;
                            }
                        }
                    }
                }

                Debug.Log("all headers processed");

                filesToSend.Clear();

                // just failsafing these, should be disposed of already
                writer.Dispose();
                bitStream.Dispose();

                #endregion

                ChangeState(LargeRPCState.Send_EnsuringIntegrity);
            }

            Debug.Log("Waiting before checking completion again");
            yield return new WaitForSeconds(1f);
        
        }

        StopListening();

        Debug.Log("files sent");
        if (OnDownloadComplete != null) OnDownloadComplete(SendOrReceiveFlag.Send, ReceiverID);

        ChangeState(LargeRPCState.Idle);

        yield break;
    }


    public void ListenForFilesNeededListOrCompletion()
    {
        CustomMessagingManager.RegisterNamedMessageHandler(MessageName, ReceiveFilesNeededListFromReceiver);
        Debug.Log("Started Listening");
    }

    // this will pass nothing when they're done
    private void ReceiveFilesNeededListFromReceiver(ulong sender, Stream _stream)
    {
        Debug.Log("receiving needed files list");
        using (PooledBitReader reader = PooledBitReader.Get(_stream))
        {
            bool isFinalPacket = reader.ReadBit();
            filesToSend.AddRange(reader.ReadIntArray());
            bool clientFinished = reader.ReadBit();

            Debug.Log("----------\n"+isFinalPacket + "\n" + filesToSend.Count + "\n" + clientFinished + "\n");

            if (isFinalPacket && !clientFinished)
            {
                ChangeState(LargeRPCState.Send_SendingFiles);
            }

            if (clientFinished)
            {
                StopListening();

                ChangeState(LargeRPCState.Complete);
                Debug.Log("complete");

                if (OnDownloadComplete != null) OnDownloadComplete(SendOrReceiveFlag.Send, ReceiverID);

                
            }

        }
    }

    #endregion


    #region Receiver

    public int numFilesNeeded { get; private set; }
    public int numFilesReceived { get; private set; }

    /// <summary>
    /// The number of bytes of file data downloaded on the receiver. (only file data is accounted for, this excludes headers (because we have a count and they're negligible in size), as well as flags and etc (because they will take a negligible amount of time)
    /// </summary>
    public long fileBytesReceived { get; private set; }

    /// <summary>
    /// The last fileID received
    /// </summary>
    int previousFileID = -1;

    FileStream receptionFileStream;
    BinaryWriter receptionFileStreamWriter;

    public void ListenForDownload()
    {
        ChangeState(LargeRPCState.Receive_AwaitingFirstPacket);
        CustomMessagingManager.RegisterNamedMessageHandler(MessageName, ReceiveFilesDownloadPieceFromSender);
        Debug.Log("Started Listening");
    }

    public void ReceiveFilesDownloadPieceFromSender(ulong _senderClientID, Stream _stream)
    {
        switch (State)
        {
            case LargeRPCState.Receive_AwaitingFirstPacket:

                // allow packets from everyone if this is the server
                // only allow packets from server otherwise
                // if it's not from the server and we're not the server, bail

                if (!NetworkingManager.Singleton.IsHost && !NetworkingManager.Singleton.IsServer && _senderClientID != NetworkingManager.Singleton.ServerClientId && _senderClientID != NetworkingManager.Singleton.LocalClientId) return;

                Debug.Log(1);
                // receive first packet
                using (PooledBitReader reader = PooledBitReader.Get(_stream))
                {
                    SenderID = _senderClientID;

                    // grab out these two before we send the packet to the header grabbing loop
                    numFilesNeeded = reader.ReadInt32();
                    DownloadSize = reader.ReadInt64();
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
                        sendNeededFilesListToSender = MainController.Instance.StartCoroutine(SendNeededFilesListToSender(GetNeededFiles()));

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

                        foreach (var header in Headers)
                        {
                            string filePath = Paths.GetDownloadPath(header);

                            if (File.Exists(filePath))
                            {
                                using (FileStream fs = new FileStream(filePath, FileMode.Open))
                                {
                                    byte[] hash = fs.sha256();

                                    Debug.Log(header.hash.toHex() + "  --  Received hash:");
                                    Debug.Log(hash.toHex() + "  --  File hash:");

                                    if (hash != header.hash)
                                    {
                                        Debug.Log("hashes not equal");
                                        //filesNeeded.Add(header.id);
                                    }
                                }
                            }
                            else
                            {
                                Debug.LogWarning("File not found, you messed up, holmes.");
                            }
                        }


                        sendNeededFilesListToSender = MainController.Instance.StartCoroutine(SendNeededFilesListToSender(filesNeeded));


                        
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
            Debug.Log("header received: " + id + " " + filename + "    hash: " + hash.ToString() + "  " + fileLength.ToString());
            
            if (OnProgressUpdated!=null) OnProgressUpdated((float)id / (float)numFilesNeeded, "Receiving headers " + id);
            Headers.Add(header);

            // if we have a header for every file, move to waiting for file data
            if (Headers.Count >= numFilesNeeded)
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
        bool packetEndHit = false;

        while (!packetEndHit)
        {
            
            int id = reader.ReadInt32();

            byte[] data = reader.ReadByteArray(null);
            packetEndHit = reader.ReadBit();

            float progress = (float)fileBytesReceived / (float)DownloadSize;

            //Debug.LogError("file packet received: " + id + " " + data.Length + "    packet finished: " + packetEndHit.ToString());

            fileBytesReceived += data.Length;
            //Debug.LogWarning("internal prog: " + progress + "     -     " + fileBytesReceived + "/" + DownloadSize);
            if (OnProgressUpdated != null) OnProgressUpdated(progress, "Receiving file: "+ Headers[id].path);

            numFilesReceived++;
            //Debug.Log(fileBytesReceived + "   /   " + DownloadSize);
            bool allFilesProcessed = fileBytesReceived >= DownloadSize;

            if (id != previousFileID)
            {

                if (receptionFileStream != null)
                {
                    receptionFileStreamWriter.Dispose();
                    receptionFileStream.Dispose();
                }

                string filePath = Paths.GetDownloadPath(Headers[id]);

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
        foreach (var header in Headers)
        {
            i++;

            string dlFilePath = Paths.GetDownloadPath(header);

            if (File.Exists(dlFilePath))
            {
                using (FileStream fs = File.Open(dlFilePath, FileMode.Open))
                {
                    byte[] fileHash = fs.sha256();

                    if (fileHash != header.hash)
                    {
                        fileIDs.Add(header.id);
                    }
                }
            }
            else
            {
                fileIDs.Add(header.id);
            }
            // TODO: use header.hash to figure out if we need each file. This allows us to dump a ton of downloaded files together (maybe separating by lua, model, material, for convenience) and get them no matter

            // we're just adding all of them without question for now (except test files)
            if (!Path.GetFileName(header.path).Contains("_test"))
            {
                fileIDs.Add(header.id);
            }
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

            CustomMessagingManager.SendNamedMessage(MessageName, SenderID, bitStream, "MLAPI_INTERNAL");

            bitStream.Dispose();
            writer.Dispose();
            _fileIDs.Clear();

            StopListening();

            if (OnDownloadComplete != null) OnDownloadComplete(SendOrReceiveFlag.Receive, SenderID);

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

                    CustomMessagingManager.SendNamedMessage(MessageName, SenderID, bitStream, "MLAPI_INTERNAL");

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

public enum SendOrReceiveFlag { Idle, Send, Receive, Busy };

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