using Fusion;

public struct RaceInputData : INetworkInput
{
    public float Steering;
    public float Throttle;
    public float Brake;
    public NetworkButtons Buttons;
}
