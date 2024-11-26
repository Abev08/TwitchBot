using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;

using Serilog;

namespace AbevBot;

/// <summary>
/// Experimental.
/// Reads YouTube chat messages and resends them in Twitch chat.
/// </summary>
public static class YouTube
{
  private static bool StreamActive;
  private static string ChatID;
  private static DateTime StreamCheckLast = DateTime.MinValue;
  private static readonly Duration StreamCheckTimeout = TimeSpan.FromMinutes(2);
  private static bool StreamCheckActive;
  private static DateTime MessagesPollLast = DateTime.MinValue;
  private static Duration MessagesPollTimeout = TimeSpan.FromSeconds(60);
  private static readonly Duration MessagesPollMinTimeout = TimeSpan.FromMinutes(1);
  private static readonly Duration MessagesPollMaxTimeout = TimeSpan.FromMinutes(10);
  private static bool MessagesPollActive, MessagesPollFirstPoll;
  private static string MessagesPollUri;
  private static int MessagesPollFailCounter;
  public static TimeSpan MessagePostInterval = TimeSpan.FromSeconds(2);
  private static DateTime MessagePostLast = DateTime.MinValue;
  private static readonly List<string> Messages = new();

  public static void CheckActiveStream()
  {
    if (StreamActive || StreamCheckActive) { return; }
    if ((DateTime.Now - StreamCheckLast) < StreamCheckTimeout) { return; }
    StreamCheckLast = DateTime.Now;

    var channelID = Secret.Data[Secret.Keys.YouTubeChannelID];
    var apiKey = Secret.Data[Secret.Keys.YouTubeAPIKey];
    if (channelID is null || channelID.Length == 0 || apiKey is null || apiKey.Length == 0) { return; }
    StreamCheckActive = true;
    Log.Error("YouTube stream active check activated"); // Test log

    Task.Run(() =>
    {
      try
      {
        var uri = $"https://www.googleapis.com/youtube/v3/search?part=snippet&channelId={channelID}&eventType=live&type=video&key={apiKey}";
        using HttpRequestMessage request = new(HttpMethod.Get, uri);
        request.Headers.Add("Accept", "application/json");
        var response = Notifications.Client.Send(request).Content.ReadAsStringAsync().Result;
        if (response is null || response.Length == 0) { StreamActive = false; return; }
        var resp = JsonNode.Parse(response);
        if (resp is null) { StreamActive = false; return; }
        var items = resp["items"];
        if (items is null || (items is JsonArray && ((JsonArray)items).Count == 0))
        {
          Log.Error("YouTube stream active check, received 0 items (videos).");
          StreamActive = false;
          return;
        }

        // Get video ID of the 1st element, it's an assumption that there is only 1 stream active
        var vidID = items[0]["id"]["videoId"];
        if (vidID is null) { StreamActive = false; return; }
        var videoID = vidID.ToString();

        // Get Chat ID from the video ID
        uri = $"https://youtube.googleapis.com/youtube/v3/videos?part=liveStreamingDetails&id={videoID}&key={apiKey}";
        using HttpRequestMessage request2 = new(HttpMethod.Get, uri);
        request2.Headers.Add("Accept", "application/json");
        response = Notifications.Client.Send(request2).Content.ReadAsStringAsync().Result;
        if (response is null || response.Length == 0) { StreamActive = false; return; }
        resp = JsonNode.Parse(response);
        if (resp is null) { StreamActive = false; return; }
        items = resp["items"];
        if (items is null || (items is JsonArray && ((JsonArray)items).Count == 0)) { StreamActive = false; return; }
        var chatID = items[0]["liveStreamingDetails"]["activeLiveChatId"];
        if (chatID is null)
        {
          Log.Error("YouTube stream active check, chat ID is missing in the video HUH.");
          StreamActive = false;
          return;
        }

        ChatID = chatID.ToString();
        MessagesPollUri = $"https://youtube.googleapis.com/youtube/v3/liveChat/messages?liveChatId={ChatID}&part=snippet%2CauthorDetails&key={apiKey}";
        MessagesPollFailCounter = 0;
        MessagesPollFirstPoll = true;
        MessagesPollActive = false; // Just to be sure
        StreamActive = true;
        Log.Error("YouTube successfully received chat ID"); // For now as an error
      }
      catch (Exception ex)
      {
        Log.Error("YouTube stream active check failed, error: {msg}", ex);
        StreamActive = false;
      }
      finally { StreamCheckActive = false; }
    });
  }

  public static void PollChatMessages()
  {
    if (!StreamActive || MessagesPollActive) { return; }
    if (MessagesPollUri is null || MessagesPollUri.Length == 0) { return; }
    if ((DateTime.Now - MessagesPollLast) < MessagesPollTimeout) { return; }
    MessagesPollActive = true;
    Log.Error("YouTube message poll activated"); // Test log

    if (MessagesPollFailCounter >= 3)
    {
      Log.Error("YouTube message poll reached maximum failed attempts");
      MessagesPollUri = string.Empty;
      MessagesPollActive = false;
      StreamActive = false;
      return;
    }

    Task.Run(() =>
    {
      string response = string.Empty;
      try
      {
        using HttpRequestMessage request = new(HttpMethod.Get, MessagesPollUri);
        request.Headers.Add("Accept", "application/json");
        response = Notifications.Client.Send(request).Content.ReadAsStringAsync().Result;
        if (response is null || response.Length == 0) { MessagesPollFailCounter++; return; }
        // if (response.Length < 3000) { Log.Error("YouTube poll response: {resp}", response); } // Maybe some short message is being sent that indicates an error?
        var resp = JsonNode.Parse(response);
        if (resp is null) { MessagesPollFailCounter++; return; }
        var interval = TimeSpan.FromMilliseconds(resp["pollingIntervalMillis"].GetValue<double>());
        Log.Error("YouTube requested poll interval of: {val}", interval);
        if (interval > MessagesPollMinTimeout && interval < MessagesPollMaxTimeout) { MessagesPollTimeout = interval; }
        var nextPage = resp["nextPageToken"].ToString();
        var items = resp["items"].AsArray();
        if (items is null) { MessagesPollFailCounter++; return; }

        if (!MessagesPollFirstPoll) // Skip 1st poll of the messages
        {
          string msgPrefix = string.Empty;
          try { msgPrefix = Config.Data[Config.Keys.YouTubeChatMessagePrefix]; }
          catch { }

          var addedMessage = false;
          lock (Messages)
          {
            foreach (var item in items)
            {
              // Message snippet
              var snippet = item["snippet"];
              if (snippet is null) { continue; }
              // Message type
              var snippetType = snippet["type"];
              if (snippetType is null || snippetType.ToString() == "messageDeletedEvent") { continue; } // Skip deleted messages

              // Message author (chatter)
              var temp = item["authorDetails"];
              if (temp is null) { continue; }
              temp = temp["displayName"];
              if (temp is null) { continue; }
              var chatter = Regex.Unescape(temp.ToString());
              if (chatter is null || chatter.Length == 0) { continue; }
              // Message text
              temp = snippet["displayMessage"];
              if (temp is null) { continue; }
              var msg = Regex.Unescape(temp.ToString());
              if (msg is null || msg.Length == 0) { continue; }

              Messages.Add(string.Concat(msgPrefix, " ", chatter, ": ", msg));
              addedMessage = true;
            }
          }

          // 0 messages, that could be an error, for now log it
          if (!addedMessage) { Log.Error("YouTube message poll, 0 chat messages received. Response: {resp}", response); }
        }
        else { MessagesPollFirstPoll = false; }

        // Update uri for next call
        var apiKey = Secret.Data[Secret.Keys.YouTubeAPIKey];
        MessagesPollUri = $"https://youtube.googleapis.com/youtube/v3/liveChat/messages?liveChatId={ChatID}&part=snippet%2CauthorDetails&pageToken={nextPage}&key={apiKey}";
        MessagesPollFailCounter = 0;
        Log.Error("YouTube message poll successful"); // Test log
      }
      catch (Exception ex)
      {
        Log.Error("YouTube stream message poll failed, error: {msg}\nresponse: {resp}", ex, response);
        MessagesPollFailCounter++;
      }
      finally
      {
        MessagesPollLast = DateTime.Now;
        MessagesPollActive = false;
      }
    });
  }

  public static void SendMessages()
  {
    if (Messages.Count > 0)
    {
      if (MessagePostInterval.TotalSeconds == 0)
      {
        // Post all of the messages at once
        lock (Messages)
        {
          Chat.AddMessagesToQueue(Messages);
          Messages.Clear();
        }
      }
      else
      {
        if (DateTime.Now - MessagePostLast >= MessagePostInterval)
        {
          MessagePostLast = DateTime.Now;
          // Post 1st message in the list
          lock (Messages)
          {
            var msg = Messages[0];
            Messages.RemoveAt(0);
            Chat.AddMessageToQueue(msg);
          }
        }
      }
    }
  }
}
