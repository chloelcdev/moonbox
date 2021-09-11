using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public static class Paths
{
    public static string MainGamePath => Path.Combine(Application.dataPath, "game");


    public static string AddonPathRelative => "addons";
    public static string AddonPath => Path.Combine(MainGamePath, AddonPathRelative);

    public static string DownloadCachePathRelative => "downloadCache";
    public static string DownloadCachePath => Path.Combine(MainGamePath, DownloadCachePathRelative);


    public static string GetInfoFilePath(string _directoryPath)
    {
        return Path.Combine(_directoryPath, "info.txt");
    }

    public static string GetGamemodeFilesPath(Addon _gamemode)
    {
        return Path.Combine(AddonPath, _gamemode.RelativePath);
    }

    public static string GetCurrentGamemodeFilesPath()
    {
        return Path.Combine(AddonPath, LobbyManager.Instance.SelectedGamemode.RelativePath);
    }

    public static string GetDownloadPath(FileHeader _header)
    {
        return Path.Combine(Paths.DownloadCachePath, _header.hash.toHex());
    }

}
