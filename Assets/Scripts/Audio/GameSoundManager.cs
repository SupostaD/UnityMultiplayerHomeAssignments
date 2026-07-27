using UnityEngine;

public class GameSoundManager : MonoBehaviour
{
    public static GameSoundManager Instance { get; private set; }

    [Header("Player Sounds")]
    [SerializeField] private AudioClip jumpClip;
    [SerializeField] private AudioClip doubleJumpClip;
    [SerializeField] private AudioClip dashClip;
    [SerializeField] private AudioClip trampolineClip;
    [SerializeField] private AudioClip drainClip;

    [Header("Explosion / Death")]
    [SerializeField] private AudioClip fallExplosionClip;
    [SerializeField] private AudioClip deathExplosionClip;

    [Header("UI / Match")]
    [SerializeField] private AudioClip victoryClip;

    [Header("Music / Ambient")]
    [SerializeField] private AudioSource musicSource;
    [SerializeField] private AudioClip mainMenuTheme;
    [SerializeField] private AudioClip gameAmbientClip;


    [Header("Volume")]
    [SerializeField, Range(0f, 1f)] private float sfxVolume = 1f;
    [SerializeField, Range(0f, 1f)] private float musicVolume = 0.5f;

    [Header("3D Sound Settings")]
    [SerializeField] private float minDistance = 2f;
    [SerializeField] private float maxDistance = 35f;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        if (musicSource != null)
        {
            musicSource.playOnAwake = false;
            musicSource.loop = true;
            musicSource.volume = musicVolume;
            musicSource.spatialBlend = 0f;
        }
    }
    

    public void PlayJump(Vector3 position)
    {
        Play3D(jumpClip, position);
    }

    public void PlayDoubleJump(Vector3 position)
    {
        Play3D(doubleJumpClip, position);
    }

    public void PlayDash(Vector3 position)
    {
        Play3D(dashClip, position);
    }

    public void PlayTrampoline(Vector3 position)
    {
        Play3D(trampolineClip, position);
    }

    public void PlayDrain(Vector3 position)
    {
        Play3D(drainClip, position);
    }

    public void PlayFallExplosion(Vector3 position)
    {
        Play3D(fallExplosionClip, position);
    }

    public void PlayDeathExplosion(Vector3 position)
    {
        Play3D(deathExplosionClip, position);
    }

    public void PlayVictory()
    {
        Play2D(victoryClip);
    }

    public void PlayMainMenuTheme()
    {
        PlayMusic(mainMenuTheme);
    }
    
    public void PlayGameAmbient()
    {
        PlayMusic(gameAmbientClip);
    }

    public void StopMusic()
    {
        if (musicSource != null)
            musicSource.Stop();
    }
    
    private void PlayMusic(AudioClip clip)
    {
        if (musicSource == null || clip == null)
            return;

        if (musicSource.clip == clip && musicSource.isPlaying)
            return;

        musicSource.clip = clip;
        musicSource.volume = musicVolume;
        musicSource.loop = true;
        musicSource.spatialBlend = 0f;
        musicSource.Play();
    }

    private void Play2D(AudioClip clip)
    {
        if (clip == null)
            return;

        AudioSource.PlayClipAtPoint(clip, Camera.main != null ? Camera.main.transform.position : Vector3.zero, sfxVolume);
    }

    private void Play3D(AudioClip clip, Vector3 position)
    {
        if (clip == null)
            return;

        GameObject soundObject = new GameObject("OneShotSound");
        soundObject.transform.position = position;

        AudioSource source = soundObject.AddComponent<AudioSource>();
        source.clip = clip;
        source.volume = sfxVolume;
        source.spatialBlend = 1f;
        source.minDistance = minDistance;
        source.maxDistance = maxDistance;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.dopplerLevel = 0f;

        source.Play();

        Destroy(soundObject, clip.length + 0.1f);
    }
}