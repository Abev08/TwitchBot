using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using Serilog;

namespace AbevBot
{
  /// <summary> Spotify integration. </summary>
  public static class Spotify
  {
    /// <summary> Timeout between song requests from the same chatter. </summary>
    public static TimeSpan SongRequestTimeout { get; set; } = TimeSpan.FromMinutes(2);
    /// <summary> Is connection to Spotify API working? </summary>
    public static bool Working { get; set; }
    /// <summary> Required !skipsong requests to skip the song. </summary>
    public const int REQUIREDSKIPS = 5;
    /// <summary> Song skip enabled. </summary>
    public static bool SkipEnabled { get; set; }
    /// <summary> Song request enabled </summary>
    public static bool RequestEnabled { get; set; }

    /// <summary> Gets currently playing track from Spotify API and formats it into a string. </summary>
    /// <returns> String describing currently playing track. </returns>
    public static string GetCurrentlyPlayingTrack()
    {
      using HttpRequestMessage request = new(HttpMethod.Get, "https://api.spotify.com/v1/me/player/currently-playing");
      request.Headers.Add("Authorization", $"Bearer {Secret.Data[Secret.Keys.SpotifyOAuthToken]}");
      string resp;
      try { resp = Notifications.Client.Send(request).Content.ReadAsStringAsync().Result; }
      catch (HttpRequestException ex) { Log.Error("Spotify request for current tracks failed. {ex}", ex); return null; }
      if (resp is null) { Log.Warning("Spotify request for current tracks failed."); }
      else if (resp.Length == 0) { return "Nothing is currently playing"; }
      else
      {
        // Deserialize the response message
        var response = SpotifyCurrentlyPlayingResponse.Deserialize(resp);
        if (response is null) return null;
        if (response.Item is null)
        {
          // Try to parse the message as generic response
          SpotifyResponse response2 = SpotifyResponse.Deserialize(resp);
          if (response2 is null) return null;
          if (response2.Error?.Status == 401)
          {
            Log.Warning("Spotify OAuth token expiried");
            if (AccessTokens.RefreshSpotifyAccessToken())
            {
              // Refresh was succesfull - call get current song again.
              // It may be an endless loop but after succesfully refreshing the token another 401 error in few ms is very not likely
              return GetCurrentlyPlayingTrack();
            }
            return null;
          }
          Log.Warning("Spotify API error: {msg}", response2.Error.Message);
          return null;
        }

        StringBuilder sb = new();
        // Add "playing" / "was playing"
        if (response.IsPlaying == false) { sb.Append("was playing "); }
        else { sb.Append("currently playing "); }

        // Add artists
        if (response.Item?.Artists?.Length > 0)
        {
          for (int i = 0; i < response.Item.Artists.Length; i++)
          {
            sb.Append(response.Item.Artists[i].Name);
            if (i != response.Item.Artists.Length - 1) sb.Append(", ");
          }

          sb.Append(": ");
        }

        // Add track name
        if (response.Item?.Name?.Length > 0)
        {
          sb.Append(response.Item.Name).Append(' ');
        }

        // Add year
        if (response.Item?.Album?.ReleaseDate?.Length > 0)
        {
          if (DateTime.TryParse(response.Item?.Album?.ReleaseDate, out DateTime date))
          {
            sb.Append('(').Append(date.Year).Append(") ");
          }
        }

        // Add link
        if (response.Item?.ExternalURLs?.Spotify?.Length > 0)
        {
          sb.Append(response.Item.ExternalURLs.Spotify);
        }

        return sb.ToString();
      }

      return null;
    }

    /// <summary> Gets 3 recently playing tracks from Spotify API and formats it into a string. </summary>
    /// <returns> String describing 3 recently playing tracks. </returns>
    public static string GetRecentlyPlayingTracks()
    {
      string uri = string.Concat(
        "https://api.spotify.com/v1/me/player/recently-played",
        "?limit=", 3,
        "&before=", new DateTimeOffset(DateTime.Now).ToUnixTimeMilliseconds()
      );
      using HttpRequestMessage request = new(HttpMethod.Get, uri);
      request.Headers.Add("Authorization", $"Bearer {Secret.Data[Secret.Keys.SpotifyOAuthToken]}");
      string resp;
      try { resp = Notifications.Client.Send(request).Content.ReadAsStringAsync().Result; }
      catch (HttpRequestException ex) { Log.Error("Spotify request for recent tracks failed. {ex}", ex); return null; }
      if (resp is null) { Log.Warning("Spotify request for recent tracks failed."); }
      else if (resp.Length == 0) { return "Nothing was recently playing"; }
      else
      {
        // Deserialize the response message
        var response = SpotifyRecentlyPlayed.Deserialize(resp);
        if (response is null || response.Items is null) return null;
        if (response.Items.Length == 0) return "Nothing was recently playing";

        int limit = 3;
        if (response.Items.Length < limit) limit = response.Items.Length;

        StringBuilder sb = new();
        sb.Append("previous tracks: ");

        for (int j = 0; j < limit; j++)
        {
          if (j == 0) sb.Append(j + 1).Append(". ");
          else sb.Append(" | ").Append(j + 1).Append(". ");

          // Add artists
          if (response.Items[j].Track?.Artists?.Length > 0)
          {
            for (int i = 0; i < response.Items[j].Track.Artists.Length; i++)
            {
              sb.Append(response.Items[j].Track.Artists[i].Name);
              if (i != response.Items[j].Track.Artists.Length - 1) sb.Append(", ");
            }

            sb.Append(": ");
          }

          // Add track name
          if (response.Items[j].Track?.Name?.Length > 0)
          {
            sb.Append(response.Items[j].Track.Name).Append(' ');
          }

          // Add year
          if (response.Items[j].Track?.Album?.ReleaseDate?.Length > 0)
          {
            if (DateTime.TryParse(response.Items[j].Track.Album?.ReleaseDate, out DateTime date))
            {
              sb.Append('(').Append(date.Year).Append(") ");
            }
          }

          // Add link
          if (response.Items[j].Track?.ExternalURLs?.Spotify?.Length > 0)
          {
            sb.Append(response.Items[j].Track.ExternalURLs.Spotify);
          }
        }

        return sb.ToString();
      }

      return null;
    }

    /// <summary> Adds provided track to queue to be played (requires Spotify premium). </summary>
    /// <param name="songURI"> Spotify specific track URI that identifies the track. </param>
    /// <returns> true if everything went ok, otherwise false. </returns>
    public static bool AddTrackToQueue(string songURI, string userName)
    {
      string uri = string.Concat(
        "https://api.spotify.com/v1/me/player/queue",
        "?uri=spotify%3Atrack%3A", songURI
      );
      using HttpRequestMessage request = new(HttpMethod.Post, uri);
      request.Headers.Add("Authorization", $"Bearer {Secret.Data[Secret.Keys.SpotifyOAuthToken]}");
      try
      {
        var response = Notifications.Client.Send(request);
        if ((int)response.StatusCode >= 200 && (int)response.StatusCode < 300)
        {
          // OK
          var n = new Notification()
          {
            Type = NotificationType.OTHER,
            SubType = "Song request",
            Sender = userName,
          };
          // TODO Create function GetTrackInfo(trackID) to get track info and pass it to notification above
          n.Dummy();
          Notifications.AddPastNotification(n);
          return true;
        }
        // Something went wrong, try to get error message
        string resp = response.Content.ReadAsStringAsync().Result;
        string msg = null;
        if (resp != null && resp.Length > 0)
        {
          var r = JsonSerializer.Deserialize<JsonObject>(resp);
          msg = r["error"]["message"].GetValue<string>();
        }
        Log.Warning("Couldn't add track to Spotify queue. {msg}", msg is null ? "No error message" : msg);
        return false;
      }
      catch (Exception ex)
      {
        Log.Error("Spotify add song to queue failed. {ex}", ex);
        return false;
      }
    }

    /// <summary> Skips current song (requires spotify premium). </summary>
    public static void SkipSong()
    {
      using HttpRequestMessage request = new(HttpMethod.Post, "https://api.spotify.com/v1/me/player/next");
      request.Headers.Add("Authorization", $"Bearer {Secret.Data[Secret.Keys.SpotifyOAuthToken]}");
      string resp;
      try { resp = Notifications.Client.Send(request).Content.ReadAsStringAsync().Result; } // Assume that it worked
      catch (HttpRequestException ex) { Log.Error("Spotify song skip failed. {ex}", ex); }
    }

    /// <summary> Gets songs in the queue. </summary>
    public static string GetSongQueue()
    {
      using HttpRequestMessage request = new(HttpMethod.Get, "https://api.spotify.com/v1/me/player/queue");
      request.Headers.Add("Authorization", $"Bearer {Secret.Data[Secret.Keys.SpotifyOAuthToken]}");
      string resp;
      try { resp = Notifications.Client.Send(request).Content.ReadAsStringAsync().Result; }
      catch (HttpRequestException ex)
      {
        Log.Error("Spotify song queue request failed. {ex}", ex);
        return "Something went wrong peepoSad";
      }

      if (resp is null) return "Something went wrong peepoSad"; // Something went wrong
      if (resp.Length != 0)
      {
        var response = SpotifyQueueResponse.Deserialize(resp);
        if (response is null) { return "Something went wrong peepoSad"; }
        else if (response.Queue is null || response.Queue.Length == 0) return "Nothing is queued";

        int limit = 3;
        if (response.Queue.Length < limit) { limit = response.Queue.Length; }

        StringBuilder sb = new();
        sb.Append("queued tracks: ");

        for (int j = 0; j < limit; j++)
        {
          if (j == 0) sb.Append(j + 1).Append(". ");
          else sb.Append(" | ").Append(j + 1).Append(". ");

          // Add artists
          if (response.Queue[j].Artists?.Length > 0)
          {
            for (int i = 0; i < response.Queue[j].Artists.Length; i++)
            {
              sb.Append(response.Queue[j].Artists[i].Name);
              if (i != response.Queue[j].Artists.Length - 1) sb.Append(", ");
            }

            sb.Append(": ");
          }

          // Add track name
          if (response.Queue[j].Name?.Length > 0)
          {
            sb.Append(response.Queue[j].Name).Append(' ');
          }

          // Add year
          if (response.Queue[j].Album?.ReleaseDate?.Length > 0)
          {
            if (DateTime.TryParse(response.Queue[j].Album?.ReleaseDate, out DateTime date))
            {
              sb.Append('(').Append(date.Year).Append(") ");
            }
          }

          // Add link
          if (response.Queue[j].ExternalURLs?.Spotify?.Length > 0)
          {
            sb.Append(response.Queue[j].ExternalURLs.Spotify);
          }
        }

        return sb.ToString();
      }

      return "Something went wrong peepoSad";
    }
  }
}
