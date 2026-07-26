using UnityEngine;

public class BallFallVFX : MonoBehaviour
{
    [SerializeField] private ParticleSystem abyssExplosionPrefab;

    public void PlayAbyssExplosion(Vector3 worldPosition)
    {
        if (abyssExplosionPrefab == null)
            return;

        ParticleSystem instance = Instantiate(
            abyssExplosionPrefab,
            worldPosition,
            Quaternion.identity
        );

        instance.Play();

        Destroy(instance.gameObject, 3f);
    }
}