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

public class RPCDownload
{
    public event Action<float> OnProgressUpdated;
    public event Action<byte[]> OnDownloadComplete;
    
    public static void ListenForRPCDownloads()
    {
        CustomMessagingManager.RegisterNamedMessageHandler("gameDownload", (senderClientId, stream) =>
        {

            using (FileStream fsNew = new FileStream("C:/NVIDIA/filedownload.txt",
                FileMode.Create, FileAccess.Write))
            {
                byte[] buffer = new byte[stream.Length];
                stream.Read(buffer, 0, (int)stream.Length);

                fsNew.Write(buffer, 0, buffer.Length);
            }
        });
    }
   

    public RPCDownload(string _messageName, ulong _clientId, byte[] _data) {
        using (MemoryStream dataStream = new MemoryStream(_data))
        {
            
            CustomMessagingManager.SendNamedMessage("gameDownload", _clientId, dataStream); //Channel is optional extra argument

        }
    }

    
    public RPCDownload SendGameDownload(ulong _clientID, byte[] _data)
    {
        RPCDownload dl = new RPCDownload("", _clientID, _data);
        

        return dl;
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

    // this function splits FILES into MEMORY SAFE sized chunks and safely sends one before starting another
    public static void SendFile(string _path = "C:/NVIDIA/file.txt", ulong _clientID = 0, int _fileChunkSize = 1024*1024*50, int _netChunkSize = 1024*16)
    {
        if (File.Exists(_path))
        {
            using (FileStream fs = File.Open(_path, FileMode.Open))
            {
                // while loop pulled from fs.Read docs from microsoft, a little confusing to the glance but works and will be fast
                if (fs.Length <= _fileChunkSize)
                {
                    //send file
                }
                else 
                {
                    int numBytesToRead = (int)fs.Length;
                    int numBytesRead = 0;
                    while (numBytesToRead > 0)
                    {

                        byte[] fileChunk = new byte[_fileChunkSize];
                        // Read may return anything from 0 to numBytesToRead.
                        int n = fs.Read(fileChunk, numBytesRead, _fileChunkSize);


                        // initial packet:  int32_filename_length_in_bytes | filename | fixed_length_hash | fileLengthInBytes

                        using (PooledBitStream bitStream = PooledBitStream.Get()) {
                            using (PooledBitWriter writer = PooledBitWriter.Get(bitStream))
                            {
                                // int32_filename_length_in_bytes
                                writer.WriteInt32(_path.Length);

                                // filename
                                writer.WriteCharArray(_path.ToCharArray(), _path.Length);

                                // fixed_length_hash
                                byte[] fixed_length_hash = sha256(BitConverter.GetBytes(0));
                                writer.WriteByteArray(fixed_length_hash, 256);

                                // fileLengthInBytes
                                writer.WriteInt64(fs.Length);
                            }

                            // MLAPI_INTERNAL is an ordered channel
                            CustomMessagingManager.SendNamedMessage("gameDownload", _clientID, bitStream, "MLAPI_INTERNAL");
                        }

                        // subsequent packets: filename_length_in_bytes | filename | partialOrWholeFileData_doesntMatterWho

                        foreach (byte[] netChunk in Split(fileChunk, _netChunkSize))
                        {

                            using (PooledBitStream bitStream = PooledBitStream.Get())
                            {
                                using (PooledBitWriter writer = PooledBitWriter.Get(bitStream))
                                {
                                    // int32_filename_length_in_bytes
                                    writer.WriteInt32(_path.Length);

                                    // filename
                                    writer.WriteCharArray(_path.ToCharArray(), _path.Length);

                                    writer.WriteByteArray(netChunk, netChunk.Length);
                                }


                                // MLAPI_INTERNAL is an ordered channel
                                CustomMessagingManager.SendNamedMessage("gameDownload", _clientID, bitStream, "MLAPI_INTERNAL");
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
    }

    static byte[] sha256(byte[] _bytes)
    {
        var crypt = new System.Security.Cryptography.SHA256Managed();
        byte[] crypto = crypt.ComputeHash(_bytes);
        return crypto;
    }

}
