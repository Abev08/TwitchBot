using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using System.Threading;

namespace AbevBot
{
  public static class Events
  {
    public static bool Started { get; private set; }
    private static Thread EventsThread;
    private static ClientWebSocket WebSocketClient;
    private static HttpClient HttpClient = new();

    public static void Start()
    {
      if (Started) return;
      Started = true;

      MainWindow.ConsoleWarning(">> Starting events bot.");

      EventsThread = new Thread(Update)
      {
        Name = "Events thread",
        IsBackground = true
      };
      EventsThread.Start();
    }

    private static void Update()
    {
      WebSocketReceiveResult receiveResult;
      byte[] receiveBuffer = new byte[8192];
      string sessionID = null;
      string message;
      EventMessage messageDeserialized;
      int zeroBytesReceivedCounter = 0;
      ManualResetEvent resetEvent = new(false);

      while (true)
      {
        // Create WebSocket connection
        // We need to set client ID and auth before connecting
        if (WebSocketClient is null)
        {
          WebSocketClient = new();
          WebSocketClient.Options.SetRequestHeader("Client-Id", Config.Data[Config.Keys.BotClientID]);
          WebSocketClient.Options.SetRequestHeader("Authorization", $"Bearer {Config.Data[Config.Keys.BotOAuthToken]}");
          WebSocketClient.ConnectAsync(new Uri("wss://eventsub.wss.twitch.tv/ws"), CancellationToken.None).Wait();

          // Check if it worked
          receiveResult = WebSocketClient.ReceiveAsync(receiveBuffer, CancellationToken.None).Result;
          if (receiveResult.Count <= 0) { MainWindow.ConsoleWarning(">> Events bot couldn't connect to wss://eventsub.wss.twitch.tv/ws."); }
          else
          {
            MainWindow.ConsoleWarning(">> Events bot connected.");
            message = Encoding.UTF8.GetString(receiveBuffer, 0, receiveResult.Count);
            // Parse welcome message
            WelcomeMessage welcomeMessage = WelcomeMessage.Deserialize(message);
            if (welcomeMessage?.Payload?.Session?.ID is null) throw new Exception(">> Couldn't read session ID.");
            else sessionID = welcomeMessage.Payload.Session.ID;

            // Subscribe to every event you want to
            // We have <10 sec to subscribe to an event, also another connection has to be used because we can't send messages to websocket server
            bool subscriptionSuccessful = false;
            subscriptionSuccessful |= Subscribe("channel.follow", "2", sessionID); // Channel got new follow
            subscriptionSuccessful |= Subscribe("channel.subscribe", "1", sessionID); // Channel got new subscription
            subscriptionSuccessful |= Subscribe("channel.subscription.gift", "1", sessionID); // Channel got gift subscription
            subscriptionSuccessful |= Subscribe("channel.subscription.message", "1", sessionID); // Channel got resubscription
            subscriptionSuccessful |= Subscribe("channel.cheer", "1", sessionID); // Channel got cheered
            subscriptionSuccessful |= Subscribe("channel.channel_points_custom_reward_redemption.add", "1", sessionID); // User redeemed channel points

            if (!subscriptionSuccessful)
            {
              MainWindow.ConsoleWarning(">> Events bot: every subscription failed, websocket connection would get disconnected every 10 seconds, closing events bot!");
              return;
            }
          }

          zeroBytesReceivedCounter = 0;
        }

        while (WebSocketClient.State == WebSocketState.Open)
        {
          receiveResult = WebSocketClient.ReceiveAsync(receiveBuffer, CancellationToken.None).Result;
          if (receiveResult.Count > 0)
          {
            zeroBytesReceivedCounter = 0;

            message = Encoding.UTF8.GetString(receiveBuffer, 0, receiveResult.Count);
            messageDeserialized = EventMessage.Deserialize(message);
            if (messageDeserialized?.Metadata?.MessageType?.Equals("session_keepalive") == true)
            {
              // Keep alive message, if it wasn't received in "keepalive_timeout_seconds" time from welcome message the connection should be restarted
              // MainWindow.ConsoleWarning(">> Events bot got keepalive message.");
            }
            else if (messageDeserialized?.Metadata?.MessageType?.Equals("notification") == true)
            {
              // Received notification event
              if (messageDeserialized?.Metadata?.SubscriptionType?.Equals("channel.follow") == true)
              {
                // Received channel follow event
                MainWindow.ConsoleWarning($">> New follow from {messageDeserialized?.Payload?.Event?.UserName}.");
              }
              else
              {
                // Some other notification message, print it
                MainWindow.ConsoleWriteLine(message);
              }
            }
            else
            {
              // Some other message, print it
              MainWindow.ConsoleWriteLine(message);
            }
          }
          else
          {
            MainWindow.ConsoleWarning(">> Events bot received 0 bytes.");
            zeroBytesReceivedCounter++;
            if (zeroBytesReceivedCounter >= 5)
            {
              // Close connection if 5 times in a row received 0 bytes
              MainWindow.ConsoleWarning(">> Events bot connection lost, waiting 2 sec to reconnect.");
              WebSocketClient.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
              Thread.Sleep(2000);
              WebSocketClient = null;
            }
          }

          Thread.Sleep(100);
        }

        if (WebSocketClient.State != WebSocketState.Open)
        {
          MainWindow.ConsoleWarning($">> Events bot connection lost, waiting 2 sec to reconnect. {WebSocketClient.CloseStatus} {WebSocketClient.CloseStatusDescription}");
          WebSocketClient.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
          Thread.Sleep(2000);
          WebSocketClient = null;
        }
      }
    }

    private static bool Subscribe(string type, string version, string sessionID)
    {
      // https://dev.twitch.tv/docs/eventsub/eventsub-subscription-types/
      MainWindow.ConsoleWarning($">> Events bot subscribing to {type} event.");
      using (HttpRequestMessage request = new HttpRequestMessage(new HttpMethod("POST"), "https://api.twitch.tv/helix/eventsub/subscriptions"))
      {
        request.Headers.Add("Client-Id", Config.Data[Config.Keys.BotClientID]);
        request.Headers.Add("Authorization", $"Bearer {Config.Data[Config.Keys.BotOAuthToken]}");
        request.Content = new StringContent(new SubscriptionMessage(type, version, Config.Data[Config.Keys.ChannelID], sessionID).ToJsonString());
        request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json");
        ResponseMessage response = ResponseMessage.Deserialize(HttpClient.SendAsync(request).Result.Content.ReadAsStringAsync().Result);
        if (response.Error != null) { MainWindow.ConsoleWarning(string.Concat(">> Events bot subscription error: ", response.Message)); }
        else
        {
          MainWindow.ConsoleWarning(string.Concat(">> Events bot subscription response: ", response.Data?[0].Type, " ", response.Data?[0].Status, "."));
          return true;
        }
      }
      return false;
    }
  }
}
