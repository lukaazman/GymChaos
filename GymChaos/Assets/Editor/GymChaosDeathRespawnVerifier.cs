#if UNITY_EDITOR
/// <summary>
/// Player death: YOU DIED, then the Load Game list; loading a save respawns
/// the player alive. Logs GYMCHAOS_DEATH_LOAD_LIST_OK and GYMCHAOS_DEATH_RESPAWN_OK.
/// </summary>
public static class GymChaosDeathRespawnVerifier
{
    public static void Run() => GymChaosClassSessionPlay.Begin("death");
}
#endif
