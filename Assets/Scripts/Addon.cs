using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class Addon
{
    public Addon() { }

    public string Type;
    public string Name;
    public Sprite Icon;
    public string Path;
    public string Version;


    public static List<Addon> Addons = new List<Addon>();

    public static void RegisterAddon(Addon _addon)
    {
        Debug.Log("Registering addon: " + _addon.Type + " " + _addon.Name);
        Addons.Add(_addon);
    }

    public static void GatherAddons()
    {
        foreach (string addonTypeDirectoryPath in Directory.GetDirectories(Application.dataPath + "/game/addons/"))
        {
            foreach (string addonDirectoryPath in Directory.GetDirectories(addonTypeDirectoryPath))
            {
                string infoFilePath = addonDirectoryPath + "/info.txt";

                Addon addon = ParseInfoFile(infoFilePath);
                if (addon != null) RegisterAddon(addon);
            }
        }
    }

    static Addon ParseInfoFile(string _infoFilePath)
    {
        Debug.Log(_infoFilePath);
        if (!File.Exists(_infoFilePath)) return null;
        Debug.Log("file exists");

        Addon newAddon = new Addon();
        string contents = File.ReadAllText(_infoFilePath);

        newAddon.Path = Directory.GetParent(_infoFilePath).FullName;

        contents = contents.Replace("\n", "");
        contents = contents.Replace("\r", "");

        // [Type]Gamemode
        foreach (var entry in contents.Split('['))
        {
            if (entry == "") continue;

            // Type]Gamemode
            var splitentry = entry.Split(']');

            var tag = splitentry[0];
            var value = splitentry[1];

            switch (tag.ToLower())
            {
                case "type":
                    newAddon.Type = value;
                    break;
                case "name":
                    newAddon.Name = value;
                    break;
                case "version":
                    newAddon.Version = value;
                    break;
            }
        }

        string iconPath = newAddon.Path + "/icon.png";
        if (File.Exists(iconPath))
        {
            newAddon.Icon = MainController.LoadSprite(iconPath);
        }

        return newAddon;

    }
}