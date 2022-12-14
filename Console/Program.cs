using SFML.Audio;
using SFML.Graphics;
using SFML.System;
using SFML.Window;

internal class Program
{
    static Vector2u MinWindowSize = new Vector2u(1280, 720);
    static Font TextFont = new Font(GetResource("SegoeUI.ttf"));

    private static void Main(string[] args)
    {
        Program.ConsoleWarning(">> Hi. I'm AbevBot.");

        // Read Config.ini
        if (Config.ParseConfigFile()) return;

        Events.Start(); // Start events bot
        Chat.Start(); // Start chat bot

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

    public static void ConsoleWarning(string text, ConsoleColor color = ConsoleColor.DarkRed)
    {
        Console.ForegroundColor = color;
        Console.WriteLine(text);
        Console.ResetColor();
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
}
