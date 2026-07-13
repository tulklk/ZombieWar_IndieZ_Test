using UnityEngine;
using UnityEngine.UI;

/// <summary>Plays AudioManager's shared button-click SFX whenever this Button is clicked.</summary>
[RequireComponent(typeof(Button))]
public class UIButtonClickSfx : MonoBehaviour
{
    private void Awake()
    {
        GetComponent<Button>().onClick.AddListener(PlayClickSfx);
    }

    private void PlayClickSfx()
    {
        AudioManager.Instance?.PlayButtonClickSfx();
    }
}
