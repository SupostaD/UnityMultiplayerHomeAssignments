using Fusion;
using UnityEngine;

public static class NetworkObjectBehaviourReferences
{
    public static bool TryGet<T>(NetworkObject networkObject, out T result)
        where T : NetworkBehaviour
    {
        result = null;

        if (networkObject == null || networkObject.NetworkedBehaviours == null)
            return false;

        foreach (NetworkBehaviour behaviour in networkObject.NetworkedBehaviours)
        {
            if (behaviour is T typedBehaviour)
            {
                result = typedBehaviour;
                return true;
            }
        }

        return false;
    }

    public static T GetRequired<T>(
        NetworkObject networkObject,
        Object logContext = null)
        where T : NetworkBehaviour
    {
        if (TryGet(networkObject, out T result))
            return result;

        string objectName = networkObject != null
            ? networkObject.name
            : "<null NetworkObject>";

        Debug.LogError(
            $"{objectName} has no baked {typeof(T).Name} network behaviour.",
            logContext != null ? logContext : networkObject
        );

        return null;
    }
}
