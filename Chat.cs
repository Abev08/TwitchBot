using System.ComponentModel;
using System.Net.Sockets;
using System.Text;

public class Chat
{
    static bool botStarted;
    static Dictionary<string, (string, DateTime)> ResponseMessages = new Dictionary<string, (string, DateTime)>();
    static TimeSpan CooldownBetweenTheSameMessage = new TimeSpan(0, 0, 10);

    public static void Start()
    {
        if (botStarted) return;
        botStarted = true;
        Program.ConsoleWarning(">> Starting chat bot.");
        LoadResponseMessages();

        new Thread(() =>
        {
            Socket socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
            byte[] receiveBuffer = new byte[10000]; // Max IRC message is 4096 bytes?
            int zeroBytesReceivedCounter = 0, currentIndex, nextIndex, bytesReceived, messageStartOffset = 0;
            string userBadge, userName, customRewardID, temp;
            List<string> messages, message;
            (string, DateTime) dictionaryResponse;
            ManualResetEvent receiveEvent = new ManualResetEvent(false);
            // Background worker for async handling received messages
            BackgroundWorker receiveWorker = new BackgroundWorker() { WorkerSupportsCancellation = true };
            receiveWorker.DoWork += (sender, e) =>
            {
                while (socket.Connected && (receiveWorker.CancellationPending == false))
                {
                    receiveEvent.Reset();

                    socket.BeginReceive(receiveBuffer, messageStartOffset, receiveBuffer.Length - messageStartOffset, SocketFlags.None, new AsyncCallback((IAsyncResult ar) =>
                    {
                        try
                        {
                            bytesReceived = socket.EndReceive(ar);
                            if (bytesReceived > 0)
                            {
                                messages = Encoding.UTF8.GetString(receiveBuffer, 0, bytesReceived + messageStartOffset).Split("\r\n", StringSplitOptions.RemoveEmptyEntries).ToList();

                                if (messageStartOffset > 0)
                                {
                                    // For some reason if the missing part of previous message is "\r\n" it's not send on next message
                                    // Cut out the new message appended to previous one and insert it as new one
                                    if (receiveBuffer[messageStartOffset] == '@')
                                    {
                                        messages.Insert(1, messages[0].Substring(messageStartOffset));
                                        messages[0] = messages[0].Substring(0, messageStartOffset);
                                    }
                                }

                                for (int i = 0; i < messages.Count; i++)
                                {
                                    // message[0] - header, message[1] - body
                                    message = messages[i].Split("#" + Config.ChannelName, StringSplitOptions.RemoveEmptyEntries).ToList();

                                    // Check if received message is incomplete (just for last received message)
                                    if ((i == messages.Count - 1) && (receiveBuffer[bytesReceived + messageStartOffset - 1] != (byte)'\n') && (receiveBuffer[bytesReceived + messageStartOffset - 2] != (byte)'\r'))
                                    {
                                        // Move the message to beginning of receiveBuffer
                                        if (messageStartOffset == 0) Array.Clear(receiveBuffer);

                                        string s = string.Join("#" + Config.ChannelName, message);
                                        for (int j = 0; j < s.Length; j++) receiveBuffer[j + messageStartOffset] = (byte)s[j];
                                        messageStartOffset += s.Length;
                                        // Program.ConsoleWarning(">> Received incomplete message, moving offset to " + messageStartOffset);
                                        continue;
                                    }
                                    else messageStartOffset = 0;

                                    // Ping request, let's play PING - PONG with the server :D
                                    if (message[0].StartsWith("PING"))
                                    {
                                        string response = message[0].Replace("PING", "PONG");
                                        // Console.WriteLine(string.Join($"#{Program.ChannelName}", message));
                                        // Program.ConsoleWarning(">> " + response + ", " + DateTime.Now.ToString());
                                        socket.Send(Encoding.UTF8.GetBytes(response + "\r\n"));
                                        continue;
                                    }

                                    // Probably there was some "#channelName" parts in the message body
                                    // Attach them together
                                    if (message.Count < 2)
                                    {
                                        // Some messages doesn't have body part :/
                                        if (message[0].Contains("msg-id=sub") || message[0].Contains("msg-id=resub") ||
                                            message[0].Contains("msg-id=subgift") || message[0].Contains("msg-id=submysterygift") ||
                                            message[0].Contains("msg-id=raid") ||
                                            message[0].Contains("msg-id=primepaidupgrade") ||
                                            message[0].StartsWith("@emote-only=") ||
                                            message[0].Contains($":tmi.twitch.tv USERSTATE"))
                                        {
                                            message.Add(":"); // Add fake message
                                        }
                                        else
                                        {
                                            // Program.ConsoleWarning(">> Something went wrong with the message, skipping it");
                                            Console.WriteLine(string.Join($"#{Config.ChannelName}", message));
                                            continue;
                                        }
                                    }
                                    while (message.Count > 2)
                                    {
                                        message[^2] += $"#{Config.ChannelName}" + message[^1];
                                        message.RemoveAt(message.Count - 1);
                                    }

                                    // Standard message without extra tags
                                    if (message[0].StartsWith(':') && message[0].Contains("PRIVMSG"))
                                    {
                                        Console.WriteLine(
                                            String.Format("{0, 20}{1, 2}{2, -0}", message[0].Substring(1, message[0].IndexOf('!') - 1) // Username
                                            , ": ",
                                            message[1].Substring(message[1].IndexOf(':') + 1)) // message
                                        );
                                    }
                                    // Standard message with extra tags
                                    else if (message[0].StartsWith("@") && message[0].Contains("PRIVMSG"))
                                    {
                                        // Check if message was from custom reward
                                        currentIndex = message[0].IndexOf("custom-reward-id=");
                                        if (currentIndex > 0)
                                        {
                                            currentIndex += 17;
                                            customRewardID = message[0].Substring(currentIndex, message[0].IndexOf(';', currentIndex) - currentIndex);

                                            // Do something based on customRewardID
                                            switch (customRewardID)
                                            {
                                                // Cakez77 - TTS
                                                case "713be8e1-266a-4b5c-bf37-c42882ddc845":
                                                    Console.WriteLine("> TTS");
                                                    break;
                                                // Cakez77 - TTS Chipmunk
                                                case "de05a92e-380d-415d-83ed-963808c394cb":
                                                    Console.WriteLine("> TTS Chipmunk");
                                                    break;
                                                default:
                                                    Console.WriteLine("> Custom reward ID: " + customRewardID);
                                                    break;
                                            }
                                        }

                                        // Check if message had some bits cheered
                                        currentIndex = message[0].IndexOf("bits=");
                                        if (currentIndex > 0)
                                        {
                                            currentIndex += 5;
                                            Console.WriteLine($"> Cheered with {message[0].Substring(currentIndex, message[0].IndexOf(';', currentIndex) - currentIndex)} bits" +
                                                                (message[1].Length > 2 ? " Message: " + message[1].Substring(message[1].IndexOf(':') + 1) : ""));
                                            continue;
                                        }

                                        currentIndex = message[0].IndexOf("badges=") + 7;
                                        userBadge = message[0].Substring(currentIndex, (nextIndex = message[0].IndexOf(';', currentIndex - 1)) - currentIndex);
                                        if (userBadge.Contains("broadcaster")) userBadge = "GOD";
                                        else if (userBadge.Contains("moderator")) userBadge = "MOD";
                                        else if (userBadge.Contains("subscriber")) userBadge = "SUB";
                                        else if (userBadge.Contains("vip")) userBadge = "VIP";
                                        else userBadge = string.Empty;
                                        currentIndex = nextIndex;

                                        userName = message[0].Substring(currentIndex = (message[0].IndexOf("display-name=") + 13), (nextIndex = message[0].IndexOf(';', currentIndex)) - currentIndex);
                                        currentIndex = nextIndex;

                                        Console.WriteLine(String.Format("{0, -4}{1, 20}{2, 2}{3, -0}",
                                                            userBadge,
                                                            userName,
                                                            ": ",
                                                            message[1].Substring(message[1].IndexOf(":") + 1))
                                        );

                                        // Check if message starts with key to get automatic response
                                        currentIndex = message[1].IndexOf(':') + 1;
                                        nextIndex = message[1].IndexOf(' ', currentIndex);
                                        if (nextIndex < 0) nextIndex = message[1].Length - currentIndex;
                                        temp = message[1].Substring(currentIndex, nextIndex).Trim();
                                        if (ResponseMessages.TryGetValue(temp, out dictionaryResponse))
                                        {
                                            // Check if the same message was send not long ago
                                            if (DateTime.Now - dictionaryResponse.Item2 >= CooldownBetweenTheSameMessage)
                                            {
                                                socket.Send(Encoding.UTF8.GetBytes($"PRIVMSG #{Config.ChannelName} :{dictionaryResponse.Item1}\r\n"));
                                                ResponseMessages[temp] = (ResponseMessages[temp].Item1, DateTime.Now);
                                            }
                                            else Program.ConsoleWarning($">> Not sending response for \"{temp}\" key. Cooldown active.");
                                        }
                                    }
                                    // Automated bot response
                                    else if (message[0].Contains(":tmi.twitch.tv USERSTATE"))
                                    {
                                        currentIndex = message[0].IndexOf("display-name=") + 13;
                                        userName = message[0].Substring(currentIndex, (nextIndex = (message[0].IndexOf(';', currentIndex))) - currentIndex);

                                        Console.WriteLine(String.Format("{0, -4}{1, 20}{2, 2}{3, -0}",
                                                            "BOT",
                                                            userName,
                                                            ": ",
                                                            "Automated bot response (message is not available)"));
                                    }
                                    // Notification - sub / announcement
                                    else if (message[0].StartsWith("@") && message[0].Contains("USERNOTICE"))
                                    {
                                        currentIndex = message[0].IndexOf("display-name=") + 13;
                                        userName = message[0].Substring(currentIndex, (nextIndex = (message[0].IndexOf(';', currentIndex))) - currentIndex);
                                        currentIndex = nextIndex;
                                        currentIndex = message[0].IndexOf("msg-id=", currentIndex) + 7;
                                        switch (message[0].Substring(currentIndex, (message[0].IndexOf(';', currentIndex)) - currentIndex))
                                        {
                                            case "sub":
                                            case "resub":
                                                Console.WriteLine("> " + userName +
                                                                    (message[0].Contains("msg-param-was-gifted=true") ? " got gifted sub for " : " subscribed for ") +
                                                                    message[0].Substring(currentIndex = (message[0].IndexOf("msg-param-cumulative-months=", currentIndex) + 28), (message[0].IndexOf(';', currentIndex)) - currentIndex) +
                                                                    " months." +
                                                                    (message[1].Length > 2 ? " Message: " + message[1].Substring(message[1].IndexOf(':') + 1) : "")
                                                );
                                                break;
                                            case "subgift":
                                                currentIndex = message[0].IndexOf("msg-param-recipient-display-name=") + 33;
                                                Console.WriteLine("> " + userName + " gifted a sub for " +
                                                                    message[0].Substring(currentIndex, message[0].IndexOf(';', currentIndex) - currentIndex) +
                                                                    (message[1].Length > 2 ? " Message: " + message[1].Substring(message[1].IndexOf(':') + 1) : "")
                                                );
                                                break;
                                            case "submysterygift":
                                                currentIndex = message[0].IndexOf("msg-param-mass-gift-count=") + 26;
                                                Console.WriteLine("> " + userName + " gifting " +
                                                                    message[0].Substring(currentIndex, message[0].IndexOf(";", currentIndex) - currentIndex) +
                                                                    " subs for random viewers" +
                                                                    (message[1].Length > 2 ? " Message: " + message[1].Substring(message[1].IndexOf(':') + 1) : "")
                                                );
                                                break;
                                            case "primepaidupgrade":
                                                Console.WriteLine("> " + userName +
                                                                    " converted prime sub to standard sub." +
                                                                    (message[1].Length > 2 ? " Message: " + message[1].Substring(message[1].IndexOf(':') + 1) : "")
                                                );
                                                break;
                                            case "announcement":
                                                currentIndex = (message[1].IndexOf(":") + 1);
                                                Console.WriteLine("> " + userName + " announced that: " + message[1].Substring(currentIndex));
                                                break;
                                            case "raid":
                                                currentIndex = (message[0].IndexOf("msg-param-viewerCount=") + 22);
                                                Console.WriteLine("> " + userName + " raided the channel with " + message[0].Substring(currentIndex, message[0].IndexOf(';', currentIndex) - currentIndex) + " viewers.");
                                                break;
                                            default:
                                                Console.WriteLine(string.Join($"#{Config.ChannelName}", message));
                                                break;
                                        }
                                    }
                                    // Timeout
                                    else if (message[0].StartsWith("@ban-duration="))
                                    {
                                        userName = message[1].Substring(message[1].IndexOf(':') + 1);
                                        Console.WriteLine($"> User {userName} got timed out for {message[0].Substring(14, message[0].IndexOf(';') - 14)} sec.");
                                    }
                                    // Timeout?
                                    else if (message[0].StartsWith("@") && message[0].Contains("CLEARMSG"))
                                    {
                                        userName = message[0].Substring(7, message[0].IndexOf(';') - 7);
                                        Console.WriteLine($"> User {userName} got banned.");
                                    }
                                    // Different timeout?
                                    else if (message[0].StartsWith("@") && message[0].Contains("CLEARCHAT"))
                                    {
                                        userName = message[1].Substring(message[1].IndexOf(":") + 1);
                                        Console.WriteLine($"> User {userName} got banned.");
                                    }
                                    // Emote only activated
                                    else if (message[0].StartsWith("@emote-only=1"))
                                    {
                                        Console.WriteLine("> Emote only activated");
                                    }
                                    // Emote only deactivated
                                    else if (message[0].StartsWith("@emote-only=0"))
                                    {
                                        Console.WriteLine("> Emote only deactivated");
                                    }
                                    // Emote only
                                    else if (message[0].StartsWith("@msg-id=emote_only_"))
                                    {
                                        // Switching emote only sends 2 messages - this is a duplicate? Do nothing?
                                        // if (message[0].Substring(19, message[0].IndexOf(" :") - 19) == "on") Console.WriteLine("> Emote only activated");
                                        // else Console.WriteLine("> Emote only deactivated");
                                    }
                                    // Other message type
                                    else
                                    {
                                        Program.ConsoleWarning(string.Join($"#{Config.ChannelName}", message), ConsoleColor.Magenta);
                                    }
                                }
                                zeroBytesReceivedCounter = 0;
                            }
                            else
                            {
                                Program.ConsoleWarning(">> Received 0 bytes.");
                                zeroBytesReceivedCounter++;
                                if (zeroBytesReceivedCounter >= 5)
                                {
                                    Program.ConsoleWarning(">> Closing connection.");
                                    socket.Close(); // Close connection if 5 times in a row received 0 bytes
                                }
                            }
                        }
                        catch (Exception ex) { Console.WriteLine(ex.Message); }

                        receiveEvent.Set();
                    }), null);

                    receiveEvent.WaitOne();
                }
            };

            while (true)
            {
                // Try to connect
                Program.ConsoleWarning(">> Connecting...");
                socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
                socket.Connect("irc.chat.twitch.tv", 6667);
                Program.ConsoleWarning(">> Connected.");
                receiveWorker.RunWorkerAsync();
                socket.Send(Encoding.UTF8.GetBytes($"PASS {Config.BotPass}\r\n"));
                socket.Send(Encoding.UTF8.GetBytes($"NICK {Config.BotName}\r\n"));
                socket.Send(Encoding.UTF8.GetBytes($"JOIN #{Config.ChannelName},#{Config.ChannelName}\r\n"));
                socket.Send(Encoding.UTF8.GetBytes("CAP REQ :twitch.tv/commands twitch.tv/tags\r\n")); // request extended chat messages

                while (socket.Connected) Thread.Sleep(500); // While connection is active do nothing

                // Connection lost
                Program.ConsoleWarning(">> Connection lost, waiting 2 sec to reconnect.");
                receiveWorker.CancelAsync();
                Thread.Sleep(2000);
            }
        })
        { Name = "Chat thread", IsBackground = true }.Start();
    }

    static void LoadResponseMessages()
    {
        Program.ConsoleWarning(">> Loading response messages.");

        FileInfo messagesFile = new FileInfo(@"ResponseMessages.csv");
        // The file doesn't exist - create new one
        if (messagesFile.Exists == false)
        {
            Program.ConsoleWarning(">> ResponseMessages.csv file not found. Generating new one.");

            using (var writer = messagesFile.Create())
            {
                writer.Write(Encoding.UTF8.GetBytes("key; message" + Environment.NewLine));
                writer.Write(Encoding.UTF8.GetBytes("!example; This is an example response." + Environment.NewLine));
            }
        }

        // Read the file
        string[] lines = File.ReadAllLines(messagesFile.FullName);
        for (int i = 0; i < lines.Length; i++)
        {
            List<string> text = lines[i].Split(';', StringSplitOptions.RemoveEmptyEntries).ToList();
            while (text.Count > 2)
            {
                text[^2] += ";" + text[^1];
                text.RemoveAt(text.Count - 1);
            }
            text[0] = text[0].Trim(); // Lead leading and trailing white space characters
            text[1] = text[1].Trim();
            if ((text[0] == "key") && (text[1] == "message")) continue; // This is the header, skip it
            if (text[0].StartsWith("//")) continue; // Commented out line - skip it

            if (ResponseMessages.TryAdd(text[0], (text[1], new DateTime()))) Program.ConsoleWarning($">> Added respoonse to \"{text[0]}\" key");
            else Program.ConsoleWarning($">> Redefiniton of \"{text[0]}\" key, in line {(i + 1)}."); // TryAdd returned false - probably a duplicate
        }
    }
}
