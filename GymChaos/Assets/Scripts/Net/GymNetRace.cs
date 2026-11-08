using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Co-op city race over the room. The host decides and simulates: a guest at the
/// RACE sign only asks ('Rq'), the host sees a popup and answers (deny 'Rd', or the
/// race starts: 'Rs' to every guest). During the race each player drives their own
/// kart and reports it ('Rk'); the host sends race state ('Rt') and results ('Re'),
/// and ends the race for everyone ('Rx'). All messages start with 'R'.
/// </summary>
public static class GymNetRace
{
    public const char Tag = 'R';
    public const float WaitTimeoutSeconds = 30f;
    public const int MaxPending = GymNetClient.MaxPlayers - 1;
    /// <summary>A guest may ask again this long after its last request (the host enforces it too).</summary>
    public static float RequestCooldownSeconds = 8f;
    /// <summary>Verification: a race started by accepting a request drives the host kart itself.</summary>
    public static bool AutopilotForVerification;

    /// <summary>A guest's side of "ask the host": at most one request in flight, a cooldown after a no.</summary>
    public sealed class GuestRequest
    {
        public enum State { None, Waiting, Cooldown }

        private readonly Action<string> send;
        private float until;

        public GuestRequest(Action<string> send) => this.send = send;

        public State Current { get; private set; }

        public bool TryRequest(float now)
        {
            Tick(now);
            if (Current != State.None) return false;
            Current = State.Waiting;
            until = now + WaitTimeoutSeconds;
            send("Rq");
            return true;
        }

        /// <summary>The host said no (or did not answer in time).</summary>
        public void Denied(float now)
        {
            if (Current != State.Waiting) return;
            Current = State.Cooldown;
            until = now + RequestCooldownSeconds;
        }

        /// <summary>The race started, or the host left: back to the default state at once.</summary>
        public void Clear() => Current = State.None;

        public void Tick(float now)
        {
            if (Current == State.None || now < until) return;
            if (Current == State.Waiting) Denied(now);
            else Current = State.None;
        }
    }

    public static readonly GuestRequest Guest = new GuestRequest(body => GymNetSession.Instance?.Client.Send(0, body));

    private static readonly List<int> pending = new List<int>(MaxPending);
    private static readonly Dictionary<int, float> lastRequest = new Dictionary<int, float>(MaxPending);

    /// <summary>Host: guests waiting for an answer, oldest first.</summary>
    public static IReadOnlyList<int> Pending => pending;
    public static bool PopupVisible => pending.Count > 0;
    public static int PopupRequester => pending.Count > 0 ? pending[0] : -1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void Reset()
    {
        pending.Clear();
        lastRequest.Clear();
        Guest.Clear();
        AutopilotForVerification = false;
    }

    /// <summary>Host: everybody who may race now (a pose arrived, not dead), by id, at most three.</summary>
    public static void CollectEligible(GymNetSession session, List<int> ids)
    {
        ids.Clear();
        for (int i = 0; i < session.Client.Peers.Count && ids.Count < MaxPending; i++)
        {
            int id = session.Client.Peers[i];
            if (id != 0 && session.TryGetAvatar(id, out GymRemoteAvatar avatar) && avatar != null && avatar.PoseCount > 0 && !avatar.IsCorpse)
            {
                ids.Add(id);
            }
        }
        ids.Sort();
    }

    public static void ClearPending() => pending.Clear();

    /// <summary>A player left the room.</summary>
    public static void PeerLeft(int id, bool localIsHost)
    {
        pending.Remove(id);
        lastRequest.Remove(id);
        if (localIsHost) GymRaceDirector.Instance?.NetPlayerLeft(id);
        else if (id == 0) GymRaceDirector.Instance?.NetHostLeft();
    }

    /// <summary>True when the message was a race message.</summary>
    public static bool Handle(GymNetSession session, int from, string body, float now)
    {
        if (string.IsNullOrEmpty(body) || body[0] != Tag) return false;
        if (body.Length < 2) return true;
        GymRaceDirector director = GymRaceDirector.Instance;
        char verb = body[1];
        if (session.Client.IsHost)
        {
            switch (verb)
            {
                case 'q': Request(session, director, from, now); break;
                case 'r': director?.NetReady(from); break;
                case 'k': director?.NetKart(from, body); break;
                case 'l': director?.NetPlayerLeft(from); break;
            }
        }
        else if (from == 0)
        {
            switch (verb)
            {
                case 'd': Guest.Denied(now); Debug.Log("GYMCHAOS_NET_RACE_DENIED"); break;
                case 's': director?.NetGuestStart(session, body); break;
                default: director?.NetGuestMessage(verb, body); break;
            }
        }
        return true;
    }

    private static void Request(GymNetSession session, GymRaceDirector director, int from, float now)
    {
        if (director == null || director.CurrentPhase != GymRaceDirector.Phase.Idle) { Reply(session, from, "race running"); return; }
        if (pending.Contains(from)) return;
        if (lastRequest.TryGetValue(from, out float last) && now - last < RequestCooldownSeconds) { Reply(session, from, "ask again later"); return; }
        if (pending.Count >= MaxPending) { Reply(session, from, "host busy"); return; }
        lastRequest[from] = now;
        pending.Add(from);
        Debug.Log("GYMCHAOS_NET_RACE_REQUEST from=" + from);
    }

    private static void Reply(GymNetSession session, int to, string reason) => session.Client.Send(to, "Rd|" + reason);

    /// <summary>Host: start the race for everybody (the oldest request is answered by it).</summary>
    public static bool Accept()
    {
        GymNetSession session = GymNetSession.Instance;
        GymRaceDirector director = GymRaceDirector.Instance;
        if (pending.Count == 0 || session == null || director == null) return false;
        int id = pending[0];
        var racing = new List<int>(MaxPending);
        CollectEligible(session, racing);
        if (director.TryStartFromSign(null))
        {
            // Waiting guests race; one that cannot (no pose yet, dead) gets an answer instead of waiting out the timeout.
            for (int i = 0; i < pending.Count; i++) if (!racing.Contains(pending[i])) Reply(session, pending[i], "not ready");
            pending.Clear();
            Debug.Log("GYMCHAOS_NET_RACE_ACCEPTED from=" + id);
            return true;
        }
        string blocked = director.BlockReasonForVerification;
        pending.RemoveAt(0);
        lastRequest[id] = Time.unscaledTime;
        Reply(session, id, string.IsNullOrEmpty(blocked) ? "host cannot race now" : blocked.ToLowerInvariant());
        return false;
    }

    public static void Deny()
    {
        GymNetSession session = GymNetSession.Instance;
        if (pending.Count == 0 || session == null) return;
        int id = pending[0];
        pending.RemoveAt(0);
        lastRequest[id] = Time.unscaledTime;
        Reply(session, id, "host said no");
        Debug.Log("GYMCHAOS_NET_RACE_DENY to=" + id);
    }

    // ------------------------------------------------------------- kart pose

    public static string FormatKart(int round, Vector3 position, float yaw, float speed)
    {
        FormattableString text = $"Rk|{round}|{position.x:F2};{position.y:F2};{position.z:F2};{yaw:F1};{speed:F1}";
        return text.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Parses "Rk|round|x;y;z;yaw;speed"; false for anything malformed or absurd.</summary>
    public static bool TryParseKart(string body, out int round, out Vector3 position, out float yaw, out float speed)
    {
        round = 0;
        position = default;
        yaw = speed = 0f;
        if (body == null || body.Length > 120 || !body.StartsWith("Rk|", StringComparison.Ordinal)) return false;
        string[] part = body.Substring(3).Split('|');
        if (part.Length != 2 || !int.TryParse(part[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out round)) return false;
        string[] v = part[1].Split(';');
        if (v.Length != 5) return false;
        float[] f = new float[5];
        for (int i = 0; i < 5; i++)
        {
            if (!float.TryParse(v[i], NumberStyles.Float, CultureInfo.InvariantCulture, out f[i]) || float.IsNaN(f[i]) || Mathf.Abs(f[i]) > 20000f) return false;
        }
        position = new Vector3(f[0], f[1], f[2]);
        yaw = f[3];
        speed = Mathf.Clamp(f[4], -60f, 120f);
        return true;
    }
}

/// <summary>
/// The co-op race UI: the host's request popup (Y accepts, N denies, or click) and the
/// guest's "waiting for the host" line. uGUI on <see cref="PersonaMenuStyle"/>.
/// </summary>
public sealed class GymNetRaceUi : MonoBehaviour
{
    private Canvas canvas;
    private GameObject popup;
    private Text popupTitle;
    private GameObject waiting;
    private int shownRequester = -2;

    public bool PopupActive => popup != null && popup.activeSelf;
    public bool WaitingActive => waiting != null && waiting.activeSelf;
    public string PopupText => popupTitle != null ? popupTitle.text : null;

    private void Build()
    {
        GameObject canvasObject = new GameObject("Net Race UI", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasObject.transform.SetParent(transform, false);
        canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 70;
        GymRuntimeSettings.ConfigureBalancedCanvasScaler(canvasObject.GetComponent<CanvasScaler>());
        Font body = PersonaMenuStyle.LoadButtonFont(PersonaMenuStyle.BuiltinFont);
        Font title = PersonaMenuStyle.LoadTitleFont(PersonaMenuStyle.BuiltinFont);

        // Art-space placement is relative to the screen: every part hangs under one full-screen root so they hide together.
        popup = NewRoot("Race Request Root", canvas.transform);
        Transform panel = popup.transform;
        PersonaMenuStyle.CreatePanel(panel, "Race Request", 960f, 250f, 900f, 260f, -1f,
            new Color(0.02f, 0.05f, 0.12f, 0.92f), PersonaMenuStyle.Accent);
        popupTitle = PersonaMenuStyle.CreateLabel(panel, "Race Request Title", title, string.Empty, 960f, 205f, 820f, 90f, 60,
            PersonaMenuStyle.Ink, TextAnchor.MiddleCenter);
        PersonaMenuStyle.CreateLabel(panel, "Race Request Hint", body, "Y ACCEPT   N DENY", 960f, 275f, 820f, 40f, 34,
            PersonaMenuStyle.Muted, TextAnchor.MiddleCenter);
        PersonaMenuStyle.CreateButton(panel, "Accept", body, "ACCEPT", 780f, 335f, 300f, 64f, 40, -1f, () => GymNetRace.Accept());
        PersonaMenuStyle.CreateButton(panel, "Deny", body, "DENY", 1140f, 335f, 300f, 64f, 40, -1f, GymNetRace.Deny);
        popup.SetActive(false);

        waiting = NewRoot("Race Waiting Root", canvas.transform);
        Transform wait = waiting.transform;
        PersonaMenuStyle.CreatePanel(wait, "Race Waiting", 960f, 250f, 900f, 150f, -1f,
            new Color(0.02f, 0.05f, 0.12f, 0.92f), PersonaMenuStyle.Accent);
        PersonaMenuStyle.CreateLabel(wait, "Race Waiting Text", title, "WAITING FOR HOST", 960f, 235f, 820f, 80f, 60,
            PersonaMenuStyle.Ink, TextAnchor.MiddleCenter);
        PersonaMenuStyle.CreateLabel(wait, "Race Waiting Sub", body, "RACE REQUEST SENT", 960f, 290f, 820f, 40f, 34,
            PersonaMenuStyle.Muted, TextAnchor.MiddleCenter);
        waiting.SetActive(false);
    }

    private static GameObject NewRoot(string name, Transform parent)
    {
        GameObject root = new GameObject(name, typeof(RectTransform));
        root.transform.SetParent(parent, false);
        GymShared.Stretch(root.GetComponent<RectTransform>());
        return root;
    }

    private void Update()
    {
        bool request = GymNetRace.PopupVisible && GymNetSession.InRoom && !GymRaceDirector.IsRaceActive;
        bool wait = GymNetRace.Guest.Current == GymNetRace.GuestRequest.State.Waiting && GymNetSession.InRoom;
        if (canvas == null)
        {
            if (!request && !wait) return;
            Build();
        }
        if (request != popup.activeSelf) popup.SetActive(request);
        if (wait != waiting.activeSelf) waiting.SetActive(wait);
        if (request)
        {
            int id = GymNetRace.PopupRequester;
            if (id != shownRequester)
            {
                shownRequester = id;
                popupTitle.text = "PLAYER " + (id + 1) + " WANTS TO RACE";
                GymRuntimeSettings.EnsureEventSystem();
            }
            ReadKeys();
        }
        else shownRequester = -2;
    }

    private static void ReadKeys()
    {
#if ENABLE_INPUT_SYSTEM
        UnityEngine.InputSystem.Keyboard keyboard = UnityEngine.InputSystem.Keyboard.current;
        if (keyboard == null) return;
        if (keyboard.yKey.wasPressedThisFrame) GymNetRace.Accept();
        else if (keyboard.nKey.wasPressedThisFrame) GymNetRace.Deny();
#else
        if (Input.GetKeyDown(KeyCode.Y)) GymNetRace.Accept();
        else if (Input.GetKeyDown(KeyCode.N)) GymNetRace.Deny();
#endif
    }
}
