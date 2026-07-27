using UnityEngine;

public class GameAmbientStarter : MonoBehaviour
{
    private void Start()
    {
        GameSoundManager.Instance?.PlayGameAmbient();
    }
}