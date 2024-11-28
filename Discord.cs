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
      if (RandomVideosMessages.Count == 50 || RandomVideosMessages.Count == 100) { Log.Warning("Discord, random videos request probably reached limit in received messages - TODO: implement requesting different ranges"); }
    }

    foreach (var m in RandomVideosMessages)
    {
      var attachments = m["attachments"];
      if (attachments is JsonArray && ((JsonArray)attachments).Count == 1)
      {
        // Found a message with attachement
        var o = attachments[0];
        var name = o["filename"].ToString();
        var url = o["url"].ToString();
        var type = o["content_type"].ToString();
        // Check if attached thing is a video
        if (type.StartsWith("video"))
        {
          videos.Add(url);
        }
      }
    }

    return videos;
  }
}
