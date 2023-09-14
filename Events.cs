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
#if true
    private const string WEBSOCKETURL = "wss://eventsub.wss.twitch.tv/ws";
    private const string SUBSCRIPTIONRUL = "https://api.twitch.tv/helix/eventsub/subscriptions";
#else
    // Test url with Twitch CLI client
    private const string WEBSOCKETURL = "ws://127.0.0.1:8080/ws";
    private const string SUBSCRIPTIONRUL = "http://127.0.0.1:8080/eventsub/subscriptions";
#endif

    /// <summary> Events bot started. </summary>
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
      string sessionID;
      string message;
      EventMessage messageDeserialized;
      int zeroBytesReceivedCounter = 0;
      ManualResetEvent resetEvent = new(false);

      while (true)
      {
        // Create WebSocket connection
        if (WebSocketClient is null)
        {
          WebSocketClient = new();
          WebSocketClient.Options.SetRequestHeader("Client-Id", Config.Data[Config.Keys.BotClientID]);
          WebSocketClient.Options.SetRequestHeader("Authorization", $"Bearer {Config.Data[Config.Keys.BotOAuthToken]}");
          try { WebSocketClient.ConnectAsync(new Uri(WEBSOCKETURL), CancellationToken.None).Wait(); }
          catch (AggregateException ex) { MainWindow.ConsoleWarning($">> Events bot error: {ex.Message}"); }

          // Check if it worked
          receiveResult = WebSocketClient.ReceiveAsync(receiveBuffer, CancellationToken.None).Result;
          if (receiveResult.Count <= 0) { MainWindow.ConsoleWarning($">> Events bot couldn't connect to {WEBSOCKETURL}."); }
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
            bool anySubscriptionSucceeded = false;
            anySubscriptionSucceeded |= Subscribe("channel.follow", "2", sessionID); // Channel got new follow
            anySubscriptionSucceeded |= Subscribe("channel.subscribe", "1", sessionID); // Channel got new subscription
            anySubscriptionSucceeded |= Subscribe("channel.subscription.gift", "1", sessionID); // Channel got gift subscription
            anySubscriptionSucceeded |= Subscribe("channel.subscription.message", "1", sessionID); // Channel got resubscription
            anySubscriptionSucceeded |= Subscribe("channel.cheer", "1", sessionID); // Channel got cheered
            anySubscriptionSucceeded |= Subscribe("channel.channel_points_custom_reward_redemption.add", "1", sessionID); // User redeemed channel points

            if (!anySubscriptionSucceeded)
            {
              MainWindow.ConsoleWarning(">> Events bot: every subscription failed, websocket connection would get disconnected every 10 seconds, closing events bot!");
              return;
            }
          }

          zeroBytesReceivedCounter = 0;
        }

        while (WebSocketClient.State == WebSocketState.Open)
        {
          // During debugging ReceiveAsync may return an exception when paused for too long
          try { receiveResult = WebSocketClient.ReceiveAsync(receiveBuffer, CancellationToken.None).Result; }
          catch (AggregateException ex) { MainWindow.ConsoleWarning($">> Events bot error: {ex.Message}"); receiveResult = null; }

          if (receiveResult?.Count > 0)
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
              // Received a notification
              if (messageDeserialized?.Metadata?.SubscriptionType?.Equals("channel.follow") == true)
              {
                // Received channel follow event
                Payload payload = Payload.Deserialize(messageDeserialized?.Payload);
                MainWindow.ConsoleWarning($">> New follow from {payload?.Event?.UserName}.");
                Notifications.CreateFollowNotification(payload?.Event?.UserName);
              }
              else if (messageDeserialized?.Metadata?.SubscriptionType?.Equals("channel.subscribe") == true)
              {
                // Received subscription event
                Payload payload = Payload.Deserialize(messageDeserialized?.Payload);
                if (payload?.Event?.IsGift == true)
                {
                  MainWindow.ConsoleWarning($">> {payload?.Event?.UserName} received a gift subscription.");
                  Notifications.CreateReceiveGiftSubscriptionNotification(payload?.Event?.UserName);
                }
                else
                {
                  // FIXME: fix subscription message, not present in twitch cli mock program?
                  MainWindow.ConsoleWarning($">> New subscription from {payload?.Event?.UserName}.");
                  Notifications.CreateSubscriptionNotification(payload?.Event?.UserName, payload?.Event?.Tier, "");
                }
                // MainWindow.ConsoleWriteLine(message);
              }
              else if (messageDeserialized?.Metadata?.SubscriptionType?.Equals("channel.subscription.gift") == true)
              {
                // Received gifted subscription event
                Payload payload = Payload.Deserialize(messageDeserialized?.Payload);
                MainWindow.ConsoleWarning($">> {payload?.Event?.UserName} gifted {payload?.Event?.TotalGifted} subscription(s).");
                // FIXME: fix subscription message, not present in twitch cli mock program?
                Notifications.CreateGiftSubscriptionNotification(payload?.Event?.UserName, payload?.Event?.Tier, (int)payload?.Event?.TotalGifted, "");
                // MainWindow.ConsoleWriteLine(message);
              }
              else if (messageDeserialized?.Metadata?.SubscriptionType?.Equals("channel.subscription.message") == true)
              {
                // Received subscription with message event
                Payload payload = Payload.Deserialize(messageDeserialized?.Payload);
                MainWindow.ConsoleWarning($">> New subscription from {payload?.Event?.UserName}. {payload?.Event?.Message.Text}");
                Notifications.CreateSubscriptionNotification(payload?.Event?.UserName, payload?.Event?.Tier, (int)payload?.Event?.MonthsDuration, (int)payload?.Event?.MonthsStreak, payload?.Event?.Message);
                // MainWindow.ConsoleWriteLine(message);
              }
              else if (messageDeserialized?.Metadata?.SubscriptionType?.Equals("channel.cheer") == true)
              {
                // Received cheer event
                PayloadCheer payload = PayloadCheer.Deserialize(messageDeserialized?.Payload);
                MainWindow.ConsoleWarning($">> {payload?.Event?.UserName} cheered with {payload?.Event?.Bits} bits.");
                Notifications.CreateCheerNotification(payload?.Event?.UserName, (int)payload?.Event?.Bits, payload?.Event?.Message);
                // MainWindow.ConsoleWriteLine(message);
              }
              else if (messageDeserialized?.Metadata?.SubscriptionType?.Equals("channel.channel_points_custom_reward_redemption") == true)
              {
                // Received channel points redemption event
                Payload payload = Payload.Deserialize(messageDeserialized?.Payload);
                MainWindow.ConsoleWarning($">> {payload?.Event?.UserName} redeemed something with channel points.");
                MainWindow.ConsoleWriteLine(message);
              }
              else if (messageDeserialized?.Metadata?.SubscriptionType?.Equals("channel.ban") == true)
              {
                // Received user banned event
                Payload payload = Payload.Deserialize(messageDeserialized?.Payload);
                if (payload?.Event.IsPermanent == true)
                {
                  MainWindow.ConsoleWarning($">> {payload?.Event?.UserName} has been permanently banned. {payload?.Event?.Reason}.");
                }
                else
                {
                  DateTime start = DateTime.Parse(payload?.Event.BannedAt);
                  DateTime end = DateTime.Parse(payload?.Event.EndsAt);
                  MainWindow.ConsoleWarning($">> {payload?.Event?.UserName} was banned for {end - start}. {payload?.Event?.Reason}.");
                }
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
      using (HttpRequestMessage request = new(new HttpMethod("POST"), SUBSCRIPTIONRUL))
      {
        request.Headers.Add("Client-Id", Config.Data[Config.Keys.BotClientID]);
        request.Headers.Add("Authorization", $"Bearer {Config.Data[Config.Keys.BotOAuthToken]}");
        request.Content = new StringContent(new SubscriptionMessage(type, version, Config.Data[Config.Keys.ChannelID], sessionID).ToJsonString());
        request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json");
        ResponseMessage response = ResponseMessage.Deserialize(HttpClient.Send(request).Content.ReadAsStringAsync().Result);
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
