using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using TeamSolve;

// Compile the actual pasted entry point against the host API signatures it uses.
public partial class CPHInline { public HostStub CPH = new HostStub(); }
public sealed class HostStub
{
    public Dictionary<string, object> Args = new Dictionary<string, object>();
    public void LogDebug(string message) { }
    public T GetGlobalVar<T>(string name, bool persisted) { return default(T); }
    public void SetGlobalVar(string name, object value, bool persisted) { }
    public void SendMessage(string message) { }
    public void WebsocketCustomServerBroadcast(string data, string sessionId, int connection = 0) { }
    public void WebsocketCustomServerCloseSession(string sessionId, int connection = 0) { }
    public bool TryGetArg<T>(string name, out T value)
    {
        object item;
        if (!Args.TryGetValue(name, out item)) { value = default(T); return false; }
        value = (T)item; return true;
    }
}

internal sealed class Fixture
{
    public DateTime Now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    public List<JObject> Sent = new List<JObject>();
    public List<string> Destinations = new List<string>(), Closed = new List<string>(), Chat = new List<string>();
    public List<JObject> Logs = new List<JObject>();
    public string Saved;
    public bool FailSend;
    public Runtime App;
    public Sender Mod = new Sender { Id = "1", Name = "moderator", Moderator = true };
    public Sender Viewer = new Sender { Id = "2", Name = "alice" };
    public Fixture()
    {
        App = NewRuntime();
    }
    public Runtime NewRuntime()
    {
        return new Runtime(new SettingsStore(() => Saved, json => Saved = json), new ChatFeedback(Chat.Add),
            new Diagnostics(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TeamSolveLogs"), line => Logs.Add(JObject.Parse(line))),
            new Transport((source, json) => {
                if (FailSend) throw new IOException("Simulated transport failure");
                Destinations.Add(source); Sent.Add(JObject.Parse(json));
            }, Closed.Add), () => Now);
    }
    public JObject Message(string type, string puzzle = "p1")
    { return new JObject { ["type"] = type, ["version"] = 1, ["requestId"] = Guid.NewGuid().ToString("N"), ["puzzleId"] = puzzle }; }
    public void Receive(string source, JObject message) { App.Browser.Receive(source, message.ToString()); }
    public void Activate(string source = "socket1", string puzzle = "p1")
    {
        App.Connection.Opened(source); Receive(source, Message("sessionActivate", puzzle));
    }
    public void Ready(bool ready = true)
    {
        var message = ConnectionManager.Envelope("readiness", App.Connection.Session); message["ready"] = ready;
        Receive(App.Connection.Session.Connection, message);
    }
    public void Start() { Activate(); Ready(); App.Chat("!teamsolve everyone", Mod); }
    public JObject Ack(JObject action)
    {
        var result = (JObject)action.DeepClone(); result["type"] = "actionAcknowledgement";
        result.Remove("action"); result["outcome"] = "understood"; return result;
    }
    public int Completions { get { return Logs.Count(log => (string)log["eventName"] == "actionCompleted"); } }
}

internal static class Program
{
    private static int checks;
    private static void Check(bool condition, string name)
    { checks++; if (!condition) throw new Exception("FAILED: " + name); }
    private static void Main()
    {
        Parser(); Permissions(); Lifecycle(); Heartbeats(); Failures(); HostRouting();
        Console.WriteLine("Passed " + checks + " checks.");
    }
    private static void Parser()
    {
        var valid = new Dictionary<string, string> {
            { "$r2c6 5", "value/set" }, { "$c6r2 A", "value/set" }, { "$r1c1 0", "value/set" },
            { "$r2c6 -", "value/clear" }, { "$r3c4 ^159ai", "cornerMarks/add" },
            { "$r3c4 -^15a", "cornerMarks/remove" }, { "$r3c4 -^", "cornerMarks/clear" },
            { "$r3c4 *237bdf", "centerMarks/add" }, { "$r3c4 -*37b", "centerMarks/remove" },
            { "$r3c4 -*", "centerMarks/clear" }, { "$r4c7 c0s3", "color/add" },
            { "$r4c7 -c2s3", "color/remove" }, { "$r4c7 -c", "color/clear" },
            { "$r2c6 x", "x/add" }, { "$r2c6p4 x c2", "x/add" }, { "$r2c6 -x", "x/remove" },
            { "$r2c6 o c7", "o/add" }, { "$r2c6p9 -o", "o/remove" },
            { "$r2c1:r2c7", "line/add" }, { "$r2c1p6:c7r2p4 c3", "line/add" },
            { "$r2c1:r2c7 -", "line/remove" }, { "$R200C100   i", "value/set" }
        };
        foreach (var sample in valid)
        {
            var action = CommandParser.Parse(sample.Key);
            Check(action != null && (string)action["kind"] + "/" + (string)action["operation"] == sample.Value, sample.Key);
        }
        foreach (var sample in new[] { "$ r1c1 2", "$r1c1^12", "$r0c1 2", "$r-1c2 2", "$r1c1p0 x", "$r1c1p10 x",
            "$r1c1p5 2", "$r1c1p5 ^2", "$r1c1 12", "$r1c1 j", "$r1c1 ^", "$r1c1 ^1 2", "$r1c1 -2",
            "$r1c1 c2s0", "$r1c1 c10", "$r1c1 x c0", "$r1c1 x c2s1", "$r1c1 -o c2", "$r1c1:r2c2 c0",
            "$r1c1:r2c2 -c2", "$r1c1:r2c2:r3c3", "$r99999999999999999c1 2", "$r1c1", "$r1c1\t2", "$r1c1 2\n" })
            Check(CommandParser.Parse(sample) == null, "reject " + sample);
        Check((string)CommandParser.Parse("$c6r2 a")["value"] == "A", "uppercase");
        Check(CommandParser.Parse("$r1c1 ^11aa0")["values"].ToString(Newtonsoft.Json.Formatting.None) == "[\"1\",\"A\",\"0\"]", "deduplicate");
        Check((int)CommandParser.Parse("$r1c1 x")["target"]["position"] == 5, "default position");
        Check((int)CommandParser.Parse("$r1c1 c2")["colorSet"] == 1, "default color set");
        Check(CommandParser.Parse("$r1c1 -x")["color"] == null, "omit removed color");
        Check(CommandParser.Parse("$r1c1 2")["target"]["position"] == null, "omit cell position");
    }
    private static void Permissions()
    {
        var f = new Fixture();
        Check(f.App.Controller.State == "Off" && f.App.Controller.Settings.Access == "list", "initial settings");
        f.App.Chat("!teamsolve list", f.Mod);
        Check(AccessValidator.Rejection(f.App.Controller, true, f.Viewer) == "permission", "empty list denies viewer");
        Check(AccessValidator.Rejection(f.App.Controller, true, f.Mod) == null, "empty list allows moderator");
        Check(AccessValidator.Rejection(f.App.Controller, true, new Sender { Broadcaster = true }) == null, "broadcaster bypasses audience");
        f.App.Chat("!teamsolve off", f.Mod);
        f.App.Chat("!teamsolve everyone", f.Viewer); Check(!f.App.Controller.Settings.Enabled, "controls restricted");
        f.App.Chat("!teamsolve list @ALICE Bob alice", f.Mod);
        Check(f.App.Controller.State == "Waiting" && f.App.Controller.Settings.Users.SequenceEqual(new[] { "alice", "bob" }), "replace normalized list");
        f.Activate(); Check(f.App.Controller.State == "Waiting", "acceptance alone not ready"); f.Ready();
        Check(f.App.Controller.State == "On", "both switches ready");
        Check(AccessValidator.Rejection(f.App.Controller, true, f.Viewer) == null, "list member");
        f.App.Chat("!teamsolve subscribers", f.Mod);
        Check(AccessValidator.Rejection(f.App.Controller, true, f.Viewer) == "permission", "list does not supplement subscribers");
        f.Viewer.Subscriber = true; Check(AccessValidator.Rejection(f.App.Controller, true, f.Viewer) == null, "subscriber");
        f.App.Chat("!teamsolve followers", f.Mod);
        Check(AccessValidator.Rejection(f.App.Controller, true, f.Viewer) == "permission", "subscriber not follower");
        f.Viewer.Follower = true; Check(AccessValidator.Rejection(f.App.Controller, true, f.Viewer) == null, "follower");
        f.App.Chat("!teamsolve list", f.Mod); Check(f.App.Controller.Settings.Users.Count == 2, "preserve list");
        var saved = f.Saved; f.App.Chat("!teamsolve list good invalid!", f.Mod); Check(f.Saved == saved, "invalid list atomic");
        f.App.Chat("!teamsolve off extra", f.Mod); Check(f.Saved == saved, "invalid control atomic");
        f.App.Chat("!teamsolve off", f.Mod); Check(f.App.Connection.Available, "off keeps browser");
        Check(AccessValidator.Rejection(f.App.Controller, true, f.Mod) == "off", "moderator requires enabled");
        f.App.Chat("!teamsolve", f.Mod); Check(!f.App.Controller.Settings.Enabled, "status does not enable");
        Check(!f.NewRuntime().Controller.Settings.Enabled, "restore off");
        f.App.Chat("!teamsolve everyone", f.Mod); Check(f.NewRuntime().Controller.State == "Waiting", "restore on waits");
        var before = f.Chat.Count; f.App.Chat("hello", f.Viewer); f.App.Chat("!teamsolveOther", f.Mod);
        Check(f.Chat.Count == before, "ignore unrelated chat");
        var restored = f.NewRuntime();
        Check(restored.Controller.Settings.Users.SequenceEqual(new[] { "alice", "bob" }), "restore saved list");
    }
    private static void Lifecycle()
    {
        var f = new Fixture(); f.Start(); var chatCount = f.Chat.Count;
        f.App.Chat("$r1c1 1", f.Viewer); f.App.Chat("$r1c1 2", f.Viewer);
        var actions = f.Sent.Where(m => (string)m["type"] == "puzzleAction").ToArray();
        Check(actions.Length == 2 && (string)actions[0]["action"]["value"] == "1" && (string)actions[1]["action"]["value"] == "2", "FIFO immediate sends");
        Check(f.Destinations.All(d => d == "socket1"), "target selected socket");
        f.Receive("intruder", f.Ack(actions[0])); Check(f.App.Processor.Dispatcher.Count == 2, "wrong source ack");
        var wrong = f.Ack(actions[0]); wrong["puzzleId"] = "wrong";
        f.Receive("socket1", wrong); Check(f.App.Processor.Dispatcher.Count == 2, "wrong puzzle ack");
        f.Receive("socket1", f.Ack(actions[0])); f.Receive("socket1", f.Ack(actions[0]));
        Check(f.Completions == 1 && f.App.Processor.Dispatcher.Count == 1, "ack exactly once");
        Check(f.Chat.Count == chatCount, "success silent");
        var old = f.App.Connection.Session; f.Activate("socket2", "p2");
        Check(f.Completions == 2 && !f.App.Connection.Available && f.Closed.Contains("socket1"), "replacement invalidates pending");
        Check(f.Sent.Any(m => (string)m["type"] == "sessionClose" && (string)m["sessionId"] == old.SessionId), "close old session");
        var release = ConnectionManager.Envelope("sessionRelease", old); release["selectionId"] = old.SelectionId;
        f.Receive("socket1", release); Check(f.App.Connection.Session.PuzzleId == "p2", "stale release");
        f.App.Connection.Opened("socket3"); var resume = f.Message("sessionResume"); resume["selectionId"] = old.SelectionId;
        f.Receive("socket3", resume); Check(f.App.Connection.Session.PuzzleId == "p2" && f.Closed.Contains("socket3"), "old selection cannot resume");
        f.Ready(); var selected = f.App.Connection.Session;
        f.App.Connection.Closed("socket2"); Check(f.App.Controller.State == "Waiting", "disconnect pauses");
        var sends = f.Sent.Count; f.App.Chat("$r1c1 3", f.Viewer); Check(f.Sent.Count == sends, "disconnected command discarded");
        f.App.Connection.Opened("socket4"); resume = f.Message("sessionResume", "p2"); resume["selectionId"] = selected.SelectionId;
        f.Receive("socket4", resume);
        Check(f.App.Connection.Session.SessionId != selected.SessionId && !f.App.Connection.Available, "resume fresh session waits");
        var staleReady = ConnectionManager.Envelope("readiness", selected); staleReady["ready"] = true;
        f.Receive("socket4", staleReady); Check(!f.App.Connection.Available, "old readiness cannot revive");
        f.Ready(); Check(f.App.Controller.State == "On", "resume ready");
        f.App.Connection.Closed("socket2"); Check(f.App.Connection.Available, "stale close cannot disconnect resumed socket");
        release = ConnectionManager.Envelope("sessionRelease", f.App.Connection.Session); release["selectionId"] = selected.SelectionId;
        f.Receive("socket4", release); Check(f.App.Connection.Session == null && f.App.Controller.Settings.Enabled, "release forgets selection only");
        f.App.Connection.Opened("socket5"); f.Receive("socket5", resume); Check(f.App.Connection.Session == null, "released selection cannot resume");
    }
    private static void Heartbeats()
    {
        var f = new Fixture(); f.Start(); f.App.Tick();
        var ping = f.Sent.Last(); Check((string)ping["type"] == "heartbeat", "timer sends heartbeat");
        var pong = (JObject)ping.DeepClone(); pong["type"] = "heartbeatReply"; pong["ready"] = true;
        var wrong = (JObject)pong.DeepClone(); wrong["requestId"] = "wrong";
        f.Receive("socket1", wrong); f.Now = f.Now.AddSeconds(9); f.Receive("socket1", pong);
        f.Now = f.Now.AddSeconds(4); f.App.Tick(); Check(f.App.Connection.Available, "valid heartbeat keeps connection");
        f.Now = f.Now.AddSeconds(1); f.App.Tick();
        f.App.Chat("$r1c1 1", f.Viewer);
        f.Now = f.Now.AddSeconds(10); f.App.Tick();
        Check(!f.App.Connection.Available && f.Completions == 1 && f.Closed.Contains("socket1"), "heartbeat expiry invalidates");
        f.Receive("socket1", pong); Check(!f.App.Connection.Available, "late heartbeat cannot revive");
        f = new Fixture(); f.Start(); f.App.Chat("$r1c1 1", f.Viewer); var action = f.Sent.Last();
        f.Now = f.Now.AddSeconds(15); f.Receive("socket1", f.Ack(action)); f.App.Tick();
        Check(f.Completions == 1 && f.Logs.Any(l => (string)l["details"]["outcome"] == "timeout"), "deadline wins over late ack before tick");
    }
    private static void Failures()
    {
        var f = new Fixture(); f.Start();
        foreach (var json in new[] { "{", "[]", "null", "{}", "{\"type\":2}", "{\"type\":\"readiness\",\"version\":1,\"sessionId\":\"s\",\"puzzleId\":\"p\",\"ready\":\"true\"}" })
            f.App.Browser.Receive("socket1", json);
        Check(f.App.Connection.Available, "malformed frames do not change state");
        f.App.Chat("$r1c1 1", f.Viewer);
        var ack = f.Ack(f.Sent.Last()); ack["outcome"] = "failed";
        f.Receive("socket1", ack); Check(f.App.Processor.Dispatcher.Count == 1, "failed ack requires reason");
        ack["reason"] = "puzzleChanged"; f.Receive("socket1", ack);
        Check(f.App.Processor.Dispatcher.Count == 0 && f.Completions == 1, "browser failure completes request");
        f = new Fixture(); f.Start();
        f.App.Chat("$r1c1 1", f.Viewer); f.Ready(false);
        Check(f.Completions == 1 && f.App.Controller.State == "Waiting", "not ready closes pending");
        f.Ready(); f.FailSend = true; f.App.Chat("$r1c1 2", f.Viewer);
        Check(f.Completions == 2 && !f.App.Connection.Available, "send failure completes once");
        f = new Fixture(); f.Start();
        for (var i = 0; i < 257; i++) f.App.Chat("$r1c1 1", f.Viewer);
        Check(f.App.Processor.Dispatcher.Count == 256, "pending storage bounded");
        f.Now = f.Now.AddSeconds(15); f.App.Processor.DoWork();
        Check(f.App.Processor.Dispatcher.Count == 0 && f.Completions == 256, "timeout drains pending without replies");
        f = new Fixture(); f.App.Connection.Opened("socket1");
        var readiness = f.Message("readiness"); readiness["sessionId"] = "invented"; readiness["ready"] = true;
        f.Receive("socket1", readiness); Check(f.App.Connection.Session == null, "readiness cannot select browser");
        f.FailSend = true; f.Receive("socket1", f.Message("sessionActivate"));
        Check(!f.App.Connection.Available && f.App.Connection.Session.Connection == null, "failed acceptance stays unavailable");
    }
    private static void HostRouting()
    {
        var entry = new CPHInline(); entry.Init();
        entry.CPH.Args["teamSolveEvent"] = "tick"; Check(entry.Execute(), "host timer route");
        entry.CPH.Args["teamSolveEvent"] = "opened"; entry.CPH.Args["sessionId"] = "s"; Check(entry.Execute(), "host connection route");
        entry.CPH.Args["teamSolveEvent"] = "closed"; Check(entry.Execute(), "host closed route");
    }
}
