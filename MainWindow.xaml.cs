using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AbevBot
{
  public partial class MainWindow : Window
  {
    [DllImport("Kernel32")]
    public static extern void AllocConsole();

    [DllImport("Kernel32")]
    public static extern void FreeConsole();

    static MainWindow window;
    static TextBlock TextOutput; // For some reason it have to be done via temp variable

    public MainWindow()
    {
      InitializeComponent();
      window = this;
      TextOutput = tbTextOutput;

      AllocConsole();
      this.Closing += (sender, e) => FreeConsole(); // Free console window on program close
      ConsoleWarning(">> Hi. I'm AbevBot.");

      // Read Config.ini
      if (Config.ParseConfigFile())
      {
        FreeConsole();
        return;
      }

      Chat.Start(); // Start chat bot
      Events.Start(); // Start events bot
      // Notifications.Start(); // Start notifications on MainWindow

      // For testing purposes some bot functions are assigned to buttons.
      // In real bot appliaction these things should be fired from received events in Event class or chat commands from Chat class or even keyboard buttons bindings
      btnTestTTS.Click += (sender, e) =>
      {
        string text = tbText.Text;
        if (string.IsNullOrEmpty(text)) return;
        // It can freeze the main thread so better use Task to add new notification
        new Task(() =>
              {
                Notifications.AddNotification(new Notification(text, true, true));
              }).Start();
      };
      btnPause.Click += (sender, e) =>
      {
        Notifications.NotificationsPaused ^= true;
        if (Notifications.NotificationsPaused) ((Button)sender).Background = Brushes.Red;
        else ((Button)sender).Background = btnSkip.Background;
      };
      btnSkip.Click += (sender, e) => Notifications.SkipNotification = true;

      // Automatically set source to null after video ended
      VideoPlayer.MediaEnded += (sender, e) => VideoPlayer.Source = null;
      // Take control over video player
      VideoPlayer.LoadedBehavior = System.Windows.Controls.MediaState.Manual;
      VideoPlayer.UnloadedBehavior = System.Windows.Controls.MediaState.Manual;

      // Wait for window to be loaded (visible) to start a demo video
      this.Loaded += (sender, e) =>
      {
        VideoPlayer.Source = new Uri(@"Resources/peepoHey.mp4", UriKind.Relative);
        VideoPlayer.Play();
      };
    }

    public static void ConsoleWarning(string text, ConsoleColor color = ConsoleColor.DarkRed)
    {
      Console.ForegroundColor = color;
      Console.WriteLine(text);
      Console.ResetColor();
    }

    public static void ConsoleWriteLine(string text)
    {
      Console.WriteLine(text.ToString());
    }

    public static void SetTextDisplayed(string text)
    {
      window.Dispatcher.Invoke(new Action(() =>
      {
        TextOutput.Text = text;
      }));
    }
  }
}
