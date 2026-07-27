using UnityEngine;

public class MainMenuMusicStarter : MonoBehaviour
{
    private void Start()
    {
        GameSoundManager.Instance?.PlayMainMenuTheme();
    }
}