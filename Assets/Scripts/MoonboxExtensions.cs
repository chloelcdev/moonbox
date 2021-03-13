using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
public static class MoonboxExtensions
{

    // thanks https://stackoverflow.com/questions/50655268/c-sharp-split-byte-array-into-separate-chunks-and-get-number-of-chunks/50655347
    /// <summary>
    /// this function chunks BYTES into NETWORK sized chunks
    /// </summary>
    public static byte[][] Split(this byte[] _data, int _chunkSize)
    {

        if (_data.Length <= _chunkSize)
        {
            byte[][] data = new byte[1][];
            data[0] = _data;
            return data;
        }

        int chunkCount = (int)Math.Ceiling(_data.Length / (float)_chunkSize);

        byte[][] chunks = new byte[chunkCount][];

        for (int i = 0; i <= chunkCount; i++)
        {
            chunks[i] = new byte[Math.Min(_chunkSize, _data.Length - i * _chunkSize)];
            for (int j = 0; j <= _chunkSize && (i * chunkCount + j) < _data.Length; j++)
            {
                Debug.Log(i + "  " + j);
                chunks[i][j] = _data[i * chunkCount + j];
            }
        }

        return chunks;

    }

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


    // took from https://stackoverflow.com/questions/11816295/splitting-a-byte-into-multiple-byte-arrays-in-c-sharp
    // aint nobody got time for dat
    public static T[] CopySlice<T>(this T[] source, int index, int length, bool padToLength = false)
    {
        int n = length;
        T[] slice = null;

        if (source.Length < index + length)
        {
            n = source.Length - index;
            if (padToLength)
            {
                slice = new T[length];
            }
        }

        if (slice == null) slice = new T[n];
        Array.Copy(source, index, slice, 0, n);
        return slice;
    }

    public static IEnumerable<T[]> Slices<T>(this T[] source, int count, bool padToLength = false)
    {
        for (var i = 0; i < source.Length; i += count)
            yield return source.CopySlice(i, count, padToLength);
    }
}