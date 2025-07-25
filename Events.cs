using System;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;

using Serilog;

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

    Log.Information("Starting events bot.");

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
    string sessionID, message;
    int zeroBytesReceivedCounter = 0;

    while (true)
    {
      // Create WebSocket connection
      if (WebSocketClient is null)
      {
        WebSocketClient = new();
        WebSocketClient.Options.SetRequestHeader("Client-Id", Secret.Data[Secret.Keys.TwitchClientID]);
        WebSocketClient.Options.SetRequestHeader("Authorization", $"Bearer {Secret.Data[Secret.Keys.TwitchOAuthToken]}");
        try { WebSocketClient.ConnectAsync(new Uri(WEBSOCKETURL), CancellationToken.None).Wait(); }
        catch (AggregateException ex) { Log.Error("Events bot error: {ex}", ex.Message); }

        // Check if it worked
        if (WebSocketClient.State == WebSocketState.Open) { receiveResult = WebSocketClient.ReceiveAsync(receiveBuffer, CancellationToken.None).Result; }
        else { receiveResult = null; }

        if (receiveResult?.Count > 0)
        {
          Log.Information("Events bot connected.");
          message = Encoding.UTF8.GetString(receiveBuffer, 0, receiveResult.Count);
          // Parse welcome message
          var welcomeMessage = WelcomeMessage.Deserialize(message);
          if (welcomeMessage is null || welcomeMessage?.Payload?.Session?.ID is null)
          {
            Log.Warning("Event bot error. Couldn't read session ID.");
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
            anySubscriptionSucceeded |= Subscribe("channel.channel_points_automatic_reward_redemption.add", "1", sessionID); // A viewer has redeemed an automatic channel points reward on the specified channel
            anySubscriptionSucceeded |= Subscribe("channel.raid", "1", sessionID,
              new JsonObject { { "condition", new JsonObject { { "to_broadcaster_user_id", Config.Data[Config.Keys.ChannelID] } } } }); // A broadcaster raids another broadcaster’s channel

            if (!anySubscriptionSucceeded)
            {
              Log.Warning("Events bot every subscription failed, websocket connection would get disconnected every 10 seconds, closing events bot!");
              return;
            }
          }
        }
        else { Log.Warning("Events bot couldn't connect to {url}.", WEBSOCKETURL); }

        zeroBytesReceivedCounter = 0;
      }

      while (WebSocketClient.State == WebSocketState.Open)
      {
        // During debugging ReceiveAsync may return an exception when paused for too long
        try { receiveResult = WebSocketClient.ReceiveAsync(receiveBuffer, CancellationToken.None).Result; }
        catch (AggregateException ex) { Log.Error("Events bot error: {ex}", ex); receiveResult = null; }

        if (receiveResult?.Count > 0)
        {
          zeroBytesReceivedCounter = 0;

          message = Encoding.UTF8.GetString(receiveBuffer, 0, receiveResult.Count);
          JsonObject eventMsg = null;
          try { eventMsg = JsonSerializer.Deserialize<JsonObject>(message); }
          catch (Exception ex) { Log.Error("Event message deserialization failed. {ex}", ex); }

          if (eventMsg != null && eventMsg.ContainsKey("metadata") && eventMsg.ContainsKey("payload"))
          {
            switch (eventMsg["metadata"]["message_type"].ToString())
            {
              case "session_keepalive":
                // Keep alive message, if it wasn't received in "keepalive_timeout_seconds" time from welcome message the connection should be restarted
                // Log.Information("Events bot got keepalive message.");
                break;

              case "notification":
                // Received a notification
                string userName = eventMsg["payload"]["event"]["user_name"]?.ToString();
                long.TryParse(eventMsg["payload"]["event"]["user_id"]?.ToString(), out var userID);
                switch (eventMsg["metadata"]["subscription_type"].ToString())
                {
                  case "channel.follow":
                    // Received channel follow event
                    if (userName.Length > 0)
                    {
                      Chatter c = Chatter.GetChatterByID(userID, userName);
                      if (c.LastTimeFollowed.Date != DateTime.Now.Date)
                      {
                        c.SetLastTimeFollowedToNow();
                        Log.Information("New follow from {name}.", c.Name);
                        Notifications.CreateFollowNotification(c.Name);
                      }
                      else { Log.Information("{name} refollowed again in the same day.", c.Name); }
                    }
                    else
                    {
                      Log.Information("New follow from {name}.", "Anonymous");
                      Notifications.CreateFollowNotification("Anonymous");
                    }
                    break;

                  case "channel.subscribe":
                    // Received subscription event
                    if (eventMsg["payload"]["event"]?["is_gift"]?.GetValue<bool>() == true)
                    {
                      Log.Information("{name} received a gift subscription.", userName);
                      Notifications.CreateReceiveGiftSubscriptionNotification(
                        userName,
                        eventMsg["payload"]["subscription"]?["created_at"]?.ToString());
                    }
                    else
                    {
                      Log.Information("New subscription from {name}.", userName);
                      Notifications.CreateSubscriptionNotification(
                        userName,
                        eventMsg["payload"]["event"]["tier"]?.ToString(),
                        eventMsg["payload"]["event"]["message"]?.ToString());
                    }
                    break;

                  case "channel.subscription.gift":
                    // Received gifted subscription event
                    if (eventMsg["payload"]["event"]["is_anonymous"]?.GetValue<bool>() == true) { userName = null; }
                    int? totalGifted = eventMsg["payload"]["event"]["total"]?.GetValue<int>();
                    Log.Information("{name} gifted {count} subscription(s).",
                      userName?.Length == 0 ? "Anonymous" : userName,
                      totalGifted);
                    Notifications.CreateGiftSubscriptionNotification(
                      userName,
                      eventMsg["payload"]["event"]["tier"]?.ToString(),
                      totalGifted.HasValue ? totalGifted.Value : 0,
                      eventMsg["payload"]["event"]["message"]?.ToString(),
                      eventMsg["payload"]["subscription"]?["created_at"]?.ToString());
                    break;

                  case "channel.subscription.message":
                    // Received subscription with message event
                    int? duration = eventMsg["payload"]["event"]["duration_months"]?.GetValue<int>();
                    int? streak = eventMsg["payload"]["event"]["streak_months"]?.GetValue<int>();
                    int? cumulative = eventMsg["payload"]["event"]["cumulative_months"]?.GetValue<int>();
                    Log.Information("New subscription from {name}. {msg}",
                      userName,
                      eventMsg["payload"]["event"]["message"]?["text"]?.ToString());
                    Notifications.CreateSubscriptionNotification(
                      userName,
                      eventMsg["payload"]["event"]["tier"]?.ToString(),
                      duration.HasValue ? duration.Value : 0,
                      streak.HasValue ? streak.Value : 0,
                      cumulative.HasValue ? cumulative.Value : 0,
                      eventMsg["payload"]["event"]["message"]);
                    break;

                  case "channel.cheer":
                    // Received cheer event
                    int? bits = eventMsg["payload"]["event"]["bits"]?.GetValue<int>();
                    Log.Information("{name} cheered with {count} bits.",
                      userName,
                      bits);
                    Notifications.CreateCheerNotification(
                      userName,
                      bits.HasValue ? bits.Value : 0,
                      eventMsg["payload"]["event"]["message"]?.ToString());
                    break;

                  case "channel.channel_points_custom_reward_redemption.add":
                    // Received channel points redemption event
                    Log.Information("{name} redeemed ID: {id} with channel points.",
                      userName,
                      eventMsg["payload"]["event"]["reward"]?["id"]?.ToString());
                    Notifications.CreateRedemptionNotificaiton(
                      userName,
                      eventMsg["payload"]["event"]["reward"]?["id"]?.ToString(),
                      eventMsg["payload"]["event"]["id"]?.ToString(),
                      eventMsg["payload"]["event"]["user_input"]?.ToString());
                    break;

                  case "channel.ban":
                    // Received user banned event
                    string reason = eventMsg["payload"]["event"]["reason"]?.ToString();
                    if (eventMsg["payload"]["event"]["is_permanent"]?.GetValue<bool>() == true)
                    {
                      Log.Information("{name} has been permanently banned. {msg}.",
                        userName,
                        reason);
                      Notifications.CreateBanNotification(
                        userName,
                        reason);
                    }
                    else
                    {
                      var dur = DateTime.Parse(eventMsg["payload"]["event"]["ends_at"]?.ToString())
                        - DateTime.Parse(eventMsg["payload"]["event"]["banned_at"]?.ToString());
                      Log.Information("{name} was timed out for {duration}. {msg}.",
                        userName,
                        dur,
                        reason);
                      Notifications.CreateTimeoutNotification(userName,
                        dur,
                        reason);
                    }

                    // Increase ban counter on the chatter
                    var chatter = Chatter.GetChatterByID(userID, userName);
                    chatter.BanCounter++;
                    break;

                  case "channel.channel_points_automatic_reward_redemption.add":
                    // Received new bits channel reward redemption
                    // The type of reward. One of:
                    // single_message_bypass_sub_mode
                    // send_highlighted_message
                    // random_sub_emote_unlock
                    // chosen_sub_emote_unlock
                    // chosen_modified_sub_emote_unlock
                    // message_effect
                    // gigantify_an_emote
                    // celebration
                    string type = eventMsg["payload"]["event"]["reward"]["type"].ToString();
                    string msg = eventMsg["payload"]["event"]["message"]["text"]?.ToString();
                    string userInput = eventMsg["payload"]["event"]["user_input"]?.ToString();

                    switch (type)
                    {
                      case "celebration":
                        Notifications.CreateOnScreenCelebrationNotification(userName, msg);
                        break;
                      case "message_effect":
                        Notifications.CreateMessageEffectNotification(userName, msg);
                        break;
                      case "gigantify_an_emote":
                        Notifications.CreateGigantifyEmoteNotification(userName, msg);
                        break;
                    }
                    LogEventToFile(message);
                    break;

                  case "channel.hype_train.progress":
                    // Received hype train progress event
                    Log.Information("{name} did something that fired hype train progress event", userName);
                    // TODO: Delete after testing, temporary event messages logging
                    LogEventToFile(message);
                    break;

                  case "channel.raid":
                    userName = eventMsg["payload"]["event"]["from_broadcaster_user_name"]?.ToString();
                    long.TryParse(eventMsg["payload"]["event"]["from_broadcaster_user_id"]?.ToString(), out userID);
                    int.TryParse(eventMsg["payload"]["event"]["viewers"]?.ToString(), out var count);
                    Notifications.CreateRaidNotification(userName, userID.ToString(), count);
                    break;

                  case "some_reconnect_message_header_i_dont_remember":
                    // Reconnect message
                    Log.Information("Event bot got session reconnect message. Should close the connection and use provided url as WEBSOCKETURL but not doing that because it's not mandatory.");
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
                    break;

                  default:
                    MainWindow.ConsoleWriteLine(message);
                    // TODO: Delete after testing, temporary event messages logging
                    LogEventToFile(message);
                    break;
                }
                break;

              default:
                MainWindow.ConsoleWriteLine(message);
                // TODO: Delete after testing, temporary event messages logging
                LogEventToFile(message);
                break;
            }
          }
          else
          {
            // Message not correctly parsed, print the message to stdout
            MainWindow.ConsoleWriteLine(message);
            // TODO: Delete after testing, temporary event messages logging
            LogEventToFile(message);
          }
        }
        else
        {
          zeroBytesReceivedCounter++;
          if (zeroBytesReceivedCounter >= 5)
          {
            // Close connection if 5 times in a row received 0 bytes
            Log.Warning("Events bot received {amount} bytes multiple times, reconnecting!", 0);
            WebSocketClient.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
            Thread.Sleep(2000);
            WebSocketClient = null;
          }
        }

        Thread.Sleep(100);
      }

      if (WebSocketClient.State != WebSocketState.Open)
      {
        Log.Warning("Events bot connection lost, waiting 2 sec to reconnect. {status}, {description}", WebSocketClient.CloseStatus, WebSocketClient.CloseStatusDescription);
        if (WebSocketClient.State != WebSocketState.Closed && WebSocketClient.State != WebSocketState.Aborted) WebSocketClient.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
        Thread.Sleep(2000);
        WebSocketClient = null;
      }
    }
  }

  private static bool Subscribe(string type, string version, string sessionID, JsonObject additionalFields = null)
  {
    // https://dev.twitch.tv/docs/eventsub/eventsub-subscription-types/
    Log.Information("Events bot subscribing to {type} event.", type);
    using HttpRequestMessage request = new(HttpMethod.Post, SUBSCRIPTIONRUL);
    var msg = new SubscriptionMessage(type, version, Config.Data[Config.Keys.ChannelID], sessionID).ToJsonString();
    if (additionalFields != null && additionalFields.Count > 0)
    {
      var n = JsonObject.Parse(msg);
      foreach (var additionalField in additionalFields) { n[additionalField.Key] = JsonObject.Parse(additionalFields[additionalField.Key].ToString()); }
      msg = n.ToString();
    }
    request.Content = new StringContent(msg, Encoding.UTF8, "application/json");
    request.Headers.Add("Client-Id", Secret.Data[Secret.Keys.TwitchClientID]);
    request.Headers.Add("Authorization", $"Bearer {Secret.Data[Secret.Keys.TwitchOAuthToken]}");

    string resp;
    try { resp = HttpClient.Send(request).Content.ReadAsStringAsync().Result; }
    catch (HttpRequestException ex) { Log.Error("Events bot subscription request failed. {ex}", ex); return false; }
    var response = ResponseMessage.Deserialize(resp);
    if (response is null || response.Error != null) { Log.Warning("Events bot subscription error: {msg}", response.Message); }
    else
    {
      Log.Information("Events bot subscription response: {type} {status}.", response.Data?[0].Type, response.Data?[0].Status);
      return true;
    }
    return false;
  }

  public static void LogEventToFile(string msg)
  {
    // It's mostly for testing, in the future it should be removed
    Log.Error("Unhandled event message: {msg}", msg);
  }
}
