using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
public static class MoonboxExtensions
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