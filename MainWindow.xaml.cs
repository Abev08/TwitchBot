using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
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
    private static bool FinishedLoading;
    /// <summary> Periodic timer calling for example refresh access token method. </summary>
    private static Timer RefreshTimer;

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
        Chatter.UpdateChattersFile(true);
        Config.UpdateVolumes();
        ConsoleFreed = true;
        FreeConsole();
      };

      ConsoleWarning(">> Hi. I'm AbevBot.");

      // Read Secrets.ini and Config.ini
      bool error = false;
      error |= Database.Init();
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

        if (Config.StartVideoEnabled)
        {
          Notifications.AddNotification(new Notification()
          {
            // VideoPath = "Resources/peepoHey.mp4",
            // VideoVolume = Config.VolumeVideos,
            VideoPath = "Resources/bot.mp4",
            VideoVolume = 0.05f,
          });
        }
      };

      // Don't allow minimizing the window
      StateChanged += (sender, e) =>
      {
        if (I.WindowState == WindowState.Minimized) I.WindowState = WindowState.Normal;
      };

      // Start refresh timer, every 10 sec. check if access token should be refreshed, also do some periodic things
      RefreshTimer = new((e) =>
      {
        AccessTokens.RefreshAccessToken();
        AccessTokens.RefreshSpotifyAccessToken();

        Chatter.UpdateChattersFile();

        Config.UpdateVolumes();

        if (Config.IsConfigFileUpdated()) { Config.ParseConfigFile(true); }

        if (Chat.IsRespMsgFileUpdated()) { Chat.LoadResponseMessages(true); }
      }, null, TimeSpan.Zero, new TimeSpan(0, 0, 10));

      FinishedLoading = true;
    }

    public static void ConsoleWarning(string text, ConsoleColor color = ConsoleColor.DarkYellow)
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

    private async void ChkTTS_CheckChanged(object sender, RoutedEventArgs e)
    {
      Notifications.ChatTTSEnabled = ((CheckBox)sender).IsChecked == true;
      if (FinishedLoading) await Database.UpdateValueInConfig(Database.Keys.EnabledChatTTS, Notifications.ChatTTSEnabled);
    }

    private async void ChkGamba_CheckChanged(object sender, RoutedEventArgs e)
    {
      MinigameGamba.Enabled = ((CheckBox)sender).IsChecked == true;
      if (FinishedLoading) await Database.UpdateValueInConfig(Database.Keys.EnabledGamba, MinigameGamba.Enabled);
    }

    private async void ChkGambaLife_CheckChanged(object sender, RoutedEventArgs e)
    {
      MinigameGamba.GambaLifeEnabled = ((CheckBox)sender).IsChecked == true;
      if (FinishedLoading) await Database.UpdateValueInConfig(Database.Keys.EnabledGambaLife, MinigameGamba.GambaLifeEnabled);
    }

    private async void ChkGambaAnim_CheckChanged(object sender, RoutedEventArgs e)
    {
      MinigameGamba.GambaAnimationsEnabled = ((CheckBox)sender).IsChecked == true;
      if (FinishedLoading) await Database.UpdateValueInConfig(Database.Keys.EnabledGambaAnimations, MinigameGamba.GambaAnimationsEnabled);
    }

    private async void ChkFight_CheckChanged(object sender, RoutedEventArgs e)
    {
      MinigameFight.Enabled = ((CheckBox)sender).IsChecked == true;
      if (FinishedLoading) await Database.UpdateValueInConfig(Database.Keys.EnabledFight, MinigameFight.Enabled);
    }

    private async void ChkWelcome_CheckChanged(object sender, RoutedEventArgs e)
    {
      Notifications.WelcomeMessagesEnabled = ((CheckBox)sender).IsChecked == true;
      if (FinishedLoading) await Database.UpdateValueInConfig(Database.Keys.EnabledWelcomeMessages, Notifications.WelcomeMessagesEnabled);
    }

    private async void ChkSkip_CheckChanged(object sender, RoutedEventArgs e)
    {
      Spotify.SkipEnabled = ((CheckBox)sender).IsChecked == true;
      if (FinishedLoading) await Database.UpdateValueInConfig(Database.Keys.EnabledSpotifySkip, Spotify.SkipEnabled);
    }

    private async void ChkRequest_CheckChanged(object sender, RoutedEventArgs e)
    {
      Spotify.RequestEnabled = ((CheckBox)sender).IsChecked == true;
      if (FinishedLoading) await Database.UpdateValueInConfig(Database.Keys.EnabledSpotifyRequest, Spotify.RequestEnabled);
    }

    private void MainVideoEnded(object sender, RoutedEventArgs e)
    {
      VideoPlayer.Source = null;
      VideoEnded = true;
    }

    private void VideoTest(object sender, RoutedEventArgs e)
    {
      Notifications.AddNotification(new Notification()
      {
        // VideoPath = "Resources/peepoHey.mp4",
        // VideoVolume = Config.VolumeVideos,
        VideoPath = "Resources/bot.mp4",
        VideoVolume = 0.05f,
      });
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
          Notifications.CreateGiftSubscriptionNotification("Chatter", "1", 7, "This is a test", null);
          break;

        case "Subscription Ext. Msg":
          Notifications.CreateSubscriptionNotification("Chatter", "1", 7, 77, 777, new EventPayloadMessage() { Text = "This is a test" });
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
      if (FinishedLoading) Config.VolumeValuesDirty = true;
    }

    private void VolumeSoundsChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
      tbVolumeSounds.Text = $"Sounds Volume: {e.NewValue}%";
      Config.VolumeSounds = (float)e.NewValue / 100f;
      if (FinishedLoading) Config.VolumeValuesDirty = true;
    }

    private void VolumeVideosChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
      tbVolumeVideos.Text = $"Videos Volume: {e.NewValue}%";
      Config.VolumeVideos = (float)e.NewValue / 100f;
      if (FinishedLoading) Config.VolumeValuesDirty = true;
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

    /// <summary> Sets enabled checkboxes statuses to values present in relevant classes. </summary>
    public void SetEnabledStatus()
    {
      chkEnableTTS.IsChecked = Notifications.ChatTTSEnabled;
      chkEnableGamba.IsChecked = MinigameGamba.Enabled;
      chkEnableGambaLife.IsChecked = MinigameGamba.GambaLifeEnabled;
      chkEnableGambaAnimations.IsChecked = MinigameGamba.GambaAnimationsEnabled;
      chkEnableFight.IsChecked = MinigameFight.Enabled;
      chkEnableWelcomeMessages.IsChecked = Notifications.WelcomeMessagesEnabled;
      chkEnableSongSkip.IsChecked = Spotify.SkipEnabled;
      chkEnableSongRequest.IsChecked = Spotify.RequestEnabled;
    }

    public void GambaAnimationStart(FileInfo videoPath, string userName, int points, int pointsResult)
    {
      Dispatcher.Invoke(new Action(() =>
      {
        tbGambaName.Text = userName;

        if (videoPath.Exists)
        {
          videoGamba.Source = new Uri(videoPath.FullName);
          videoGamba.Play();
        }

        while (!videoGamba.HasVideo || !videoGamba.NaturalDuration.HasTimeSpan || videoGamba.Position.TotalSeconds == 0) Task.Delay(1).Wait();

        var duration = videoGamba.NaturalDuration.TimeSpan.TotalSeconds;
        double sleepDuration = 0;

        tbGambaPoints.Margin = new Thickness(0, -210, 0, 0);

        // Animate points
        Task.Run(async () =>
        {
          for (int i = 0; i <= 60; i++)
          {
            I.Dispatcher.Invoke(new Action(() =>
            {
              if (i == 0) tbGambaPoints.Text = points.ToString();

              tbGambaPoints.Margin = new Thickness(0, tbGambaPoints.Margin.Top + 1, 0, 0);

              if (i == 60)
              {
                tbGambaPoints.Text = string.Empty;
                if (!videoGamba.HasVideo) tbGambaName.Text = string.Empty; // If the video doesn't play "finish" the animation
                sleepDuration = (duration - videoGamba.Position.TotalSeconds - 1d) * 1000d;
              }
            }));
            await Task.Delay(10);
          }

          if (pointsResult > 0)
          {
            if (sleepDuration > 0) await Task.Delay((int)sleepDuration);

            for (int i = 0; i <= 60; i++)
            {
              I.Dispatcher.Invoke(new Action(() =>
              {
                if (i == 0) tbGambaPoints.Text = $"+{pointsResult}";

                tbGambaPoints.Margin = new Thickness(0, tbGambaPoints.Margin.Top - 1, 0, 0);
                if (i == 60)
                {
                  tbGambaPoints.Text = string.Empty;
                  if (!videoGamba.HasVideo) tbGambaName.Text = string.Empty; // If the video doesn't play "finish" the animation
                }
              }));
              await Task.Delay(10);
            }
          }
        });
      }));
    }

    private void GambaVideoEnded(object sender, RoutedEventArgs e)
    {
      ((MediaElement)sender).Source = null;
      ((MediaElement)sender).Stop();
      ((MediaElement)sender).Position = new TimeSpan();
      tbGambaName.Text = string.Empty;
    }

    private void GambaTest(object sender, RoutedEventArgs e)
    {
      GambaAnimationStart(new FileInfo("Resources/Gamba/GambaLoose1.mp4"), "Chatter", 1000, 0);
    }
  }
}
