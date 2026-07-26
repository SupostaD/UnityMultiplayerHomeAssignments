using UnityEngine;

public class BallFallVFX : MonoBehaviour
{
    [SerializeField] private ParticleSystem abyssExplosionPrefab;
    
    [Header("Audio")]
    [SerializeField] private AudioClip abyssExplosionClip;
    [SerializeField, Range(0f, 1f)] private float abyssExplosionVolume = 1f;

    public void PlayAbyssExplosion(Vector3 worldPosition)
    {
        if (abyssExplosionPrefab != null)
        {
            ParticleSystem instance = Instantiate(
                abyssExplosionPrefab,
                worldPosition,
                Quaternion.identity
            );

            instance.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            instance.Play(true);

            Destroy(instance.gameObject, 3f);
        }

        GameSoundManager.Instance?.PlayFallExplosion(worldPosition);
    }
}