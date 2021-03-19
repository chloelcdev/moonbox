using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;
using MLAPI.Connection;

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
}