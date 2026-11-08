using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

/// <summary>
/// Co-op race (see <see cref="GymNetRace"/>). The host simulates the AI karts, counts laps and decides
/// places; every player drives their own kart and reports its pose. Guests build the same track and
/// grid locally, drive their own kart, and show the AI karts and the other players as puppets that
/// follow the host's state. Extra players take grid slots after the six solo racers.
/// </summary>
public sealed partial class GymRaceDirector
{
    public enum NetRoleKind { None, Host, Guest }

    private const float NetStateInterval = 0.1f;
    private const float NetKartInterval = 1f / 12f;
    private const float NetReadyTimeout = 25f;
    private const float NetHostFinishGrace = 25f;
    private const string CountdownHint = "W GAS   S BRAKE   A/D STEER   SPACE / RMB DRIFT   SHIFT / LMB BOOST   Q LOOK BACK   R RESPAWN";

    private static readonly Color[] NetColors =
    {
        new Color(0.85f, 0.55f, 1f), new Color(0.4f, 1f, 0.85f), new Color(1f, 0.65f, 0.35f)
    };

    private NetRoleKind netRole;
    private readonly List<int> netIds = new List<int>(GymNetRace.MaxPending);
    // Ids in grid order as sent with 'Rs': the roster never changes after the start, so slots match on every machine.
    private readonly List<int> netRosterIds = new List<int>(GymNetRace.MaxPending);
    private readonly HashSet<int> netReady = new HashSet<int>();
    private readonly Dictionary<int, float> netKartAt = new Dictionary<int, float>(GymNetRace.MaxPending);
    private readonly StringBuilder netText = new StringBuilder(512);
    private int netRound;
    private int netDifficulty;
    private int netRematchDifficulty;
    private bool netCountdownSignal;
    private bool netGoSignal;
    private bool netEnded;
    private bool netRematch;
    private bool netResultsReceived;
    private bool netGuestFinishHandled;
    private bool netWaitLocked;
    private string netState;
    private string netResults;
    private float netNextSend;
    private float netNextKart;

    public NetRoleKind NetRole => netRole;
    public int NetRound => netRound;
    public IReadOnlyList<int> NetPlayerIds => netIds;

    /// <summary>True for a driver seated in one of this race's cars (not a gym fighter the net layer should mirror).</summary>
    public static bool IsRaceDriver(EnemyFighter fighter) =>
        Instance != null && fighter != null && fighter.transform.IsChildOf(Instance.transform);

    private static bool NetIsGuestInRoom()
    {
        GymNetSession session = GymNetSession.Instance;
        return session != null && GymNetSession.InRoom && !session.Client.IsHost;
    }

    private void NetSendTo(int id, string message)
    {
        GymNetSession session = GymNetSession.Instance;
        if (session != null) session.Client.Send(id, message);
    }

    private void NetSendAll(string message)
    {
        for (int i = 0; i < netIds.Count; i++) NetSendTo(netIds[i], message);
    }

    private void NetResetSignals()
    {
        netCountdownSignal = netGoSignal = netEnded = netRematch = netResultsReceived = netGuestFinishHandled = false;
        netState = netResults = null;
        netNextSend = netNextKart = 0f;
    }

    // ---------------------------------------------------------------- host

    /// <summary>Host with guests in the room: they all race. Otherwise the race is solo as before.</summary>
    private void NetPrepareHost()
    {
        netRole = NetRoleKind.None;
        netIds.Clear();
        netRosterIds.Clear();
        netReady.Clear();
        netKartAt.Clear();
        netRound = 0;
        NetResetSignals();
        GymNetSession session = GymNetSession.Instance;
        if (session == null || !GymNetSession.InRoom || !session.Client.IsHost) return;
        GymNetRace.CollectEligible(session, netIds);
        if (netIds.Count > 0) netRole = NetRoleKind.Host;
    }

    private void NetHostBegin()
    {
        if (netRole != NetRoleKind.Host) return;
        GymNetRace.ClearPending();
        netRosterIds.Clear();
        netRosterIds.AddRange(netIds);
        string ids = string.Join(",", netIds);
        NetSendAll("Rs|" + difficulty + "|" + ids);
        NetSetAvatarsVisible(false);
        Debug.Log("GYMCHAOS_NET_RACE_START guests=" + ids + " difficulty=" + difficulty);
    }

    /// <summary>The host backed out of the difficulty screen: guests waiting for the answer get a no.</summary>
    private void NetHostCancelled()
    {
        if (netRole != NetRoleKind.Host) return;
        NetSendAll("Rd|host cancelled");
        GymNetRace.ClearPending();
        netRole = NetRoleKind.None;
    }

    private IEnumerator NetWaitForGuests()
    {
        float until = Time.time + NetReadyTimeout;
        bool shown = false;
        while (Time.time < until && !NetAllReady())
        {
            if (!shown)
            {
                hud.ShowBanner("WAITING FOR PLAYERS", PersonaMenuStyle.Gold, 600f);
                shown = true;
            }
            yield return null;
        }
        if (shown) hud.ShowBanner(string.Empty, PersonaMenuStyle.Gold, 0f);
        for (int i = netRosterIds.Count - 1; i >= 0; i--)
        {
            int id = netRosterIds[i];
            if (!netReady.Contains(id)) NetDrop(id, netIds.Contains(id));
        }
    }

    private bool NetAllReady()
    {
        for (int i = 0; i < netIds.Count; i++)
        {
            if (!netReady.Contains(netIds[i])) return false;
        }
        return true;
    }

    public void NetReady(int from)
    {
        if (netRole == NetRoleKind.Host && netIds.Contains(from)) netReady.Add(from);
    }

    /// <summary>Host: a guest left the room or the race. Its kart goes; the race goes on for the others.</summary>
    public void NetPlayerLeft(int id) => NetDrop(id, false);

    private void NetDrop(int id, bool notify)
    {
        if (netRole != NetRoleKind.Host) return;
        bool known = netIds.Remove(id);
        netReady.Remove(id);
        netKartAt.Remove(id);
        if (notify) NetSendTo(id, "Rx");
        if (RemoveRacerOf(id)) NetSendAll("Ro|" + id);
        if (known && GymNetSession.Instance != null && GymNetSession.Instance.TryGetAvatar(id, out GymRemoteAvatar avatar) && avatar != null)
        {
            avatar.gameObject.SetActive(true);
        }
    }

    // Takes the kart of a player out of the race (host and guest).
    private bool RemoveRacerOf(int id)
    {
        bool removed = false;
        for (int i = racers.Count - 1; i >= 0; i--)
        {
            GymRaceRacer racer = racers[i];
            if (!racer.RemoteHuman || racer.NetId != id) continue;
            racers.RemoveAt(i);
            ordered.Remove(racer);
            if (racer.GridSlot < cars.Count && cars[racer.GridSlot].Holder != null) Destroy(cars[racer.GridSlot].Holder.gameObject);
            Destroy(racer.gameObject);
            if (hud != null) hud.RacerTotal = racers.Count;
            removed = true;
            Debug.Log("GYMCHAOS_NET_RACE_PLAYER_LEFT id=" + id);
        }
        return removed;
    }

    /// <summary>Host: a guest's kart pose. Wrong round (before a rematch reset) or too frequent ones are dropped.</summary>
    public void NetKart(int from, string body)
    {
        if (netRole != NetRoleKind.Host || !netIds.Contains(from)) return;
        if (!GymNetRace.TryParseKart(body, out int round, out Vector3 position, out float yaw, out float speed) || round != netRound) return;
        float now = Time.unscaledTime;
        if (netKartAt.TryGetValue(from, out float last) && now - last < NetKartInterval * 0.5f) return;
        netKartAt[from] = now;
        for (int i = 0; i < racers.Count; i++)
        {
            if (racers[i].RemoteHuman && racers[i].NetId == from)
            {
                racers[i].Kart.SetRemoteState(position, Quaternion.Euler(0f, yaw, 0f) * Vector3.forward, speed);
                return;
            }
        }
    }

    private void NetHostResults()
    {
        if (netRole != NetRoleKind.Host) return;
        netText.Clear();
        netText.Append("Re");
        for (int i = 0; i < ordered.Count; i++)
        {
            GymRaceRacer racer = ordered[i];
            float best = 0f;
            for (int k = 0; k < racer.LapTimes.Count; k++) best = best <= 0f ? racer.LapTimes[k] : Mathf.Min(best, racer.LapTimes[k]);
            netText.Append('|').Append(racer.GridSlot).Append(';').Append(racer.FinishPlace).Append(';')
                .Append(racer.FinishTime.ToString("F3", CultureInfo.InvariantCulture)).Append(';')
                .Append(best.ToString("F3", CultureInfo.InvariantCulture)).Append(';').Append(racer.Finished ? 1 : 0);
        }
        NetSendAll(netText.ToString());
    }

    private void NetHostRematch()
    {
        if (netRole != NetRoleKind.Host) return;
        netRound++;
        NetSendAll("Ra|" + difficulty + "|" + netRound);
    }

    // ---------------------------------------------------------------- roster

    private void NetAdjustRoster(List<RacerSpec> list)
    {
        if (netRole == NetRoleKind.None) return;
        GymNetSession session = GymNetSession.Instance;
        int myId = session != null ? session.Client.LocalId : -1;
        RacerSpec buggy = list[list.Count - 1];
        if (netRole == NetRoleKind.Guest)
        {
            // Slot 5 is the host's buggy; this machine's own kart is one of the extra slots.
            RacerSpec host = list[list.Count - 1];
            host.Name = "Player 1";
            host.Player = false;
            host.Remote = true;
            host.NetId = 0;
            list[list.Count - 1] = host;
        }
        for (int i = 0; i < netRosterIds.Count; i++)
        {
            bool me = netRole == NetRoleKind.Guest && netRosterIds[i] == myId;
            list.Add(new RacerSpec
            {
                Name = me ? "You" : "Player " + (netRosterIds[i] + 1),
                Player = me,
                Remote = !me,
                NetId = netRosterIds[i],
                Color = me ? Color.white : NetColors[i % NetColors.Length],
                Stats = buggy.Stats,
                Lane = 0f,
                Skill = 1f
            });
        }
    }

    /// <summary>Hide the avatar capsules of racing players: their karts show them.</summary>
    private void NetSetAvatarsVisible(bool visible)
    {
        GymNetSession session = GymNetSession.Instance;
        if (session == null) return;
        if (netRole == NetRoleKind.Guest)
        {
            foreach (GymRemoteAvatar avatar in session.Avatars) if (avatar != null) avatar.gameObject.SetActive(visible);
            return;
        }
        for (int i = 0; i < netIds.Count; i++)
        {
            if (session.TryGetAvatar(netIds[i], out GymRemoteAvatar avatar) && avatar != null) avatar.gameObject.SetActive(visible);
        }
    }

    // ---------------------------------------------------------------- state traffic

    /// <summary>Host: send race state; guest: apply the newest state and report the own kart.</summary>
    private void NetPump()
    {
        if (netRole == NetRoleKind.None || racers.Count == 0) return;
        float now = Time.unscaledTime;
        if (netRole == NetRoleKind.Host)
        {
            if (now < netNextSend) return;
            netNextSend = now + NetStateInterval;
            NetSendAll(EncodeState());
            return;
        }
        ApplyNetState();
        if (now < netNextKart || playerRacer == null || playerRacer.Kart.Body == null) return;
        netNextKart = now + NetKartInterval;
        Vector3 heading = playerRacer.Kart.Heading;
        NetSendTo(0, GymNetRace.FormatKart(netRound, playerRacer.Kart.Body.position, Mathf.Atan2(heading.x, heading.z) * Mathf.Rad2Deg,
            playerRacer.Kart.ForwardSpeed));
    }

    private string EncodeState()
    {
        CultureInfo c = CultureInfo.InvariantCulture;
        netText.Clear();
        netText.Append("Rt|").Append((phase == Phase.Countdown ? 0f : Time.time - raceStartTime).ToString("F2", c))
            .Append('|').Append((int)phase);
        for (int i = 0; i < racers.Count; i++)
        {
            GymRaceRacer racer = racers[i];
            Vector3 position = racer.Kart.Body != null ? racer.Kart.Body.position : racer.transform.position;
            Vector3 heading = racer.Kart.Heading;
            netText.Append('|').Append(racer.GridSlot)
                .Append(';').Append(position.x.ToString("F2", c)).Append(';').Append(position.y.ToString("F2", c)).Append(';').Append(position.z.ToString("F2", c))
                .Append(';').Append((Mathf.Atan2(heading.x, heading.z) * Mathf.Rad2Deg).ToString("F1", c))
                .Append(';').Append(racer.Kart.ForwardSpeed.ToString("F1", c))
                .Append(';').Append(racer.Crossings).Append(';').Append(racer.Finished ? 1 : 0)
                .Append(';').Append(racer.FinishPlace).Append(';').Append(racer.FinishTime.ToString("F2", c));
        }
        return netText.ToString();
    }

    private GymRaceRacer RacerBySlot(int slot)
    {
        for (int i = 0; i < racers.Count; i++)
        {
            if (racers[i].GridSlot == slot) return racers[i];
        }
        return null;
    }

    private void ApplyNetState()
    {
        string state = netState;
        if (state == null) return;
        netState = null;
        CultureInfo c = CultureInfo.InvariantCulture;
        string[] part = state.Split('|');
        if (part.Length < 4 || !float.TryParse(part[1], NumberStyles.Float, c, out float raceTime) ||
            !int.TryParse(part[2], NumberStyles.Integer, c, out int hostPhase)) return;
        if (hostPhase >= (int)Phase.Racing && hostPhase <= (int)Phase.Results && !float.IsNaN(raceTime))
        {
            raceStartTime = Time.time - raceTime;
        }
        for (int i = 3; i < part.Length; i++)
        {
            string[] v = part[i].Split(';');
            if (v.Length != 10 || !int.TryParse(v[0], NumberStyles.Integer, c, out int slot)) continue;
            GymRaceRacer racer = RacerBySlot(slot);
            if (racer == null) continue;
            float[] f = new float[9];
            bool ok = true;
            for (int k = 1; k < 10 && ok; k++)
            {
                ok = float.TryParse(v[k], NumberStyles.Float, c, out f[k - 1]) && !float.IsNaN(f[k - 1]) && Mathf.Abs(f[k - 1]) < 20000f;
            }
            if (!ok) continue;
            if (racer.Kart.RemoteDriven)
            {
                racer.Kart.SetRemoteState(new Vector3(f[0], f[1], f[2]), Quaternion.Euler(0f, f[3], 0f) * Vector3.forward, f[4]);
            }
            racer.ApplyNet(Mathf.RoundToInt(f[5]), f[6] > 0.5f, Mathf.RoundToInt(f[7]), f[8]);
        }
    }

    // ---------------------------------------------------------------- guest

    /// <summary>Guest: the host started a race with this machine in it ("Rs|difficulty|ids").</summary>
    public void NetGuestStart(GymNetSession session, string body)
    {
        GymNetRace.Guest.Clear();
        string[] part = body.Split('|');
        if (part.Length != 3 || !int.TryParse(part[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int level)) return;
        List<int> ids = new List<int>(GymNetRace.MaxPending);
        foreach (string text in part[2].Split(','))
        {
            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id) && id > 0 && !ids.Contains(id) &&
                ids.Count < GymNetRace.MaxPending) ids.Add(id);
        }
        if (!ids.Contains(session.Client.LocalId)) return;
        string blocked = BlockReason();
        if (blocked != null)
        {
            Debug.Log("GYMCHAOS_NET_RACE_DECLINED reason=" + blocked);
            session.Client.Send(0, "Rl");
            return;
        }
        netIds.Clear();
        netIds.AddRange(ids);
        netRosterIds.Clear();
        netRosterIds.AddRange(ids);
        netReady.Clear();
        netRole = NetRoleKind.Guest;
        netDifficulty = Mathf.Clamp(level, 0, DifficultyNames.Length - 1);
        netRound = 0;
        NetResetSignals();
        AutoContinueResults = false;
        NetSetAvatarsVisible(false);
        Debug.Log("GYMCHAOS_NET_RACE_JOINED id=" + session.Client.LocalId + " difficulty=" + netDifficulty);
        StartRun(RaceRoutine(false));
    }

    /// <summary>Guest: countdown ('c'), go ('g'), state ('t'), results ('e'), end ('x') or rematch ('a') from the host.</summary>
    public void NetGuestMessage(char verb, string body)
    {
        if (netRole != NetRoleKind.Guest) return;
        switch (verb)
        {
            case 'c': netCountdownSignal = true; break;
            case 'g': netGoSignal = true; break;
            case 't': netState = body; break;
            case 'e': netResults = body; netResultsReceived = true; break;
            case 'x': netEnded = true; break;
            case 'o':
                if (int.TryParse(body.Substring(Mathf.Min(3, body.Length)), NumberStyles.Integer, CultureInfo.InvariantCulture, out int gone)) RemoveRacerOf(gone);
                break;
            case 'a':
                string[] part = body.Split('|');
                if (part.Length == 3 && int.TryParse(part[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int level) &&
                    int.TryParse(part[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int round))
                {
                    netRematchDifficulty = Mathf.Clamp(level, 0, DifficultyNames.Length - 1);
                    netRound = round;
                    netResults = null;
                    netResultsReceived = false;
                    netRematch = true;
                }
                break;
        }
    }

    /// <summary>The host left the room: a guest race cannot go on.</summary>
    public void NetHostLeft()
    {
        GymNetRace.Guest.Clear();
        if (netRole == NetRoleKind.Guest) netEnded = true;
    }

    private void NetSendReady()
    {
        if (netRole == NetRoleKind.Guest) NetSendTo(0, "Rr");
    }

    private IEnumerator RunRaceGuest()
    {
        phase = Phase.Countdown;
        hud.SetRaceWidgetsVisible(true);
        hud.SetHint(CountdownHint);
        float until = Time.time + 40f;
        while (!netCountdownSignal && !netEnded && Time.time < until)
        {
            UpdateHud();
            yield return null;
        }
        if (!netCountdownSignal)
        {
            Debug.LogWarning("GYMCHAOS_NET_RACE_NO_COUNTDOWN");
            netEnded = true;
            yield break;
        }
        netCountdownSignal = false;
        countdownStart = Time.time;
        firstThrottleAt = -1f;
        for (int i = 0; i < racers.Count; i++) racers[i].ResetProgress(Time.time);
        yield return CountdownSequence();
        float goUntil = Time.time + 2f;
        while (!netGoSignal && Time.time < goUntil)
        {
            UpdateHud();
            yield return null;
        }
        netGoSignal = false;
        raceStartTime = Time.time;
        StartRacing();
        netGuestFinishHandled = false;
        phase = Phase.Racing;
        hud.SetHint(string.Empty);
        while (!netResultsReceived && !netEnded)
        {
            TickRaceGuest();
            yield return null;
        }
    }

    private void TickRaceGuest()
    {
        float now = Time.time;
        float raceTime = now - raceStartTime;
        for (int i = 0; i < racers.Count; i++)
        {
            GymRaceRacer racer = racers[i];
            GymRaceKart kart = racer.Kart;
            if (racer.IsPlayer)
            {
                if (!racer.Finished)
                {
                    GymRaceDriveInput input = GymRaceInput.Read();
                    kart.Input = input;
                    chase.LookBack = input.LookBack;
                    if (input.RespawnPressed) racer.RespawnAtSafePoint();
                }
                else
                {
                    // Cool-down lap after the finish, as in a solo race.
                    GymRaceAIDriver ai = kart.GetComponent<GymRaceAIDriver>();
                    if (ai == null) ai = kart.gameObject.AddComponent<GymRaceAIDriver>();
                    kart.Input = ai.Think(now);
                }
            }
            racer.Tick(raceTime, Time.deltaTime, true);
            if (racer.IsPlayer && racer.NeedsRespawn) racer.RespawnAtSafePoint();
        }
        if (playerRacer.Finished && !netGuestFinishHandled)
        {
            netGuestFinishHandled = true;
            phase = Phase.Finishing;
            playerRacer.Kart.MeterFilling = false;
            GymAudio.Play2D(GymSoundEffect.RaceFinish, 0.9f);
            hud.ShowBanner(playerRacer.FinishPlace == 1 ? "YOU WIN!" : "FINISH! " + GymRaceHud.Ordinal(playerRacer.FinishPlace),
                playerRacer.FinishPlace == 1 ? PersonaMenuStyle.Gold : PersonaMenuStyle.Ink, 3f);
            chase.StartOrbit();
        }
        SortPlaces();
        PlayerLapCues();
        UpdateHud();
    }

    private List<GymRaceHud.ResultRow> NetGuestRows()
    {
        List<GymRaceHud.ResultRow> rows = new List<GymRaceHud.ResultRow>();
        CultureInfo c = CultureInfo.InvariantCulture;
        LastPlayerPlace = 0;
        string[] part = netResults != null ? netResults.Split('|') : new string[0];
        for (int i = 1; i < part.Length; i++)
        {
            string[] v = part[i].Split(';');
            if (v.Length != 5 || !int.TryParse(v[0], NumberStyles.Integer, c, out int slot) ||
                !int.TryParse(v[1], NumberStyles.Integer, c, out int place) ||
                !float.TryParse(v[2], NumberStyles.Float, c, out float finishTime) ||
                !float.TryParse(v[3], NumberStyles.Float, c, out float best) ||
                !int.TryParse(v[4], NumberStyles.Integer, c, out int finished)) continue;
            GymRaceRacer racer = RacerBySlot(slot);
            if (racer == null) continue;
            racer.ApplyNet(racer.Crossings, finished > 0, place, finishTime);
            if (racer == playerRacer)
            {
                LastPlayerPlace = place;
                racer.LapTimes.Clear();
                if (best > 0f) racer.LapTimes.Add(best);
            }
            rows.Add(new GymRaceHud.ResultRow
            {
                Place = place,
                Name = racer.DisplayName,
                Color = racer.Color,
                IsPlayer = racer == playerRacer,
                Time = finishTime,
                BestLap = best,
                Finished = finished > 0
            });
        }
        rows.Sort((a, b) => a.Place.CompareTo(b.Place));
        return rows;
    }

    // ---------------------------------------------------------------- sign and per-frame upkeep

    /// <summary>F at the sign as a guest: ask the host once, then wait for the answer.</summary>
    private bool NetGuestPress()
    {
        string blocked = BlockReason();
        if (blocked != null)
        {
            Debug.Log($"GYMCHAOS_RACE_BLOCKED reason={blocked}");
            GymHud.Active?.PushNotice("Race unavailable: " + blocked.ToLowerInvariant(), 3f, 1);
            GymAudio.Play2D(GymSoundEffect.UiError);
            return false;
        }
        if (!GymNetRace.Guest.TryRequest(Time.unscaledTime))
        {
            GymHud.Active?.PushNotice(GymNetRace.Guest.Current == GymNetRace.GuestRequest.State.Waiting
                ? "Race: waiting for the host" : "Race: ask again in a moment", 2.5f, 1);
            GymAudio.Play2D(GymSoundEffect.UiError);
            return false;
        }
        GymAudio.Play2D(GymSoundEffect.UiConfirm);
        Debug.Log("GYMCHAOS_NET_RACE_ASKED");
        return true;
    }

    private bool NetGuestPrompt(out string prompt)
    {
        prompt = null;
        if (phase != Phase.Idle || !NetIsGuestInRoom()) return false;
        switch (GymNetRace.Guest.Current)
        {
            case GymNetRace.GuestRequest.State.Waiting: prompt = "RACE: WAITING FOR HOST"; break;
            case GymNetRace.GuestRequest.State.Cooldown: prompt = "RACE: ASK AGAIN SOON"; break;
            default:
                string blocked = BlockReason();
                prompt = blocked == null ? "ASK HOST FOR CITY RACE" : "RACE: " + blocked;
                break;
        }
        return true;
    }

    /// <summary>Per frame. True when a guest race was ended here.</summary>
    private bool NetUpdate()
    {
        bool guestRoom = NetIsGuestInRoom();
        GymNetRace.Guest.Tick(Time.unscaledTime);
        if (!guestRoom && GymNetRace.Guest.Current != GymNetRace.GuestRequest.State.None) GymNetRace.Guest.Clear();
        // A waiting guest is taken out of normal play until the answer comes.
        bool waiting = guestRoom && phase == Phase.Idle && GymNetRace.Guest.Current == GymNetRace.GuestRequest.State.Waiting;
        if (phase != Phase.Idle) netWaitLocked = false;
        else if (waiting != netWaitLocked)
        {
            netWaitLocked = waiting;
            if (player != null) player.SetCinematicLock(waiting);
        }
        if (GymRaceSign.Instance != null) GymRaceSign.Instance.SetStatus(waiting ? "WAITING FOR HOST" : null);
        if (netRole == NetRoleKind.Guest && phase != Phase.Idle && (netEnded || !guestRoom))
        {
            runStack.Clear();
            if (player != null) player.SetCursorCaptured(true);
            Debug.Log("GYMCHAOS_NET_RACE_ENDED_BY_HOST");
            AbortRace();
            return true;
        }
        return false;
    }

    private void NetBeforeTeardown()
    {
        GymNetSession session = GymNetSession.Instance;
        if (netRole == NetRoleKind.Host) NetSendAll("Rx");
        else if (netRole == NetRoleKind.Guest && !netEnded && session != null && GymNetSession.InRoom) session.Client.Send(0, "Rl");
        NetSetAvatarsVisible(true);
    }

    private void NetAfterTeardown()
    {
        netRole = NetRoleKind.None;
        netIds.Clear();
        netRosterIds.Clear();
        netReady.Clear();
        netKartAt.Clear();
        NetResetSignals();
    }
}
