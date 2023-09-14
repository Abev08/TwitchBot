using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;

namespace AbevBot
{
  public static class AccessTokens
  {
    public static DateTime BotOAuthTokenExpiration { get; private set; }
    // Time before OAuth token expiries to try to refresh it
    public static TimeSpan OAuthTokenExpirationSomething { get; } = new TimeSpan(0, 10, 0);
    private static Timer RefreshTimer;

    public static void GetAccessTokens()
    {
      FileInfo oauthFile = new(".tokens");

      if (oauthFile.Exists)
      {
        // The file exists, try it out
        string[] lines = File.ReadAllLines(oauthFile.FullName);
        if (lines.Length < 2) { GetNewOAuthToken(); } // File is corrupt, generate new access token
        else
        {
          // Read file data
          Config.Data[Config.Keys.BotOAuthToken] = lines[0].Trim();
          Config.Data[Config.Keys.BotOAuthRefreshToken] = lines[1].Trim();

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
          }
        }
      }
      else
      {
        // The file doesn't exist, get new access token
        GetNewOAuthToken();
      }

      UpdateTokensFile();

      // Start refresh timer, every 2 min. check if access token should be refreshed
      if (RefreshTimer is null)
      {
        RefreshTimer = new((e) => RefreshAccessToken(), null, TimeSpan.Zero, new TimeSpan(0, 2, 0));
      }
    }

    private static void UpdateTokensFile()
    {
      File.WriteAllLines(".tokens",
        new string[] {
          Config.Data[Config.Keys.BotOAuthToken],
          Config.Data[Config.Keys.BotOAuthRefreshToken]
        });
    }

    private static void GetNewOAuthToken()
    {
      MainWindow.ConsoleWarning(">> Requesting new access token.");

      string uri = string.Concat(
        "https://id.twitch.tv/oauth2/authorize?",
        "client_id=", Config.Data[Config.Keys.BotClientID],
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

            // Events bot scopes
            "+channel:read:redemptions", // View Channel Points custom rewards and their redemptions on a channel
            "+channel:read:subscriptions", // View a list of all subscribers to a channel and check if a user is subscribed to a channel
            "+moderator:read:followers" // Read the followers of a broadcaster
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
        using (HttpRequestMessage request = new(new HttpMethod("POST"), "https://id.twitch.tv/oauth2/token"))
        {
          request.Content = new StringContent(string.Concat(
              "client_id=", Config.Data[Config.Keys.BotClientID],
              "&client_secret=", Config.Data[Config.Keys.BotPass],
              "&code=", code,
              "&grant_type=authorization_code",
              "&redirect_uri=http://localhost:3000"
            ));
          request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/x-www-form-urlencoded");

          using (HttpClient client = new())
          {
            AccessTokenResponse response = AccessTokenResponse.Deserialize(client.Send(request).Content.ReadAsStringAsync().Result);
            if (response is null || response.Token is null || response.RefreshToken is null) throw new Exception("Response was empty or didn't received access token!");
            MainWindow.ConsoleWarning(response.ToString());
            // Read information from received data
            Config.Data[Config.Keys.BotOAuthToken] = response.Token;
            Config.Data[Config.Keys.BotOAuthRefreshToken] = response.RefreshToken;
            BotOAuthTokenExpiration = DateTime.Now + new TimeSpan(0, 0, response.ExpiresIn.Value) - OAuthTokenExpirationSomething;
          }
        }
      }
      else
      {
        // Something went wrong
        throw new Exception("Something went wrong! Response url didn't include code part!");
      }
    }

    private static bool ValidateOAuthToken()
    {
      using (HttpRequestMessage request = new(new HttpMethod("GET"), "https://id.twitch.tv/oauth2/validate"))
      {
        request.Headers.Add("Authorization", $"OAuth {Config.Data[Config.Keys.BotOAuthToken]}");

        using (HttpClient client = new())
        {
          AccessTokenValidationResponse response = AccessTokenValidationResponse.Deserialize(client.Send(request).Content.ReadAsStringAsync().Result);
          if (response?.ClientID?.Equals(Config.Data[Config.Keys.BotClientID]) == true && response?.ExpiresIn > 0)
          {
            BotOAuthTokenExpiration = DateTime.Now + new TimeSpan(0, 0, response.ExpiresIn.Value) - OAuthTokenExpirationSomething;
            MainWindow.ConsoleWarning($">> Access token validation succeeded. Token expiries in {response.ExpiresIn.Value / 3600f} hours.");
            return true;
          }
          else
          {
            MainWindow.ConsoleWarning(">> Access token validation failed.");
          }
        }
      }

      return false;
    }

    public static void RefreshAccessToken()
    {
      if (DateTime.Now < BotOAuthTokenExpiration) return;

      MainWindow.ConsoleWarning(">> Refreshing access token.");
      using (HttpRequestMessage request = new(new HttpMethod("POST"), "https://id.twitch.tv/oauth2/token"))
      {
        request.Content = new StringContent(string.Concat(
            "client_id=", Config.Data[Config.Keys.BotClientID],
            "&client_secret=", Config.Data[Config.Keys.BotPass],
            "&grant_type=refresh_token",
            "&refresh_token=", Config.Data[Config.Keys.BotOAuthRefreshToken].Replace(":", "%3A") // Change to url encoded
        ));
        request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/x-www-form-urlencoded");

        using (HttpClient client = new())
        {
          AccessTokenResponse response = AccessTokenResponse.Deserialize(client.Send(request).Content.ReadAsStringAsync().Result);
          if (response is null || response.Token is null || response.RefreshToken is null) throw new Exception("Response was empty or didn't received access token!");
          MainWindow.ConsoleWarning(response.ToString());
          // Read information from received data
          Config.Data[Config.Keys.BotOAuthToken] = response.Token;
          Config.Data[Config.Keys.BotOAuthRefreshToken] = response.RefreshToken;
          BotOAuthTokenExpiration = DateTime.Now + new TimeSpan(0, 0, response.ExpiresIn.Value) - OAuthTokenExpirationSomething;
          UpdateTokensFile();
        }
      }
    }
  }
}