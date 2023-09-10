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

    public SubscriptionMessage(string type, string channelID, string sessionID)
    {
      Type = type;
      Version = "2";
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
    public Payload? Payload { get; set; }

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
    [JsonPropertyName("followed_at")]
    public string? FollowedAt { get; set; }
  }

#nullable restore
}
