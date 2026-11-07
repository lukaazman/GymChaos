using System;

/// <summary>
/// A guest whose room closed because the host went down. It joins the same
/// code again, over and over with a growing pause, until it is back in the
/// room or its time is up. The relay keeps the code for the host for a while and
/// answers "host down" until the host is back; "character in use" is also
/// retried because the old socket may not be gone yet.
/// </summary>
public sealed class GymNetReconnect
{
    public const float DefaultLimitSeconds = 90f;
    /// <summary>Verification: when above 0, replaces the limit of every reconnect created after it is set.</summary>
    public static float LimitOverrideSeconds;
    private const float FirstPauseSeconds = 1f;
    private const float LongestPauseSeconds = 8f;
    private const float AttemptTimeoutSeconds = 12f;

    private readonly GymNetClient client;
    private readonly string url;
    private readonly string code;
    private readonly string character;
    private readonly float limit;
    private float startedAt;
    private float nextAttemptAt;
    private float attemptStartedAt;
    private float pause = FirstPauseSeconds;
    private bool attemptRunning;

    public bool Active { get; private set; }
    public bool GaveUp { get; private set; }
    public string GiveUpReason { get; private set; }
    public int Attempts { get; private set; }
    public float Elapsed(float now) => Active ? now - startedAt : 0f;

    /// <summary>The guest is back in the room.</summary>
    public event Action Reconnected;

    public GymNetReconnect(GymNetClient client, string url, string code, string character, float limitSeconds = DefaultLimitSeconds)
    {
        this.client = client;
        this.url = url;
        this.code = code;
        this.character = character;
        limit = LimitOverrideSeconds > 0f ? LimitOverrideSeconds : limitSeconds;
    }

    public void Begin(float now)
    {
        Active = true;
        GaveUp = false;
        GiveUpReason = null;
        Attempts = 0;
        startedAt = now;
        nextAttemptAt = now;
        pause = FirstPauseSeconds;
        attemptRunning = false;
    }

    public void Stop() => Active = false;

    /// <summary>Call every frame. Starts by itself when the room closes under a guest that was in it.</summary>
    public void Tick(float now)
    {
        if (!Active)
        {
            if (!GaveUp && client.CurrentPhase == GymNetClient.Phase.Closed) Begin(now);
            return;
        }
        if (attemptRunning)
        {
            if (client.CurrentPhase == GymNetClient.Phase.Lobby)
            {
                Active = false;
                attemptRunning = false;
                Reconnected?.Invoke();
                return;
            }
            bool failed = client.CurrentPhase == GymNetClient.Phase.Failed || client.CurrentPhase == GymNetClient.Phase.Closed ||
                client.CurrentPhase == GymNetClient.Phase.Idle;
            if (failed || now - attemptStartedAt > AttemptTimeoutSeconds)
            {
                string reason = client.Error;
                attemptRunning = false;
                client.Disconnect();
                if (now - startedAt >= limit) { GiveUp(string.IsNullOrEmpty(reason) ? "the host did not come back" : reason); return; }
                nextAttemptAt = now + pause;
                pause = Math.Min(pause * 2f, LongestPauseSeconds);
            }
            return;
        }
        if (now - startedAt >= limit)
        {
            GiveUp("the host did not come back");
            return;
        }
        if (now < nextAttemptAt) return;
        Attempts++;
        attemptRunning = true;
        attemptStartedAt = now;
        client.Join(url, code, character);
    }

    private void GiveUp(string reason)
    {
        Active = false;
        GaveUp = true;
        GiveUpReason = reason;
    }
}
