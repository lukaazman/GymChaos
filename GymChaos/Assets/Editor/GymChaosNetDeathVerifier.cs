#if UNITY_EDITOR
/// <summary>
/// Player death in multiplayer in one real game (through the real start menu), against the real relay.
/// As a guest of a scripted host: damage from another player kills (no health floor); the death
/// reaches the others; the death menu opens and the room is not left; LOAD GAME brings the character
/// back from the guest's own save, alive, where it died; the guest never writes the world section.
/// As the host of a scripted guest: the dead guest is a corpse where it fell until it respawns.
/// Logs GYMCHAOS_NET_DEATH_OK. With -Graphics it also writes evidence PNGs.
/// </summary>
public static class GymChaosNetDeathVerifier
{
    public static void Run() => GymChaosClassSessionPlay.Begin("netDeath");
}
#endif
