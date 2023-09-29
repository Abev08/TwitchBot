using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace AbevBot
{
  public partial class MainWindow : Window
  {
    [DllImport("Kernel32")]
    private static extern void AllocConsole();
    [DllImport("Kernel32")]
    private static extern void FreeConsole();
    public static bool ConsoleFreed { get; private set; }

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
    // from winuser.h
    private const int GWL_STYLE = -16;
    private const int WS_MAXIMIZEBOX = 0x10000;
    private const int WS_MINIMIZEBOX = 0x20000;

    private static MainWindow WindowRef;
    private static TextBlock TBMiddleRef, TBTopRef, TBBottomRef;
    private static TextBlock NotificationsQueueCountRef;
    private static MediaElement VideoPlayerRef;
    public static bool VideoEnded { get; private set; }

    public MainWindow(string[] args = null)
    {
      InitializeComponent();
      WindowRef = this;
      TBTopRef = tbTop;
      TBMiddleRef = tbMid;
      TBBottomRef = tbBottom;
      NotificationsQueueCountRef = tbNotificationsQueue;
      VideoPlayerRef = VideoPlayer;

      // Catch all unhandled exceptions and print them into a file
      AppDomain.CurrentDomain.UnhandledException += (sender, ex) =>
      {
        using StreamWriter writer = new("error.txt");
        writer.WriteLine(DateTime.Now);
        writer.WriteLine(ex.ExceptionObject);
      };

      AllocConsole();
      // Free console window on program close
      Closing += (sender, e) =>
      {
        Chatter.UpdateChattersFile();
        ConsoleFreed = true;
        FreeConsole();
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

      // Wait for window to be loaded (visible) to start a demo video
      Loaded += (sender, e) =>
      {
        // Try to switch to software renderer - it fixes MediaElement (video player) playing on other monitors
        try
        {
          var hwndSource = PresentationSource.FromVisual(this) as HwndSource;
          var hwndTarget = hwndSource.CompositionTarget;
          hwndTarget.RenderMode = RenderMode.SoftwareOnly;

          // Hide minimize button
          var currentStyle = GetWindowLong(hwndSource.Handle, GWL_STYLE);
          SetWindowLong(hwndSource.Handle, GWL_STYLE, currentStyle & ~WS_MINIMIZEBOX);
        }
        catch (Exception ex) { ConsoleWarning($">> {ex.Message}"); }

        VideoPlayerRef.Source = new Uri(new FileInfo("Resources/peepoHey.mp4").FullName);
        VideoPlayerRef.Play();
      };

      // Don't allow minimizing the window
      StateChanged += (sender, e) =>
      {
        if (WindowRef.WindowState == WindowState.Minimized) WindowRef.WindowState = WindowState.Normal;
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

    private void PauseNotifications(object sender, RoutedEventArgs e)
    {
      Notifications.NotificationsPaused ^= true;
      if (Notifications.NotificationsPaused) ((Button)sender).Background = Brushes.Red;
      else ((Button)sender).Background = btnSkip.Background;
    }

    private void SkipNotification(object sender, RoutedEventArgs e)
    {
      Notifications.SkipNotification = true;
    }

    public static void SetTextDisplayed(string text, Notifications.TextPosition position)
    {
      WindowRef.Dispatcher.Invoke(new Action(() =>
      {
        switch (position)
        {
          case Notifications.TextPosition.TOP:
            TBTopRef.Text = text;
            break;

          case Notifications.TextPosition.MIDDLE:
            TBMiddleRef.Text = text;
            break;

          case Notifications.TextPosition.BOTTOM:
            TBBottomRef.Text = text;
            break;
        }
      }));
    }

    public static void ClearTextDisplayed()
    {
      WindowRef.Dispatcher.Invoke(new Action(() =>
      {
        TBTopRef.Text = string.Empty;
        TBMiddleRef.Text = string.Empty;
        TBBottomRef.Text = string.Empty;
      }));
    }

    public static void StartVideoPlayer(string path, float volume)
    {
      WindowRef.Dispatcher.Invoke(new Action(() =>
      {
        if (VideoPlayerRef.Source != null)
        {
          VideoEnded = true;
          return;
        }

        FileInfo file = new(path);
        if (!file.Exists)
        {
          VideoEnded = true;
          return;
        }

        VideoEnded = false;
        VideoPlayerRef.Source = new Uri(file.FullName);
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

    private void ChkGamba_CheckChanged(object sender, RoutedEventArgs e)
    {
      MinigameGamba.Enabled = ((CheckBox)sender).IsChecked == true;
    }

    private void ChkGambaLife_CheckChanged(object sender, RoutedEventArgs e)
    {
      MinigameGamba.GambaLifeEnabled = ((CheckBox)sender).IsChecked == true;
    }

    private void ChkFight_CheckChanged(object sender, RoutedEventArgs e)
    {
      MinigameFight.Enabled = ((CheckBox)sender).IsChecked == true;
    }

    private void MainVideoEnded(object sender, RoutedEventArgs e)
    {
      VideoPlayerRef.Source = null;
      VideoEnded = true;
    }

    private void VideoTest(object sender, RoutedEventArgs e)
    {
      VideoPlayerRef.Source = new Uri(new FileInfo("Resources/peepoHey.mp4").FullName);
      VideoPlayerRef.Play();
    }

    private void TTSTest(object sender, RoutedEventArgs e)
    {
      string text = tbTTSText.Text.Trim();
      if (string.IsNullOrEmpty(text)) return;
      Notifications.CreateTTSNotification(text);
    }

    private void NotificationTest(object sender, RoutedEventArgs e)
    {
      switch (cbNotificationType.Text)
      {
        case "Follow":
          Notifications.CreateFollowNotification("Chatter");
          break;

        case "Subscription":
          Notifications.CreateSubscriptionNotification("Chatter", "1", "This is a test");
          break;

        case "Gifted Subscription":
          Notifications.CreateGiftSubscriptionNotification("Chatter", "1", 7, "This is a test");
          break;

        case "Cheer":
          Notifications.CreateCheerNotification("Chatter", 100, "This is a test");
          break;
      }
    }








    #region GAMBA TESTING, DO NOT LOOK :D
    public void GambaVideoStart(string videoPath, string userName, string points)
    {
      WindowRef.Dispatcher.Invoke(new Action(() =>
      {
        tbGambaName.Text = userName;
        FileInfo file = new(videoPath);
        if (file.Exists)
        {
          videoGamba.Source = new Uri(file.FullName);
          videoGamba.Play();
        }

        tbGambaPoints.Margin = new Thickness(0, -210, 0, 0);
        tbGambaPoints.Text = points;

        // Animate points
        Task.Run(() =>
        {
          for (int i = 0; i <= 60; i++)
          {
            WindowRef.Dispatcher.Invoke(new Action(() =>
            {
              tbGambaPoints.Margin = new Thickness(0, tbGambaPoints.Margin.Top + 1, 0, 0);
              if (i == 60)
              {
                tbGambaPoints.Text = string.Empty;
                if (!videoGamba.HasVideo) tbGambaName.Text = string.Empty; // If the video doesn't play "finish" the animation
              }
            }));
            Task.Delay(10).Wait();
          }
        });
      }));
    }

    private void GambaVideoEnded(object sender, RoutedEventArgs e)
    {
      ((MediaElement)sender).Source = null;
      tbGambaName.Text = string.Empty;
    }

    private void GambaTest(object sender, RoutedEventArgs e)
    {
      GambaVideoStart("Resources/Gamba/GambaLoose1.mp4", "chatter", "1000");
    }
    #endregion
  }
}
