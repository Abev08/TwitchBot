using System.ComponentModel;
using System.Net.Sockets;
using System.Text;

public class Chat
{
    public static string BotName = string.Empty;
    public static string BotPass = string.Empty;
    static bool botStarted;

    public static void Start()
    {
        if (botStarted) return;
        botStarted = true;

        new Thread(() =>
        {
            Socket socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
            byte[] receiveBuffer = new byte[10000]; // Max IRC message is 4096 bytes?
            int zeroBytesReceivedCounter = 0, currentIndex, nextIndex, bytesReceived, messageStartOffset = 0;
            string userBadge, userName, customRewardID;
            List<string> message;
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
                                List<string> messages = Encoding.UTF8.GetString(receiveBuffer, 0, bytesReceived + messageStartOffset).Split("\r\n", StringSplitOptions.RemoveEmptyEntries).ToList();

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
                                    message = messages[i].Split("#" + Program.ChannelName, StringSplitOptions.RemoveEmptyEntries).ToList();

                                    // Check if received message is incomplete (just for last received message)
                                    if ((i == messages.Count - 1) && (receiveBuffer[bytesReceived + messageStartOffset - 1] != (byte)'\n') && (receiveBuffer[bytesReceived + messageStartOffset - 2] != (byte)'\r'))
                                    {
                                        // Move the message to beginning of receiveBuffer
                                        if (messageStartOffset == 0) Array.Clear(receiveBuffer);

                                        string s = string.Join("#" + Program.ChannelName, message);
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
                                        // Console.WriteLine(string.Join("", message));
                                        // Program.ConsoleWarning(">> " + response + ", " + DateTime.Now.ToString());
                                        socket.Send(Encoding.UTF8.GetBytes(response + "\r\n"));
                                        continue;
                                    }

                                    // Probably there was some "#channelName" parts in the message body
                                    // Attach them together
                                    if (message.Count < 2)
                                    {
                                        // Sub without message doesn't have body part of the message :/
                                        if (message[0].Contains("msg-id=sub") || message[0].Contains("msg-id=resub") || message[0].Contains("msg-id=primepaidupgrade"))
                                        {
                                            message.Add(":"); // Add fake message
                                        }
                                        else
                                        {
                                            // Program.ConsoleWarning(">> Something went wrong with the message, skipping it");
                                            Console.WriteLine(string.Join($"#{Program.ChannelName}", message));
                                            continue;
                                        }
                                    }
                                    while (message.Count > 2)
                                    {
                                        message[^2] += $"#{Program.ChannelName}" + message[^1];
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
                                            Console.WriteLine($"> Cheered with {message[0].Substring(currentIndex, message[0].IndexOf(';', currentIndex) - currentIndex)} bits");
                                            continue; // Not sure about continue here, need someone to cheer to test it out :D 
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
                                                Console.WriteLine("> User " + userName +
                                                                    (message[0].Contains("msg-param-was-gifted=true") ? " got gifted sub for " : " subscribed for ") +
                                                                    message[0].Substring(currentIndex = (message[0].IndexOf("msg-param-cumulative-months=", currentIndex) + 28), (message[0].IndexOf(';', currentIndex)) - currentIndex) +
                                                                    " months." +
                                                                    (message[1].Length > 2 ? " Message: " + message[1].Substring(message[1].IndexOf(':') + 1) : "")
                                                );
                                                break;
                                            case "primepaidupgrade":
                                                Console.WriteLine("> User " + userName +
                                                                    " converted prime sub to standard sub." +
                                                                    (message[1].Length > 2 ? " Message: " + message[1].Substring(message[1].IndexOf(':') + 1) : "")
                                                );
                                                break;
                                            case "announcement":
                                                currentIndex = (message[1].IndexOf(":") + 1);
                                                Console.WriteLine("> User " + userName + " announced that: " + message[1].Substring(currentIndex));
                                                break;
                                            default:
                                                Console.WriteLine(string.Join("", message));
                                                break;
                                        }
                                    }
                                    // Ban
                                    else if (message[0].StartsWith("@ban-duration="))
                                    {
                                        userName = message[1].Substring(message[1].IndexOf(':') + 1);
                                        Console.WriteLine($"> User {userName} got banned for {message[0].Substring(14, message[0].IndexOf(';') - 14)} min.");
                                    }
                                    // Timeout?
                                    else if (message[0].StartsWith("@") && message[0].Contains("CLEARMSG"))
                                    {
                                        userName = message[0].Substring(7, message[0].IndexOf(';') - 7);
                                        Console.WriteLine($"> User {userName} got timed out.");
                                    }
                                    // Different timeout?
                                    else if (message[0].StartsWith("@") && message[0].Contains("CLEARCHAT"))
                                    {
                                        userName = message[1].Substring(message[1].IndexOf(":") + 1);
                                        Console.WriteLine($"> User {userName} got timed out.");
                                    }
                                    // Other message type
                                    else
                                    {
                                        Console.ForegroundColor = ConsoleColor.Magenta;
                                        Console.WriteLine(string.Join("", message));
                                        Console.ForegroundColor = Program.ConsoleDefaultColor;
                                    }
                                }
                                zeroBytesReceivedCounter = 0;
                            }
                            else
                            {
                                Program.ConsoleWarning(">> Received 0 bytes");
                                zeroBytesReceivedCounter++;
                                if (zeroBytesReceivedCounter >= 5)
                                {
                                    Program.ConsoleWarning(">> Closing connection");
                                    socket.Close(); // Close connection if 5 times in a row received 0 bytes
                                }
                            }
                        }
                        catch (Exception ex) { Console.WriteLine(ex.Message); }

                        receiveEvent.Set();
                    }), null);

                    receiveEvent.WaitOne();
                    Thread.Sleep(1000);
                }
            };

            while (true)
            {
                // Try to connect
                Program.ConsoleWarning(">> Connecting...");
                socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
                socket.Connect("irc.chat.twitch.tv", 6667);
                Program.ConsoleWarning(">> Connected");
                receiveWorker.RunWorkerAsync();
                socket.Send(Encoding.UTF8.GetBytes($"PASS {BotPass}\r\n"));
                socket.Send(Encoding.UTF8.GetBytes($"NICK {BotName}\r\n"));
                socket.Send(Encoding.UTF8.GetBytes($"JOIN #{Program.ChannelName},#{Program.ChannelName}\r\n"));
                socket.Send(Encoding.UTF8.GetBytes("CAP REQ :twitch.tv/commands twitch.tv/tags\r\n")); // request extended chat messages

                while (socket.Connected) Thread.Sleep(500); // While connection is active do nothing

                // Connection lost
                Program.ConsoleWarning(">> Connection lost, waiting 2s to reconnect");
                receiveWorker.CancelAsync();
                Thread.Sleep(2000);
            }
        })
        { Name = "Chat thread", IsBackground = true }.Start();
    }
}
