using System.ComponentModel;
using System.Net.Sockets;
using System.Text;

public class Chat
{
    static bool botStarted;

    public static void Start()
    {
        if (botStarted) return;
        botStarted = true;

        new Thread(() =>
        {
            Socket socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
            byte[] receiveBuffer = new byte[10000]; // Max IRC message is 4096 bytes?
            int zeroBytesReceivedCounter = 0, currentIndex, nextIndex, bytesReceived;
            string userBadge, userName, customRewardID;
            ManualResetEvent receiveEvent = new ManualResetEvent(false);
            // Background worker for async handling received messages
            BackgroundWorker receiveWorker = new BackgroundWorker() { WorkerSupportsCancellation = true };
            receiveWorker.DoWork += (sender, e) =>
            {
                while (socket.Connected && (receiveWorker.CancellationPending == false))
                {
                    receiveEvent.Reset();

                    socket.BeginReceive(receiveBuffer, 0, receiveBuffer.Length, SocketFlags.None, new AsyncCallback((IAsyncResult ar) =>
                    {
                        try
                        {
                            bytesReceived = socket.EndReceive(ar);
                            if (bytesReceived > 0)
                            {
                                string[] messages = Encoding.Default.GetString(receiveBuffer, 0, bytesReceived).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
                                foreach (string message in messages)
                                {
                                    // Standard message without extra tags
                                    if (message.StartsWith(':') && message.Contains("PRIVMSG"))
                                    {
                                        Console.WriteLine(
                                            String.Format("{0, 20}{1, 2}{2, -0}", message.Substring(1, message.IndexOf('!') - 1) // Username
                                            , ": ",
                                            message.Substring(message.IndexOf(':', 1) + 1)) // message
                                        );
                                    }
                                    // Standard message with extra tags
                                    else if (message.StartsWith("@") && message.Contains("PRIVMSG"))
                                    {
                                        // Console.WriteLine("------");
                                        // Console.WriteLine(message);
                                        // Console.WriteLine("------");

                                        // Check if message was from custom reward
                                        currentIndex = message.IndexOf("custom-reward-id=");
                                        if (currentIndex > 0)
                                        {
                                            currentIndex += 17;
                                            customRewardID = message.Substring(currentIndex, message.IndexOf(';', currentIndex) - currentIndex);

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
                                        currentIndex = message.IndexOf("bits=");
                                        if (currentIndex > 0)
                                        {
                                            currentIndex += 5;
                                            Console.WriteLine($"> Cheered with {message.Substring(currentIndex, message.IndexOf(';', currentIndex) - currentIndex)} bits");
                                        }

                                        currentIndex = message.IndexOf("badges=") + 7;
                                        userBadge = message.Substring(currentIndex, (nextIndex = message.IndexOf(';', currentIndex - 1)) - currentIndex);
                                        if (userBadge.Contains("broadcaster")) userBadge = "GOD";
                                        else if (userBadge.Contains("moderator")) userBadge = "MOD";
                                        else if (userBadge.Contains("subscriber")) userBadge = "SUB";
                                        else if (userBadge.Contains("vip")) userBadge = "VIP";
                                        else userBadge = string.Empty;
                                        currentIndex = nextIndex;

                                        userName = message.Substring(currentIndex = (message.IndexOf("display-name=") + 13), (nextIndex = message.IndexOf(';', currentIndex)) - currentIndex);
                                        currentIndex = nextIndex;

                                        Console.WriteLine(String.Format("{0, -4}{1, 20}{2, 2}{3, -0}",
                                                            userBadge,
                                                            userName,
                                                            ": ",
                                                            message.Substring(message.IndexOf($"PRIVMSG #{Program.ChannelName} :", currentIndex) + 11 + Program.ChannelName.Length))
                                        );
                                    }
                                    // Notification - sub / announcement
                                    else if (message.StartsWith("@") && message.Contains("USERNOTICE"))
                                    {
                                        currentIndex = message.IndexOf("display-name=") + 13;
                                        userName = message.Substring(currentIndex, (nextIndex = (message.IndexOf(';', currentIndex))) - currentIndex);
                                        currentIndex = nextIndex;
                                        currentIndex = message.IndexOf("msg-id=", currentIndex) + 7;
                                        switch (message.Substring(currentIndex, (message.IndexOf(';', currentIndex)) - currentIndex))
                                        {
                                            case "sub":
                                                Console.WriteLine("> User " + userName +
                                                                    (message.Contains("msg-param-was-gifted=true") ? " got gifted sub for " : " subscribed for ") +
                                                                    message.Substring(currentIndex = (message.IndexOf("msg-param-cumulative-months=", currentIndex) + 28), (message.IndexOf(';', currentIndex)) - currentIndex) +
                                                                    " months."
                                                );
                                                break;
                                            case "announcement":
                                                currentIndex = (message.IndexOf("USERNOTICE", currentIndex) + 10);
                                                currentIndex = (message.IndexOf(" :", currentIndex) + 2);
                                                Console.WriteLine("> User " + userName + " announced that: " + message.Substring(currentIndex));
                                                break;
                                            default:
                                                Console.WriteLine(message);
                                                break;
                                        }
                                    }
                                    // Ban
                                    else if (message.StartsWith("@ban-duration="))
                                    {
                                        currentIndex = message.IndexOf("CLEARCHAT") + 10;
                                        currentIndex = message.IndexOf(':', currentIndex) + 1;
                                        userName = message.Substring(currentIndex);
                                        Console.WriteLine($"> User {userName} got banned for {message.Substring(14, message.IndexOf(';') - 14)} min.");
                                    }
                                    // Timeout?
                                    else if (message.StartsWith("@") && message.Contains("CLEARMSG"))
                                    {
                                        userName = message.Substring(7, message.IndexOf(';') - 7);
                                        Console.WriteLine($"> User {userName} got timed out.");
                                    }
                                    // Ping request, let's play PING - PONG with the server :D
                                    else if (message.Split(" ")[0] == "PING")
                                    {
                                        string response = message.Replace("PING", "PONG");
                                        // Console.WriteLine(message);
                                        // Program.ConsoleWarning("> " + response + ", " + DateTime.Now.ToString());
                                        socket.Send(Encoding.UTF8.GetBytes(response + "\r\n"));
                                    }
                                    // Other message type
                                    else
                                    {
                                        Console.WriteLine(message);
                                    }
                                }
                                zeroBytesReceivedCounter = 0;
                            }
                            else
                            {
                                Program.ConsoleWarning("> Received 0 bytes");
                                zeroBytesReceivedCounter++;
                                if (zeroBytesReceivedCounter >= 5) socket.Close(); // Close connection if 5 times in a row received 0 bytes
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
                Program.ConsoleWarning("> Connecting...");
                socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
                socket.Connect("irc.chat.twitch.tv", 6667);
                Program.ConsoleWarning("> Connected");
                receiveWorker.RunWorkerAsync();
                socket.Send(Encoding.UTF8.GetBytes("PASS oauth:pemxb4hgv68mmloe7216qrjpgjrbic\r\n"));
                socket.Send(Encoding.UTF8.GetBytes("NICK abevbot\r\n"));
                socket.Send(Encoding.UTF8.GetBytes($"JOIN #{Program.ChannelName},#{Program.ChannelName}\r\n"));
                socket.Send(Encoding.UTF8.GetBytes("CAP REQ :twitch.tv/commands twitch.tv/tags\r\n")); // request extended chat messages

                while (socket.Connected) Thread.Sleep(500); // While connection is active do nothing

                // Connection lost
                Program.ConsoleWarning("> Connection lost, waiting 2s to reconnect");
                receiveWorker.CancelAsync();
                Thread.Sleep(2000);
            }
        })
        { Name = "Chat thread", IsBackground = true }.Start();
    }
}
