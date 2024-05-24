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

using Serilog;

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
  private (double x, double y) tbTextDesiredPosition = new();
  private Thickness playerDesiredPosition = new();
  private Notifications.TextPosition tbTextPositionAnchor;
  private bool TextPositionShouldUpdate;
  /// <summary> Window / program close was requested. </summary>
  public static bool CloseRequested { get; private set; }

  public MainWindow(string[] args = null)
  {
    InitializeComponent();
    I = this;

    // Catch all unhandled exceptions and print them into a file
    AppDomain.CurrentDomain.UnhandledException += (sender, ex) =>
    {
      LogError("UnhandledException", ex.ExceptionObject as Exception);
    };

    // Configure the logger
    Log.Logger = new LoggerConfiguration()
#if DEBUG
      .MinimumLevel.Debug()
#endif
      .WriteTo.Console()
      .CreateLogger();

    // AllocConsole();
    // Free console window on program close
    Closing += (sender, e) =>
    {
      CloseRequested = true;
      Log.Information("Bot close requested");
      Chatter.UpdateChattersFile(true);
      Config.UpdateVolumes();
      ConsoleFreed = true;
      FreeConsole();
    };

    Log.Information("Hi. I'm {AbevBot}.", "AbevBot");

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
        var result = Notifications.Client.Send(new(HttpMethod.Get, "https://www.twitch.tv"));
        if (result.IsSuccessStatusCode) break;
      }
      catch { }
      Log.Warning("Internet connection not active. Waiting 5s and trying out again.");
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
    Server.Start(); // Start http server

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
      catch (Exception ex) { Log.Error("{exception}", ex); }

      if (Config.StartVideoEnabled)
      {
        Notifications.AddNotification(new Notification()
        {
          // VideoPath = "Resources/peepoHey.mp4",
          VideoPath = "Resources/bot.mp4",
          VideoVolume = Config.VolumeVideo,
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
        if (CloseRequested) { break; }

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
                  Log.Information("Notification pause hotkey activated.");
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
                  Log.Information("Notification skip hotkey activated.");
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

  /// <summary> Writes a message to console window. </summary>
  /// <param name="msg">The message</param>
  public static void ConsoleWriteLine(string msg)
  {
    if (ConsoleFreed) return;
    Console.WriteLine(msg);
  }

  /// <summary> Writes error message to 'lasterror.log' file. </summary>
  /// <param name="msg">Error message</param>
  public static void LogError(string msg)
  {
    var file = new FileInfo("lasterror.log");
    var clearContents = file.Exists && file.LastWriteTime.Date != DateTime.Now.Date;
    using StreamWriter writer = clearContents ? file.CreateText() : file.AppendText();
    file.LastWriteTime = DateTime.Now - TimeSpan.FromDays(1);
    writer.WriteLine(DateTime.Now);
    writer.WriteLine(msg);
    writer.WriteLine();
  }

  /// <summary> Writes exception message to 'lasterror.log' file. </summary>
  /// <param name="ex">Exception object</param>
  public static void LogError(string header, Exception ex)
  {
    LogError($"{header}: {ex.Message}\r\n{ex.StackTrace}");
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

  public void SetTextDisplayed(string text, Notifications.TextPosition position, double textSize, VideoParameters videoParams)
  {
    VideoParameters video = null;

    if (position == Notifications.TextPosition.VIDEOABOVE ||
         position == Notifications.TextPosition.VIDEOCENTER ||
         position == Notifications.TextPosition.VIDEOBELOW)
    {
      if (videoParams is null) position = Notifications.TextPosition.CENTER;
      else
      {
        video = new()
        {
          Left = videoParams.Left + (videoParams.Width / 2d),
          Top = videoParams.Top + (videoParams.Height / 2d),
          Width = videoParams.Width,
          Height = videoParams.Height
        };

        if (videoParams.Left <= 0 && videoParams.Top <= 0)
        {
          video.Left += MainGrid.ActualWidth / 2d;
          video.Top += MainGrid.ActualHeight / 2d;
        }
      }
    }

    Dispatcher.Invoke(new Action(() =>
    {
      tbText.Text = text;
      tbText.FontSize = textSize;
      tbText.Margin = new Thickness();
      tbTextPositionAnchor = position;
      tbTextDesiredPosition.x = 0;
      tbTextDesiredPosition.y = 0;
      UpdateTextDesiredPosition(video);
      switch (position)
      {
        case Notifications.TextPosition.VIDEOABOVE:
        case Notifications.TextPosition.VIDEOCENTER:
        case Notifications.TextPosition.VIDEOBELOW:
          tbText.Visibility = Visibility.Hidden;
          break;
        default:
          tbText.Visibility = Visibility.Visible;
          break;
      }
      TextPositionShouldUpdate = true;
    }));
  }

  private void UpdateTextDesiredPosition(VideoParameters video)
  {
    var lineCount = 1;
    double lineSpacing = tbText.FontSize * 0.75d;
    switch (tbTextPositionAnchor)
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
        tbTextDesiredPosition.x = video.Left;
        foreach (char c in tbText.Text) if (c == '\n') lineCount++;
        tbTextDesiredPosition.y = video.Top - (video.Height / 2d) - (lineSpacing * lineCount);
        break;
      case Notifications.TextPosition.VIDEOCENTER:
        tbText.VerticalAlignment = VerticalAlignment.Top;
        tbText.HorizontalAlignment = HorizontalAlignment.Left;
        tbTextDesiredPosition.x = video.Left;
        tbTextDesiredPosition.y = video.Top;
        break;
      case Notifications.TextPosition.VIDEOBELOW:
        tbText.VerticalAlignment = VerticalAlignment.Top;
        tbText.HorizontalAlignment = HorizontalAlignment.Left;
        tbTextDesiredPosition.x = video.Left;
        foreach (char c in tbText.Text) if (c == '\n') lineCount++;
        tbTextDesiredPosition.y = video.Top + (video.Height / 2d) + (lineSpacing * lineCount);
        break;
    }
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
        if (videoParams.Left >= 0 || videoParams.Top >= 0)
        {
          VideoPlayer.HorizontalAlignment = HorizontalAlignment.Left;
          VideoPlayer.VerticalAlignment = VerticalAlignment.Top;
          playerDesiredPosition.Left = videoParams.Left;
          playerDesiredPosition.Top = videoParams.Top;
        }
        VideoPlayer.Height = videoParams.Height > 0 ? videoParams.Height : double.NaN;
        VideoPlayer.Width = videoParams.Width > 0 ? videoParams.Width : double.NaN;
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
      VideoPlayer.Volume = Config.WindowAudioDisabled ? 0 : volume; // Override audio volume
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

  public void PauseVideoPlayer()
  {
    Dispatcher.Invoke(new Action(() =>
    {
      VideoPlayer.Pause();
    }));
  }

  public void ResumeVideoPlayer()
  {
    Dispatcher.Invoke(new Action(() =>
    {
      VideoPlayer.Play();
    }));
  }

  public void SetNotificationQueueCount(int count, int maybeCount)
  {
    Dispatcher.Invoke(new Action(() =>
    {
      tbNotificationsQueue.Text = string.Concat("Notifications in queue: ", count,
       maybeCount > 0 ? $" ({maybeCount})" : "");
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

  private async void ChkVanish_CheckChanged(object sender, RoutedEventArgs e)
  {
    Chat.VanishEnabled = ((CheckBox)sender).IsChecked == true;
    if (FinishedLoading) await Database.UpdateValueInConfig(Database.Keys.EnabledVanish, Chat.VanishEnabled);
  }

  private async void ChkWindowAudio_CheckChanged(object sender, RoutedEventArgs e)
  {
    Config.WindowAudioDisabled = ((CheckBox)sender).IsChecked == true;
    if (FinishedLoading) await Database.UpdateValueInConfig(Database.Keys.WindowAudioDisabled, Config.WindowAudioDisabled);
  }

  private void MainVideoEnded(object sender, RoutedEventArgs e)
  {
    VideoPlayer.Source = null;
    VideoEnded = true;

    ClearTextDisplayed();
  }

  private void VideoTest(object sender, RoutedEventArgs e)
  {
    Notifications.AddNotification(new Notification()
    {
      // VideoPath = "Resources/peepoHey.mp4",
      // VideoVolume = Config.VolumeVideos,
      VideoPath = "Resources/bot.mp4",
      VideoVolume = Config.VolumeVideo,
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

      case "Chat sub message":
        Notifications.CreateMaybeSubscriptionNotification("Chatter", "", "This is test sub received in chat");
        break;
    }
  }

  private void VolumeAudioChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
  {
    tbVolumeAudio.Text = $"Sounds Volume: {e.NewValue}%";
    Config.VolumeAudio = (float)e.NewValue / 100f;
    if (FinishedLoading) Config.VolumeValuesDirty = true;
  }

  private void VolumeVideoChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
  {
    tbVolumeVideo.Text = $"Videos Volume: {e.NewValue}%";
    Config.VolumeVideo = (float)e.NewValue / 100f;
    if (FinishedLoading) Config.VolumeValuesDirty = true;
  }

  private void VolumeChange(object sender, MouseWheelEventArgs e)
  {
    ((Slider)sender).Value += e.Delta > 0 ? 1 : -1;
  }

  /// <summary> Sets volume sliders value to values present in Config class. </summary>
  public void SetVolumeSliderValues()
  {
    volumeAudio.Value = MathF.Round(Config.VolumeAudio * 100);
    volumeVideo.Value = MathF.Round(Config.VolumeVideo * 100);
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
    chkEnableVanish.IsChecked = Chat.VanishEnabled;
    chkDisableWindowAudio.IsChecked = Config.WindowAudioDisabled;
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
    if (!TextPositionShouldUpdate) return;
    TextPositionShouldUpdate = false;

    var tb = (TextBlock)sender;
    if (tbTextDesiredPosition.x != 0 || tbTextDesiredPosition.y != 0)
    {
      double width = e is null ? tb.ActualWidth : e.NewSize.Width;
      double height = e is null ? tb.ActualHeight : e.NewSize.Height;
      tb.Margin = new Thickness(tbTextDesiredPosition.x - (width / 2d), tbTextDesiredPosition.y - (height / 2d), 0, 0);
    }
  }

  private void VideoPlayer_SizeChanged(object sender, SizeChangedEventArgs e)
  {
    var player = (MediaElement)sender;
    if (playerDesiredPosition.Left != 0 || playerDesiredPosition.Top != 0)
    {
      player.Margin = new Thickness(playerDesiredPosition.Left - e.NewSize.Width / 2d, playerDesiredPosition.Top - e.NewSize.Height / 2d, 0, 0);

      if (!VideoEnded)
      {
        VideoParameters video = new()
        {
          Left = playerDesiredPosition.Left,
          Top = playerDesiredPosition.Top,
          Width = e.NewSize.Width,
          Height = e.NewSize.Height,
        };
        UpdateTextDesiredPosition(video);
        TextPositionShouldUpdate = true;
        tbText_SizeChanged(tbText, null);
        tbText.Visibility = Visibility.Visible;
      }
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
    Left = Top = -1;
    Width = Height = 0;
  }
}
