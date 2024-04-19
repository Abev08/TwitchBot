using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;

using Serilog;

namespace AbevBot;

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
  /// <summary> Date and time when access token expiries. </summary>
  public static DateTime DiscordOAuthTokenExpiration;
  /// <summary> Twitch scope of permissions. https://dev.twitch.tv/docs/authentication/scopes </summary>
  private static readonly string[] TwitchScopes = new[] {
    "bits:read", // View Bits information for a channel
    "channel:manage:redemptions", // Manage Channel Points custom rewards and their redemptions on a channel
    "channel:moderate", // Perform moderation actions in a channel. The user requesting the scope must be a moderator in the channel
    "channel:read:hype_train", // View Hype Train information for a channel
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
  /// <summary> Discord scope of permissions. https://discord.com/developers/docs/topics/oauth2#shared-resources-oauth2-scopes </summary>
  private static readonly string[] DiscordScopes = new[] {
    "identify", // allows /users/@me without email
    "messages.read", // for local rpc server api access, this allows you to read messages from all client channels (otherwise restricted to channels/guilds your app creates)
    "bot", // for oauth2 bots, this puts the bot in the user's selected guild by default
    "guilds", // allows /users/@me/guilds to return basic information about all of a user's guilds
    "connections" // allows /users/@me/connections to return linked third-party accounts
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
        if (!RefreshSpotifyAccessToken(true))
        {
          // The refresh was unsuccesfull, get new token
          GetNewSpotifyOAuthToken();
        }
      }

      Spotify.Working = Secret.Data[Secret.Keys.SpotifyOAuthToken].Length > 0 && Secret.Data[Secret.Keys.SpotifyOAuthRefreshToken].Length > 0;
      UpdateSpotifyTokens();
    }

    // Discord
    if (Secret.Data[Secret.Keys.DiscrodClientID]?.Length > 0 && Secret.Data[Secret.Keys.DiscordClientSecret]?.Length > 0)
    {
      if (string.IsNullOrEmpty(Secret.Data[Secret.Keys.DiscordOAuthToken]) ||
          string.IsNullOrEmpty(Secret.Data[Secret.Keys.DiscordOAuthRefreshToken]) ||
          DiscordOAuthTokenExpiration == DateTime.MinValue)
      {
        GetNewDiscordOAuthToken();
        UpdateDiscordTokens();
      }
      else
      {
        // Check if readed token works
        if (!ValidateDiscordOAuthToken())
        {
          // The verification failed. First try to refresh access token before requesting new one
          RefreshDiscordAccessToken(true);
          if (!ValidateDiscordOAuthToken())
          {
            // Refreshing access token also failed, request new one
            GetNewDiscordOAuthToken();
          }

          UpdateDiscordTokens();
        }
      }

      Discord.Working = Secret.Data[Secret.Keys.DiscordOAuthToken].Length > 0 && Secret.Data[Secret.Keys.DiscordOAuthRefreshToken].Length > 0;
    }
  }

  /// <summary> Updates tokens in database saving current access tokens. </summary>
  public static void UpdateTokens()
  {
    Database.UpdateValueInConfig(Database.Keys.TwitchOAuth, Secret.Data[Secret.Keys.OAuthToken]).Wait();
    Database.UpdateValueInConfig(Database.Keys.TwitchOAuthRefresh, Secret.Data[Secret.Keys.OAuthRefreshToken]).Wait();
  }

  /// <summary> Request new access token. </summary>
  private static void GetNewOAuthToken()
  {
    Log.Information("Requesting new Twitch OAuth token.");

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

      string resp = Client.Send(request).Content.ReadAsStringAsync().Result; // No try/catch because it MUST work
      var response = AccessTokenResponse.Deserialize(resp);
      if (response is null || response.Token is null || response.RefreshToken is null)
      {
        // Something went really wrong, we were requesting new token with fresh authentication and the response was corrupted
        throw new Exception("Response was empty or didn't received access token!\nProbably ClientID or ClientPassowrd doesn't match!");
      }
      Log.Information(response.ToString());
      // Read information from received data
      Secret.Data[Secret.Keys.OAuthToken] = response.Token;
      Secret.Data[Secret.Keys.OAuthRefreshToken] = response.RefreshToken;
      BotOAuthTokenExpiration = DateTime.Now + TimeSpan.FromSeconds(response.ExpiresIn.Value) - OAuthTokenExpirationSomething;
    }
    else
    {
      // Something went really wrong, request url didn't had "?code=" part
      throw new Exception("Something went wrong! Response url didn't include code part!");
    }
  }

  /// <summary> Validates access token. </summary>
  /// <returns> true if access token is valid, otherwise false. </returns>
  private static bool ValidateOAuthToken()
  {
    using HttpRequestMessage request = new(HttpMethod.Get, "https://id.twitch.tv/oauth2/validate");
    request.Headers.Add("Authorization", $"OAuth {Secret.Data[Secret.Keys.OAuthToken]}");

    string resp;
    try { resp = Client.Send(request).Content.ReadAsStringAsync().Result; }
    catch (HttpRequestException ex) { Log.Error("Twitch OAuth token validation failed. {ex}", ex); return false; }
    var response = AccessTokenValidationResponse.Deserialize(resp);
    if (response?.ClientID?.Equals(Secret.Data[Secret.Keys.CustomerID]) == true && response?.ExpiresIn > 0)
    {
      if (response?.Scopes?.Length != TwitchScopes.Length) { Log.Warning("Current Twitch OAuth token is missing some scopes."); }
      else
      {
        BotOAuthTokenExpiration = DateTime.Now + TimeSpan.FromSeconds(response.ExpiresIn.Value) - OAuthTokenExpirationSomething;
        Log.Information("Twitch OAuth token validation succeeded. Token expiries in {time} hours.", MathF.Round(response.ExpiresIn.Value / 3600f, 2));
        return true;
      }
    }
    else { Log.Error("Twitch OAuth token validation failed."); }

    return false;
  }

  /// <summary> Refreshes access token using refresh token. </summary>
  /// <returns>true if new token was acquired, otherwise false.</returns>
  public static bool RefreshAccessToken()
  {
    if (DateTime.Now < BotOAuthTokenExpiration) return false;

    Log.Information("Refreshing Twitch OAuth token.");
    using HttpRequestMessage request = new(HttpMethod.Post, "https://id.twitch.tv/oauth2/token");
    request.Content = new StringContent(string.Concat(
        "client_id=", Secret.Data[Secret.Keys.CustomerID],
        "&client_secret=", Secret.Data[Secret.Keys.Password],
        "&grant_type=refresh_token",
        "&refresh_token=", Secret.Data[Secret.Keys.OAuthRefreshToken].Replace(":", "%3A") // Change to url encoded
    ));
    request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/x-www-form-urlencoded");

    string resp;
    try { resp = Client.Send(request).Content.ReadAsStringAsync().Result; }
    catch (HttpRequestException ex) { Log.Error("Twitch OAuth token refresh failed. {ex}", ex); return false; }
    var response = AccessTokenResponse.Deserialize(resp);
    if (response is null || response.Token is null || response.RefreshToken is null)
    {
      Log.Error("Twitch OAuth token refresh failed. Response was empty or didn't received access token!");
      return false;
    }
    Log.Information(response.ToString());
    // Read information from received data
    Secret.Data[Secret.Keys.OAuthToken] = response.Token;
    Secret.Data[Secret.Keys.OAuthRefreshToken] = response.RefreshToken;
    BotOAuthTokenExpiration = DateTime.Now + TimeSpan.FromSeconds(response.ExpiresIn.Value) - OAuthTokenExpirationSomething;

    return true;
  }

  /// <summary> Requests new Spotify OAuth token using Authorization Code Flow. </summary>
  private static void GetNewSpotifyOAuthToken()
  {
    Log.Information("Requesting new Spotify OAuth token.");

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

      string resp;
      try { resp = Client.Send(request).Content.ReadAsStringAsync().Result; }
      catch (HttpRequestException ex) { Log.Error("Spotify OAuth token request failed. {ex}", ex); return; }
      SpotifyAccessTokenResponse response = SpotifyAccessTokenResponse.Deserialize(resp);
      if (response is null || response.Token is null || response.RefreshToken is null)
      {
        Log.Warning("Spotify OAuth token request failed. Response was empty or didn't received access token!\nProbably ClientID or ClientPassowrd doesn't match!");
      }
      else
      {
        Log.Information(response.ToString());
        // Read information from received data
        Secret.Data[Secret.Keys.SpotifyOAuthToken] = response.Token;
        Secret.Data[Secret.Keys.SpotifyOAuthRefreshToken] = response.RefreshToken;
        SpotifyOAuthTokenExpiration = DateTime.Now + TimeSpan.FromSeconds(response.ExpiresIn.Value) - OAuthTokenExpirationSomething;
      }
    }
    else
    {
      // Something went wrong
      Log.Warning("Spotify OAuth token request failed. Something went wrong! Response url didn't include code part!");
    }
  }

  /// <summary> Updates Spotify tokens in database saving current access tokens. </summary>
  public static void UpdateSpotifyTokens()
  {
    Database.UpdateValueInConfig(Database.Keys.SpotifyOAuth, Secret.Data[Secret.Keys.SpotifyOAuthToken]).Wait();
    Database.UpdateValueInConfig(Database.Keys.SpotifyOAuthRefresh, Secret.Data[Secret.Keys.SpotifyOAuthRefreshToken]).Wait();
  }

  /// <summary> Refreshes Spotify access token using refresh token. </summary>
  /// <returns> true if everything was ok, otherwise false</returns>
  public static bool RefreshSpotifyAccessToken(bool forceRefresh = false)
  {
    if (!Spotify.Working && !forceRefresh) return false;
    if (DateTime.Now < SpotifyOAuthTokenExpiration) return false;

    Log.Information("Refreshing Spotify access token.");
    using HttpRequestMessage request = new(HttpMethod.Post, "https://accounts.spotify.com/api/token");
    request.Content = new StringContent(string.Concat(
        "grant_type=refresh_token",
        "&refresh_token=", Secret.Data[Secret.Keys.SpotifyOAuthRefreshToken].Replace(":", "%3A") // Change to url encoded
    ));
    request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/x-www-form-urlencoded");
    request.Headers.Add("Authorization", $"Basic {Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Secret.Data[Secret.Keys.SpotifyClientID]}:{Secret.Data[Secret.Keys.SpotifyClientSecret]}"))}");

    string resp;
    try { resp = Client.Send(request).Content.ReadAsStringAsync().Result; }
    catch (HttpRequestException ex) { Log.Error("Spotify OAuth token refresh failed. {ex}", ex); return false; }
    SpotifyAccessTokenResponse response = SpotifyAccessTokenResponse.Deserialize(resp);
    if (response is null || response.Token is null)
    {
      Log.Warning("Spotify OAuth token refresh failed. Response was empty or didn't received access token!\nProbably ClientID or ClientPassowrd doesn't match!");
      return false;
    }
    else
    {
      Log.Information(response.ToString());
      // Read information from received data
      Secret.Data[Secret.Keys.SpotifyOAuthToken] = response.Token;
      // When refreshing refresh token stays the same??
      if (response.RefreshToken?.Length > 0) Secret.Data[Secret.Keys.SpotifyOAuthRefreshToken] = response.RefreshToken;
      SpotifyOAuthTokenExpiration = DateTime.Now + TimeSpan.FromSeconds(response.ExpiresIn.Value) - OAuthTokenExpirationSomething;
      return true;
    }
  }

  /// <summary> Request new access token. </summary>
  private static void GetNewDiscordOAuthToken()
  {
    Log.Information("Requesting new Discord OAuth token.");

    string uri = string.Concat(
      "https://discord.com/oauth2/authorize?",
      "client_id=", Secret.Data[Secret.Keys.DiscrodClientID],
      "&permissions=274878057472",
      "&redirect_uri=", "http://localhost:3000/".Replace(":", "%3A").Replace("/", "%2F"), // Change to url encoded
      "&response_type=code",
      "&scope=", string.Join('+', DiscordScopes).Replace(":", "%3A"), // Change to url encoded
      "&prompt=consent"
    );

    // Open the link for the user to complete authorization
    Process.Start(new ProcessStartInfo() { FileName = uri, UseShellExecute = true });

    // Local server is needed to get response to user authorizing the app (to grab the access token)
    using HttpListener localServer = new();
    localServer.Prefixes.Add("http://localhost:3000/"); // Where local server should listen for connections, maybe it should be in Config.ini? Hmm
    localServer.Start();
    HttpListenerContext context = localServer.GetContext(); // Await connection

    // For now lets just redirect to google to hide received code in browser url
    using (HttpListenerResponse resp = context.Response)
    {
      resp.Headers.Set("Content-Type", "text/plain");
      resp.Redirect("https://www.google.com");
    }

    // Close local server, it's no longer needed
    localServer.Close();

    string requestUrl = context.Request.Url != null ? context.Request.Url.Query : string.Empty;
    // Parse received request url
    if (requestUrl.StartsWith("?code="))
    {
      // Next step - request user token with received authorization code
      string code = requestUrl[6..]; // "?code=".Length
      if (code.IndexOf('&') > 0) code = code[..code.IndexOf('&')];
      using HttpRequestMessage request = new(HttpMethod.Post, "https://discord.com/api/v10/oauth2/token");
      request.Content = new StringContent(string.Concat(
          "client_id=", Secret.Data[Secret.Keys.DiscrodClientID],
          "&client_secret=", Secret.Data[Secret.Keys.DiscordClientSecret],
          "&code=", code,
          "&grant_type=authorization_code",
          "&redirect_uri=", "http://localhost:3000/".Replace(":", "%3A").Replace("/", "%2F")
        ));
      request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/x-www-form-urlencoded");

      string resp;
      try { resp = Client.Send(request).Content.ReadAsStringAsync().Result; }
      catch (HttpRequestException ex) { Log.Error("Discord OAuth token request failed. {ex}", ex); return; }
      var response = DiscordTokenResponse.Deserialize(resp);
      if (response is null || response.Token is null || response.RefreshToken is null)
      {
        Log.Warning("Discord OAuth token request failed. Response was empty or didn't received access token!\nProbably ClientID or ClientPassowrd doesn't match!");
      }
      // Read information from received data
      Secret.Data[Secret.Keys.DiscordOAuthToken] = response.Token;
      Secret.Data[Secret.Keys.DiscordOAuthRefreshToken] = response.RefreshToken;
      DiscordOAuthTokenExpiration = DateTime.Now + TimeSpan.FromSeconds(response.ExpiresIn.Value) - OAuthTokenExpirationSomething;
    }
    else
    {
      // Something went wrong
      Log.Warning("Discord OAuth token request failed. Response url didn't include code part!");
    }
  }

  /// <summary> Updates Discord tokens in database saving current access tokens. </summary>
  public static void UpdateDiscordTokens()
  {
    Database.UpdateValueInConfig(Database.Keys.DiscordOAuth, Secret.Data[Secret.Keys.DiscordOAuthToken]).Wait();
    Database.UpdateValueInConfig(Database.Keys.DiscordOAuthRefresh, Secret.Data[Secret.Keys.DiscordOAuthRefreshToken]).Wait();
    Database.UpdateValueInConfig(Database.Keys.DiscordOAuthExpiration, DiscordOAuthTokenExpiration).Wait();
  }

  /// <summary> Refreshes access token using refresh token. </summary>
  /// <returns>true if new token was acquired, otherwise false.</returns>
  public static bool RefreshDiscordAccessToken(bool forceRefresh = false)
  {
    if (!Discord.Working && !forceRefresh) return false;
    if (DiscordOAuthTokenExpiration == DateTime.MinValue || DateTime.Now < DiscordOAuthTokenExpiration) return false;

    Log.Information("Refreshing Discord OAuth token.");
    using HttpRequestMessage request = new(HttpMethod.Post, "https://discord.com/api/v10/oauth2/token");
    request.Content = new StringContent(string.Concat(
        "client_id=", Secret.Data[Secret.Keys.DiscrodClientID],
        "&client_secret=", Secret.Data[Secret.Keys.DiscordClientSecret],
        "&grant_type=refresh_token",
        "&refresh_token=", Secret.Data[Secret.Keys.DiscordOAuthRefreshToken].Replace(":", "%3A") // Change to url encoded
    ));
    request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/x-www-form-urlencoded");

    string resp;
    try { resp = Client.Send(request).Content.ReadAsStringAsync().Result; }
    catch (HttpRequestException ex) { Log.Error("Discord OAuth token refresh failed. {ex}", ex); return false; }
    if (string.IsNullOrEmpty(resp) || resp.StartsWith("{\"error"))
    {
      Log.Warning("Discord OAuth token refresh failed. Response contained an error! Discord integration won't work! Message:\n{resp}", resp);
      return false;
    }
    var response = DiscordTokenResponse.Deserialize(resp);
    if (response is null || response.Token is null || response.RefreshToken is null)
    {
      Log.Warning("Discord OAuth token refresh failed. Response was empty or didn't received access token! Discord integration won't work!");
      return false;
    }
    Log.Information(response.ToString());
    // Read information from received data
    Secret.Data[Secret.Keys.DiscordOAuthToken] = response.Token;
    Secret.Data[Secret.Keys.DiscordOAuthRefreshToken] = response.RefreshToken;
    DiscordOAuthTokenExpiration = DateTime.Now + TimeSpan.FromSeconds(response.ExpiresIn.Value) - OAuthTokenExpirationSomething;

    return true;
  }

  /// <summary> Validates access token. </summary>
  /// <returns> true if access token is valid, otherwise false. </returns>
  private static bool ValidateDiscordOAuthToken()
  {
    using HttpRequestMessage request = new(HttpMethod.Get, "https://discord.com/api/v10/users/@me");
    request.Headers.Add("Authorization", $"Bearer {Secret.Data[Secret.Keys.DiscordOAuthToken]}");
    string resp;
    try { resp = Client.Send(request).Content.ReadAsStringAsync().Result; }
    catch (HttpRequestException ex) { Log.Error("Discrod OAuth token validation failed. {ex}", ex); return false; }
    var response = DiscordMeResponse.Deserialize(resp);
    if (response?.ID?.Length > 0 && response?.UserName?.Length > 0)
    {
      Log.Information("Discord OAuth token validation succeeded.");
      return true;
    }
    else
    {
      Log.Warning("Discrod OAuth token validation failed.");
      return false;
    }
  }
}
