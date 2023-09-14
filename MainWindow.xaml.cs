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

    private static MainWindow WindowRef;
    private static TextBlock TextOutputRef;
    private static TextBlock NotificationsQueueCountRef;
    private static MediaElement VideoPlayerRef;
    public static bool VideoEnded { get; private set; }

    public MainWindow()
    {
      InitializeComponent();
      WindowRef = this;
      TextOutputRef = tbTextOutput;
      NotificationsQueueCountRef = tbNotificationsQueue;
      VideoPlayerRef = VideoPlayer;

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
      Console.ForegroundColor = color;
      Console.WriteLine(text);
      Console.ResetColor();
    }

    public static void ConsoleWriteLine(string text)
    {
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
