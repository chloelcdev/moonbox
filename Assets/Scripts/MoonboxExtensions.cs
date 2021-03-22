using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;
using MLAPI.Connection;
using MLAPI.Serialization.Pooled;
using System.Text;
using System.IO.Compression;
using CompressionLevel = System.IO.Compression.CompressionLevel;
//using SevenZip;

public static class MoonboxExtensions
{

    public static Player GetPlayer(this NetworkedClient _client)
    {
        if (_client.PlayerObject != null)
        {
            return _client.PlayerObject.GetComponent<Player>();
        }

        Debug.LogError("Client doesn't have a PlayerObject! This is not the character, it should exist very early on and should never be destroyed, something is wrong.");
        return null;
    }

    /// <summary>
    /// DEPRECATED - Put a UIScreen component on your screen/popup and use UIScreen.Hide()
    /// </summary>
    /// <param name="_t"></param>
    public static void AnimatedUIClose(this Transform _t)
    {
        _t.DOScale(Vector3.zero, 0.4f).onComplete += () =>
        {
            _t.gameObject.SetActive(false);
        };
    }

    /// <summary>
    /// DEPRECATED - Put a UIScreen component on your screen/popup and use UIScreen.Show()
    /// </summary>
    /// <param name="_t"></param>
    public static void AnimatedUIOpen(this Transform _t)
    {
        _t.gameObject.SetActive(true);
        _t.DOScale(Vector3.one, 0.4f);
    }


    #region string to bytes
    //https://www.codeproject.com/Questions/211192/How-to-convert-string-to-byte-array-and-vice-versa

    public static string GetAsString(this byte[] _bytes, Encoding _encoding = null)
    {
        if (_encoding == null) _encoding = Encoding.Unicode;

        return new string(_encoding.GetChars(_bytes));
    }

    public static byte[] GetAsBytes(this string _string, Encoding _encoding = null)
    {
        if (_encoding == null) _encoding = Encoding.Unicode;

        return _encoding.GetBytes(_string);
    }

    #endregion


    #region byte[] compression
    //https://stackoverflow.com/questions/39191950/how-to-compress-a-byte-array-without-stream-or-system-io

    public static byte[] GetCompressed(this byte[] data, CompressionLevel _compressionLevel = CompressionLevel.Fastest)
    {
        MemoryStream output = new MemoryStream();
        using (DeflateStream dstream = new DeflateStream(output, _compressionLevel))
        {
            dstream.Write(data, 0, data.Length);
        }
        return output.ToArray();
    }

    public static byte[] GetDecompressed(this byte[] data)
    {
        MemoryStream input = new MemoryStream(data);
        MemoryStream output = new MemoryStream();
        using (DeflateStream dstream = new DeflateStream(input, CompressionMode.Decompress))
        {
            dstream.CopyTo(output);
        }
        return output.ToArray();
    }

    #endregion


    #region serialize-the-crap-out-of-it

    public static byte[] GetSerializedAndCompressed(this object _obj)
    {
        return JsonUtility.ToJson(_obj).GetAsBytes().GetCompressed();
    }

    public static T GetDecompressedAndDeserialized<T>(this byte[] _compressedSerializedData)
    {
        return JsonUtility.FromJson<T>(_compressedSerializedData.GetDecompressed().GetAsString());
    }

    /// <summary>
    /// Takes an object, serializes it to json, converts that to a byte array, compresses that byte array, and puts that in a stream\
    /// 
    /// Be sure to dispose of the stream
    /// </summary>
    /// <param name="_obj"></param>
    /// <returns></returns>
    public static PooledBitStream GetNetworkCompressedStream(this object _obj)
    {
        PooledBitStream stream = PooledBitStream.Get();

        using (PooledBitWriter writer = PooledBitWriter.Get(stream))
        {
            writer.WriteByteArray(_obj.GetSerializedAndCompressed());
        }

        return stream;
    }

    /// <summary>
    /// Returns the original object from an object.GetNetworkCompressed()
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="_compressedSerializedDataStream"></param>
    /// <returns></returns>
    public static T DeserializeNetworkCompressedStream<T>(this Stream _compressedSerializedDataStream)
    {
        using (PooledBitReader reader = PooledBitReader.Get(_compressedSerializedDataStream))
        {
            return reader.ReadByteArray().GetDecompressedAndDeserialized<T>();
        }
    }

    #endregion


    #region Byte array to hex

    // https://stackoverflow.com/questions/311165/how-do-you-convert-a-byte-array-to-a-hexadecimal-string-and-vice-versa
    // dude did a speed test, this is the fastest way

    private static readonly uint[] _lookup32 = CreateLookup32();
    private static uint[] CreateLookup32()
    {
        var result = new uint[256];
        for (int i = 0; i < 256; i++)
        {
            string s = i.ToString("X2");
            result[i] = ((uint)s[0]) + ((uint)s[1] << 16);
        }
        return result;
    }
    public static string toHex(this byte[] bytes)
    {
        var lookup32 = _lookup32;
        var result = new char[bytes.Length * 2];
        for (int i = 0; i < bytes.Length; i++)
        {
            var val = lookup32[bytes[i]];
            result[2 * i] = (char)val;
            result[2 * i + 1] = (char)(val >> 16);
        }
        return new string(result);
    }

    #endregion


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


    #region thanks to https://stackoverflow.com/questions/11816295/splitting-a-byte-into-multiple-byte-arrays-in-c-sharp aint nobody got time for dat

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

    #endregion

    // https://social.msdn.microsoft.com/Forums/vstudio/en-US/b0c31115-f6f0-4de5-a62d-d766a855d4d1/directorygetfiles-with-searchpattern-to-get-all-dll-and-exe-files-in-one-call?forum=netfxbcl
    public static string[] GetFiles(string path, string searchPattern, SearchOption searchOption)
    {
        string[] searchPatterns = searchPattern.Split('|');
        List<string> files = new List<string>();
        foreach (string sp in searchPatterns)
            files.AddRange(System.IO.Directory.GetFiles(path, sp, searchOption));
        files.Sort();
        return files.ToArray();
    }
}





//https://answers.unity.com/questions/888257/access-left-right-top-and-bottom-of-recttransform.html


 public static class RectTransformExtensions
{
    public static void SetLeft(this RectTransform rt, float left)
    {
        rt.offsetMin = new Vector2(left, rt.offsetMin.y);
    }

    public static void SetRight(this RectTransform rt, float right)
    {
        rt.offsetMax = new Vector2(-right, rt.offsetMax.y);
    }

    public static void SetTop(this RectTransform rt, float top)
    {
        rt.offsetMax = new Vector2(rt.offsetMax.x, -top);
    }

    public static void SetBottom(this RectTransform rt, float bottom)
    {
        rt.offsetMin = new Vector2(rt.offsetMin.x, bottom);
    }
}