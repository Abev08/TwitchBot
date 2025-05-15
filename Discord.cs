using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using Serilog;

namespace AbevBot;

public static class Discord
{
  /// <summary> Is connection to Discord API working? </summary>
  public static bool Working { get; set; }
  /// <summary> Custom "stream went online" message that should be sent instead of default one. </summary>
  public static string CustomOnlineMessage { get; set; }
  /// <summary> Last known stream title. </summary>
  public static string LastStreamTitle { get; set; }
  /// <summary> Recently received discord messages (probably) containing messages with random videos. </summary>
  private static JsonArray RandomVideosMessages { get; set; }
  /// <summary> Time of last random videos request. </summary>
  private static DateTime RandomVideosRequestTime = DateTime.Now;
  /// <summary> Timeout between random videos requests. </summary>
  private static readonly TimeSpan RandomVideosRefreshTimeout = TimeSpan.FromSeconds(10);

  /// <summary> Sends "stream went online" message to configured Discord channel. </summary>
  public static void SendOnlineMessage()
  {
    if (!Working) return;
    if (string.IsNullOrEmpty(Secret.Data[Secret.Keys.DiscordChannelID])) return;

    Log.Information("Sending Discord Online message.");

    string message;
    if (CustomOnlineMessage?.Length > 0) { message = CustomOnlineMessage; }
    else { message = $"Hello @everyone, stream just started https://twitch.tv/{Config.Data[Config.Keys.ChannelName]} ! {{title}}"; }
    message = message.Replace("{title}", LastStreamTitle).Trim();

    using HttpRequestMessage request = new(HttpMethod.Post, $"https://discord.com/api/v10/channels/{Secret.Data[Secret.Keys.DiscordChannelID]}/messages");
    request.Content = new StringContent(
      $"{{ \"content\": \"{message}\" }}",
      Encoding.UTF8, "application/json");
    request.Headers.Add("Authorization", $"Bot {Secret.Data[Secret.Keys.DiscordBotToken]}");

    try
    {
      var resp = Notifications.Client.Send(request);
      if (resp.StatusCode != System.Net.HttpStatusCode.OK)
      {
        // Message sent failed
        Log.Error("Discord, sending online message failed, response: {resp}", resp.Content.ReadAsStringAsync().Result);
      }
    }
    catch (HttpRequestException ex) { Log.Error("Discord, sending online message failed. {ex}", ex); }
  }

  /// <summary> Requests random video urls from Discord channel. </summary>
  /// <returns>List of urls of random videos</returns>
  public static List<string> GetRandomVideos()
  {
    var videos = new List<string>();
    var channelID = Secret.Data[Secret.Keys.DiscordRandomVideosChannelID];
    if (channelID is null || channelID.Length == 0) { return videos; }

    if (RandomVideosMessages is null || DateTime.Now - RandomVideosRequestTime >= RandomVideosRefreshTimeout)
    {
      RandomVideosRequestTime = DateTime.Now;

      using HttpRequestMessage request = new(HttpMethod.Get, $"https://discord.com/api/v10/channels/{channelID}/messages");
      // request.Content = new StringContent(
      //   "{{\"limit\": 100}}",
      //   Encoding.UTF8, "application/json"); // Limit can be increased to 100 messages?
      request.Headers.Add("Authorization", $"Bot {Secret.Data[Secret.Keys.DiscordBotToken]}");

      string resp;
      try { resp = Notifications.Client.Send(request).Content.ReadAsStringAsync().Result; }
      catch (HttpRequestException ex)
      {
        Log.Error("Discord, random videos request failed. {ex}", ex);
        return videos;
      }

      try { RandomVideosMessages = (JsonArray)JsonNode.Parse(resp); }
      catch (JsonException ex)
      {
        Log.Error("Discord, random videos request failed. {ex}", ex);
        RandomVideosMessages = null;
        return videos;
      }
      if (RandomVideosMessages?.Count == 50 || RandomVideosMessages?.Count == 100)
      {
        Log.Warning("Discord, random videos request probably reached limit in received messages - TODO: implement requesting different ranges");
      }
    }

    foreach (var m in RandomVideosMessages)
    {
      if (m is null) { continue; }

      // Check start of the message
      var content = m["content"].ToString();
      if (content.ToLower().StartsWith("ignore")) { continue; } // Skip the message

      // Check reactions on the message
      var reactions = m["reactions"];
      if (reactions is JsonArray re && re.Count > 0)
      {
        int yesCount = 0, noCount = 0;
        foreach (var reaction in re)
        {
          var emoji = reaction["emoji"]["name"].ToString();
          var count = (int)reaction["count_details"]["normal"];
          if (emoji == "🟩") { yesCount += count; }
          else if (emoji == "🟥") { noCount += count; }
        }

        if (noCount >= yesCount) { continue; } // Skip the message
      }

      // Check attachments (attached videos)
      var attachments = m["attachments"];
      if (attachments is JsonArray att && att.Count == 1)
      {
        var name = att[0]["filename"].ToString();
        var url = att[0]["url"].ToString();
        var type = att[0]["content_type"].ToString();
        if (type.StartsWith("video")) { videos.Add(url); }
      }
      else
      {
        // Check embeds - maybe the message is a link to a video
        var embeds = m["embeds"];
        if (embeds is JsonArray em && em.Count == 1)
        {
          var url = em[0]["url"].ToString();
          var type = em[0]["type"].ToString();
          if (type == "video") { videos.Add(url); }
        }
        else
        {
          // Maybe the message is a link to a video but for some reason the video is not embedding in the Discord message
          if (content.ToLower().EndsWith(".mp4") || content.ToLower().EndsWith(".webm"))
          {
            if (Uri.TryCreate(content, UriKind.Absolute, out Uri uriResult) &&
              (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps)) { videos.Add(content); }
          }
        }
      }
    }

    return videos;
  }
}
