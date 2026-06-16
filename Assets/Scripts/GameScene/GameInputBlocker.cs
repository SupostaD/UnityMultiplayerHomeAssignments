public static class GameInputBlocker
{
    public static bool IsGameplayInputBlocked { get; private set; }

    public static void BlockGameplayInput()
    {
        IsGameplayInputBlocked = true;
    }

    public static void UnblockGameplayInput()
    {
        IsGameplayInputBlocked = false;
    }
}