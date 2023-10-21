using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;

namespace AbevBot
{
  /// <summary> Everything related to access tokens. </summary>
  public static class AccessTokens
  {
    /// <summary> Date and time when access token expiries. </summary>
    private static DateTime BotOAuthTokenExpiration;
    /// <summary> Time before OAuth token expiries to try to refresh it. </summary>
    private static readonly TimeSpan OAuthTokenExpirationSomething = new(0, 10, 0);
    /// <summary> Http client for GET and POST requests. </summary>
    private static readonly HttpClient Client = new();
    /// <summary> Date and time when access token expiries. </summary>
    private static DateTime SpotifyOAuthTokenExpiration;
    /// <summary> Twitch scope of permissions. https://dev.twitch.tv/docs/authentication/scopes </summary>
    private static readonly string[] TwitchScopes = new[] {
      "bits:read", // View Bits information for a channel
      "channel:manage:redemptions", // Manage Channel Points custom rewards and their redemptions on a channel
      "channel:read:redemptions", // View Channel Points custom rewards and their redemptions on a channel
      "channel:read:subscriptions", // View a list of all subscribers to a channel and check if a user is subscribed to a channel
      "chat:edit", // Send live stream chat messages
      "chat:read", // View live stream chat messages
      "moderator:manage:banned_users", // Ban and unban users
      "moderator:manage:shoutouts", // Manage a broadcaster’s shoutouts
      "moderator:read:chatters", // View the chatters in a broadcaster’s chat room
      "moderator:read:followers", // Read the followers of a broadcaster
      "whispers:edit", // Send whisper messages
      "whispers:read", // View your whisper messages
    };
    /// <summary> Spotify scope of permissions. https://developer.spotify.com/documentation/web-api/concepts/scopes </summary>
    private static readonly string[] SpotifyScopes = new[] {
      "user-read-playback-state", // Read access to a user’s player state.
      "user-modify-playback-state", // Write access to a user’s playback state.
      "user-read-currently-playing", // Read access to a user’s currently playing content.
      "user-read-recently-played" // Read access to a user’s recently playing content.
    };

    /// <summary> Tries to get access token by: validating existing one or refreshing existing one or requesting new one. </summary>
    public static void GetAccessTokens()
    {
      // Twitch
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

      // Spotify, Spotify doesn't have API endpoint to validate tokens, also their ExpiresIn is short and doesn't send new refresh token on refresh?
      if (Secret.Data[Secret.Keys.SpotifyClientID]?.Length > 0 && Secret.Data[Secret.Keys.SpotifyClientSecret]?.Length > 0)
      {
        if (string.IsNullOrEmpty(Secret.Data[Secret.Keys.SpotifyOAuthToken]) ||
            string.IsNullOrEmpty(Secret.Data[Secret.Keys.SpotifyOAuthRefreshToken]))
        {
          // One or both of tokens was empty - get new one
          GetNewSpotifyOAuthToken();
        }
        else
        {
          // Try to refresh the token
          if (!RefreshSpotifyAccessToken())
          {
            // The refresh was unsuccesfull, get new token
            GetNewSpotifyOAuthToken();
          }
        }

        Spotify.Working = Secret.Data[Secret.Keys.SpotifyOAuthToken].Length > 0 && Secret.Data[Secret.Keys.SpotifyOAuthRefreshToken].Length > 0;
        UpdateSpotifyTokens();
      }
    }

    /// <summary> Updates tokens in database saving current access tokens. </summary>
    private static void UpdateTokens()
    {
      Database.UpdateValueInConfig(Database.Keys.TwitchOAuth, Secret.Data[Secret.Keys.OAuthToken]).Wait();
      Database.UpdateValueInConfig(Database.Keys.TwitchOAuthRefresh, Secret.Data[Secret.Keys.OAuthRefreshToken]).Wait();
    }

    /// <summary> Request new access token. </summary>
    private static void GetNewOAuthToken()
    {
      MainWindow.ConsoleWarning(">> Requesting new Twitch OAuth token.");

      string uri = string.Concat(
        "https://id.twitch.tv/oauth2/authorize?",
        "client_id=", Secret.Data[Secret.Keys.CustomerID],
        "&redirect_uri=http://localhost:3000",
        "&response_type=code",
        "&scope=", string.Join('+', TwitchScopes).Replace(":", "%3A") // Change to url encoded
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

        string resp = Client.Send(request).Content.ReadAsStringAsync().Result;
        AccessTokenResponse response = AccessTokenResponse.Deserialize(resp);
        if (response is null || response.Token is null || response.RefreshToken is null)
        {
          throw new Exception("Response was empty or didn't received access token!\nProbably ClientID or ClientPassowrd doesn't match!");
        }
        MainWindow.ConsoleWarning(response.ToString());
        // Read information from received data
        Secret.Data[Secret.Keys.OAuthToken] = response.Token;
        Secret.Data[Secret.Keys.OAuthRefreshToken] = response.RefreshToken;
        BotOAuthTokenExpiration = DateTime.Now + TimeSpan.FromSeconds(response.ExpiresIn.Value) - OAuthTokenExpirationSomething;
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

      string resp = Client.Send(request).Content.ReadAsStringAsync().Result;
      AccessTokenValidationResponse response = AccessTokenValidationResponse.Deserialize(resp);
      if (response?.ClientID?.Equals(Secret.Data[Secret.Keys.CustomerID]) == true && response?.ExpiresIn > 0)
      {
        if (response?.Scopes?.Length != TwitchScopes.Length) { MainWindow.ConsoleWarning(">> Current Twitch OAuth token is missing some scopes."); }
        else
        {
          BotOAuthTokenExpiration = DateTime.Now + TimeSpan.FromSeconds(response.ExpiresIn.Value) - OAuthTokenExpirationSomething;
          MainWindow.ConsoleWarning($">> Twitch OAuth token validation succeeded. Token expiries in {response.ExpiresIn.Value / 3600f} hours.");
          return true;
        }
      }
      else { MainWindow.ConsoleWarning(">> Twitch OAuth token validation failed."); }

      return false;
    }

    /// <summary> Refreshes access token using refresh token. </summary>
    public static void RefreshAccessToken()
    {
      if (DateTime.Now < BotOAuthTokenExpiration) return;

      MainWindow.ConsoleWarning(">> Refreshing Twitch OAuth token.");
      using HttpRequestMessage request = new(HttpMethod.Post, "https://id.twitch.tv/oauth2/token");
      request.Content = new StringContent(string.Concat(
          "client_id=", Secret.Data[Secret.Keys.CustomerID],
          "&client_secret=", Secret.Data[Secret.Keys.Password],
          "&grant_type=refresh_token",
          "&refresh_token=", Secret.Data[Secret.Keys.OAuthRefreshToken].Replace(":", "%3A") // Change to url encoded
      ));
      request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/x-www-form-urlencoded");

      string resp = Client.Send(request).Content.ReadAsStringAsync().Result;
      AccessTokenResponse response = AccessTokenResponse.Deserialize(resp);
      if (response is null || response.Token is null || response.RefreshToken is null) throw new Exception("Response was empty or didn't received access token!");
      MainWindow.ConsoleWarning(response.ToString());
      // Read information from received data
      Secret.Data[Secret.Keys.OAuthToken] = response.Token;
      Secret.Data[Secret.Keys.OAuthRefreshToken] = response.RefreshToken;
      BotOAuthTokenExpiration = DateTime.Now + TimeSpan.FromSeconds(response.ExpiresIn.Value) - OAuthTokenExpirationSomething;
    }

    /// <summary> Gets Broadcaster ID from Channel Name. Should be used once at bot startup. </summary>
    public static void GetBroadcasterID()
    {
      MainWindow.ConsoleWarning(">> Getting broadcaster ID.");
      string uri = $"https://api.twitch.tv/helix/users?login={Config.Data[Config.Keys.ChannelName]}";
      using HttpRequestMessage request = new(HttpMethod.Get, uri);
      request.Headers.Add("Authorization", $"Bearer {Secret.Data[Secret.Keys.OAuthToken]}");
      request.Headers.Add("Client-Id", Secret.Data[Secret.Keys.CustomerID]);

      string resp = Client.Send(request).Content.ReadAsStringAsync().Result;
      ChannelIDResponse response = ChannelIDResponse.Deserialize(resp);
      if (response != null && response?.Data?.Length == 1) { Config.Data[Config.Keys.ChannelID] = response.Data[0].ID; }
      else { MainWindow.ConsoleWarning(">> Couldn't acquire broadcaster ID. Probably defined channel name doesn't exist."); }
    }

    /// <summary> Requests new Spotify OAuth token using Authorization Code Flow. </summary>
    private static void GetNewSpotifyOAuthToken()
    {
      MainWindow.ConsoleWarning(">> Requesting new Spotify OAuth token.");

      string uri = string.Concat(
        "https://accounts.spotify.com/authorize?",
        "client_id=", Secret.Data[Secret.Keys.SpotifyClientID],
        "&redirect_uri=http://localhost:3000",
        "&response_type=code",
        "&scope=", string.Join('+', SpotifyScopes).Replace(":", "%3A") // Change to url encoded
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
        resp.Redirect("https://spotify.com");
      }

      // Close local server, it's no longer needed
      localServer.Close();

      string requestUrl = context.Request.Url != null ? context.Request.Url.Query : string.Empty;
      // Parse received request url
      if (requestUrl.StartsWith("?code="))
      {
        // Next step - request user token with received authorization code
        int symbolIndex = requestUrl.IndexOf('&');
        string code;
        if (symbolIndex < 0) code = requestUrl[6..];
        else code = requestUrl.Substring(6, symbolIndex - 6);
        using HttpRequestMessage request = new(HttpMethod.Post, "https://accounts.spotify.com/api/token");
        request.Content = new StringContent(string.Concat(
            "&grant_type=authorization_code",
            "&code=", code,
            "&redirect_uri=http://localhost:3000"
          ));
        request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/x-www-form-urlencoded");
        request.Headers.Add("Authorization", $"Basic {Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Secret.Data[Secret.Keys.SpotifyClientID]}:{Secret.Data[Secret.Keys.SpotifyClientSecret]}"))}");

        string resp = Client.Send(request).Content.ReadAsStringAsync().Result;
        SpotifyAccessTokenResponse response = SpotifyAccessTokenResponse.Deserialize(resp);
        if (response is null || response.Token is null || response.RefreshToken is null)
        {
          MainWindow.ConsoleWarning(">> Response was empty or didn't received access token!\nProbably ClientID or ClientPassowrd doesn't match!");
        }
        else
        {
          MainWindow.ConsoleWarning(response.ToString());
          // Read information from received data
          Secret.Data[Secret.Keys.SpotifyOAuthToken] = response.Token;
          Secret.Data[Secret.Keys.SpotifyOAuthRefreshToken] = response.RefreshToken;
          SpotifyOAuthTokenExpiration = DateTime.Now + TimeSpan.FromSeconds(response.ExpiresIn.Value) - OAuthTokenExpirationSomething;
        }
      }
      else
      {
        // Something went wrong
        MainWindow.ConsoleWarning(">> Something went wrong! Response url didn't include code part!");
      }
    }

    /// <summary> Updates Spotify tokens in database saving current access tokens. </summary>
    private static void UpdateSpotifyTokens()
    {
      Database.UpdateValueInConfig(Database.Keys.SpotifyOAuth, Secret.Data[Secret.Keys.SpotifyOAuthToken]).Wait();
      Database.UpdateValueInConfig(Database.Keys.SpotifyOAuthRefresh, Secret.Data[Secret.Keys.SpotifyOAuthRefreshToken]).Wait();
    }

    /// <summary> Refreshes Spotify access token using refresh token. </summary>
    /// <returns> true if everything was ok, otherwise false</returns>
    public static bool RefreshSpotifyAccessToken()
    {
      if (DateTime.Now < SpotifyOAuthTokenExpiration) return true;

      MainWindow.ConsoleWarning(">> Refreshing Spotify access token.");
      using HttpRequestMessage request = new(HttpMethod.Post, "https://accounts.spotify.com/api/token");
      request.Content = new StringContent(string.Concat(
          "grant_type=refresh_token",
          "&refresh_token=", Secret.Data[Secret.Keys.SpotifyOAuthRefreshToken].Replace(":", "%3A") // Change to url encoded
      ));
      request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/x-www-form-urlencoded");
      request.Headers.Add("Authorization", $"Basic {Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Secret.Data[Secret.Keys.SpotifyClientID]}:{Secret.Data[Secret.Keys.SpotifyClientSecret]}"))}");

      string resp = Client.Send(request).Content.ReadAsStringAsync().Result;
      SpotifyAccessTokenResponse response = SpotifyAccessTokenResponse.Deserialize(resp);
      if (response is null || response.Token is null)
      {
        MainWindow.ConsoleWarning(">> Response was empty or didn't received access token!\nProbably ClientID or ClientPassowrd doesn't match!");
        return false;
      }
      else
      {
        MainWindow.ConsoleWarning(response.ToString());
        // Read information from received data
        Secret.Data[Secret.Keys.SpotifyOAuthToken] = response.Token;
        // When refreshing refresh token stays the same??
        if (response.RefreshToken?.Length > 0) Secret.Data[Secret.Keys.SpotifyOAuthRefreshToken] = response.RefreshToken;
        SpotifyOAuthTokenExpiration = DateTime.Now + TimeSpan.FromSeconds(response.ExpiresIn.Value) - OAuthTokenExpirationSomething;
        return true;
      }
    }
  }
}
