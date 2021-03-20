using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.Events;
using System;
using System.Text.RegularExpressions;

public class GameConsole : MonoBehaviour
{
    // each Console will tap into this to print the output into that console
    public static UnityEvent<string> OnOutput = new UnityEvent<string>();

    // when we want to run an effect when a command happens we can do Console.OnCommandRun.AddListener(function);
    public static UnityEvent<string> OnCommandRun = new UnityEvent<string>();

    public static void RunCommand(string _command)
    {
        OnCommandRun.Invoke(_command);
    }

    public static void Print(string _output)
    {
        OnOutput.Invoke(_output);
    }




    public TMP_InputField inputField;
    public TMP_InputField outputField;

    // Start is called before the first frame update
    void Start()
    {
        OnOutput.AddListener(OnConsoleOutput);
    }


    public void InputToConsole(string _command)
    {
        inputField.text = "";
        //inputField.textComponent.text = "";
        Print("> " + _command + "\n");
    }

    void OnConsoleOutput(string _output)
    {
        outputField.text += "\n";
        outputField.text += _output;
    }
}