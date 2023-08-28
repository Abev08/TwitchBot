using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TwitchBotWPF;

public class Config
{
    public enum Keys
    {
        ChannelName,
        BotNick, BotClientID, BotPass,
        BotOAuthToken, // I would like to generate this token programmatically but I don't know how :/
        NgrokAuthtoken,
        FollowsNotifications, BitsNotifications, RedemptionsNotifications
    };
    public static Dictionary<Keys, DataType> Data = Enum.GetValues(typeof(Keys)).Cast<Keys>().ToDictionary(k => k, k => new DataType()
    {
        BoolValue = k == Keys.FollowsNotifications || k == Keys.BitsNotifications || k == Keys.RedemptionsNotifications
    });

    public static bool RequiredUserToken { get; private set; } = false;
    public static string BotAppAccessToken { get; set; } = string.Empty;
    public static string BotUserAccessToken { get; set; } = string.Empty;
    public static string BotUserRefreshToken { get; set; } = string.Empty;
    public static string BroadcasterID { get; set; } = string.Empty;
    public static string NgrokTunnelAddress { get; set; } = string.Empty;

    public static bool ParseConfigFile()
    {
        MainWindow.ConsoleWarning(">> Reading Config.ini file.");

        FileInfo configFile = new FileInfo(@"./Config.ini");
        if (configFile.Exists == false)
        {
            // The file doesn't exist - create empty one
            using (var stream = configFile.Create())
            {
                foreach (var data in Data)
                {
                    if (data.Value.BoolValue) stream.Write(Encoding.UTF8.GetBytes(data.Key.ToString() + " = false" + Environment.NewLine));
                    else stream.Write(Encoding.UTF8.GetBytes(data.Key.ToString() + " = " + Environment.NewLine));
                }
                stream.Flush();
            }
            // Notify the user and close bot
            MainWindow.ConsoleWarning("Missing required info in Config.ini file." + Environment.NewLine +
                                    "The file was generated." + Environment.NewLine + "Please fill it up and restart the bot.");
            Console.ReadLine();
            return true;
        }
        else
        {
            using (var reader = configFile.OpenText())
            {
                string[] lines = reader.ReadToEnd().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
                Keys key;
                foreach (string line in lines)
                {
                    string[] text = line.Split('=');
                    if (text.Length < 2) continue;
                    for (int i = 0; i < text.Length; i++) text[i] = text[i].Trim(); // Trim white spaces
                    if (string.IsNullOrEmpty(text[1])) continue; // Skip if nothing was assigned
                    try
                    {
                        key = (Keys)Enum.Parse(typeof(Keys), text[0]);
                        switch (key)
                        {
                            case Keys.NgrokAuthtoken:
                                Data[key].Value = text[1];
                                break;
                            case Keys.FollowsNotifications:
                            case Keys.BitsNotifications:
                            case Keys.RedemptionsNotifications:
                                Data[key].BoolValue = bool.Parse(text[1].ToLower());
                                break;
                            default:
                                Data[key].Value = text[1].ToLower();
                                break;
                        }
                        Data[key].Readed = true;
                    }
                    catch (Exception ex) { MainWindow.ConsoleWarning(ex.Message); }
                }
            }

            // Check if all needed data was read
            bool configDataMissing = false;
            foreach (var data in Data)
            {
                if (data.Value.Readed == false)
                {
                    configDataMissing = true;
                    MainWindow.ConsoleWarning($"Missing {data.Key.ToString()} key.");
                }
            }

            // Check if something is missing
            if (configDataMissing)
            {
                MainWindow.ConsoleWarning("Missing required info in Config.ini file." + Environment.NewLine +
                                "Look inside \"Required information in Config.ini\" section in README for help." + Environment.NewLine +
                                "You can delete Config.ini file to generate new one. ! WARNING - ALL DATA INSIDE IT WILL BE LOST !");
                Console.ReadLine();
                return true;
            }

            RequiredUserToken = Data[Keys.BitsNotifications].BoolValue || Data[Keys.RedemptionsNotifications].BoolValue; // Check if user token will be required
        }

        return false;
    }
}

public class DataType
{
    /// <summary> Data was readed from Config.ini </summary>
    public bool Readed;
    /// <summary> Value readed from Config.ini </summary>
    public string Value = string.Empty;
    /// <summary> Value readed from Config.ini parsed to bool </summary>
    public bool BoolValue;
}
