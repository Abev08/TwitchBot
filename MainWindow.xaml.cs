using System;
using System.IO;
using System.Runtime.InteropServices;
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
    public static bool ConsoleFreed { get; private set; }

    private static MainWindow WindowRef;
    private static TextBlock TextOutputRef;
    private static TextBlock NotificationsQueueCountRef;
    private static MediaElement VideoPlayerRef;
    public static bool VideoEnded { get; private set; }

    public MainWindow(string[] args = null)
    {
      InitializeComponent();
      WindowRef = this;
      TextOutputRef = tbTextOutput;
      NotificationsQueueCountRef = tbNotificationsQueue;
      VideoPlayerRef = VideoPlayer;

      AllocConsole();
      // Free console window on program close
      this.Closing += (sender, e) =>
      {
        ConsoleFreed = true;
        FreeConsole();
      };

      // Catch all unhandled exceptions and print them into a file
      AppDomain.CurrentDomain.UnhandledException += (sender, ex) =>
      {
        using (StreamWriter writer = new("error.txt"))
        {
          writer.WriteLine(DateTime.Now);
          writer.WriteLine(ex.ExceptionObject);
        }
      };

      ConsoleWarning(">> Hi. I'm AbevBot.");

      // Read Config.ini
      if (Config.ParseConfigFile())
      {
        ConsoleFreed = true;
        FreeConsole();
        return;
      }

      if (args.Length > 0 && args[0] == "--consoleVisible") { } // Force console visibility in vscode with command line args
      else if (!Config.ConsoleVisible)
      {
        ConsoleFreed = true;
        FreeConsole();
      }

      Chat.Start(); // Start chat bot
      Events.Start(); // Start events bot
      Notifications.Start(); // Start notifications on MainWindow

      // For testing purposes some bot functions are assigned to buttons.
      // In real bot appliaction these things should be fired from received events in Event class or chat commands from Chat class or even keyboard buttons bindings
      btnTestTTS.Click += (sender, e) =>
      {
        string text = tbTTSText.Text;
        string voice = tbTTSVoice.Text;
        if (string.IsNullOrEmpty(text)) return;
        Notifications.AddNotification(new Notification()
        {
          TextToDisplay = text,
          TextToRead = text,
          TTSVoice = voice
        });
      };
      btnPause.Click += (sender, e) =>
      {
        Notifications.NotificationsPaused ^= true;
        if (Notifications.NotificationsPaused) ((Button)sender).Background = Brushes.Red;
        else ((Button)sender).Background = btnSkip.Background;
      };
      btnSkip.Click += (sender, e) => Notifications.SkipNotification = true;

      // Automatically set source to null after video ended
      VideoPlayerRef.MediaEnded += (sender, e) =>
      {
        VideoPlayerRef.Source = null;
        VideoEnded = true;
      };
      // Take control over video player
      VideoPlayerRef.LoadedBehavior = System.Windows.Controls.MediaState.Manual;
      VideoPlayerRef.UnloadedBehavior = System.Windows.Controls.MediaState.Manual;

      // Wait for window to be loaded (visible) to start a demo video
      this.Loaded += (sender, e) =>
      {
        VideoPlayerRef.Source = new Uri(new FileInfo("Resources/peepoHey.mp4").FullName);
        VideoPlayerRef.Play();
      };

      btnTestVideo.Click += (sender, e) =>
      {
        VideoPlayerRef.Source = new Uri(new FileInfo("Resources/peepoHey.mp4").FullName);
        VideoPlayerRef.Play();
      };
    }

    public static void ConsoleWarning(string text, ConsoleColor color = ConsoleColor.DarkRed)
    {
      if (ConsoleFreed) return;

      Console.ForegroundColor = color;
      Console.WriteLine(text);
      Console.ResetColor();
    }

    public static void ConsoleWriteLine(string text)
    {
      if (ConsoleFreed) return;

      Console.WriteLine(text);
    }

    public static void SetTextDisplayed(string text)
    {
      WindowRef.Dispatcher.Invoke(new Action(() =>
      {
        TextOutputRef.Text = text;
      }));
    }

    public static void StartVideoPlayer(string path, float volume)
    {
      WindowRef.Dispatcher.Invoke(new Action(() =>
      {
        if (VideoPlayerRef.Source != null) return;
        VideoEnded = false;
        VideoPlayerRef.Source = new Uri(new FileInfo(path).FullName);
        VideoPlayerRef.Volume = volume;
        VideoPlayerRef.Play();
      }));
    }

    public static void StopVideoPlayer()
    {
      WindowRef.Dispatcher.Invoke(new Action(() =>
      {
        VideoPlayerRef.Stop();
        VideoPlayerRef.Source = null;
        VideoEnded = true;
      }));
    }

    public static void SetNotificationQueueCount(int count)
    {
      WindowRef.Dispatcher.Invoke(new Action(() =>
      {
        NotificationsQueueCountRef.Text = $"Notifications in queue: {count}";
      }));
    }

    private void ChkTTS_CheckChanged(object sender, RoutedEventArgs e)
    {
      Notifications.ChatTTSEnabled = ((CheckBox)sender).IsChecked == true;
    }
  }
}
