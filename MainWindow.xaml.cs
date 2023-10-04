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

    /// <summary> MainWindow instance - the window. </summary>
    public static MainWindow I { get; private set; }
    public static bool VideoEnded { get; private set; }

    public MainWindow(string[] args = null)
    {
      InitializeComponent();
      I = this;

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
        Config.UpdateVolumesFile();
        ConsoleFreed = true;
        FreeConsole();
      };

      ConsoleWarning(">> Hi. I'm AbevBot.");

      // Read Secrets.ini and Config.ini
      bool error = false;
      error |= Secret.ParseSecretFile();
      error |= Config.ParseConfigFile();
      if (error)
      {
        Console.ReadLine();
        ConsoleFreed = true;
        FreeConsole();
        return;
      }

      AccessTokens.GetAccessTokens();
      AccessTokens.GetBroadcasterID();

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

        VideoPlayer.Source = new Uri(new FileInfo("Resources/peepoHey.mp4").FullName);
        VideoPlayer.Play();
      };

      // Don't allow minimizing the window
      StateChanged += (sender, e) =>
      {
        if (I.WindowState == WindowState.Minimized) I.WindowState = WindowState.Normal;
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

    public void SetTextDisplayed(string text, Notifications.TextPosition position)
    {
      Dispatcher.Invoke(new Action(() =>
      {
        switch (position)
        {
          case Notifications.TextPosition.TOP:
            tbTop.Text = text;
            break;

          case Notifications.TextPosition.MIDDLE:
            tbMid.Text = text;
            break;

          case Notifications.TextPosition.BOTTOM:
            tbBottom.Text = text;
            break;
        }
      }));
    }

    public void ClearTextDisplayed()
    {
      Dispatcher.Invoke(new Action(() =>
      {
        tbTop.Text = string.Empty;
        tbMid.Text = string.Empty;
        tbBottom.Text = string.Empty;
      }));
    }

    public void StartVideoPlayer(string path, float volume)
    {
      Dispatcher.Invoke(new Action(() =>
      {
        if (VideoPlayer.Source != null)
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
        VideoPlayer.Source = new Uri(file.FullName);
        VideoPlayer.Volume = volume;
        VideoPlayer.Play();
      }));
    }

    public void StopVideoPlayer()
    {
      Dispatcher.Invoke(new Action(() =>
      {
        VideoPlayer.Stop();
        VideoPlayer.Source = null;
        VideoEnded = true;
      }));
    }

    public void SetNotificationQueueCount(int count)
    {
      Dispatcher.Invoke(new Action(() =>
      {
        tbNotificationsQueue.Text = $"Notifications in queue: {count}";
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
      VideoPlayer.Source = null;
      VideoEnded = true;
    }

    private void VideoTest(object sender, RoutedEventArgs e)
    {
      VideoPlayer.Source = new Uri(new FileInfo("Resources/peepoHey.mp4").FullName);
      VideoPlayer.Play();
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

        case "Subscription Gifted":
          Notifications.CreateGiftSubscriptionNotification("Chatter", "1", 7, "This is a test");
          break;

        case "Subscription Ext. Msg":
          Notifications.CreateSubscriptionNotification("Chatter", "1", 7, 77, new EventPayloadMessage() { Text = "This is a test" });
          break;

        case "Cheer":
          Notifications.CreateCheerNotification("Chatter", 100, "This is a test");
          break;
      }
    }

    private void VolumeTTSChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
      tbVolumeTTS.Text = $"TTS Volume: {e.NewValue}%";
      Config.VolumeTTS = (float)e.NewValue / 100f;
      Config.VolumeValuesDirty = true;
    }

    private void VolumeSoundsChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
      tbVolumeSounds.Text = $"Sounds Volume: {e.NewValue}%";
      Config.VolumeSounds = (float)e.NewValue / 100f;
      Config.VolumeValuesDirty = true;
    }

    private void VolumeVideosChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
      tbVolumeVideos.Text = $"Videos Volume: {e.NewValue}%";
      Config.VolumeVideos = (float)e.NewValue / 100f;
      Config.VolumeValuesDirty = true;
    }

    private void VolumeChange(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
      ((Slider)sender).Value += e.Delta > 0 ? 1 : -1;
    }

    /// <summary> Sets volume sliders value to values present in Config class. </summary>
    public void SetVolumeSliderValues()
    {
      volumeTTS.Value = MathF.Round(Config.VolumeTTS * 100);
      volumeSounds.Value = MathF.Round(Config.VolumeSounds * 100);
      volumeVideos.Value = MathF.Round(Config.VolumeVideos * 100);
    }








    #region GAMBA TESTING, DO NOT LOOK :D
    public void GambaVideoStart(string videoPath, string userName, string points)
    {
      Dispatcher.Invoke(new Action(() =>
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
            I.Dispatcher.Invoke(new Action(() =>
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
