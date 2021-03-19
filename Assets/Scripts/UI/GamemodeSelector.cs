using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class GamemodeSelector : MonoBehaviour
{
    [SerializeField] TMP_Dropdown dropdown;

    Addon selectedGamemode
    {
        get
        {
            if (dropdown == null) return null;

            if (Addon.Addons.Count >= dropdown.value + 1)
            {
                return Addon.Addons[dropdown.value];
            }

            return null;
        }
    }

    private void Start()
    {
        Addon.GatherAddons();
        Refresh(Addon.Addons);
    }

    void Refresh(List<Addon> _addons)
    {
        dropdown.ClearOptions();

        List <TMP_Dropdown.OptionData> options = new List<TMP_Dropdown.OptionData>();
        foreach (Addon gm in _addons)
        {

            if (gm.Type.ToLower() == "gamemode")
            {
                TMP_Dropdown.OptionData option = new TMP_Dropdown.OptionData();
                option.image = gm.Icon;
                option.text = gm.Name;
                option.image = gm.Icon;

                options.Add(option);
            }
        }

        dropdown.AddOptions(options);
    }
}
