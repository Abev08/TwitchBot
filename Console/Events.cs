using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

public class Events
{
    static bool botStarted;
    static HttpClient Client = new HttpClient();
    static HttpListener LocalServer = new HttpListener();
    static List<object> EventQueue = new List<object>();
    static byte[] ServerReceiveBuffer = new byte[10000];
    static Process NgrokProcess;
    static ManualResetEvent ManualEvent;

    public static void Start()
    {
        if (botStarted) return;
        botStarted = true;
        Program.ConsoleWarning(">> Starting event bot.");

        if (GetLocalTunnel()) return; // Return on error

        // Start local server
        Program.ConsoleWarning(">> Starting local server.");
        LocalServer.Prefixes.Add("http://localhost:3000/"); // Where local server should listen for connections, maybe it should be in Config.ini? Hmm
        LocalServer.Start();

        GetNewAccessToken(Config.RedemptionsAndBitsNotifications);

        // Get broadcaster ID
        Program.ConsoleWarning(">> Getting broadcaster ID.");
        string uri = $"https://api.twitch.tv/helix/users?login={Config.ChannelName}";
        using (HttpRequestMessage request = new HttpRequestMessage(new HttpMethod("GET"), uri))
        {
            request.Headers.Add("Authorization", $"Bearer {Config.BotAccessToken}");
            request.Headers.Add("Client-Id", $"{Config.BotClientID}");

            string response = Client.SendAsync(request).Result.Content.ReadAsStringAsync().Result;
            if (response.Contains("\"id\":"))
            {
                int index = response.IndexOf("\"id\":") + 6;
                Config.BroadcasterID = response.Substring(index, response.IndexOf("\",", index) - index);
            }
        }

        // Clear previous event subscriptions
        Program.ConsoleWarning(">> Clearing previous event subscriptions.");
        ClearPreviousEventSubscriptions();

        // Start listening to event messages
        new Thread(() =>
        {
            while (true)
            {
                HttpListenerContext context = LocalServer.GetContext();
                ParseServerRequest(context);
            }
        })
        { Name = "Events thread", IsBackground = true }.Start();

        ManualEvent = new ManualResetEvent(false); // Reset manual event
        bool awaitingResponse = false;

        if (Config.FollowsNotifications)
        {
            // Subscribe to follow events (possible with app token and user token)
            Program.ConsoleWarning(">> Subscribing to channel follow event.");
            using (HttpRequestMessage request = new HttpRequestMessage(new HttpMethod("POST"), "https://api.twitch.tv/helix/eventsub/subscriptions"))
            {
                request.Headers.Add("Authorization", $"Bearer {Config.BotAccessToken}");
                request.Headers.Add("Client-Id", $"{Config.BotClientID}");
                request.Content = new StringContent("{\"type\":\"channel.follow\"," +
                                                    "\"version\":\"1\"," +
                                                    "\"condition\":{\"broadcaster_user_id\":\"" + Config.BroadcasterID + "\"}," +
                                                    "\"transport\":{\"method\":\"webhook\",\"callback\":\"" + Config.NgrokTunnelAddress + "\",\"secret\":\"secretsecret\"}}");
                request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json");

                string response = Client.SendAsync(request).Result.Content.ReadAsStringAsync().Result;
                awaitingResponse = ParseSubscribeResponse(response);
            }

            if (awaitingResponse)
            {
                if (ManualEvent.WaitOne(5000) == false) Program.ConsoleWarning(">> Reached maximum response waiting time."); // Wait for response, return false if timed out
            }
            ManualEvent.Reset(); // Reset manual event
        }

        // Subscription to those events require user token
        if (Config.RedemptionsAndBitsNotifications)
        {
            // Subscribe to custom rewards redemptions events (possible only with user token)
            Program.ConsoleWarning(">> Subscribing to channel custom reward redemption event.");
            using (HttpRequestMessage request = new HttpRequestMessage(new HttpMethod("POST"), "https://api.twitch.tv/helix/eventsub/subscriptions"))
            {
                request.Headers.Add("Authorization", $"Bearer {Config.BotAccessToken}");
                request.Headers.Add("Client-Id", $"{Config.BotClientID}");
                request.Content = new StringContent("{\"type\":\"channel.channel_points_custom_reward_redemption.add\"," +
                                                    "\"version\":\"1\"," +
                                                    "\"condition\":{\"broadcaster_user_id\":\"" + Config.BroadcasterID + "\"}," +
                                                    "\"transport\":{\"method\":\"webhook\",\"callback\":\"" + Config.NgrokTunnelAddress + "\",\"secret\":\"secretsecret\"}}");
                request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json");

                string response = Client.SendAsync(request).Result.Content.ReadAsStringAsync().Result;
                awaitingResponse = ParseSubscribeResponse(response);
            }

            if (awaitingResponse)
            {
                if (ManualEvent.WaitOne(5000) == false) Program.ConsoleWarning(">> Reached maximum response waiting time."); // Wait for response, return false if timed out
            }
            ManualEvent.Reset(); // Reset manual event

            // Subscribe to cheer events (possible only with user token)
            Program.ConsoleWarning(">> Subscribing to cheer event.");
            using (HttpRequestMessage request = new HttpRequestMessage(new HttpMethod("POST"), "https://api.twitch.tv/helix/eventsub/subscriptions"))
            {
                request.Headers.Add("Authorization", $"Bearer {Config.BotAccessToken}");
                request.Headers.Add("Client-Id", $"{Config.BotClientID}");
                request.Content = new StringContent("{\"type\":\"channel.cheer\"," +
                                                    "\"version\":\"1\"," +
                                                    "\"condition\":{\"broadcaster_user_id\":\"" + Config.BroadcasterID + "\"}," +
                                                    "\"transport\":{\"method\":\"webhook\",\"callback\":\"" + Config.NgrokTunnelAddress + "\",\"secret\":\"secretsecret\"}}");
                request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json");

                string response = Client.SendAsync(request).Result.Content.ReadAsStringAsync().Result;
                awaitingResponse = ParseSubscribeResponse(response);
            }

            if (awaitingResponse)
            {
                if (ManualEvent.WaitOne(5000) == false) Program.ConsoleWarning(">> Reached maximum response waiting time."); // Wait for response, return false if timed out
            }
            ManualEvent.Reset(); // Reset manual event
        }

        Program.ConsoleWarning(">> Event bot finished initializing.");
    }

    static void GetNewAccessToken(bool involweUser = true)
    {
        Program.ConsoleWarning(">> Getting new access token.");

        if (involweUser)
        {
            string uri = "https://id.twitch.tv/oauth2/authorize?" +
                        $"client_id={Config.BotClientID}" +
                        "&redirect_uri=http://localhost:3000" +
                        "&response_type=code" +
                        "&scope=" + ("bits:read" // View Bits information for a channel
                                    + "+channel:read:redemptions" // View Channel Points custom rewards and their redemptions on a channel.
                                    ).Replace(":", "%3A");

            // Open the link for the user to complete authorization
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo() { FileName = uri, UseShellExecute = true });

            HttpListenerContext context = LocalServer.GetContext(); // Await connection

            // For now lets just redirect to twitch to hide received code in browser url
            using (HttpListenerResponse resp = context.Response)
            {
                resp.Headers.Set("Content-Type", "text/plain");
                resp.Redirect(@"https://www.twitch.tv");
            }

            string requestUrl = context.Request.Url != null ? context.Request.Url.Query : string.Empty;
            // Parse received request url
            if (requestUrl.StartsWith("?code="))
            {
                // Next step - request user token with received authorization code
                string code = requestUrl.Substring(6, requestUrl.IndexOf('&', 6) - 6);
                using (HttpRequestMessage request = new HttpRequestMessage(new HttpMethod("POST"), "https://id.twitch.tv/oauth2/token"))
                {
                    request.Content = new StringContent($"client_id={Config.BotClientID}" +
                                                        $"&client_secret={Config.BotSecret}" +
                                                        $"&code={code}" +
                                                        "&grant_type=authorization_code" +
                                                        "&redirect_uri=http://localhost:3000");
                    request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/x-www-form-urlencoded");

                    string response2 = Client.SendAsync(request).Result.Content.ReadAsStringAsync().Result;
                    TwitchUserTokenResponse? response = JsonSerializer.Deserialize<TwitchUserTokenResponse>(response2);
                    if (response != null)
                    {
                        Config.BotUserAccessToken = response.access_token;
                        Config.BotRefreshToken = response.refresh_token;
                    }
                }
            }
            else
            {
                // Something went wrong
                throw new NotImplementedException("Implement something here :)");
            }
        }

        // Access token without getting user involved (just app token)
        using (HttpRequestMessage request = new HttpRequestMessage(new HttpMethod("POST"), "https://id.twitch.tv/oauth2/token"))
        {
            request.Content = new StringContent($"client_id={Config.BotClientID}" +
                                                $"&client_secret={Config.BotSecret}" +
                                                "&grant_type=client_credentials");
            request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/x-www-form-urlencoded");

            string response = Client.SendAsync(request).Result.Content.ReadAsStringAsync().Result;
            if ((response != null) && (response.Contains("access_token")))
            {
                int index = response.IndexOf("access_token") + 15;
                Config.BotAccessToken = response.Substring(index, response.IndexOf(',', index) - index - 1);
            }
            else
            {
                // Something went wrong
                throw new NotImplementedException("Implement something here :)");
            }
        }
    }

    static bool GetLocalTunnel()
    {
        FileInfo ngrokFile = new FileInfo(@"ngrok.exe");
        if (ngrokFile.Exists == false)
        {
            Program.ConsoleWarning(">> ngrok.exe file not found." + Environment.NewLine + ">> Extended bot capability not available.");
            return true; // Return error
        }

        // Start ngrok tunnel
        Program.ConsoleWarning(">> Starting ngrok http tunnel.");
        if (NgrokProcess == null)
        {
            NgrokProcess = new Process();
            AppDomain.CurrentDomain.ProcessExit += (sender, e) => NgrokProcess.Close(); // Close ngrok process on app exit, I think that problem is only when stopping the debugger
        }
        NgrokProcess.StartInfo.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;
        NgrokProcess.StartInfo.UseShellExecute = false;
        NgrokProcess.StartInfo.RedirectStandardOutput = true;
        NgrokProcess.StartInfo.FileName = "cmd.exe";
        NgrokProcess.StartInfo.Arguments = $"/C ngrok http 3000 --authtoken={Config.NgrokAuthToken} --host-header=\"localhost:3000\"";
        NgrokProcess.Start(); // In debugging make sure that ngrok.exe procces is closed otherwise you will get spammed :D

        using (HttpRequestMessage request = new HttpRequestMessage(new HttpMethod("GET"), "http://localhost:4040/api/tunnels"))
        {
            string response = Client.SendAsync(request).Result.Content.ReadAsStringAsync().Result;
            int index = response.IndexOf("public_url\":");
            if (index < 0)
            {
                // Something went wrong
                Program.ConsoleWarning(">> Error. Can't connect to ngrok api." + Environment.NewLine + ">> Extended bot capability not available.");
                return true; // Return error
            }
            else
            {
                index += 13;
                Config.NgrokTunnelAddress = response.Substring(index, response.IndexOf('"', index) - index);
            }
        }

        return false;
    }

    static void ParseServerRequest(HttpListenerContext context)
    {
        int bytesRead = context.Request.InputStream.Read(ServerReceiveBuffer);
        if (bytesRead >= ServerReceiveBuffer.Length) Program.ConsoleWarning(">> Message exceeded buffer size");

        string text = Encoding.UTF8.GetString(ServerReceiveBuffer, 0, bytesRead);

        context.Response.StatusCode = 200; // Assume status code 200

        try
        {
            JsonObject? message = JsonSerializer.Deserialize<JsonObject>(text);
            if (message != null)
            {
                if (message["subscription"]["status"].ToString() == "webhook_callback_verification_pending")
                {
                    // Program.ConsoleWarning(">> Received webhook_callback_verification_pending message.");
                    int index = text.LastIndexOf("challenge") + 12;
                    string challenge = text.Substring(index, text.LastIndexOf('"') - index);
                    // Program.ConsoleWarning(">> Sending response with challenge text.");
                    Program.ConsoleWarning(">> Subscription successful.");
                    context.Response.OutputStream.Write(Encoding.UTF8.GetBytes(challenge));
                }
                else if (message["subscription"]["status"].ToString() == "enabled")
                {
                    if (message["subscription"]["type"].ToString() == "channel.follow")
                    {
                        Console.WriteLine("> " + message["event"]["user_name"] + " followed " + message["event"]["broadcaster_user_name"]);
                    }
                }
                else
                {
                    Program.ConsoleWarning(">> Received new event message!");
                    Console.WriteLine(text); // Print received message
                }
            }
        }
        catch (Exception ex)
        {
            Program.ConsoleWarning(ex.Message);
            Console.WriteLine(context.Request.Url);
        }

        context.Response.Close(); // Send server response
        ManualEvent.Set(); // Set manual event to message received, probably should be inside "verification_pending" if, but it's here, for now :)
    }

    static void ClearPreviousEventSubscriptions()
    {
        // Why clear all? Because callback url from ngrok can change and previous subscriptions wouldn't work.
        // Get subscription list
        using (HttpRequestMessage request = new HttpRequestMessage(new HttpMethod("GET"), "https://api.twitch.tv/helix/eventsub/subscriptions"))
        {
            request.Headers.Add("Authorization", $"Bearer {Config.BotAccessToken}");
            request.Headers.Add("Client-Id", $"{Config.BotClientID}");

            string response = Client.SendAsync(request).Result.Content.ReadAsStringAsync().Result;
            if (response != null)
            {
                try
                {
                    JsonObject? message = JsonSerializer.Deserialize<JsonObject>(response);
                    if (message != null)
                    {
                        int? count = (int)message["total"];
                        if (count == null) count = 0;

                        for (int i = 0; i < count; i++)
                        {
                            Program.ConsoleWarning(">> Deleting event subscription with ID: " + message["data"][i]["id"] + ".");
                            using (HttpRequestMessage request2 = new HttpRequestMessage(new HttpMethod("DELETE"), "https://api.twitch.tv/helix/eventsub/subscriptions?id=" + message["data"][i]["id"]))
                            {
                                request2.Headers.Add("Authorization", $"Bearer {Config.BotAccessToken}");
                                request2.Headers.Add("Client-Id", $"{Config.BotClientID}");

                                string response2 = Client.SendAsync(request2).Result.Content.ReadAsStringAsync().Result;
                                if (string.IsNullOrEmpty(response2) == false) Console.WriteLine(response2);
                            }
                        }
                    }
                }
                catch (Exception ex) { Program.ConsoleWarning(ex.Message); }
            }
            else
            {
                // Something went wrong
                throw new NotImplementedException("Implement something here :)");
            }
        }
    }

    static bool ParseSubscribeResponse(string text)
    {
        try
        {
            JsonObject? message = JsonSerializer.Deserialize<JsonObject>(text);
            if (message == null) return false; // Response was empty? Assume that it failed

            if (string.IsNullOrEmpty(Convert.ToString(message["error"])) == false)
            {
                Program.ConsoleWarning(">> Subscription failed. " + message["message"]);
                return false;
            }
        }
        catch { }

        return true;
    }

    class TwitchUserTokenResponse
    {
        public string access_token { get; set; }
        public int expires_in { get; set; }
        public string refresh_token { get; set; }
        public string[] scope { get; set; }
        public string token_type { get; set; }
    }
}
