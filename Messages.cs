using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AbevBot
{
#nullable enable
  public class WelcomeMessage
  {
    [JsonPropertyName("metadata")]
    public Metadata? Metadata { get; set; }
    [JsonPropertyName("payload")]
    public Payload? Payload { get; set; }

    public static WelcomeMessage Deserialize(string message)
    {
      WelcomeMessage? ret = JsonSerializer.Deserialize<WelcomeMessage>(message);
      if (ret is null) throw new JsonException("Couldn't parse welcome message.");

      return ret;
    }
  }

  public class ResponseMessage
  {
    [JsonPropertyName("error")]
    public string? Error { get; set; }
    [JsonPropertyName("status")]
    public int? Status { get; set; }
    [JsonPropertyName("message")]
    public string? Message { get; set; }
    [JsonPropertyName("total")]
    public int? Total { get; set; }
    [JsonPropertyName("max_total_cost")]
    public int? MaxTotalCost { get; set; }
    [JsonPropertyName("total_cost")]
    public int? TotalCost { get; set; }
    [JsonPropertyName("data")]
    public Data[]? Data { get; set; }

    public static ResponseMessage Deserialize(string message)
    {
      ResponseMessage? ret = JsonSerializer.Deserialize<ResponseMessage>(message);
      if (ret is null) throw new JsonException("Couldn't parse response message.");

      return ret;
    }
  }

  public class SubscriptionMessage
  {
    [JsonPropertyName("type")]
    public string? Type { get; set; }
    [JsonPropertyName("version")]
    public string? Version { get; set; }
    [JsonPropertyName("condition")]
    public Condition? Condition { get; set; }
    [JsonPropertyName("transport")]
    public Transport? Transport { get; set; }

    public SubscriptionMessage(string type, string version, string channelID, string sessionID)
    {
      Type = type;
      Version = version;
      Condition = new Condition(channelID);
      Transport = new Transport("websocket", sessionID);
    }

    public string ToJsonString()
    {
      return JsonSerializer.Serialize(this);
    }
  }

  public class EventMessage
  {
    [JsonPropertyName("metadata")]
    public Metadata? Metadata { get; set; }
    [JsonPropertyName("payload")]
    public object? Payload { get; set; }

    public static EventMessage Deserialize(string message)
    {
      EventMessage? ret = JsonSerializer.Deserialize<EventMessage>(message);
      if (ret is null) throw new JsonException("Couldn't parse event message.");

      return ret;
    }
  }

  public class ChannelIDResponse
  {
    [JsonPropertyName("data")]
    public ChannelIDData[]? Data { get; set; }

    public static ChannelIDResponse Deserialize(string message)
    {
      ChannelIDResponse? ret = JsonSerializer.Deserialize<ChannelIDResponse>(message);
      if (ret is null) throw new JsonException("Couldn't parse channel id response.");

      return ret;
    }
  }

  public class AccessTokenResponse
  {
    [JsonPropertyName("access_token")]
    public string? Token { get; set; }
    [JsonPropertyName("expires_in")]
    public int? ExpiresIn { get; set; }
    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; set; }
    [JsonPropertyName("scope")]
    public string[]? Scope { get; set; }
    [JsonPropertyName("token_type")]
    public string? TokenType { get; set; }

    public static AccessTokenResponse Deserialize(string message)
    {
      AccessTokenResponse? ret = JsonSerializer.Deserialize<AccessTokenResponse>(message);
      if (ret is null) throw new JsonException("Couldn't parse access token response.");

      return ret;
    }

    public override string ToString()
    {
      return string.Concat(">> Got ", TokenType, " token that expires in ", ExpiresIn, " seconds (", ExpiresIn / 3600f, " hours).");
    }
  }

  public class AccessTokenValidationResponse
  {
    [JsonPropertyName("client_id")]
    public string? ClientID { get; set; }
    [JsonPropertyName("login")]
    public string? Login { get; set; }
    [JsonPropertyName("scopes")]
    public string[]? Scopes { get; set; }
    [JsonPropertyName("user_id")]
    public string? UserID { get; set; }
    [JsonPropertyName("expires_in")]
    public int? ExpiresIn { get; set; }

    public static AccessTokenValidationResponse Deserialize(string message)
    {
      AccessTokenValidationResponse? ret = JsonSerializer.Deserialize<AccessTokenValidationResponse>(message);
      if (ret is null) throw new JsonException("Couldn't parse access token validation response.");

      return ret;
    }
  }

  public class GetChattersResponse
  {
    [JsonPropertyName("data")]
    public GetChattersData[]? Data { get; set; }
    [JsonPropertyName("pagination")]
    public Pagination? Pagination { get; set; }
    [JsonPropertyName("total")]
    public int? Total { get; set; }

    public static GetChattersResponse Deserialize(string message)
    {
      GetChattersResponse? ret = JsonSerializer.Deserialize<GetChattersResponse>(message);
      if (ret is null) throw new JsonException("Couldn't parse get chatters response.");

      return ret;
    }
  }

  public class Metadata
  {
    [JsonPropertyName("message_id")]
    public string? ID { get; set; }
    [JsonPropertyName("message_type")]
    public string? MessageType { get; set; }
    [JsonPropertyName("message_timestamp")]
    public string? Timestamp { get; set; }
    [JsonPropertyName("subscription_type")]
    public string? SubscriptionType { get; set; }
    [JsonPropertyName("subscription_version")]
    public string? SubscriptionVersion { get; set; }
  }

  public class Payload
  {
    [JsonPropertyName("session")]
    public Session? Session { get; set; }
    [JsonPropertyName("subscription")]
    public Subscription? Subscription { get; set; }
    [JsonPropertyName("event")]
    public Event? Event { get; set; }

    public static Payload Deserialize(object o)
    {
      string? str = o.ToString();
      Payload? ret = JsonSerializer.Deserialize<Payload>(str?.Length > 0 ? str : "");
      if (ret is null) throw new JsonException("Couldn't parse access token validation response.");

      return ret;
    }
  }

  public class PayloadCheer
  {
    [JsonPropertyName("session")]
    public Session? Session { get; set; }
    [JsonPropertyName("subscription")]
    public Subscription? Subscription { get; set; }
    [JsonPropertyName("event")]
    public EventCheer? Event { get; set; }

    public static PayloadCheer Deserialize(object o)
    {
      string? str = o.ToString();
      PayloadCheer? ret = JsonSerializer.Deserialize<PayloadCheer>(str?.Length > 0 ? str : "");
      if (ret is null) throw new JsonException("Couldn't parse access token validation response.");

      return ret;
    }
  }

  public class Session
  {
    [JsonPropertyName("id")]
    public string? ID { get; set; }
    [JsonPropertyName("status")]
    public string? Status { get; set; }
    [JsonPropertyName("connected_at")]
    public string? ConnectedTime { get; set; }
    [JsonPropertyName("keepalive_timeout_seconds")]
    public int? KeepAliveTime { get; set; }
    [JsonPropertyName("reconnect_url")]
    public string? ReconnectUrl { get; set; }
  }

  public class Condition
  {
    [JsonPropertyName("broadcaster_user_id")]
    public string? Broadcaster_ID { get; set; }
    [JsonPropertyName("moderator_user_id")]
    public string? Moderator_ID { get; set; }

    public Condition() { }
    public Condition(string id)
    {
      Broadcaster_ID = Moderator_ID = id;
    }
  }

  public class Transport
  {
    [JsonPropertyName("method")]
    public string? Method { get; set; }
    [JsonPropertyName("session_id")]
    public string? SessionID { get; set; }

    public Transport() { }
    public Transport(string method, string sessionID)
    {
      Method = method;
      SessionID = sessionID;
    }
  }

  public class Data
  {
    [JsonPropertyName("id")]
    public string? ID { get; set; }
    [JsonPropertyName("status")]
    public string? Status { get; set; }
    [JsonPropertyName("type")]
    public string? Type { get; set; }
    [JsonPropertyName("version")]
    public string? Version { get; set; }
    [JsonPropertyName("condition")]
    public Condition? Condition { get; set; }
    [JsonPropertyName("created_at")]
    public string? CreatedAt { get; set; }
    [JsonPropertyName("transport")]
    public Transport? Transport { get; set; }
    [JsonPropertyName("cost")]
    public int? Cost { get; set; }
  }

  public class ChannelIDData
  {
    [JsonPropertyName("id")]
    public string? ID { get; set; }
    [JsonPropertyName("login")]
    public string? Login { get; set; }
    [JsonPropertyName("display_name")]
    public string? DisplayName { get; set; }
    [JsonPropertyName("type")]
    public string? Type { get; set; }
    [JsonPropertyName("broadcaster_type")]
    public string? BroadcasterType { get; set; }
    [JsonPropertyName("description")]
    public string? Description { get; set; }
    [JsonPropertyName("profile_image_url")]
    public string? ProfileImageUrl { get; set; }
    [JsonPropertyName("offline_image_url")]
    public string? OfflineImageUrl { get; set; }
    [JsonPropertyName("view_count")]
    public int? ViewCount { get; set; }
    [JsonPropertyName("created_at")]
    public string? CreatedAt { get; set; }
  }

  public class Subscription
  {
    [JsonPropertyName("id")]
    public string? ID { get; set; }
    [JsonPropertyName("status")]
    public string? Status { get; set; }
    [JsonPropertyName("type")]
    public string? Type { get; set; }
    [JsonPropertyName("version")]
    public string? Version { get; set; }
    [JsonPropertyName("condition")]
    public Condition? Condition { get; set; }
    [JsonPropertyName("transport")]
    public Transport? Transport { get; set; }
    [JsonPropertyName("created_at")]
    public string? CreatedAt { get; set; }
    [JsonPropertyName("cost")]
    public int? Cost { get; set; }
  }

  public class Event
  {
    [JsonPropertyName("user_id")]
    public string? UserID { get; set; }
    [JsonPropertyName("user_login")]
    public string? UserLogin { get; set; }
    [JsonPropertyName("user_name")]
    public string? UserName { get; set; }
    [JsonPropertyName("broadcaster_user_id")]
    public string? BroadcasterUserID { get; set; }
    [JsonPropertyName("broadcaster_user_login")]
    public string? BroadcasterUserLogin { get; set; }
    [JsonPropertyName("broadcaster_user_name")]
    public string? BroadcasterUserName { get; set; }
    [JsonPropertyName("moderator_user_id")]
    public string? ModeratorUserID { get; set; }
    [JsonPropertyName("moderator_user_login")]
    public string? ModeratorUserLogin { get; set; }
    [JsonPropertyName("moderator_user_name")]
    public string? ModeratorUserName { get; set; }

    // channel.follow
    [JsonPropertyName("followed_at")]
    public string? FollowedAt { get; set; }

    // channel.ban
    [JsonPropertyName("banned_at")]
    public string? BannedAt { get; set; }
    [JsonPropertyName("ends_at")]
    public string? EndsAt { get; set; }
    [JsonPropertyName("is_permanent")]
    public bool? IsPermanent { get; set; }
    [JsonPropertyName("reason")]
    public string? Reason { get; set; }

    // channel.subscribe
    [JsonPropertyName("is_gift")]
    public bool? IsGift { get; set; }
    [JsonPropertyName("is_anonymous")]
    public bool? IsAnonymous { get; set; }
    [JsonPropertyName("tier")]
    public string? Tier { get; set; }
    [JsonPropertyName("total")]
    public int? TotalGifted { get; set; }
    [JsonPropertyName("cumulative_months")]
    public int? MonthsCumulative { get; set; }
    [JsonPropertyName("duration_months")]
    public int? MonthsDuration { get; set; }
    [JsonPropertyName("streak_months")]
    public int? MonthsStreak { get; set; }
    [JsonPropertyName("message")]
    public EventPayloadMessage? Message { get; set; }

    // channel points redemption
    [JsonPropertyName("id")]
    public string? ID { get; set; }
    [JsonPropertyName("redeemed_at")]
    public string? RedeemedAt { get; set; }
    [JsonPropertyName("reward")]
    public Reward? Reward { get; set; }
    [JsonPropertyName("status")]
    public string? Status { get; set; }
  }

  // Twitch used the same json name for different type variable between sub and cheer event message...
  public class EventCheer : Event
  {
    // channel.cheer
    [JsonPropertyName("bits")]
    public int? Bits { get; set; }
    [JsonPropertyName("message")]
    public new string? Message { get; set; }
  }

  public class EventPayloadMessage
  {
    [JsonPropertyName("emotes")]
    public EventMessageEmote[]? Emotes { get; set; }
    [JsonPropertyName("text")]
    public string? Text { get; set; }
  }

  public class EventMessageEmote
  {
    [JsonPropertyName("begin")]
    public int? Begin { get; set; }
    [JsonPropertyName("end")]
    public int? End { get; set; }
    [JsonPropertyName("id")]
    public string? ID { get; set; }
  }

  public class GetChattersData
  {
    [JsonPropertyName("user_id")]
    public string? UserID { get; set; }
    [JsonPropertyName("user_login")]
    public string? UserLogin { get; set; }
    [JsonPropertyName("user_name")]
    public string? UserName { get; set; }
  }

  public class Pagination
  {
    [JsonPropertyName("cursor")]
    public string? Cursor { get; set; }
  }

  public class BanMessageRequest
  {
    [JsonPropertyName(name: "data")]
    public BanMessageRequestData? Data { get; set; }

    public BanMessageRequest(long userID, int duration, string reason)
    {
      Data = new()
      {
        UserID = userID.ToString(),
        Duration = duration,
        Reason = reason
      };
    }

    public string ToJsonString()
    {
      return JsonSerializer.Serialize(this);
    }
  }

  public class BanMessageRequestData
  {
    [JsonPropertyName("user_id")]
    public string? UserID { get; set; }
    [JsonPropertyName("duration")]
    public int? Duration { get; set; }
    [JsonPropertyName("reason")]
    public string? Reason { get; set; }
  }

  public class Reward
  {
    [JsonPropertyName("cost")]
    public int? Cost { get; set; }
    [JsonPropertyName("id")]
    public string? ID { get; set; }
    [JsonPropertyName("prompt")]
    public string? Prompt { get; set; }
    [JsonPropertyName("title")]
    public string? Title { get; set; }
  }

  public class StreamElementsResponse
  {
    [JsonPropertyName("statusCode")]
    public int? StatusCode { get; set; }
    [JsonPropertyName("error")]
    public string? Error { get; set; }
    [JsonPropertyName("message")]
    public string? Message { get; set; }

    public static StreamElementsResponse Deserialize(string message)
    {
      StreamElementsResponse? ret = JsonSerializer.Deserialize<StreamElementsResponse>(message);
      if (ret is null) throw new JsonException("Couldn't parse stream elements response.");

      return ret;
    }
  }

  public class GlotPaste
  {
    // Glot.io
    [JsonPropertyName("language")]
    public string? Language { get; set; }
    [JsonPropertyName("title")]
    public string? Title { get; set; }
    [JsonPropertyName("public")]
    public bool Public { get; set; }
    [JsonPropertyName("files")]
    public List<GlotFile> Files { get; set; } = new();

    public string ToJsonString()
    {
      return JsonSerializer.Serialize(this);
    }
  }

  public class GlotFile
  {
    [JsonPropertyName("name")]
    public string? Name { get; set; }
    [JsonPropertyName("content")]
    public string? Content { get; set; }
  }

  public class GlotResponse
  {
    // Just Url is needed
    [JsonPropertyName("url")]
    public string? Url { get; set; }

    public static GlotResponse Deserialize(string message)
    {
      GlotResponse? ret = JsonSerializer.Deserialize<GlotResponse>(message);
      if (ret is null) throw new JsonException("Couldn't parse Glot.io response.");

      return ret;
    }
  }

  public class TikTokTTSResponse
  {
    [JsonPropertyName(name: "data")]
    public TikTokTTSResponseData? Data { get; set; }
    [JsonPropertyName(name: "extra")]
    public TikTokTTSResponseExtra? Extra { get; set; }
    [JsonPropertyName(name: "message")]
    public string? Message { get; set; }
    [JsonPropertyName("status_code")]
    public int? StatusCode { get; set; }
    [JsonPropertyName("status_msg")]
    public string? StatusMessage { get; set; }

    public static TikTokTTSResponse Deserialize(string message)
    {
      TikTokTTSResponse? ret = JsonSerializer.Deserialize<TikTokTTSResponse>(message);
      if (ret is null) throw new JsonException("Couldn't parse TikTok TTS response.");

      return ret;
    }
  }

  public class TikTokTTSResponseExtra
  {
    [JsonPropertyName("log_id")]
    public string? LogID { get; set; }
  }

  public class TikTokTTSResponseData
  {
    [JsonPropertyName("s_key")]
    public string? SKey { get; set; }
    [JsonPropertyName("v_str")]
    public string? VStr { get; set; }
    [JsonPropertyName("duration")]
    public string? Duration { get; set; }
    [JsonPropertyName("speaker")]
    public string? Speaker { get; set; }
  }

#nullable restore
}
