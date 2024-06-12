using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

using Serilog;

namespace AbevBot;

public static class Counter
{
  public static bool IsStarted { get; private set; }
  private static readonly List<WebSocketConnection> WsConnections = new();
  private static Counters CurrentCounters;
  /// <summary> Counter thread. </summary>
  private static readonly Thread CounterThread = new(Update) { IsBackground = true };

  public static void Start()
  {
    if (IsStarted) { return; }
    IsStarted = true;

    CurrentCounters = Database.GetLastCounters();
    if (CurrentCounters is null || CurrentCounters.Name.Length == 0)
    {
      Log.Warning("Counters - couldn't load last counter from database, creating new default one");
      CurrentCounters = new() { Name = "default" };
      Database.UpdateCounterData(CurrentCounters);
    }

    CounterThread.Start();

    Log.Information("Counters view available at: {address}", $"http://{Server.IP}:{Server.Port}/counter");
  }

  private static void Update()
  {
    while (true)
    {
      if (MainWindow.CloseRequested) { return; }

      lock (WsConnections)
      {
        var sendRequired = CurrentCounters.Dirty;

        // Handle WebSocket server
        for (int i = WsConnections.Count - 1; i >= 0; i--)
        {
          var ws = WsConnections[i];
          if (ws.Conn.State != WebSocketState.Open)
          {
            ws.Conn.Dispose();
            WsConnections.RemoveAt(i);
            continue;
          }

          // Receive
          if (ws.ReceiveTask is null) { ws.ReceiveTask = ws.Conn.ReceiveAsync(ws.ReceiveBuffer, CancellationToken.None); }
          if (ws.ReceiveTask != null && ws.ReceiveTask.IsCompleted)
          {
            if (ws.ReceiveTask.Status == TaskStatus.RanToCompletion && ws.ReceiveTask.Result.Count > 0)
            {
              // Do something with received data
              string msg = Encoding.UTF8.GetString(ws.ReceiveBuffer, 0, ws.ReceiveTask.Result.Count);
              if (msg == "message_parsed")
              {
                if (ws.SendTask != null && ws.SendTask.IsCompleted) { ws.SendTask = null; }
              }
            }
            ws.ReceiveTask = null;
          }

          if (ws.SendTask != null && ws.SendTask.Status == TaskStatus.RanToCompletion) { ws.SendTask = null; }
          // Every second send to all websockets current counter state
          if (sendRequired)
          {
            // Send
            if (ws.SendTask == null)
            {
              ws.SendBuffer = Encoding.UTF8.GetBytes(CurrentCounters.GetCountersData());
              ws.SendTask = ws.Conn.SendAsync(ws.SendBuffer, WebSocketMessageType.Text, true, CancellationToken.None);
            }
          }
        }

        if (sendRequired) { CurrentCounters.Dirty = false; }
      }

      Thread.Sleep(100);
    }
  }

  public static void ParseChatMessage(string msg, string chatMessageID)
  {
    if (msg.Length == 0 || msg == "help")
    {
      // Send "!counter help"
      Chat.AddMessageResponseToQueue(string.Concat(
        "Control on screen counters. ",
        "\"!counter <action> <counter name>\". ",
        "Action can be: add, remove, ++, -- or value. ",
        "Example 1, increase value of \"first counter\": !counter ++ first counter. ",
        "Example 2, add new counter with name \"new counter\": !counter add new counter."),
        chatMessageID);
      return;
    }

    int temp = msg.IndexOf(' ');
    if (temp == -1) { return; }
    // data[0] == action (e.g. add, remove, ++, --, <value> - set to value)
    // data[1] == counter name
    string[] data = new string[2];
    data[0] = msg[..temp].Trim().ToLower();
    data[1] = msg[(temp + 1)..].Trim();
    if (data[0]?.Length == 0 || data[1]?.Length == 0) { return; }
    int value = -1;
    if (int.TryParse(data[0], out value)) { data[0] = "set_to_value"; }

    switch (data[0].ToLower())
    {
      case "add":
        // Adds new counter to current counters
        CurrentCounters.AddNew(data[1]);
        break;
      case "remove":
        // Removes existing counter from current counters
        CurrentCounters.Remove(data[1]);
        break;
      case "++":
        // Increases existing counter from current counters
        CurrentCounters.Increase(data[1]);
        break;
      case "--":
        // Decreases existing counter from current counters
        CurrentCounters.Decrease(data[1]);
        break;
      case "set_to_value":
        // Sets existing counter to provided value
        CurrentCounters.SetToValue(data[1], value);
        break;
    }
  }

  public static void AddNewWebsocketConnection(ref WebSocketConnection ws)
  {
    lock (WsConnections)
    {
      WsConnections.Add(ws);
    }
    CurrentCounters.Dirty = true;
  }
}

/// <summary> Object representing currently active counters. </summary>
public class Counters
{
  /// <summary> Name of counters collection </summary>
  public string Name;
  /// <summary> List of counter positions with values </summary>
  private readonly List<CCounter> _counters = new();
  public bool Dirty = true;

  public void AddNew(string name)
  {
    // Check if provided name already exists, compare case insensitive
    string n = name.ToLower();
    foreach (var s in _counters) { if (s.Name.ToLower() == n) { return; } }

    _counters.Add(new CCounter() { Name = name });
    Database.UpdateCounterData(this);
  }

  public void Remove(string name)
  {
    string n = name.ToLower();
    for (int i = _counters.Count - 1; i >= 0; i--)
    {
      if (_counters[i].Name.ToLower() == n)
      {
        _counters.RemoveAt(i);
        break;
      }
    }
    Database.UpdateCounterData(this);
  }

  public void Increase(string name, int count = 1)
  {
    string n = name.ToLower();
    for (int i = 0; i < _counters.Count; i++)
    {
      if (_counters[i].Name.ToLower() == n)
      {
        _counters[i].Value += count;
        break;
      }
    }
    Database.UpdateCounterData(this);
  }

  public void Decrease(string name, int count = 1) { Increase(name, -count); }

  public void SetToValue(string name, int value)
  {
    string n = name.ToLower();
    for (int i = 0; i < _counters.Count; i++)
    {
      if (_counters[i].Name.ToLower() == n)
      {
        _counters[i].Value = value;
        break;
      }
    }
    Database.UpdateCounterData(this);
  }

  public string GetCountersData()
  {
    JsonArray data = new();
    foreach (var c in _counters)
    {
      data.Add(new JsonArray() { c.Name, c.Value, c.IconPath });
    }
    return data.ToJsonString();
  }

  public void ParseCountersData(string s)
  {
    JsonArray arr = JsonNode.Parse(s).AsArray();
    CCounter c;
    foreach (JsonArray a in arr)
    {
      if (a.Count != 3) { continue; }
      c = new();

      for (int i = 0; i < a.Count; i++)
      {
        switch (i)
        {
          case 0:
            c.Name = a[i].GetValue<string>();
            break;
          case 1:
            c.Value = a[i].GetValue<int>();
            break;
          case 2:
            c.IconPath = a[i].GetValue<string>();
            break;
        }
      }
      _counters.Add(c);
    }
  }
}

public class CCounter
{
  public string Name;
  public int Value;
  public string IconPath;

  public CCounter()
  {
    Name = string.Empty;
    IconPath = "Resources/CounterIcon.png";
  }
}
