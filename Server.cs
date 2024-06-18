using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

using Serilog;

namespace AbevBot;

/// <summary> HTTP server with WebSocket connection to play notifications (instead of in the main window). </summary>
public static class Server
{
  public static bool IsStarted { get; private set; }
  /// <summary> HTTP server address. </summary>
  public static IPAddress IP { get; private set; } = IPAddress.Parse("127.0.0.1");
  /// <summary> HTTP server port. </summary>
  public static ushort Port { get; private set; } = 40000;
  /// <summary> Server thread. </summary>
  private static readonly Thread ServerThread = new(Update) { IsBackground = true };
  /// <summary> Is HTTP server required? </summary>
  private static bool IsServerRestartRequired = true;
  /// <summary> WebSocket message send queue. </summary>
  private static readonly List<byte[]> WsSendQueue = new();
  /// <summary> Current audio data that should be played. </summary>
  public static byte[] CurrentAudio { get; set; }
  /// <summary> Browser views video playing ended. </summary>
  public static bool VideoEnded { get; private set; } = true;
  /// <summary> Amount of browser views on which the video playing has ended. </summary>
  private static int VideoEndedCounter;
  /// <summary> Browser views audio playing ended. </summary>
  public static bool AudioEnded { get; private set; } = true;
  /// <summary> Amount of browser views on which the audio playing has ended. </summary>
  private static int AudioEndedCounter;

  /// <summary> Starts the HTTP server. </summary>
  public static void Start()
  {
    if (IsStarted) { return; }
    IsStarted = true;

    // If everything is ok, start the server
    ServerThread.Start();

    Log.Information("HTTP server started at: {address}", $"http://{IP}:{Port}");
  }

  /// <summary> Updates IP address used by the HTTP server. </summary>
  /// <param name="ip">IP address</param>
  /// <param name="port">Port</param>
  public static void UpdateIPAddress(string ip, ushort port)
  {
    // Try to parse provided IP address
    if (!IPAddress.TryParse(ip, out var _ip) || ip is null)
    {
      Log.Error("HTTP server provided IP address is not recognized!");
      return;
    }

    UpdateIPAddress(_ip, port);
  }

  /// <summary> Updates IP address used by the HTTP server. </summary>
  /// <param name="ip">IP address</param>
  /// <param name="port">Port</param>
  public static void UpdateIPAddress(IPAddress ip, ushort port)
  {
    // Check if provided IP address is accessible
    string strHostName = Dns.GetHostName();
    IPHostEntry ipEntry = Dns.GetHostEntry(strHostName);
    IPAddress[] addr = ipEntry.AddressList;
    bool ipOk = false;
    foreach (var address in addr)
    {
      if (ip.Equals(address))
      {
        ipOk = true;
        break;
      }
    }
    if (!ipOk)
    {
      Log.Error("HTTP server provided IP address is not accessible!");
      return;
    }

    IsServerRestartRequired = true;
  }

  /// <summary> Main update method used by the HTTP server thread. </summary>
  private static void Update()
  {
    // Create and start the HTTP server
    TcpListener httpServer = null;
    var buffer = new byte[65535];

    // Create and start WebSocket server
    List<WebSocketConnection> wsConnections = new();
    // string wsMsg = string.Empty;
    byte[] wsSendBuffer = null;
    bool wsSendTaskActive = false;

    while (true)
    {
      if (MainWindow.CloseRequested) { return; }

      if (IsServerRestartRequired || httpServer is null)
      {
        IsServerRestartRequired = false;

        // Close all of the current ws connections
        for (int i = wsConnections.Count - 1; i >= 0; i--)
        {
          wsConnections[i].Conn.CloseAsync(WebSocketCloseStatus.InternalServerError, null, CancellationToken.None);
          wsConnections.RemoveAt(i);
        }

        // Restart the server
        httpServer?.Stop();
        httpServer = new(IP, Port);
        httpServer.Start();
      }

      // Handle HTTP server
      if (httpServer.Pending())
      {
        var conn = httpServer.AcceptTcpClient();
        var stream = conn.GetStream();
        var length = stream.Read(buffer, 0, buffer.Length);
        var incomingMessage = Encoding.UTF8.GetString(buffer, 0, length).Split("\r\n");
        var metadata = incomingMessage[0].Split(' ');
        string secWebSocketKey = string.Empty;
        if (metadata.Length != 3) { Log.Warning("HTTP server received bad request: {request}", incomingMessage[0]); }
        else if (metadata[0] == "GET")
        {
          // Check connection type
          for (int i = 1; i < incomingMessage.Length; i++)
          {
            if (incomingMessage[i].StartsWith("Connection:"))
            {
              var type = incomingMessage[i][12..];
              if (type != "Upgrade") { break; }
            }
            if (incomingMessage[i].StartsWith("Upgrade:"))
            {
              var type = incomingMessage[i][9..];
              if (type != "websocket") { break; }
            }
            if (incomingMessage[i].StartsWith("Sec-WebSocket-Key:"))
            {
              secWebSocketKey = incomingMessage[i][19..];
              break;
            }
          }

          if (secWebSocketKey.Length > 0)
          {
            // Upgrade tcp connection to websocket
            Log.Information("HTTP server new request: {request}", "WebSocket upgrade");
            var key = secWebSocketKey + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
            byte[] keyHashed = SHA1.HashData(Encoding.UTF8.GetBytes(key));
            string keyBase64 = Convert.ToBase64String(keyHashed);

            byte[] response = Encoding.UTF8.GetBytes(string.Concat(
              "HTTP/1.1 101 Switching Protocols\r\n",
              "Connection: Upgrade\r\n",
              "Upgrade: websocket\r\n",
              "Sec-WebSocket-Accept: ", keyBase64,
              "\r\n\r\n"));
            stream.Write(response);

            var ws = new WebSocketConnection(
              WebSocket.CreateFromStream(stream, true, null, TimeSpan.FromSeconds(1)));

            switch (metadata[1])
            {
              case "/":
                // Websocket connection to main notification view
                wsConnections.Add(ws);
                break;
              case "/counter":
                // Websocket connection to counter view
                Counter.AddNewWebsocketConnection(ref ws);
                break;
              default:
                // Unrecognized websocket connection? Just close it
                stream.Close();
                break;
            }
          }
          else
          {
            Log.Information("HTTP server new request: {request}", metadata[1]);
            byte[] response = null;

            if (metadata[1] == "/")
            {
              var html = GetFileOrEmbedded("client.html");
              response = Encoding.UTF8.GetBytes(string.Concat(
                "HTTP/1.1 200 OK\r\n",
                "Content-Length: ", html.Length, "\r\n",
                "Content-Type: text/html\r\n\r\n",
                html,
                "\r\n\r\n"));
            }
            else if (metadata[1] == "/client.js")
            {
              var js = GetFileOrEmbedded("client.js");
              js = "let fromServer = true;\r\n" + js;
              response = Encoding.UTF8.GetBytes(string.Concat(
                "HTTP/1.1 200 OK\r\n",
                "Content-Length: ", js.Length, "\r\n",
                "Content-Type: text/javascript\r\n\r\n",
                js,
                "\r\n\r\n"));
            }
            else if (metadata[1] == "/favicon.ico")
            {
              response = Encoding.UTF8.GetBytes("HTTP/1.1 404 Not Found\r\n\r\n");
            }
            else if (metadata[1] == "/audio")
            {
              if (CurrentAudio != null)
              {
                // Create repsonse header
                var header = Encoding.UTF8.GetBytes(string.Concat(
                    "HTTP/1.1 200 OK\r\n",
                    "Content-Length: ", CurrentAudio.Length, "\r\n",
                    $"Content-Type: audio/wav\r\n\r\n"));
                response = new byte[header.Length + CurrentAudio.Length];
                Array.Copy(header, response, header.Length);
                Array.Copy(CurrentAudio, 0, response, header.Length, CurrentAudio.Length);
              }
            }
            else if (metadata[1].StartsWith("/Resources"))
            {
              FileInfo file = new(Uri.UnescapeDataString(metadata[1][1..]));
              bool error = !file.Exists;

              // do {} while (false) loop for easy breaks
              do
              {
                if (error) break;

                // Check if requested file is in Resources direcotry
                DirectoryInfo resources = new("Resources");
                error = !resources.Exists;
                if (error) break;
                var parentDir = file.Directory;
                while (!resources.FullName.Equals(parentDir.FullName))
                {
                  parentDir = parentDir.Parent;
                  if (parentDir is null)
                  {
                    error = true;
                    break;
                  }
                }
                if (error) break;

                // Get content type
                string contentType;
                if (Array.IndexOf(Notifications.SupportedVideoFormats, file.Extension) >= 0) { contentType = "video"; }
                else if (Array.IndexOf(Notifications.SupportedAudioFormats, file.Extension) >= 0) { contentType = "audio"; }
                else if (Array.IndexOf(Notifications.SupportedImageFormats, file.Extension) >= 0) { contentType = "image"; }
                else
                {
                  error = true;
                  break;
                }

                // Create repsonse header
                var header = Encoding.UTF8.GetBytes(string.Concat(
                    "HTTP/1.1 200 OK\r\n",
                    "Content-Length: ", file.Length, "\r\n",
                    $"Content-Type: {contentType}/{file.Extension[1..]}\r\n\r\n"));
                response = new byte[header.Length + file.Length];
                Array.Copy(header, response, header.Length);

                // Append the file
                using var s = file.OpenRead();
                s.Read(response, header.Length, (int)file.Length);
              } while (false);

              if (error) { response = Encoding.UTF8.GetBytes("HTTP/1.1 404 Not Found\r\n\r\n"); }
            }
            else if (metadata[1] == "/counter")
            {
              var html = GetFileOrEmbedded("counter.html");
              response = Encoding.UTF8.GetBytes(string.Concat(
                "HTTP/1.1 200 OK\r\n",
                "Content-Length: ", html.Length, "\r\n",
                "Content-Type: text/html\r\n\r\n",
                html,
                "\r\n\r\n"));
            }
            else if (metadata[1] == "/counter.js")
            {
              var js = GetFileOrEmbedded("counter.js");
              js = "let fromServer = true;\r\n" + js;
              response = Encoding.UTF8.GetBytes(string.Concat(
                "HTTP/1.1 200 OK\r\n",
                "Content-Length: ", js.Length, "\r\n",
                "Content-Type: text/javascript\r\n\r\n",
                js,
                "\r\n\r\n"));
            }
            else { response = Encoding.UTF8.GetBytes("HTTP/1.1 404 Not Found\r\n\r\n"); }

            if (response?.Length > 0) { stream.Write(response); }
            stream.Close();
          }
        }
        else
        {
          stream.Close();
          Log.Error("HTTP server not handled request: {msg}", incomingMessage[0]);
        }
      }

      // Check if there is a message to be send via websocket connection
      lock (WsSendQueue)
      {
        if (!wsSendTaskActive && WsSendQueue.Count > 0)
        {
          wsSendBuffer = WsSendQueue[0];
          WsSendQueue.RemoveAt(0);
        }
      }
      wsSendTaskActive = false;

      // Handle WebSocket server
      for (int i = wsConnections.Count - 1; i >= 0; i--)
      {
        var ws = wsConnections[i];
        if (ws.Conn.State != WebSocketState.Open)
        {
          ws.Conn.Dispose();
          wsConnections.RemoveAt(i);
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
            else if (msg == "video_end") { VideoEndedCounter += 1; }
            else if (msg == "audio_end") { AudioEndedCounter += 1; }
          }
          ws.ReceiveTask = null;
        }

        // Send
        if (ws.SendTask != null) { wsSendTaskActive = true; }
        else if (wsSendBuffer?.Length > 0)
        {
          // Fill the send buffer
          ws.SendBuffer = new byte[wsSendBuffer.Length];
          Array.Copy(wsSendBuffer, ws.SendBuffer, wsSendBuffer.Length);

          // Send the message
          ws.SendTask = ws.Conn.SendAsync(ws.SendBuffer, WebSocketMessageType.Text, true, CancellationToken.None);
        }
      }
      wsSendBuffer = null;

      VideoEnded = VideoEndedCounter >= wsConnections.Count;
      AudioEnded = AudioEndedCounter >= wsConnections.Count;

      Thread.Sleep(10);
    }
  }

  /// <summary> Sends "clear" command to every connected HTTP client. </summary>
  public static void ClearAll()
  {
    var msg = Encoding.UTF8.GetBytes(new JsonObject()
      {
        { "type", "clear_all" },
      }.ToJsonString());

    VideoEndedCounter = int.MaxValue;
    AudioEndedCounter = int.MaxValue;

    lock (WsSendQueue)
    {
      WsSendQueue.Add(msg);
    }
  }

  /// <summary> Sends "clear_video" command to every connected HTTP client. </summary>
  public static void ClearVideo()
  {
    var msg = Encoding.UTF8.GetBytes(new JsonObject()
      {
        { "type", "clear_video" },
      }.ToJsonString());

    VideoEndedCounter = int.MaxValue;

    lock (WsSendQueue)
    {
      WsSendQueue.Add(msg);
    }
  }

  /// <summary> Sends "clear_audio" command to every connected HTTP client. </summary>
  public static void ClearAudio()
  {
    var msg = Encoding.UTF8.GetBytes(new JsonObject()
      {
        { "type", "clear_audio" },
      }.ToJsonString());

    AudioEndedCounter = int.MaxValue;

    lock (WsSendQueue)
    {
      WsSendQueue.Add(msg);
    }
  }

  /// <summary> Sends "clear_text" command to every connected HTTP client. </summary>
  public static void ClearText()
  {
    var msg = Encoding.UTF8.GetBytes(new JsonObject()
      {
        { "type", "clear_text" },
      }.ToJsonString());

    lock (WsSendQueue)
    {
      WsSendQueue.Add(msg);
    }
  }

  /// <summary> Sends "display text" command to every connected HTTP client. </summary>
  /// <param name="text">Text to be displayed</param>
  /// <param name="position">Text position</param>
  /// <param name="size">Text size</param>
  public static void DisplayText(string text, Notifications.TextPosition position, double size = -1)
  {
    if (text is null || text.Length == 0) return;

    var msg = Encoding.UTF8.GetBytes(new JsonObject()
      {
        { "type", "new_notification" },
        { "text", text },
        { "text_position", position.ToString() },
        { "text_size", size }
      }.ToJsonString());

    lock (WsSendQueue)
    {
      WsSendQueue.Add(msg);
    }
  }

  /// <summary> Sends "play video" command to every connected HTTP client. </summary>
  /// <param name="videoPath">Path to the video file</param>
  /// <param name="left">Video player position from left edge of the screen</param>
  /// <param name="top">Video player position from top of the screen</param>
  /// <param name="width">Video player width</param>
  /// <param name="height">Video player height</param>
  /// <param name="volume">Volume of video player</param>
  public static void PlayVideo(string videoPath, float left, float top, float width, float height, float volume)
  {
    if (videoPath is null || videoPath.Length == 0) return;

    FileInfo videoFile = new(videoPath);
    if (!videoFile.Exists)
    {
      Log.Warning("Video file not found: {file}", videoFile.FullName);
      return;
    }

    VideoEndedCounter = 0;

    var msg = Encoding.UTF8.GetBytes(new JsonObject()
      {
        { "type", "new_notification" },
        { "video", videoPath },
        { "video_position", new JsonArray(left, top) },
        { "video_size", new JsonArray(width, height) },
        { "video_volume", volume }
      }.ToJsonString());

    lock (WsSendQueue)
    {
      WsSendQueue.Add(msg);
    }
  }

  /// <summary> Sends "play audio" command to every connected HTTP client. </summary>
  /// <param name="audioPath">Path to audio file</param>
  /// <param name="volume">Volume</param>
  public static void PlayAudio(string audioPath, float volume)
  {
    if (audioPath is null || audioPath.Length == 0) return;

    FileInfo audioFile = new(audioPath);
    if (!audioFile.Exists)
    {
      Log.Warning("Audio file not found: {file}", audioFile.FullName);
      return;
    }

    var msg = Encoding.UTF8.GetBytes(new JsonObject()
      {
        { "type", "new_notification" },
        { "audio", audioPath },
        { "audio_volume", volume }
      }.ToJsonString());

    AudioEndedCounter = 0;

    lock (WsSendQueue)
    {
      WsSendQueue.Add(msg);
    }
  }

  /// <summary> Sends "play video" command to every connected HTTP client. Uses audio set by current notification. </summary>
  /// <param name="volume">Volume</param>
  public static void PlayAudio(float volume)
  {
    var msg = Encoding.UTF8.GetBytes(new JsonObject()
      {
        { "type", "new_notification" },
        { "audio", "audio" },
        { "audio_volume", volume }
      }.ToJsonString());

    AudioEndedCounter = 0;

    lock (WsSendQueue)
    {
      WsSendQueue.Add(msg);
    }
  }

  /// <summary> Sends "pause" command to every connected HTTP client. </summary>
  public static void Pause()
  {
    var msg = Encoding.UTF8.GetBytes(new JsonObject()
      {
        { "type", "pause" },
      }.ToJsonString());

    lock (WsSendQueue)
    {
      WsSendQueue.Add(msg);
    }
  }

  /// <summary> Sends "resume" command to every connected HTTP client. </summary>
  public static void Resume()
  {
    var msg = Encoding.UTF8.GetBytes(new JsonObject()
      {
        { "type", "resume" },
      }.ToJsonString());

    lock (WsSendQueue)
    {
      WsSendQueue.Add(msg);
    }
  }

  /// <summary> Sends "gamba animation" command to every connected HTTP client. </summary>
  /// <param name="animationFilePath">Relative path to a file with animation that should be played</param>
  /// <param name="name">Name of a chatter that does the gamba</param>
  /// <param name="value">Amount of gambled points</param>
  public static void GambaAnimation(string animationFilePath, string name, int points_rolled, int points_received)
  {
    var msg = Encoding.UTF8.GetBytes(new JsonObject()
      {
        { "type", "gamba_animation" },
        { "gamba", animationFilePath },
        { "gamba_name", name },
        { "gamba_points_rolled", points_rolled },
        { "gamba_points_received", points_received },
      }.ToJsonString());

    lock (WsSendQueue)
    {
      WsSendQueue.Add(msg);
    }
  }

  public static string GetFileOrEmbedded(string name)
  {
    string data = string.Empty;
    var file = new FileInfo($"server/{name}");
    // If the file exists return it's content, otherwise get embedded data
    if (file.Exists) { data = File.ReadAllText(file.FullName); }
    else
    {
      using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"AbevBot.server.{name}");
      if (stream is null) { Log.Error("File {file} not found!", file.FullName); }
      else
      {
        using var reader = new StreamReader(stream);
        data = reader.ReadToEnd();
      }
    }
    return data;
  }
}

/// <summary> HTTP server WebSocket "wrapper". </summary>
public class WebSocketConnection
{
  /// <summary> The connection. </summary>
  public WebSocket Conn { get; init; }
  /// <summary> Connection receive async task. </summary>
  public Task<WebSocketReceiveResult> ReceiveTask { get; set; }
  /// <summary> Receive buffer. </summary>
  public byte[] ReceiveBuffer { get; set; } = new byte[65535];
  /// <summary> Connection send async task. </summary>
  public Task SendTask { get; set; }
  /// <summary> Send buffer. </summary>
  public byte[] SendBuffer { get; set; }

  /// <summary> Creates new HTTP server WebSocket "wrapper". </summary>
  /// <param name="ws">WebSocket connection</param>
  public WebSocketConnection(WebSocket ws) { Conn = ws; }
}
