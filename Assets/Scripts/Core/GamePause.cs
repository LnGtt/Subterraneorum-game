using UnityEngine;

public static class GamePause
{
    private static object owner;

    public static bool IsPaused => owner != null;
    public static object Owner => owner;

    public static bool TryAcquire(object requester)
    {
        if (requester == null) return false;
        if (owner != null) return ReferenceEquals(owner, requester);

        owner = requester;
        Time.timeScale = 0f;
        return true;
    }

    public static void Release(object requester)
    {
        if (owner == null || !ReferenceEquals(owner, requester)) return;

        owner = null;
        Time.timeScale = 1f;
    }

    public static void ForceRelease()
    {
        owner = null;
        Time.timeScale = 1f;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        owner = null;
        Time.timeScale = 1f;
    }
}
