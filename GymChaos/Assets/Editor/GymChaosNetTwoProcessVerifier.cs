#if UNITY_EDITOR
/// <summary>
/// Two real game processes against one real relay (Tools/Invoke-NetTwoProcess.ps1
/// starts the relay and both): the host and the guest each go through the real
/// start menu with a new character. Covers joining by code, the host's world and
/// puppets on the guest, player against player, a guest's death (replicated
/// corpse, death menu, respawn in place), the host's death (same room code after
/// loading its save, the guest reconnecting by itself) and leaving. Needs -Graphics.
/// </summary>
public static class GymChaosNetTwoProcessVerifier
{
    public static void RunHost() => GymChaosClassSessionPlay.Begin("netHost");

    public static void RunGuest() => GymChaosClassSessionPlay.Begin("netGuest");
}
#endif
