using System.Net.Http;
using System.Text;

namespace AbevBot;

public static class Discrod
{
  /// <summary> Is connection to Discord API working? </summary>
  public static bool Working { get; set; }
  public static string CustomOnlineMessage { get; set; }

  public static void SendOnlineMessage()
  {
    if (!Working) return;
    if (string.IsNullOrEmpty(Secret.Data[Secret.Keys.DiscordChannelID])) return;

    MainWindow.ConsoleWarning(">> Sending Discord Online message.");

    string message;
    if (CustomOnlineMessage?.Length > 0) message = CustomOnlineMessage;
    else message = $"Hello @everyone, stream just started https://twitch.tv/{Config.Data[Config.Keys.ChannelName]} !";

    using HttpRequestMessage request = new(HttpMethod.Post, $"https://discord.com/api/v10/channels/{Secret.Data[Secret.Keys.DiscordChannelID]}/messages");
    request.Content = new StringContent(
      $"{{\"content\": \"{message}\"}}",
      Encoding.UTF8, "application/json");

    request.Headers.Add("Authorization", $"Bot {Secret.Data[Secret.Keys.DiscordBotToken]}");
    string resp = Notifications.Client.Send(request).Content.ReadAsStringAsync().Result;
    // Assume that it worked
  }
}
