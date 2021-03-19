using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;


public class ScreenSwapper : MonoBehaviour
{
    [SerializeField] SerializableDictionary<string, UIScreen> screens;

    UIScreen currentScreen = null;


    public void SwitchScreen(string _name, bool _instant = false)
    {
        if (screens.Keys.Contains(_name))
        {
            if (currentScreen != null) currentScreen.Hide(_instant);
            screens[_name].Show(_instant);
            currentScreen = screens[_name];
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

