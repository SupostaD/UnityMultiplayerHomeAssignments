using Fusion;
using UnityEngine;

public class NetworkRunnerPrefabReferences : MonoBehaviour
{
    [SerializeField] private NetworkRunner runner;
    [SerializeField] private NetworkSceneManagerDefault sceneManager;

    public NetworkRunner Runner => runner;
    public NetworkSceneManagerDefault SceneManager => sceneManager;

    public static NetworkRunnerPrefabReferences Active { get; private set; }

    private void Awake()
    {
        Active = this;

        if (runner == null)
            Debug.LogError("Network Runner reference is not assigned.", this);

        if (sceneManager == null)
            Debug.LogError("Network Scene Manager reference is not assigned.", this);
    }

    private void OnDestroy()
    {
        if (Active == this)
            Active = null;
    }
}
