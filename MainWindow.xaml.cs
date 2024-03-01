using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace AbevBot;

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
  private Thickness tbTextDesiredPosition = new();
  private Thickness playerDesiredPosition = new();

  public MainWindow(string[] args = null)
  {
    InitializeComponent();
    I = this;

    // Catch all unhandled exceptions and print them into a file
    AppDomain.CurrentDomain.UnhandledException += (sender, ex) =>
    {
      using StreamWriter writer = new("lasterror.log");
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

    // Check if internet connection is active, if not the bot hangs up here and waits for the connection to be active
    do
    {
      try
      {
        var result = Notifications.Client.Send(new(HttpMethod.Head, "http://www.google.com"));
        if (result.IsSuccessStatusCode) break;
      }
      catch { }
      ConsoleWarning(">> Internet connection not active. Waiting 5 s and trying out again.");
      Thread.Sleep(5000);
    } while (true);

    AccessTokens.GetAccessTokens();
    Config.GetBroadcasterID();

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
          VideoPath = "Resources/bot.mp4",
          VideoVolume = Config.VolumeVideos,
        });
      }
    };

    // Don't allow minimizing the window
    StateChanged += (sender, e) =>
    {
      if (I.WindowState == WindowState.Minimized) I.WindowState = WindowState.Normal;
    };

    // Start something like main background loop
    new Thread(() =>
    {
      Stopwatch sw = Stopwatch.StartNew();
      bool hotkeyPausePressed = false, hotkeySkipPressed = false;
      bool shouldHotkeysBeChecked = Config.HotkeysForPauseNotification.Count > 0 || Config.HotkeysForSkipNotification.Count > 0;
      bool hotkeyActive;
      while (true)
      {
        // every tick check for active hotkeys
        if (shouldHotkeysBeChecked)
        {
          Dispatcher.Invoke(() =>
          {
            // Hotkey pause
            if (Config.HotkeysForPauseNotification.Count > 0)
            {
              hotkeyActive = true;
              foreach (var k in Config.HotkeysForPauseNotification)
              {
                if (!Keyboard.IsKeyDown(k)) { hotkeyActive = false; }
              }
              if (hotkeyActive)
              {
                if (!hotkeyPausePressed)
                {
                  hotkeyPausePressed = true;
                  PauseNotificationsClicked(btnPause, null);
                  ConsoleWarning(">> Notification pause hotkey activated.");
                }
              }
              else { hotkeyPausePressed = false; }
            }

            // Hotkey skip
            if (Config.HotkeysForSkipNotification.Count > 0)
            {
              hotkeyActive = true;
              foreach (var k in Config.HotkeysForSkipNotification)
              {
                if (!Keyboard.IsKeyDown(k)) { hotkeyActive = false; }
              }
              if (hotkeyActive)
              {
                if (!hotkeySkipPressed)
                {
                  hotkeySkipPressed = true;
                  SkipNotificationClicked(btnSkip, null);
                  ConsoleWarning(">> Notification skip hotkey activated.");
                }
              }
              else { hotkeySkipPressed = false; }
            }
          });
        }

        // every 10 sec. check if access token should be refreshed, also do some periodic things
        if (sw.ElapsedMilliseconds >= 10000)
        {
          shouldHotkeysBeChecked = true || Config.HotkeysForPauseNotification.Count > 0 || Config.HotkeysForSkipNotification.Count > 0;

          // Check if any access token should be updated
          if (AccessTokens.RefreshAccessToken()) AccessTokens.UpdateTokens();
          if (AccessTokens.RefreshSpotifyAccessToken()) AccessTokens.UpdateSpotifyTokens();
          if (AccessTokens.RefreshDiscordAccessToken()) AccessTokens.UpdateDiscordTokens();

          Chatter.UpdateChattersFile();

          Config.UpdateVolumes();

          if (Config.IsConfigFileUpdated()) Config.ParseConfigFile(true);

          if (Chat.IsRespMsgFileUpdated()) Chat.LoadResponseMessages(true);

          // Check if broadcaster is online
          if (DateTime.Now - Config.BroadcasterLastOnlineCheck >= Config.BroadcasterOnlineCheckInterval)
          {
            if (Config.GetBroadcasterStatus())
            {
              if (DateTime.Now - Config.BroadcasterLastOnline >= Config.BroadcasterOfflineTimeout)
              {
                Discord.SendOnlineMessage();
              }
              Config.BroadcasterLastOnline = DateTime.Now;
              Config.UpdateLastOnline();
            }
            Config.BroadcasterLastOnlineCheck = DateTime.Now;
          }
          sw.Restart();
        }

        Thread.Sleep(100);
      }
    })
    {
      Name = "MainLoop",
      IsBackground = true,
    }.Start();

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

  private void PauseNotificationsClicked(object sender, RoutedEventArgs e)
  {
    Notifications.NotificationsPaused ^= true;
    if (Notifications.NotificationsPaused) ((Button)sender).Background = Brushes.Red;
    else ((Button)sender).Background = btnSkip.Background;
  }

  private void SkipNotificationClicked(object sender, RoutedEventArgs e)
  {
    Notifications.SkipNotification = true;
  }

  private void StopFollowBotsClicked(object sender, RoutedEventArgs e)
  {
    Chatter.StopFollowBots();
  }

  public void SetTextDisplayed(string text, Notifications.TextPosition position, VideoParameters videoParams)
  {
    (double x, double y) videoCenter = (0d, 0d);
    if (position == Notifications.TextPosition.VIDEOABOVE ||
         position == Notifications.TextPosition.VIDEOCENTER ||
         position == Notifications.TextPosition.VIDEOBELOW)
    {
      if (videoParams is null) position = Notifications.TextPosition.CENTER;
      else
      {
        videoCenter.x = videoParams.Left + (videoParams.Width / 2d);
        videoCenter.y = videoParams.Top + (videoParams.Height / 2d);
        if (videoParams.Left == 0 && videoParams.Top == 0)
        {
          videoCenter.x += MainGrid.ActualWidth / 2d;
          videoCenter.y += MainGrid.ActualHeight / 2d;
        }
      }
    }

    Dispatcher.Invoke(new Action(() =>
    {
      tbText.Text = text;
      tbText.Margin = new Thickness();
      tbTextDesiredPosition.Left = 0;
      tbTextDesiredPosition.Top = 0;

      switch (position)
      {
        case Notifications.TextPosition.TOPLEFT:
          tbText.VerticalAlignment = VerticalAlignment.Top;
          tbText.HorizontalAlignment = HorizontalAlignment.Left;
          break;
        case Notifications.TextPosition.TOP:
          tbText.VerticalAlignment = VerticalAlignment.Top;
          tbText.HorizontalAlignment = HorizontalAlignment.Center;
          break;
        case Notifications.TextPosition.TOPRIGHT:
          tbText.VerticalAlignment = VerticalAlignment.Top;
          tbText.HorizontalAlignment = HorizontalAlignment.Right;
          break;
        case Notifications.TextPosition.LEFT:
          tbText.VerticalAlignment = VerticalAlignment.Center;
          tbText.HorizontalAlignment = HorizontalAlignment.Left;
          break;
        case Notifications.TextPosition.CENTER:
          tbText.VerticalAlignment = VerticalAlignment.Center;
          tbText.HorizontalAlignment = HorizontalAlignment.Center;
          break;
        case Notifications.TextPosition.RIGHT:
          tbText.VerticalAlignment = VerticalAlignment.Center;
          tbText.HorizontalAlignment = HorizontalAlignment.Right;
          break;
        case Notifications.TextPosition.BOTTOMLEFT:
          tbText.VerticalAlignment = VerticalAlignment.Bottom;
          tbText.HorizontalAlignment = HorizontalAlignment.Left;
          break;
        case Notifications.TextPosition.BOTTOM:
          tbText.VerticalAlignment = VerticalAlignment.Bottom;
          tbText.HorizontalAlignment = HorizontalAlignment.Center;
          break;
        case Notifications.TextPosition.BOTTOMRIGHT:
          tbText.VerticalAlignment = VerticalAlignment.Bottom;
          tbText.HorizontalAlignment = HorizontalAlignment.Right;
          break;
        case Notifications.TextPosition.VIDEOABOVE:
          tbText.VerticalAlignment = VerticalAlignment.Top;
          tbText.HorizontalAlignment = HorizontalAlignment.Left;
          tbTextDesiredPosition.Left = videoCenter.x;
          tbTextDesiredPosition.Top = videoCenter.y - (videoParams.Height / 2d) - (tbText.FontSize / 2d);
          break;
        case Notifications.TextPosition.VIDEOCENTER:
          tbText.VerticalAlignment = VerticalAlignment.Top;
          tbText.HorizontalAlignment = HorizontalAlignment.Left;
          tbTextDesiredPosition.Left = videoCenter.x;
          tbTextDesiredPosition.Top = videoCenter.y;
          break;
        case Notifications.TextPosition.VIDEOBELOW:
          tbText.VerticalAlignment = VerticalAlignment.Top;
          tbText.HorizontalAlignment = HorizontalAlignment.Left;
          tbTextDesiredPosition.Left = videoCenter.x;
          tbTextDesiredPosition.Top = videoCenter.y + (videoParams.Height / 2d) + (tbText.FontSize / 2d);
          break;
      }
      tbText.Visibility = Visibility.Visible;
    }));
  }

  public void ClearTextDisplayed()
  {
    Dispatcher.Invoke(new Action(() =>
    {
      tbText.Text = string.Empty;
      tbText.Visibility = Visibility.Hidden;
    }));
  }

  private void ResetVideoPlayer()
  {
    VideoPlayer.Height = double.NaN; // default: double.NaN
    VideoPlayer.Width = double.NaN;
    VideoPlayer.HorizontalAlignment = HorizontalAlignment.Stretch; // default: Stretch
    VideoPlayer.VerticalAlignment = VerticalAlignment.Stretch; // default: Stretch
    VideoPlayer.Margin = new Thickness(0);
    playerDesiredPosition = new();
  }

  public void StartVideoPlayer(string path, float volume, VideoParameters videoParams = null)
  {
    Dispatcher.Invoke(new Action(() =>
    {
      ResetVideoPlayer();
      if (videoParams != null)
      {
        if (videoParams.Left != 0 || videoParams.Top != 0)
        {
          VideoPlayer.HorizontalAlignment = HorizontalAlignment.Left;
          VideoPlayer.VerticalAlignment = VerticalAlignment.Top;
          playerDesiredPosition.Left = videoParams.Left;
          playerDesiredPosition.Top = videoParams.Top;
        }
        VideoPlayer.Height = videoParams.Height;
        VideoPlayer.Width = videoParams.Width;
      }

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
      ResetVideoPlayer();
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
      VideoVolume = Config.VolumeVideos,
    });
  }

  private void TTSTest(object sender, RoutedEventArgs e)
  {
    string text = tbTTSText.Text.Trim();
    if (string.IsNullOrEmpty(text)) return;
    Notifications.CreateTTSNotification(text);
  }

  private void NotificationTestClicked(object sender, RoutedEventArgs e)
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

      case "Discord message":
        Discord.SendOnlineMessage();
        break;

      case "Random video":
        Notifications.CreateRandomVideoNotification(string.Empty);
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

  private void tbText_SizeChanged(object sender, SizeChangedEventArgs e)
  {
    if (!this.IsLoaded) return;

    var tb = (TextBlock)sender;
    if (tbTextDesiredPosition.Left != 0 || tbTextDesiredPosition.Top != 0)
    {
      Dispatcher.Invoke(new Action(() =>
      {
        tb.Margin = new Thickness(tbTextDesiredPosition.Left - (e.NewSize.Width / 2d), tbTextDesiredPosition.Top - (e.NewSize.Height / 2d), 0, 0);
      }));
    }
  }

  private void VideoPlayer_SizeChanged(object sender, SizeChangedEventArgs e)
  {
    var player = (MediaElement)sender;
    if (playerDesiredPosition.Left != 0 || playerDesiredPosition.Top != 0)
    {
      player.Margin = new Thickness(playerDesiredPosition.Left - e.NewSize.Width / 2d, playerDesiredPosition.Top - e.NewSize.Height / 2d, 0, 0);
    }
  }
}

public class VideoParameters
{
  public double Width;
  public double Height;
  public double Left;
  public double Top;

  public void Reset()
  {
    Width = Height = Left = Top = 0;
  }
}
