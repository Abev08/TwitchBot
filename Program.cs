using System.ComponentModel;
using System.Net.Sockets;
using System.Text;
using SFML.Audio;
using SFML.Graphics;
using SFML.System;
using SFML.Window;

internal class Program
{
    public static string ChannelName = string.Empty;
    static Vector2u MinWindowSize = new Vector2u(1280, 720);
    static Font TextFont = new Font(GetResource("SegoeUI.ttf"));
    public static ConsoleColor ConsoleDefaultColor;

    private static void Main(string[] args)
    {
        // Read Config.ini
        if (ParseConfigFile()) return;

        ConsoleDefaultColor = Console.ForegroundColor;

        Chat.Start(); // Start chatbot
        while (true) Thread.Sleep(100); // Disable window for now

        Text message = new Text("Test string", TextFont, 42);
        message.Position = new Vector2f(10, 10);
        message.FillColor = Color.White;
        message.OutlineColor = Color.Black;
        message.OutlineThickness = 1f;

        RectangleShape rect = new RectangleShape(new Vector2f(200, 200));
        rect.Position = new Vector2f(200, 200);
        rect.FillColor = Color.Magenta;
        rect.OutlineThickness = 0;

        // Okno do renderowania rzeczy
        RenderWindow window = new RenderWindow(new VideoMode(MinWindowSize.X, MinWindowSize.Y), "Twitch Bot");
        window.SetFramerateLimit(60);
        window.Resized += (sender, e) =>
        {
            if (e.Width < MinWindowSize.X) window.Size = new Vector2u(MinWindowSize.X, window.Size.Y);
            if (e.Height < MinWindowSize.Y) window.Size = new Vector2u(window.Size.X, MinWindowSize.Y);
            window.SetView(new View(new FloatRect(0, 0, window.Size.X, window.Size.Y)));
        };
        window.Closed += (sender, e) => window.Close();

        while (window.IsOpen)
        {
            window.DispatchEvents();

            window.Clear(Color.Green);

            window.Draw(rect);
            window.Draw(message);

            window.Display();
        }
    }

    public static void ConsoleWarning(string text)
    {
        Console.ForegroundColor = ConsoleColor.DarkRed;
        Console.WriteLine(text);
        Console.ForegroundColor = ConsoleDefaultColor;
    }

    static byte[] GetResource(string name)
    {
        System.Reflection.Assembly assembly = System.Reflection.Assembly.GetExecutingAssembly();
        string? namespaceName = assembly.GetName().Name; // Get Assembly namespace

        using (var stream = assembly.GetManifestResourceStream(namespaceName + ".Resources." + name))
        {
            if (stream == null) throw new Exception(Environment.NewLine + "Failed to load." + Environment.NewLine + "Resource name - " + name + " - not found.");

            byte[] data = new byte[stream.Length];
            stream.Read(data);
            return data;
        }
    }

    static bool ParseConfigFile()
    {
        FileInfo configFile = new FileInfo(@"./Config.ini");
        if (configFile.Exists == false)
        {
            // The file doesn't exist - create empty one
            using (var stream = configFile.Create())
            {
                stream.Write(Encoding.UTF8.GetBytes("NICK = \r\n"));
                stream.Write(Encoding.UTF8.GetBytes("PASS = \r\n"));
                stream.Write(Encoding.UTF8.GetBytes("Channel name = "));
                stream.Flush();
            }
            // Notify the user and close bot
            Console.WriteLine("Missing required info in Config.ini file.");
            Console.WriteLine("Please fill it up.");
            Console.ReadLine();
            return true;
        }
        else
        {
            using (var reader = configFile.OpenText())
            {
                string[] lines = reader.ReadToEnd().Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
                bool[] readedRequiredData = new bool[3]; // { NICK, PASS, ChannelName }
                foreach (string line in lines)
                {
                    string[] text = line.Split('=');
                    if (text.Length < 2) continue;
                    for (int i = 0; i < text.Length; i++) text[i] = text[i].Trim(); // Trim white spaces
                    if (string.IsNullOrEmpty(text[1])) continue;
                    switch (text[0])
                    {
                        case "NICK":
                            if (readedRequiredData[0] == false)
                            {
                                readedRequiredData[0] = true;
                                Chat.BotName = text[1].ToLower();
                            }
                            break;
                        case "PASS":
                            if (readedRequiredData[1] == false)
                            {
                                if (text[1].ToLower().Contains("oauth:") == false) break;
                                readedRequiredData[1] = true;
                                Chat.BotPass = text[1].ToLower();
                            }
                            break;
                        case "Channel name":
                            if (readedRequiredData[2] == false)
                            {
                                readedRequiredData[2] = true;
                                ChannelName = text[1].ToLower();
                            }
                            break;
                    }
                }
                // Check if all needed data was read
                if (readedRequiredData.All(x => x == true) == false)
                {
                    // Something is missing, notify the user and close bot
                    Console.WriteLine("Missing required info in Config.ini file.");
                    if (readedRequiredData[0] == false) Console.WriteLine("Missing bot NICK. Correct syntax is \"NICK = TheBot\".");
                    if (readedRequiredData[1] == false) Console.WriteLine("Missing bot PASS. Correct syntax is \"PASS = oauth:1234\".\r\nTo generate oauth token visit: https://twitchapps.com/tmi");
                    if (readedRequiredData[2] == false) Console.WriteLine("Missing channel name. Correct syntax is \"Channel name = SomeChannelName\".");
                    Console.ReadLine();
                    return true;
                }
            }
        }

        return false;
    }
}
