using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
class ScreenRef
{
    public string Name = "";
    public UIScreen Screen = null;

    public ScreenRef(string _name, UIScreen _screen)
    {
        Name = _name;
        Screen = _screen;
    }
}

public class ScreenSwapper : MonoBehaviour
{
    [SerializeField] List<ScreenRef> screens;
    Dictionary<string, UIScreen> screensDictionary = new Dictionary<string, UIScreen>();

    UIScreen currentScreen = null;

    public void Awake()
    {
        BuildScreenDictionary();
    }

    void BuildScreenDictionary()
    {
        foreach (var screenRef in screens)
        {
            screensDictionary.Add(screenRef.Name, screenRef.Screen);
        }
    }

    public void SwitchScreen(string _name, bool _instant = false)
    {
        if (screensDictionary.ContainsKey(_name))
        {
            if (currentScreen != null) currentScreen.Hide(_instant);
            screensDictionary[_name].Show(_instant);
            currentScreen = screensDictionary[_name];
            return;
        }

        Debug.LogError(_name + " is not a registered screen on this ScreenSwapper");
    }
    public void SwitchScreen(string _name) { SwitchScreen(_name, false); }


    public void HideCurrent()
    {
        if (currentScreen != null) currentScreen.Hide();
    }
}

