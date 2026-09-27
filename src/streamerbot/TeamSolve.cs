using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// Paste this entire file into ONE cached Execute C# Code sub-action.
// All trigger wrappers must call that same action on a blocking queue.
public partial class CPHInline
{
    private TeamSolve.Runtime runtime;
    private const int ServerIndex = 0;

    public void Init()
    {
        var log = new TeamSolve.Diagnostics(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TeamSolveLogs"),
            message => CPH.LogDebug(message));
        runtime = new TeamSolve.Runtime(
            new TeamSolve.SettingsStore(() => CPH.GetGlobalVar<string>("teamSolve.settings", true),
                value => CPH.SetGlobalVar("teamSolve.settings", value, true)),
            new TeamSolve.ChatFeedback(message => CPH.SendMessage(message)), log,
            new TeamSolve.Transport((connection, json) => CPH.WebsocketCustomServerBroadcast(json, connection, ServerIndex),
                connection => CPH.WebsocketCustomServerCloseSession(connection, ServerIndex)),
            () => DateTime.UtcNow);
    }

    public bool Execute()
    {
        var kind = Arg<string>("teamSolveEvent");
        switch (kind)
        {
            case "chat":
                runtime.Chat(Arg<string>("message"), new TeamSolve.Sender {
                    Id = Arg<string>("userId"), Name = Arg<string>("userName"),
                    Moderator = Arg<bool>("isModerator"), Broadcaster = Arg<bool>("isBroadcaster"),
                    Subscriber = Arg<bool>("isSubscribed"), Follower = Arg<bool>("isFollowing")
                });
                break;
            case "tick": runtime.Tick(); break;
            case "opened": runtime.Connection.Opened(Arg<string>("sessionId")); break;
            case "closed": runtime.Connection.Closed(Arg<string>("sessionId")); break;
            case "browser": runtime.Browser.Receive(Arg<string>("sessionId"), Arg<string>("data")); break;
            case "streamStarted": runtime.Log.StartStream(); break;
            default: throw new InvalidOperationException("Unknown configured Team Solve event: " + kind);
        }
        return true;
    }

    private T Arg<T>(string name)
    {
        T value;
        if (!CPH.TryGetArg(name, out value))
            throw new InvalidOperationException("Missing configured Team Solve argument: " + name);
        return value;
    }
}

namespace TeamSolve
{
    public sealed class Sender
    {
        public string Id, Name;
        public bool Moderator, Broadcaster, Subscriber, Follower;
        public bool CanControl { get { return Moderator || Broadcaster; } }
    }

    public sealed class Settings
    {
        public bool Enabled;
        public string Access = "list";
        public List<string> Users = new List<string>();
    }

    public sealed class SettingsStore
    {
        private readonly Func<string> read;
        private readonly Action<string> write;
        public SettingsStore(Func<string> read, Action<string> write) { this.read = read; this.write = write; }
        public Settings Load()
        {
            var json = read();
            return json == null ? new Settings() : JsonConvert.DeserializeObject<Settings>(json);
        }
        public void Save(Settings settings) { write(JsonConvert.SerializeObject(settings)); }
    }

    // Only this component decides what appears in public chat.
    public sealed class ChatFeedback
    {
        private readonly Action<string> send;
        public ChatFeedback(Action<string> send) { this.send = send; }
        public void Status(Controller controller)
        {
            var names = controller.Settings.Users.Count == 0 ? "(empty)" : string.Join(", ", controller.Settings.Users);
            Send("Team Solve: " + controller.State + ". Access: " + controller.Settings.Access + ". Saved list: " + names + ".");
        }
        public void Rejected(Sender sender, string reason)
        {
            var messages = new Dictionary<string, string> {
                { "controlPermission", "only the streamer and moderators can use !teamsolve." },
                { "off", "Team Solve is off." },
                { "waiting", "Team Solve is waiting for a ready puzzle connection." },
                { "permission", "you are not in the selected Team Solve audience." },
                { "busy", "too many puzzle commands are awaiting a reply; try again shortly." },
                { "syntax", "invalid puzzle command. Example: $r2c6 5. Use a space after the target." },
                { "controlSyntax", "use !teamsolve [off|everyone|followers|subscribers|list [names]]." }
            };
            Send("@" + sender.Name + " " + messages[reason]);
        }
        private void Send(string message)
        {
            // Twitch messages have a length limit; keep long saved lists readable.
            while (message.Length > 450)
            {
                var split = message.LastIndexOf(' ', 450);
                if (split < 1) split = 450;
                send(message.Substring(0, split));
                message = message.Substring(split).TrimStart();
            }
            send(message);
        }
    }

    public sealed class Diagnostics
    {
        private readonly string directory;
        private readonly Action<string> debug;
        private string path;
        public Diagnostics(string directory, Action<string> debug)
        {
            this.directory = directory; this.debug = debug;
            Directory.CreateDirectory(directory);
            StartStream();
        }
        public void StartStream()
        {
            path = Path.Combine(directory, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N") + ".jsonl");
            Write("logStarted", new { });
        }
        public void Write(string name, object details)
        {
            var line = JsonConvert.SerializeObject(new { timestamp = DateTime.UtcNow, eventName = name, details = details });
            File.AppendAllText(path, line + Environment.NewLine);
            debug(line);
        }
    }

    public sealed class Controller
    {
        private readonly SettingsStore store;
        private readonly ChatFeedback feedback;
        private readonly Diagnostics log;
        private bool available;
        public Settings Settings { get; private set; }
        public string State { get { return !Settings.Enabled ? "Off" : available ? "On" : "Waiting"; } }
        public Controller(SettingsStore store, ChatFeedback feedback, Diagnostics log)
        {
            this.store = store; this.feedback = feedback; this.log = log;
            Settings = store.Load();
        }
        public void AvailabilityChanged(bool value)
        {
            var previous = State;
            available = value;
            if (State != previous) feedback.Status(this);
        }
        public void Control(string parameters, Sender sender)
        {
            log.Write("controlInput", new { sender, parameters });
            if (!sender.CanControl) { feedback.Rejected(sender, "controlPermission"); return; }
            var words = parameters.ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) { feedback.Status(this); return; }
            var option = words[0];
            if (!(new[] { "off", "everyone", "followers", "subscribers", "list" }).Contains(option)
                || (words.Length > 1 && option != "list")
                || words.Skip(1).Any(name => !Regex.IsMatch(name, @"\A@?[a-z0-9_]{1,25}\z")))
            { feedback.Rejected(sender, "controlSyntax"); return; }
            Settings.Enabled = option != "off";
            if (option != "off") Settings.Access = option;
            if (words.Length > 1) Settings.Users = words.Skip(1).Select(name => name.TrimStart('@')).Distinct().ToList();
            store.Save(Settings);
            log.Write("settingsChanged", Settings);
            feedback.Status(this);
        }
    }

    public static class AccessValidator
    {
        public static string Rejection(Controller controller, bool available, Sender sender)
        {
            var settings = controller.Settings;
            if (!settings.Enabled) return "off";
            if (!available) return "waiting";
            if (sender.CanControl) return null;
            switch (settings.Access)
            {
                case "everyone": return null;
                case "followers": return sender.Follower ? null : "permission";
                case "subscribers": return sender.Subscriber ? null : "permission";
                case "list": return settings.Users.Contains(sender.Name.ToLowerInvariant()) ? null : "permission";
                default: throw new InvalidOperationException("Unknown saved access option: " + settings.Access);
            }
        }
    }

    // Pure chat-input boundary: no settings, transport, logging, or feedback.
    public static class CommandParser
    {
        private static readonly Regex Shape = new Regex(@"\A\$(?<target>[^ ]+)(?: +(?<details>.*))?\z");
        private static readonly Regex Cell = new Regex(@"\A(?:r(?<r>[0-9]+)c(?<c>[0-9]+)|c(?<c>[0-9]+)r(?<r>[0-9]+))(?:p(?<p>[1-9]))?\z");
        public static JObject Parse(string input)
        {
            var match = Shape.Match(input.TrimEnd(' ').ToLowerInvariant());
            if (!match.Success) return null;
            var targets = match.Groups["target"].Value.Split(':');
            var details = match.Groups["details"].Value;
            if (targets.Length == 2)
            {
                var start = Target(targets[0], true); var end = Target(targets[1], true);
                if (start == null || end == null || !Regex.IsMatch(details, @"\A(?:c[1-9]|-)?\z")) return null;
                var line = Action("line", details == "-" ? "remove" : "add", new JObject { ["start"] = start, ["end"] = end });
                if (details != "-") line["color"] = details == "" ? 1 : details[1] - '0';
                return line;
            }
            if (targets.Length != 1) return null;
            var drawing = Regex.Match(details, @"\A(?<remove>-)?(?<kind>[xo])(?: +c(?<color>[1-9]))?\z");
            if (drawing.Success)
            {
                var target = Target(targets[0], true);
                var remove = drawing.Groups["remove"].Success;
                if (target == null || (remove && drawing.Groups["color"].Success)) return null;
                var action = Action(drawing.Groups["kind"].Value, remove ? "remove" : "add", target);
                if (!remove) action["color"] = drawing.Groups["color"].Success ? int.Parse(drawing.Groups["color"].Value) : 1;
                return action;
            }
            var cell = Target(targets[0], false);
            if (cell == null) return null;
            if (details == "-") return Action("value", "clear", cell);
            if (Regex.IsMatch(details, @"\A[0-9a-i]\z"))
            {
                var action = Action("value", "set", cell); action["value"] = details.ToUpperInvariant(); return action;
            }
            var marks = Regex.Match(details, @"\A(?<remove>-)?(?<kind>[\^*])(?<values>[0-9a-i]*)\z");
            if (marks.Success)
            {
                var remove = marks.Groups["remove"].Success;
                var values = marks.Groups["values"].Value.ToUpperInvariant();
                if (!remove && values.Length == 0) return null;
                var action = Action(marks.Groups["kind"].Value == "^" ? "cornerMarks" : "centerMarks",
                    values.Length == 0 ? "clear" : remove ? "remove" : "add", cell);
                if (values.Length > 0) action["values"] = new JArray(values.Select(c => c.ToString()).Distinct());
                return action;
            }
            if (details == "-c") return Action("color", "clear", cell);
            var color = Regex.Match(details, @"\A(?<remove>-)?c(?<color>[0-9])(?:s(?<set>[1-3]))?\z");
            if (!color.Success) return null;
            var colored = Action("color", color.Groups["remove"].Success ? "remove" : "add", cell);
            colored["color"] = int.Parse(color.Groups["color"].Value);
            colored["colorSet"] = color.Groups["set"].Success ? int.Parse(color.Groups["set"].Value) : 1;
            return colored;
        }
        private static JObject Target(string text, bool positioned)
        {
            var match = Cell.Match(text);
            int row, column;
            if (!match.Success || !int.TryParse(match.Groups["r"].Value, out row) || row < 1
                || !int.TryParse(match.Groups["c"].Value, out column) || column < 1
                || (!positioned && match.Groups["p"].Success)) return null;
            var result = new JObject { ["row"] = row, ["column"] = column };
            if (positioned) result["position"] = match.Groups["p"].Success ? int.Parse(match.Groups["p"].Value) : 5;
            return result;
        }
        private static JObject Action(string kind, string operation, JObject target)
        { return new JObject { ["kind"] = kind, ["operation"] = operation, ["target"] = target }; }
    }

    public sealed class Transport
    {
        private readonly Action<string, string> send;
        private readonly Action<string> close;
        public Transport(Action<string, string> send, Action<string> close) { this.send = send; this.close = close; }
        public void Send(string connection, JObject message) { send(connection, message.ToString(Formatting.None)); }
        public void Close(string connection) { close(connection); }
    }

    public sealed class BrowserSession
    {
        public string SelectionId, SessionId, PuzzleId, Connection;
        public bool Ready;
        public bool Matches(string connection, JObject message)
        {
            return Connection == connection && SessionId == (string)message["sessionId"] && PuzzleId == (string)message["puzzleId"];
        }
    }

    public sealed class HeartbeatMonitor
    {
        public string RequestId { get; private set; }
        private DateTime next, deadline;
        public void Reset(DateTime now) { RequestId = null; next = now; }
        public bool Expired(DateTime now) { return RequestId != null && now >= deadline; }
        public bool Due(DateTime now) { return RequestId == null && now >= next; }
        public string Begin(DateTime now)
        {
            RequestId = Guid.NewGuid().ToString("N"); deadline = now.AddSeconds(10); return RequestId;
        }
        public bool Reply(string id, DateTime now)
        {
            if (RequestId != id || now >= deadline) return false;
            RequestId = null; next = now.AddSeconds(5); return true;
        }
    }

    public sealed class ConnectionManager
    {
        private readonly Transport transport;
        private readonly Diagnostics log;
        private readonly Func<DateTime> now;
        private readonly HashSet<string> open = new HashSet<string>();
        private readonly HeartbeatMonitor heartbeat = new HeartbeatMonitor();
        public BrowserSession Session { get; private set; }
        public bool Available { get { return Session != null && Session.Connection != null && Session.Ready; } }
        public event Action<bool> AvailabilityChanged = delegate { };
        // Invalidation happens even if readiness was already false.
        public event Action<string> Invalidated = delegate { };
        public ConnectionManager(Transport transport, Diagnostics log, Func<DateTime> now)
        { this.transport = transport; this.log = log; this.now = now; }
        public void Opened(string connection) { open.Add(connection); log.Write("connectionOpened", new { connection }); }
        public void Closed(string connection)
        {
            open.Remove(connection);
            if (Session != null && Session.Connection == connection) Disconnect("connectionLost", false);
            log.Write("connectionClosed", new { connection });
        }
        public void Handle(string connection, JObject message)
        {
            if (!open.Contains(connection)) { log.Write("staleConnectionMessage", new { connection }); return; }
            var type = (string)message["type"];
            if (type == "sessionActivate" || type == "sessionResume")
            {
                Accept(connection, message, type == "sessionResume"); return;
            }
            if (Session == null || !Session.Matches(connection, message))
            { log.Write("staleSessionMessage", new { connection, type }); return; }
            if (type == "sessionRelease")
            {
                if ((string)message["selectionId"] != Session.SelectionId) return;
                Disconnect("released", true); transport.Close(connection); return;
            }
            if (type == "heartbeatReply" && !heartbeat.Reply((string)message["requestId"], now()))
            { log.Write("unmatchedHeartbeat", new { connection, requestId = (string)message["requestId"] }); return; }
            SetReady((bool)message["ready"]);
        }
        private void Accept(string connection, JObject message, bool resume)
        {
            if (resume && (Session == null || Session.Connection != null
                || Session.SelectionId != (string)message["selectionId"] || Session.PuzzleId != (string)message["puzzleId"]))
            {
                SendControl(connection, new JObject { ["type"] = "sessionRejected", ["version"] = 1,
                    ["requestId"] = message["requestId"], ["reason"] = "resumeRejected" });
                transport.Close(connection); return;
            }
            var selection = resume ? Session.SelectionId : Guid.NewGuid().ToString("N");
            if (Session != null)
            {
                var previous = Session;
                Disconnect("replaced", true);
                if (previous.Connection != null)
                {
                    var close = Envelope("sessionClose", previous); close["reason"] = "replaced";
                    SendControl(previous.Connection, close);
                    // A browser may explicitly activate another puzzle on the same socket.
                    if (previous.Connection != connection) { open.Remove(previous.Connection); transport.Close(previous.Connection); }
                }
            }
            Session = new BrowserSession { SelectionId = selection, Connection = connection,
                SessionId = Guid.NewGuid().ToString("N"), PuzzleId = (string)message["puzzleId"] };
            heartbeat.Reset(now());
            var accepted = Envelope("sessionAccepted", Session);
            accepted["requestId"] = message["requestId"]; accepted["selectionId"] = selection;
            if (!SendControl(connection, accepted)) { Disconnect("sendFailed", false); return; }
            log.Write("sessionAccepted", new { Session.SessionId, Session.PuzzleId, connection });
        }
        public bool SendAction(JObject message)
        {
            if (!Available || (string)message["sessionId"] != Session.SessionId) return false;
            if (SendControl(Session.Connection, message)) return true;
            Disconnect("sendFailed", false); return false;
        }
        private bool SendControl(string connection, JObject message)
        {
            // The host API returns void. An exception is the only immediate failure signal.
            try { transport.Send(connection, message); return true; }
            catch (Exception error)
            {
                log.Write("transportSendFailed", new { connection, type = (string)message["type"], error = error.ToString() });
                return false;
            }
        }
        public void DoWork()
        {
            if (Session == null || Session.Connection == null) return;
            if (heartbeat.Expired(now()))
            {
                var connection = Session.Connection;
                Disconnect("heartbeatExpired", false); open.Remove(connection); transport.Close(connection); return;
            }
            if (heartbeat.Due(now()))
            {
                var ping = Envelope("heartbeat", Session); ping["requestId"] = heartbeat.Begin(now());
                if (!SendControl(Session.Connection, ping)) Disconnect("sendFailed", false);
            }
        }
        private void SetReady(bool ready)
        {
            var previous = Available; Session.Ready = ready;
            if (previous && !ready) Invalidated("puzzleNotReady");
            if (previous != Available) AvailabilityChanged(Available);
            log.Write("readiness", new { Session.SessionId, ready });
        }
        private void Disconnect(string reason, bool forget)
        {
            var previous = Available;
            // Replace the snapshot so revocation can still target the old connection.
            Session = forget ? null : new BrowserSession { SelectionId = Session.SelectionId,
                SessionId = Session.SessionId, PuzzleId = Session.PuzzleId };
            Invalidated(reason);
            if (previous) AvailabilityChanged(false);
            log.Write("sessionUnavailable", new { reason });
        }
        public static JObject Envelope(string type, BrowserSession session)
        {
            return new JObject { ["type"] = type, ["version"] = 1,
                ["sessionId"] = session.SessionId, ["puzzleId"] = session.PuzzleId };
        }
    }

    public sealed class PendingRequest
    {
        public JObject Message;
        public Sender Sender;
        public string Text, Connection;
        public DateTime Sent, Deadline;
    }

    public sealed class OrderedDispatcher
    {
        private readonly Dictionary<string, PendingRequest> pending = new Dictionary<string, PendingRequest>();
        private readonly ConnectionManager connection;
        private readonly Diagnostics log;
        private readonly Func<DateTime> now;
        public int Count { get { return pending.Count; } }
        public OrderedDispatcher(ConnectionManager connection, Diagnostics log, Func<DateTime> now)
        { this.connection = connection; this.log = log; this.now = now; connection.Invalidated += Invalidate; }
        public void Dispatch(JObject action, Sender sender, string text, string requestId)
        {
            var message = ConnectionManager.Envelope("puzzleAction", connection.Session);
            message["requestId"] = requestId; message["action"] = action;
            pending.Add(requestId, new PendingRequest { Message = message, Sender = sender, Text = text,
                Connection = connection.Session.Connection, Sent = now(), Deadline = now().AddSeconds(15) });
            log.Write("actionSending", new { requestId, sender, text, message });
            if (!connection.SendAction(message)) Complete(requestId, "sendFailed", null);
        }
        public void Acknowledge(string source, JObject message)
        {
            var id = (string)message["requestId"];
            PendingRequest request;
            if (!pending.TryGetValue(id, out request) || request.Connection != source
                || (string)request.Message["sessionId"] != (string)message["sessionId"]
                || (string)request.Message["puzzleId"] != (string)message["puzzleId"])
            { log.Write("unmatchedAcknowledgement", new { source, message }); return; }
            if (now() >= request.Deadline) { Complete(id, "timeout", null); return; }
            Complete(id, (string)message["outcome"], message);
        }
        public void DoWork()
        {
            foreach (var id in pending.Where(item => now() >= item.Value.Deadline).Select(item => item.Key).ToArray())
                Complete(id, "timeout", null);
        }
        private void Invalidate(string reason)
        { foreach (var id in pending.Keys.ToArray()) Complete(id, reason, null); }
        private void Complete(string id, string outcome, JObject reply)
        {
            PendingRequest request;
            if (!pending.TryGetValue(id, out request)) return; // Send failure may already invalidate the session.
            pending.Remove(id);
            log.Write("actionCompleted", new { requestId = id, outcome, reply, request.Sender, request.Text,
                sessionId = (string)request.Message["sessionId"], puzzleId = (string)request.Message["puzzleId"],
                elapsedMs = (now() - request.Sent).TotalMilliseconds });
        }
    }

    public sealed class PuzzleCommandProcessor
    {
        private readonly Controller controller;
        private readonly ConnectionManager connection;
        private readonly ChatFeedback feedback;
        private readonly Diagnostics log;
        public OrderedDispatcher Dispatcher { get; private set; }
        public PuzzleCommandProcessor(Controller controller, ConnectionManager connection, ChatFeedback feedback, Diagnostics log, Func<DateTime> now)
        {
            this.controller = controller; this.connection = connection; this.feedback = feedback; this.log = log;
            Dispatcher = new OrderedDispatcher(connection, log, now);
        }
        public void Process(string text, Sender sender)
        {
            var requestId = Guid.NewGuid().ToString("N");
            log.Write("puzzleInput", new { requestId, sender, text });
            var rejection = AccessValidator.Rejection(controller, connection.Available, sender);
            JObject action = null;
            if (rejection == null) { action = CommandParser.Parse(text); if (action == null) rejection = "syntax"; }
            if (rejection == null && Dispatcher.Count >= 256) rejection = "busy";
            if (rejection != null)
            {
                log.Write("puzzleRejected", new { requestId, rejection }); feedback.Rejected(sender, rejection); return;
            }
            Dispatcher.Dispatch(action, sender, text, requestId);
        }
        public void DoWork() { Dispatcher.DoWork(); }
    }

    // Browser messages are decoded here once. State-dependent checks belong to the manager/tracker.
    public sealed class BrowserMessageRouter
    {
        private readonly ConnectionManager connection;
        private readonly OrderedDispatcher dispatcher;
        private readonly Diagnostics log;
        public BrowserMessageRouter(ConnectionManager connection, OrderedDispatcher dispatcher, Diagnostics log)
        { this.connection = connection; this.dispatcher = dispatcher; this.log = log; }
        public void Receive(string source, string json)
        {
            JObject message;
            try { message = JObject.Parse(json); }
            catch (JsonReaderException error) { log.Write("invalidBrowserJson", new { source, error = error.Message }); return; }
            if (!Valid(message)) { log.Write("invalidBrowserEnvelope", new { source }); return; }
            if ((string)message["type"] == "actionAcknowledgement") dispatcher.Acknowledge(source, message);
            else connection.Handle(source, message);
        }
        private static bool Text(JObject message, string field)
        { return message[field] != null && message[field].Type == JTokenType.String && !string.IsNullOrWhiteSpace((string)message[field]); }
        private static bool Valid(JObject message)
        {
            if (!Text(message, "type") || message["version"] == null || message["version"].Type != JTokenType.Integer
                || !JToken.DeepEquals(message["version"], new JValue(1))) return false;
            var type = (string)message["type"];
            if (type == "sessionActivate") return Text(message, "requestId") && Text(message, "puzzleId");
            if (type == "sessionResume") return Text(message, "requestId") && Text(message, "puzzleId") && Text(message, "selectionId");
            if (!Text(message, "sessionId") || !Text(message, "puzzleId")) return false;
            switch (type)
            {
                case "sessionRelease": return Text(message, "selectionId");
                case "readiness": return message["ready"] != null && message["ready"].Type == JTokenType.Boolean;
                case "heartbeatReply": return Text(message, "requestId") && message["ready"] != null && message["ready"].Type == JTokenType.Boolean;
                case "actionAcknowledgement":
                    return Text(message, "requestId") && Text(message, "outcome")
                        && ((string)message["outcome"] == "understood" || ((string)message["outcome"] == "failed" && Text(message, "reason")))
                        && (message["description"] == null || message["description"].Type == JTokenType.String);
                default: return false;
            }
        }
    }

    public sealed class Runtime
    {
        public Controller Controller { get; private set; }
        public ConnectionManager Connection { get; private set; }
        public PuzzleCommandProcessor Processor { get; private set; }
        public BrowserMessageRouter Browser { get; private set; }
        public Diagnostics Log { get; private set; }
        public Runtime(SettingsStore store, ChatFeedback feedback, Diagnostics log, Transport transport, Func<DateTime> now)
        {
            Log = log;
            Controller = new Controller(store, feedback, log);
            Connection = new ConnectionManager(transport, log, now);
            Connection.AvailabilityChanged += Controller.AvailabilityChanged;
            Processor = new PuzzleCommandProcessor(Controller, Connection, feedback, log, now);
            Browser = new BrowserMessageRouter(Connection, Processor.Dispatcher, log);
            log.Write("initialized", new { Controller.Settings, Controller.State });
        }
        public void Chat(string text, Sender sender)
        {
            var firstSpace = text.IndexOf(' ');
            var command = firstSpace < 0 ? text : text.Substring(0, firstSpace);
            if (command.Equals("!teamsolve", StringComparison.OrdinalIgnoreCase))
                Controller.Control(firstSpace < 0 ? "" : text.Substring(firstSpace + 1), sender);
            else if (text.StartsWith("$", StringComparison.Ordinal)) Processor.Process(text, sender);
        }
        public void Tick() { Processor.DoWork(); Connection.DoWork(); }
    }
}
