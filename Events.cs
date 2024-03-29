using System;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Threading;

namespace AbevBot;

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
  private static readonly HttpClient HttpClient = new();

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

    while (true)
    {
      // Create WebSocket connection
      if (WebSocketClient is null)
      {
        WebSocketClient = new();
        WebSocketClient.Options.SetRequestHeader("Client-Id", Secret.Data[Secret.Keys.CustomerID]);
        WebSocketClient.Options.SetRequestHeader("Authorization", $"Bearer {Secret.Data[Secret.Keys.OAuthToken]}");
        try { WebSocketClient.ConnectAsync(new Uri(WEBSOCKETURL), CancellationToken.None).Wait(); }
        catch (AggregateException ex) { MainWindow.ConsoleWarning($">> Events bot error: {ex.Message}"); }

        // Check if it worked
        if (WebSocketClient.State == WebSocketState.Open) receiveResult = WebSocketClient.ReceiveAsync(receiveBuffer, CancellationToken.None).Result;
        else receiveResult = null;

        if (receiveResult?.Count > 0)
        {
          MainWindow.ConsoleWarning(">> Events bot connected.");
          message = Encoding.UTF8.GetString(receiveBuffer, 0, receiveResult.Count);
          // Parse welcome message
          WelcomeMessage welcomeMessage = WelcomeMessage.Deserialize(message);
          if (welcomeMessage?.Payload?.Session?.ID is null)
          {
            MainWindow.ConsoleWarning(">> Event bot error. Couldn't read session ID.");
            WebSocketClient.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
          }
          else
          {
            sessionID = welcomeMessage.Payload.Session.ID;

            // Subscribe to every event you want to, https://dev.twitch.tv/docs/eventsub/eventsub-subscription-types
            // We have <10 sec to subscribe to an event, also another connection has to be used because we can't send messages to websocket server
            bool anySubscriptionSucceeded = false;
            anySubscriptionSucceeded |= Subscribe("channel.follow", "2", sessionID); // Channel got new follow
            anySubscriptionSucceeded |= Subscribe("channel.subscribe", "1", sessionID); // Channel got new subscription
            anySubscriptionSucceeded |= Subscribe("channel.subscription.gift", "1", sessionID); // Channel got gift subscription
            anySubscriptionSucceeded |= Subscribe("channel.subscription.message", "1", sessionID); // Channel got resubscription
            anySubscriptionSucceeded |= Subscribe("channel.cheer", "1", sessionID); // Channel got cheered
            anySubscriptionSucceeded |= Subscribe("channel.channel_points_custom_reward_redemption.add", "1", sessionID); // User redeemed channel points
            anySubscriptionSucceeded |= Subscribe("channel.hype_train.progress", "1", sessionID); // A Hype Train makes progress on the specified channel
            anySubscriptionSucceeded |= Subscribe("channel.ban", "1", sessionID); // A viewer is banned from the specified channel

            if (!anySubscriptionSucceeded)
            {
              MainWindow.ConsoleWarning(">> Events bot: every subscription failed, websocket connection would get disconnected every 10 seconds, closing events bot!");
              return;
            }
          }
        }
        else { MainWindow.ConsoleWarning($">> Events bot couldn't connect to {WEBSOCKETURL}."); }

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
              if (payload?.Event?.UserName?.Length > 0)
              {
                Chatter c = Chatter.GetChatterByID(long.Parse(payload.Event.UserID), payload.Event.UserName);
                if (c.LastTimeFollowed.Date != DateTime.Now.Date)
                {
                  c.SetLastTimeFollowedToNow();
                  MainWindow.ConsoleWarning($">> New follow from {c.Name}.");
                  Notifications.CreateFollowNotification(payload?.Event?.UserName);
                }
                else { MainWindow.ConsoleWarning($">> {c.Name} refollowed again in the same day."); }
              }
            }
            else if (messageDeserialized?.Metadata?.SubscriptionType?.Equals("channel.subscribe") == true)
            {
              // Received subscription event
              Payload payload = Payload.Deserialize(messageDeserialized?.Payload);
              if (payload?.Event?.IsGift == true)
              {
                MainWindow.ConsoleWarning($">> {payload?.Event?.UserName} received a gift subscription.");
                Notifications.CreateReceiveGiftSubscriptionNotification(payload?.Event?.UserName, payload?.Subscription?.CreatedAt);
              }
              else
              {
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
              Notifications.CreateGiftSubscriptionNotification(
                payload?.Event?.IsAnonymous == true ? null : payload?.Event?.UserName, payload?.Event?.Tier,
                (int)payload?.Event?.TotalGifted,
                payload?.Event?.Message?.Text,
                payload?.Subscription?.CreatedAt);
              // MainWindow.ConsoleWriteLine(message);
            }
            else if (messageDeserialized?.Metadata?.SubscriptionType?.Equals("channel.subscription.message") == true)
            {
              // Received subscription with message event
              Payload payload = Payload.Deserialize(messageDeserialized?.Payload);
              MainWindow.ConsoleWarning($">> New subscription from {payload?.Event?.UserName}. {payload?.Event?.Message.Text}");
              Notifications.CreateSubscriptionNotification(
                payload?.Event?.UserName, payload?.Event?.Tier,
                (int)payload?.Event?.MonthsDuration.Value,
                (int)payload?.Event?.MonthsStreak.Value,
                (int)payload?.Event?.MonthsCumulative.Value,
                payload?.Event?.Message);
              // MainWindow.ConsoleWriteLine(message);
            }
            else if (messageDeserialized?.Metadata?.SubscriptionType?.Equals("channel.cheer") == true)
            {
              // Received cheer event
              PayloadCheer payload = PayloadCheer.Deserialize(messageDeserialized?.Payload);
              MainWindow.ConsoleWarning($">> {payload?.Event?.UserName} cheered with {payload?.Event?.Bits} bits.");
              Notifications.CreateCheerNotification(payload?.Event?.UserName, (int)payload?.Event?.Bits.Value, payload?.Event?.Message);
              // MainWindow.ConsoleWriteLine(message);
            }
            else if (messageDeserialized?.Metadata?.SubscriptionType?.Equals("channel.channel_points_custom_reward_redemption.add") == true)
            {
              // Received channel points redemption event
              Payload payload = Payload.Deserialize(messageDeserialized?.Payload);
              MainWindow.ConsoleWarning($">> {payload?.Event?.UserName} redeemed ID: {payload?.Event?.Reward?.ID} with channel points.");
              Notifications.CreateRedemptionNotificaiton(payload?.Event?.UserName, payload?.Event?.Reward?.ID, payload?.Event?.ID, payload?.Event?.Reward?.Prompt);
              // MainWindow.ConsoleWriteLine(message);
            }
            else if (messageDeserialized?.Metadata?.SubscriptionType?.Equals("channel.ban") == true)
            {
              // Received user banned event
              Payload payload = Payload.Deserialize(messageDeserialized?.Payload);
              if (payload?.Event != null)
              {
                if (payload.Event.IsPermanent == true)
                {
                  MainWindow.ConsoleWarning($">> {payload.Event.UserName} has been permanently banned. {payload.Event.Reason}.");
                  Notifications.CreateBanNotification(payload.Event.UserName, payload.Event.Reason);
                }
                else
                {
                  var duration = DateTime.Parse(payload.Event.EndsAt) - DateTime.Parse(payload.Event.BannedAt);
                  MainWindow.ConsoleWarning($">> {payload.Event.UserName} was timed out for {duration}. {payload.Event.Reason}.");
                  Notifications.CreateTimeoutNotification(payload.Event.UserName, duration, payload.Event.Reason);
                }
              }
            }
            else if (messageDeserialized?.Metadata?.SubscriptionType?.Equals("channel.hype_train.progress") == true)
            {
              // TODO: Delete after testing, temporary event messages logging
              {
                try
                {
                  System.IO.File.AppendAllText($"eventlog_{DateTime.Now:d}.log", $"{DateTime.Now:G}\r\n");
                  System.IO.File.AppendAllText($"eventlog_{DateTime.Now:d}.log", message);
                  System.IO.File.AppendAllText($"eventlog_{DateTime.Now:d}.log", "\r\n\r\n");
                }
                catch { }
              }
              // Received hype train progress event
              //PayloadHypeTrain payload = PayloadHypeTrain.Deserialize(messageDeserialized?.Payload);
            }
            else
            {
              // Some other notification message, print it
              MainWindow.ConsoleWriteLine(message);
            }
          }
          else if (messageDeserialized?.Metadata?.MessageType?.Equals("notification") == true)
          {
            // Reconnect message
            MainWindow.ConsoleWarning(">> Event bot got session reconnect message. Should close the connection and use provided url as WEBSOCKETURL but not doing that because it's not mandatory.");
            // TODO: Think about it :)
            // The message is rare and is sent only when edge server that the client is connected to needs to be swapped.
            // Well if we would use it the events subscriptions are left untouched on the new url.
            // So a check would have to be implemented to not subscribe again on a reconnection from this message.
            // Also the previous connection should be left connected until new connection is established (received welcome message).
            // That would require a temporary websocket (would need to be implemented).
            // If it's not used some events may get missed during the reconnection? It's not good.
            // If it's used and two connections would be present at a certain time an event could be received two times?
            // An event ID check would have to be implemented not to parse the same events multiple times.
            // Well an event ID check should be implemented even without doing this because twtich may send the same event multiple times if he this that I might missed it
            // Also there is time limit for doint it - 30 sec. After that time Twitch force closes the first connection.
            // MainWindow.ConsoleWriteLine(message);
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
        if (WebSocketClient.State != WebSocketState.Closed && WebSocketClient.State != WebSocketState.Aborted) WebSocketClient.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
        Thread.Sleep(2000);
        WebSocketClient = null;
      }
    }
  }

  private static bool Subscribe(string type, string version, string sessionID)
  {
    // https://dev.twitch.tv/docs/eventsub/eventsub-subscription-types/
    MainWindow.ConsoleWarning($">> Events bot subscribing to {type} event.");
    using HttpRequestMessage request = new(HttpMethod.Post, SUBSCRIPTIONRUL);
    request.Content = new StringContent(new SubscriptionMessage(type, version, Config.Data[Config.Keys.ChannelID], sessionID).ToJsonString(), Encoding.UTF8, "application/json");
    request.Headers.Add("Client-Id", Secret.Data[Secret.Keys.CustomerID]);
    request.Headers.Add("Authorization", $"Bearer {Secret.Data[Secret.Keys.OAuthToken]}");

    string resp;
    try { resp = HttpClient.Send(request).Content.ReadAsStringAsync().Result; }
    catch (HttpRequestException ex) { MainWindow.ConsoleWarning($">> Events bot subscription request failed. {ex.Message}"); return false; }
    var response = ResponseMessage.Deserialize(resp);
    if (response.Error != null) { MainWindow.ConsoleWarning($">> Events bot subscription error: {response.Message}"); }
    else
    {
      MainWindow.ConsoleWarning(string.Concat(">> Events bot subscription response: ", response.Data?[0].Type, " ", response.Data?[0].Status, "."));
      return true;
    }
    return false;
  }
}
