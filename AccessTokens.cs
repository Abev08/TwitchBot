using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;

namespace AbevBot
{
  /// <summary> Everything related to access tokens. </summary>
  public static class AccessTokens
  {
    /// <summary> Date and time when access token expiries. </summary>
    public static DateTime BotOAuthTokenExpiration { get; private set; }
    /// <summary> Time before OAuth token expiries to try to refresh it. </summary>
    public static TimeSpan OAuthTokenExpirationSomething { get; } = new TimeSpan(0, 10, 0);
    /// <summary> Http client for GET and POST requests. </summary>
    private static readonly HttpClient Client = new();

    /// <summary> Tries to get access token by: validating existing one or refreshing existing one or requesting new one. </summary>
    public static void GetAccessTokens()
    {
      if (string.IsNullOrEmpty(Secret.Data[Secret.Keys.OAuthToken]) ||
          string.IsNullOrEmpty(Secret.Data[Secret.Keys.OAuthRefreshToken]))
      {
        GetNewOAuthToken();
        UpdateTokens();
      }
      else
      {
        // Check if readed token works
        if (!ValidateOAuthToken())
        {
          // The verification failed. First try to refresh access token before requesting new one
          RefreshAccessToken();
          if (!ValidateOAuthToken())
          {
            // Refreshing access token also failed, request new one
            GetNewOAuthToken();
          }

          UpdateTokens();
        }
      }
    }

    /// <summary> Updates tokens file saving current access tokens. </summary>
    private static void UpdateTokens()
    {
      Database.UpdateValueInConfig(Database.Keys.TwitchOAuth, Secret.Data[Secret.Keys.OAuthToken]).Wait();
      Database.UpdateValueInConfig(Database.Keys.TwitchOAuthRefresh, Secret.Data[Secret.Keys.OAuthRefreshToken]).Wait();
    }

    /// <summary> Request new access token. </summary>
    private static void GetNewOAuthToken()
    {
      MainWindow.ConsoleWarning(">> Requesting new access token.");

      string uri = string.Concat(
        "https://id.twitch.tv/oauth2/authorize?",
        "client_id=", Secret.Data[Secret.Keys.CustomerID],
        "&redirect_uri=http://localhost:3000",
        "&response_type=code",
        // When asking for permissions the scope of permissions has to be determined
        // if tried to follow to event without getting permissions for it, the follow returns an error
        // https://dev.twitch.tv/docs/authentication/scopes/
        "&scope=",
          string.Concat(
            // Chat bot scopes
            "chat:read", // View live stream chat messages
            "+chat:edit", // 	Send live stream chat messages
            "+whispers:read", // View your whisper messages
            "+whispers:edit", // 	Send whisper messages
            "+bits:read", // View Bits information for a channel
            "+moderator:manage:banned_users", // Ban chatters
            "+moderator:manage:shoutouts", // Create and receive shoutout information

            // Events bot scopes
            "+channel:read:redemptions", // View Channel Points custom rewards and their redemptions on a channel
            "+channel:read:subscriptions", // View a list of all subscribers to a channel and check if a user is subscribed to a channel
            "+moderator:read:followers", // Read the followers of a broadcaster
            "+moderator:read:chatters" // Read chatters
          ).Replace(":", "%3A") // Change to url encoded
        );

      // Open the link for the user to complete authorization
      Process.Start(new ProcessStartInfo() { FileName = uri, UseShellExecute = true });

      // Local server is needed to get response to user authorizing the app (to grab the access token)
      using HttpListener localServer = new();
      localServer.Prefixes.Add("http://localhost:3000/"); // Where local server should listen for connections, maybe it should be in Config.ini? Hmm
      localServer.Start();
      HttpListenerContext context = localServer.GetContext(); // Await connection

      // For now lets just redirect to twitch to hide received code in browser url
      using (HttpListenerResponse resp = context.Response)
      {
        resp.Headers.Set("Content-Type", "text/plain");
        resp.Redirect("https://www.twitch.tv");
      }

      // Close local server, it's no longer needed
      localServer.Close();

      string requestUrl = context.Request.Url != null ? context.Request.Url.Query : string.Empty;
      // Parse received request url
      if (requestUrl.StartsWith("?code="))
      {
        // Next step - request user token with received authorization code
        string code = requestUrl.Substring(6, requestUrl.IndexOf('&', 6) - 6);
        using HttpRequestMessage request = new(HttpMethod.Post, "https://id.twitch.tv/oauth2/token");
        request.Content = new StringContent(string.Concat(
            "client_id=", Secret.Data[Secret.Keys.CustomerID],
            "&client_secret=", Secret.Data[Secret.Keys.Password],
            "&code=", code,
            "&grant_type=authorization_code",
            "&redirect_uri=http://localhost:3000"
          ));
        request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/x-www-form-urlencoded");
        AccessTokenResponse response = AccessTokenResponse.Deserialize(Client.Send(request).Content.ReadAsStringAsync().Result);
        if (response is null || response.Token is null || response.RefreshToken is null)
        {
          throw new Exception("Response was empty or didn't received access token!\nProbably ClientID or ClientPassowrd doesn't match!");
        }
        MainWindow.ConsoleWarning(response.ToString());
        // Read information from received data
        Secret.Data[Secret.Keys.OAuthToken] = response.Token;
        Secret.Data[Secret.Keys.OAuthRefreshToken] = response.RefreshToken;
        BotOAuthTokenExpiration = DateTime.Now + new TimeSpan(0, 0, response.ExpiresIn.Value) - OAuthTokenExpirationSomething;
      }
      else
      {
        // Something went wrong
        throw new Exception("Something went wrong! Response url didn't include code part!");
      }
    }

    /// <summary> Validates access token. </summary>
    /// <returns> true if access token is valid, otherwise false. </returns>
    private static bool ValidateOAuthToken()
    {
      using HttpRequestMessage request = new(HttpMethod.Get, "https://id.twitch.tv/oauth2/validate");
      request.Headers.Add("Authorization", $"OAuth {Secret.Data[Secret.Keys.OAuthToken]}");

      AccessTokenValidationResponse response = AccessTokenValidationResponse.Deserialize(Client.Send(request).Content.ReadAsStringAsync().Result);
      if (response?.ClientID?.Equals(Secret.Data[Secret.Keys.CustomerID]) == true && response?.ExpiresIn > 0)
      {
        BotOAuthTokenExpiration = DateTime.Now + new TimeSpan(0, 0, response.ExpiresIn.Value) - OAuthTokenExpirationSomething;
        MainWindow.ConsoleWarning($">> Access token validation succeeded. Token expiries in {response.ExpiresIn.Value / 3600f} hours.");
        return true;
      }
      else { MainWindow.ConsoleWarning(">> Access token validation failed."); }

      return false;
    }

    /// <summary> Refreshes access token using refresh token. </summary>
    public static void RefreshAccessToken()
    {
      if (DateTime.Now < BotOAuthTokenExpiration) return;

      MainWindow.ConsoleWarning(">> Refreshing access token.");
      using HttpRequestMessage request = new(HttpMethod.Post, "https://id.twitch.tv/oauth2/token");
      request.Content = new StringContent(string.Concat(
          "client_id=", Secret.Data[Secret.Keys.CustomerID],
          "&client_secret=", Secret.Data[Secret.Keys.Password],
          "&grant_type=refresh_token",
          "&refresh_token=", Secret.Data[Secret.Keys.OAuthRefreshToken].Replace(":", "%3A") // Change to url encoded
      ));
      request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/x-www-form-urlencoded");

      AccessTokenResponse response = AccessTokenResponse.Deserialize(Client.Send(request).Content.ReadAsStringAsync().Result);
      if (response is null || response.Token is null || response.RefreshToken is null) throw new Exception("Response was empty or didn't received access token!");
      MainWindow.ConsoleWarning(response.ToString());
      // Read information from received data
      Secret.Data[Secret.Keys.OAuthToken] = response.Token;
      Secret.Data[Secret.Keys.OAuthRefreshToken] = response.RefreshToken;
      BotOAuthTokenExpiration = DateTime.Now + new TimeSpan(0, 0, response.ExpiresIn.Value) - OAuthTokenExpirationSomething;
    }

    public static void GetBroadcasterID()
    {
      MainWindow.ConsoleWarning(">> Getting broadcaster ID.");
      string uri = $"https://api.twitch.tv/helix/users?login={Config.Data[Config.Keys.ChannelName]}";
      using HttpRequestMessage request = new(HttpMethod.Get, uri);
      request.Headers.Add("Authorization", $"Bearer {Secret.Data[Secret.Keys.OAuthToken]}");
      request.Headers.Add("Client-Id", Secret.Data[Secret.Keys.CustomerID]);

      ChannelIDResponse response = ChannelIDResponse.Deserialize(Client.Send(request).Content.ReadAsStringAsync().Result);
      if (response != null && response?.Data?.Length == 1) { Config.Data[Config.Keys.ChannelID] = response.Data[0].ID; }
      else { MainWindow.ConsoleWarning(">> Couldn't acquire broadcaster ID. Probably defined channel name doesn't exist."); }
    }
  }
}
