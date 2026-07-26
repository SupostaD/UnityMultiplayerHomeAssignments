using Fusion;
using UnityEngine;

public struct HexBallInputData : INetworkInput
{
    public Vector2 Move;
    public NetworkButtons Buttons;
}

public enum HexBallInputButton
{
    Jump = 0,
    Dash = 1
}
