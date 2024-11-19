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
  private static readonly Duration StreamCheckTimeout = TimeSpan.FromMinutes(5);
  private static bool StreamCheckActive;
  private static DateTime MessagesPollLast = DateTime.MinValue;
  private static Duration MessagesPollTimeout = TimeSpan.FromSeconds(60);
  private static readonly Duration MessagesPollMinTimeout = TimeSpan.FromSeconds(60);
  private static bool MessagesPollActive, MessagesPollFirstPoll;
  private static string MessagesPollUri;
  private static int MessagesPollFailCounter;

  public static void CheckActiveStream()
  {
    if (StreamActive || StreamCheckActive) { return; }
    if (DateTime.Now - StreamCheckLast < StreamCheckTimeout) { return; }
    StreamCheckLast = DateTime.Now;

    var channelID = Secret.Data[Secret.Keys.YouTubeChannelID];
    var apiKey = Secret.Data[Secret.Keys.YouTubeAPIKey];
    if (channelID is null || channelID.Length == 0 || apiKey is null || apiKey.Length == 0) { return; }
    StreamCheckActive = true;

    Task.Run(() =>
    {
      string response;
      var uri = $"https://www.googleapis.com/youtube/v3/search?part=snippet&channelId={channelID}&eventType=live&type=video&key={apiKey}";
      try
      {
        using HttpRequestMessage request = new(new HttpMethod("GET"), uri);
        request.Headers.Add("Accept", "application/json");
        response = Notifications.Client.Send(request).Content.ReadAsStringAsync().Result;
        if (response is null || response.Length == 0) { StreamActive = false; return; }
        var resp = JsonNode.Parse(response);
        if (resp is null) { StreamActive = false; return; }
        var items = resp["items"];
        if (items is null || (items is JsonArray && ((JsonArray)items).Count == 0)) { StreamActive = false; return; }

        // Get video ID of the 1st element, it's an assumption that there is only 1 stream active
        var vidID = items[0]["id"]["videoId"];
        if (vidID is null) { StreamActive = false; return; }
        var videoID = vidID.ToString();

        // Get Chat ID from the video ID
        uri = $"https://youtube.googleapis.com/youtube/v3/videos?part=liveStreamingDetails&id={videoID}&key={apiKey}";
        using HttpRequestMessage request2 = new(new HttpMethod("GET"), uri);
        request2.Headers.Add("Accept", "application/json");
        response = Notifications.Client.Send(request2).Content.ReadAsStringAsync().Result;
        if (response is null || response.Length == 0) { StreamActive = false; return; }
        resp = JsonNode.Parse(response);
        if (resp is null) { StreamActive = false; return; }
        items = resp["items"];
        if (items is null || (items is JsonArray && ((JsonArray)items).Count == 0)) { StreamActive = false; return; }
        var chatID = items[0]["liveStreamingDetails"]["activeLiveChatId"];
        if (chatID is null) { StreamActive = false; return; }

        ChatID = chatID.ToString();
        MessagesPollUri = $"https://youtube.googleapis.com/youtube/v3/liveChat/messages?liveChatId={ChatID}&part=snippet%2CauthorDetails&key={apiKey}";
        MessagesPollFailCounter = 0;
        MessagesPollFirstPoll = true;
        Log.Information("YouTube, successfully received chat ID");

        StreamActive = true;
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
    if (DateTime.Now - MessagesPollLast < MessagesPollTimeout) { return; }
    MessagesPollActive = true;

    if (MessagesPollFailCounter >= 5)
    {
      MessagesPollUri = string.Empty;
      StreamActive = false;
      return;
    }

    Task.Run(() =>
    {
      string response;
      try
      {
        using HttpRequestMessage request = new(new HttpMethod("GET"), MessagesPollUri);
        request.Headers.Add("Accept", "application/json");
        response = Notifications.Client.Send(request).Content.ReadAsStringAsync().Result;
        if (response is null || response.Length == 0) { MessagesPollFailCounter++; return; }
        var resp = JsonNode.Parse(response);
        if (resp is null) { MessagesPollFailCounter++; return; }
        var interval = TimeSpan.FromMilliseconds(resp["pollingIntervalMillis"].GetValue<double>());
        if (interval > MessagesPollMinTimeout) { MessagesPollTimeout = interval; }
        var nextPage = resp["nextPageToken"].ToString();
        var items = resp["items"].AsArray();
        if (items is null) { MessagesPollFailCounter++; return; }

        if (!MessagesPollFirstPoll) // Skip 1st poll of the messages
        {
          List<string> messages = new();
          foreach (var item in items)
          {
            var chatter = item["authorDetails"]["displayName"].ToString();
            var msg = Regex.Unescape(item["snippet"]["displayMessage"].ToString());
            if (chatter?.Length > 0 && msg?.Length > 0)
            {
              messages.Add(string.Concat("[YT] ", chatter, ": ", msg));
            }
          }
          if (messages.Count > 0) { Chat.AddMessagesToQueue(messages); }
        }
        else { MessagesPollFirstPoll = false; }

        // Update uri for next call
        var apiKey = Secret.Data[Secret.Keys.YouTubeAPIKey];
        MessagesPollUri = $"https://youtube.googleapis.com/youtube/v3/liveChat/messages?liveChatId={ChatID}&part=snippet%2CauthorDetails&pageToken={nextPage}&key={apiKey}";
        MessagesPollFailCounter = 0;
      }
      catch (Exception ex)
      {
        Log.Error("YouTube stream message poll failed, error: {msg}", ex);
        MessagesPollFailCounter++;
      }
      finally
      {
        MessagesPollLast = DateTime.Now;
        MessagesPollActive = false;
      }
    });
  }
}
