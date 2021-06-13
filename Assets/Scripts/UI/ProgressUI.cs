using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;

public class ProgressUI : MonoBehaviour
{
    public static ProgressUI Instance;

    [SerializeField] private Transform progressPanel;

    [SerializeField] private Slider progressBar;
    [SerializeField] private TextMeshProUGUI messageLabel;
    [SerializeField] private TextMeshProUGUI infoLabel;

    private void Start()
    {
        Instance = this;
    }

    public static void SetMessage(string _message, string _info = "")
    {
        Instance.messageLabel.text = _message;
        SetInfo(_info);
    }

    public static void SetInfo(string _info)
    {
        Instance.infoLabel.text = _info;
    }

    public static void SetProgess(float _progress)
    {
        //Debug.Log("Progress set : " + _progress);
        Instance.progressBar.value = _progress;
    }

    public static void Show()
    {
        Instance.progressPanel.AnimatedUIOpen();
    }

    public static void Hide()
    {
        Instance.progressPanel.AnimatedUIClose();
    }
}
