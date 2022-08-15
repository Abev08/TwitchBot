using System;
using System.IO;
using System.Linq;
using System.Text;
using TwitchBotWPF;

public class Config
{
    public static string ChannelName { get; private set; }
    public static string BotName { get; private set; }
    public static string BotPass { get; private set; }
    public static string BotClientID { get; private set; }
    public static string BotSecret { get; private set; }
    public static string BotUserAccessToken { get; set; } // Twitch User Access Token
    public static string BotAccessToken { get; set; } // Twitch App Access Token
    public static string BotRefreshToken { get; set; }
    public static string BroadcasterID { get; set; }
    public static string NgrokAuthToken { get; set; }
    public static string NgrokTunnelAddress { get; set; }
    public static bool FollowsNotifications { get; set; }
    public static bool RedemptionsAndBitsNotifications { get; set; }

    public static bool ParseConfigFile()
    {
        MainWindow.ConsoleWarning(">> Reading Config.ini file.");

        FileInfo configFile = new FileInfo(@"./Config.ini");
        if (configFile.Exists == false)
        {
            // The file doesn't exist - create empty one
            using (var stream = configFile.Create())
            {
                stream.Write(Encoding.UTF8.GetBytes("ChannelName = " + Environment.NewLine));
                stream.Write(Encoding.UTF8.GetBytes("NICK = " + Environment.NewLine));
                stream.Write(Encoding.UTF8.GetBytes("PASS = " + Environment.NewLine));
                stream.Write(Encoding.UTF8.GetBytes("ClientID = " + Environment.NewLine));
                stream.Write(Encoding.UTF8.GetBytes("Secret = " + Environment.NewLine));
                stream.Write(Encoding.UTF8.GetBytes("ngrokAuthToken = " + Environment.NewLine));
                stream.Write(Encoding.UTF8.GetBytes("FollowsNotifications = false" + Environment.NewLine));
                stream.Write(Encoding.UTF8.GetBytes("RedemptionsAndBitsNotifications = false" + Environment.NewLine));
                stream.Flush();
            }
            // Notify the user and close bot
            MainWindow.ConsoleWarning("Missing required info in Config.ini file." + Environment.NewLine + "The file was generated." + Environment.NewLine + "Please fill it up.");
            Console.ReadLine();
            return true;
        }
        else
        {
            bool[] readedRequiredData = new bool[8]; // { NICK, PASS, ChannelName, etc. }

            using (var reader = configFile.OpenText())
            {
                string[] lines = reader.ReadToEnd().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
                foreach (string line in lines)
                {
                    string[] text = line.Split('=');
                    if (text.Length < 2) continue;
                    for (int i = 0; i < text.Length; i++) text[i] = text[i].Trim(); // Trim white spaces
                    if (string.IsNullOrEmpty(text[1])) continue; // Skip if nothing was assigned
                    switch (text[0])
                    {
                        case "NICK":
                            readedRequiredData[0] = true;
                            BotName = text[1].ToLower();
                            break;
                        case "PASS":
                            if (text[1].ToLower().Contains("oauth:") == false) break;
                            readedRequiredData[1] = true;
                            BotPass = text[1].ToLower();
                            break;
                        case "ChannelName":
                            readedRequiredData[2] = true;
                            ChannelName = text[1].ToLower();
                            break;
                        case "ClientID":
                            readedRequiredData[3] = true;
                            BotClientID = text[1].ToLower();
                            break;
                        case "Secret":
                            readedRequiredData[4] = true;
                            BotSecret = text[1].ToLower();
                            break;
                        case "ngrokAuthToken":
                            readedRequiredData[5] = true;
                            NgrokAuthToken = text[1];
                            break;
                        case "FollowsNotifications":
                            readedRequiredData[6] = true;
                            FollowsNotifications = bool.Parse(text[1]);
                            break;
                        case "RedemptionsAndBitsNotifications":
                            readedRequiredData[7] = true;
                            RedemptionsAndBitsNotifications = bool.Parse(text[1]);
                            break;
                    }
                }
            }

            // Check if all needed data was read
            if (readedRequiredData.Any(x => x == false))
            {
                // Something is missing, notify the user and close the bot
                Console.ForegroundColor = ConsoleColor.DarkRed;
                Console.WriteLine("Missing required info in Config.ini file.");
                if (readedRequiredData[2] == false) Console.WriteLine("Missing channel name. Correct syntax is \"ChannelName = SomeChannelName\".");
                if (readedRequiredData[0] == false) Console.WriteLine("Missing bot's NICK. Correct syntax is \"NICK = TheBot\".");
                if (readedRequiredData[1] == false) Console.WriteLine("Missing bot's PASS. Correct syntax is \"PASS = oauth:1234\"." + Environment.NewLine + "To generate oauth token visit: https://twitchapps.com/tmi");
                if (readedRequiredData[3] == false) Console.WriteLine("Missing bot's Client ID. Correct syntax is \"ClientID = 1234\"." + Environment.NewLine + "To generate bot's Client ID visit: https://dev.twitch.tv/console");
                if (readedRequiredData[4] == false) Console.WriteLine("Missing bot's Secret. Correct syntax is \"Secret = 1234\"." + Environment.NewLine + "To generate bot's Secret visit: https://dev.twitch.tv/console");
                if (readedRequiredData[5] == false) Console.WriteLine("Missing ngrok Authtoken. Correct syntax is \"ngrokAuthToken = 1234\"." + Environment.NewLine + "Create free account at https://ngrok.com to get ngrok Authtoken");
                if (readedRequiredData[6] == false) Console.WriteLine("Missing specification if bot should get follows notifications. Correct syntax is \"FollowsNotifications = false\". Or replace \"false\" with \"true\" to allow it.");
                if (readedRequiredData[7] == false) Console.WriteLine("Missing specification if bot should get bits and redemptions notifications. Correct syntax is \"RedemptionsAndBitsNotifications = false\". Or replace \"false\" with \"true\" to allow it.");
                Console.WriteLine("You can delete Config.ini file to generate new one. ! WARNING - ALL DATA INSIDE IT WILL BE LOST !");
                Console.ResetColor();
                Console.ReadLine();
                return true;
            }
        }

        return false;
    }
}
