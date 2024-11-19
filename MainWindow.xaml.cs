using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

using Serilog;

namespace AbevBot;

public partial class MainWindow : Window
{
  [DllImport("Kernel32")]
  private static extern void AllocConsole();
  [DllImport("Kernel32")]
  private static extern void FreeConsole();
  /// <summary> Has the console got freed? </summary>
  public static bool ConsoleFreed { get; private set; }

  /// <summary> MainWindow instance - the window. </summary>
  public static MainWindow I { get; private set; }
  /// <summary> Has the window finished loading? </summary>
  private static bool FinishedLoading { get; set; }
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

    // Free console window on program close
    Closing += (sender, e) =>
    {
      CloseRequested = true;
      Log.Information("Bot close requested");
      Chatter.UpdateChattersFile(true);
      Config.UpdateVolumes();
      Database.Connection.Close();
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
    Counter.Start(); // Start on screen counters

    // Wait for window to be loaded (visible) to start a demo video
    Loaded += (sender, e) =>
    {
      if (Config.StartVideoEnabled)
      {
        var n = new Notification()
        {
          Type = NotificationType.OTHER,
          SubType = "Start video",
          VideoPath = "Resources/bot.mp4",
        };
        n.UpdateControl();
        Notifications.AddNotification(n);
      }
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
          Dispatcher.Invoke(new Action(() =>
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
          }));
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

        // YouTube
        YouTube.CheckActiveStream();
        YouTube.PollChatMessages();

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

  public void SetNotificationQueueCount(int count, int maybeCount)
  {
    Dispatcher.Invoke(new Action(() =>
    {
      tbNotificationsQueue.Text = string.Concat(
        "Notifications in queue: ", count,
        maybeCount > 0 ? $" ({maybeCount})" : "");
    }));
  }

  public void UpdateNotificationQueue(int queueCount, int maybeCount, Notification notif, bool removeNotif)
  {
    Dispatcher.Invoke(new Action(() =>
    {
      tbNotificationsQueue.Text = string.Concat(
        "Notifications in queue: ", queueCount,
        maybeCount > 0 ? $" ({maybeCount})" : "");

      if (notif != null && notif.Control != null)
      {
        if (removeNotif)
        {
          if (CurrentNotificationsPanel.Children.Contains(notif.Control)) { CurrentNotificationsPanel.Children.Remove(notif.Control); }
        }
        else
        {
          CurrentNotificationsPanel.Children.Add(notif.Control);
        }
      }
    }));
  }

  public void UpdatePastNotificationQueue(Notification notif, bool removeNotif)
  {
    Dispatcher.Invoke(new Action(() =>
    {
      if (notif != null && notif.Control != null)
      {
        if (removeNotif)
        {
          notif.UpdateControl();
          if (PastNotificationsPanel.Children.Contains(notif.Control)) { PastNotificationsPanel.Children.Remove(notif.Control); }
        }
        else
        {
          PastNotificationsPanel.Children.Insert(0, notif.Control);
        }
      }
    }));
  }

  public void RecreateNotificationQueue()
  {
    CurrentNotificationsPanel.Children.Clear();

    for (int i = 0; i < Notifications.NotificationQueue.Count; i++)
    {
      CurrentNotificationsPanel.Children.Add(Notifications.NotificationQueue[i].Control);
    }
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

  private void VideoTest(object sender, RoutedEventArgs e)
  {
    var n = new Notification()
    {
      Type = NotificationType.OTHER,
      SubType = "Test video",
      VideoPath = "Resources/bot.mp4",
    };
    n.UpdateControl();
    Notifications.AddNotification(n);
  }

  private void TTSTest(object sender, RoutedEventArgs e)
  {
    string text = tbTTSText.Text.Trim();
    if (string.IsNullOrEmpty(text)) return;
    Notifications.CreateTTSNotification(text, "Chatter");
  }

  private void NotificationTestClicked(object sender, RoutedEventArgs e)
  {
    if (!int.TryParse(tbTestNotificationValue.Text, out int value)) { value = 7; }

    switch (cbNotificationType.Text)
    {
      case "Follow":
        Notifications.CreateFollowNotification("Chatter");
        break;

      case "Subscription":
        Notifications.CreateSubscriptionNotification("Chatter", "1", "This is a test");
        break;

      case "Subscription Gifted":
        Notifications.CreateGiftSubscriptionNotification("Chatter", "1", value, "This is a test", null);
        break;

      case "Subscription Ext. Msg":
        Notifications.CreateSubscriptionNotification("Chatter", "1", 7, value, 777, new EventPayloadMessage() { Text = "This is a test" });
        break;

      case "Cheer":
        Notifications.CreateCheerNotification("Chatter", value, "This is a test");
        break;

      case "Discord message":
        Discord.SendOnlineMessage();
        break;

      case "Random video":
        Notifications.CreateRandomVideoNotification(string.Empty, "Chatter");
        break;

      case "Chat sub message":
        Notifications.CreateMaybeSubscriptionNotification("Prime Chatter",
          "prime", "1", value.ToString(), "3",
          "This is test sub received in chat");
        break;

      case "On screen celebration":
        Notifications.CreateOnScreenCelebrationNotification("Chatter", "This is a test");
        break;

      case "Key combination":
        var n = new Notification()
        {
          Type = NotificationType.OTHER,
          SubType = "Channel points redemption",
          Sender = "Chatter",
          Redemption = new ChannelRedemption()
          {
            KeysToPress = new System.Collections.Generic.List<Key>() { Key.LeftCtrl, Key.LeftShift, Key.Escape }
          }
        };
        n.UpdateControl();
        Notifications.AddNotification(n);
        break;

      case "Message effect":
        Notifications.CreateMessageEffectNotification("Chatter", "This is a test");
        break;

      case "Gigantify an emote":
        Notifications.CreateGigantifyEmoteNotification("Chatter", "This is a test");
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
  }

  /// <summary> Preview of text input into notification test textbox. It should allow only numbers. </summary>
  private void TestNotificationValue_TextInput(object sender, TextCompositionEventArgs e)
  {
    if (!int.TryParse(e.Text, out _)) { e.Handled = true; }
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
