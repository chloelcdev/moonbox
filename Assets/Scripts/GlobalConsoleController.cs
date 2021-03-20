using DG.Tweening;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class GlobalConsoleController : MonoBehaviour
{
    public float ScreenCoverage = 0.7f;
    public GameConsole Mainconsole;

    public static GlobalConsoleController Instance;

    /// <summary>
    /// Whether or not this is in the full shown position
    /// </summary>
    public bool isVisible { get; private set; }

    

    void Start()
    {
        Instance = this;
    }

    private void Update()
    {
        // toggle console
        if (Keyboard.current.backquoteKey.wasPressedThisFrame)
        {
            Instance.Mainconsole.inputField.onValueChanged.AddListener(RemoveBackQuotes);
                

            if (isVisible)
            {
                Hide();
            }
            else
            {
                Show();
            }
        }
    }

    private void RemoveBackQuotes(string _newValue)
    {
        Instance.Mainconsole.inputField.text = Instance.Mainconsole.inputField.text.Replace("`", "");
    }

    public static void Show(bool _instant = false)
    {
        Instance.Mainconsole.gameObject.SetActive(true);

        var consoleHeight = Screen.height / Instance.ScreenCoverage;

        var rt = Instance.Mainconsole.GetComponent<RectTransform>();
        rt.DOKill();

        if (!_instant)
        {
            

            var tween = rt.DOSizeDelta(new Vector2(rt.sizeDelta.x, consoleHeight), UIScreen.animationTime);
            tween.onComplete = ShowFinished;
        }
        else
        {
            rt.SetBottom(consoleHeight);
            ShowFinished();
        }
    }

    static void ShowFinished()
    {
        // set true just in case?
        Instance.Mainconsole.gameObject.SetActive(true);
        Instance.isVisible = true;
    }

    public static void Hide(bool _instant = false)
    {

        Instance.isVisible = false;

        var rt = Instance.Mainconsole.GetComponent<RectTransform>();
        rt.DOKill();

        if (!_instant)
        {
            var tween = rt.DOSizeDelta(new Vector2(rt.sizeDelta.x, 0), UIScreen.animationTime);
            tween.onComplete = HideFinished;
        }
        else
        {
            rt.SetBottom(0);
            HideFinished();
        }
    }

    static void HideFinished()
    {
        Instance.Mainconsole.gameObject.SetActive(false);
    }
}
