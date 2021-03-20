using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using UnityEngine.Events;
using DG.Tweening;

public class UIReference
{
    public Image image;
    public TextMeshProUGUI label;
    public Button button;
}


public class UIScreen : MonoBehaviour
{
    public const float animationTime = 0.3f;

    [SerializeField] bool hideOnStart = false;
    [SerializeField] bool disableOnHide = true;

    [SerializeField] List<UIReference> references;

    public UnityEvent OnShow = new UnityEvent();
    public UnityEvent OnShowFinished = new UnityEvent();

    public UnityEvent OnHide = new UnityEvent();
    public UnityEvent OnHideFinished = new UnityEvent();

    /// <summary>
    /// Whether or not this is in the full shown position
    /// </summary>
    public bool isVisible { get; private set; }

    public void Start()
    {
        if (hideOnStart) Hide(true);
    }

    public void Show(bool _instant = false)
    {
        gameObject.SetActive(true);

        OnShow.Invoke();
        transform.DOKill();

        if (!_instant)
        {
            transform.DOScale(Vector3.one, animationTime).onComplete = ShowFinished;
        }
        else
        {
            transform.localScale = Vector3.one;
            ShowFinished();
        }
    }

    void ShowFinished()
    {
        // set true just in case?
        gameObject.SetActive(true);
        isVisible = true;
        OnShowFinished.Invoke();
    }

    public void Hide(bool _instant = false)
    {
        isVisible = false;

        OnHide.Invoke();
        transform.DOKill();

        if (!_instant)
        {
            transform.DOScale(Vector3.zero, animationTime).onComplete = HideFinished;
        }
        else
        {
            transform.localScale = Vector3.zero;
            HideFinished();
        }
    }

    void HideFinished()
    {
        OnHideFinished.Invoke();
        if (disableOnHide) gameObject.SetActive(false);
    }
}
