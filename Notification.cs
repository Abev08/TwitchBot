using System.Collections.Generic;
using System.Threading;
using NAudio.Wave;

namespace AbevBot
{
  public class Notifications
  {
    static bool NotificationStarted;
    public static bool NotificationsPaused = false;
    public static bool SkipNotification = false;
    static List<Notification> NotificationQueue = new List<Notification>();

    public static void Start()
    {
      if (NotificationStarted) return;
      NotificationStarted = true;
      MainWindow.ConsoleWarning(">> Starting stream notifications");

      new Thread(() =>
      {
        bool notificationEnded = false;
        while (true)
        {
          if ((NotificationQueue.Count > 0) && ((NotificationsPaused == false) || NotificationQueue[0].Started))
          {
            lock (NotificationQueue)
            {
              if (NotificationQueue[0].Started == false) NotificationQueue[0].Start();
              if (NotificationQueue[0].Update(SkipNotification))
              {
                // Update returned true == notificaion has ended, remove it from queue
                NotificationQueue.RemoveAt(0);
                notificationEnded = true;
              }
            }

            // Maybe there should be some delay between notificaions?
            if (notificationEnded)
            {
              if (NotificationQueue.Count > 0) Thread.Sleep(500); // Delay between notifications
              notificationEnded = false;
            }
            else Thread.Sleep(100); // Slow down the loop while notification is playing, 100 ms shouldn't be a problem?
          }
          else
          {
            Thread.Sleep(500); // Check for new notification every 0.5 s?
          }
        }
      })
      { Name = "Notifications Thread", IsBackground = true }.Start();
    }

    public static void AddNotification(Notification notification)
    {
      lock (NotificationQueue)
      {
        NotificationQueue.Add(notification);
      }
    }
  }

  public class Notification
  {
    public bool Started;
    string Text;
    bool DisplayText, ReadText;
    WaveOut AudioPlayer;

    /// <summary> A notification to be displayed on stream </summary>
    /// <param name="text">Notification text</param>
    /// <param name="displayText">Display notification text on window?</param>
    /// <param name="readText">Read notification text out loud?</param>
    public Notification(string text, bool displayText, bool readText)
    {
      Text = text;
      DisplayText = displayText;
      ReadText = readText;
    }

    /// <summary> Initializes required things and starts the notification </summary>
    public void Start()
    {
      if (Started) return;
      Started = true;

      // First acquire audio and start playing
      if (ReadText && Events.Started)
      {
        // AudioPlayer = Events.GetTTS(Text);
      }
      // Then display the text because acquiring the audio could take a while
      if (DisplayText) MainWindow.SetTextDisplayed(Text);
    }

    /// <summary> Update status of playing notification </summary>
    /// <returns> true if notification ended </returns>
    public bool Update(bool forceEnd)
    {
      if (forceEnd) Notifications.SkipNotification = false; // In main Notifications loop it didn't work 100%, here works so much better

      if (Started == false) return false;

      if (forceEnd)
      {
        AudioPlayer.Stop();
      }
      else
      {
        if (Notifications.NotificationsPaused)
        {
          AudioPlayer.Pause();
        }
        else
        {
          AudioPlayer.Play();
        }
      }

      if (AudioPlayer.PlaybackState != PlaybackState.Stopped) return false;
      // The notification is over, clear after it
      else
      {
        AudioPlayer.Dispose(); // Probably it's better to dispose the player after it finished
        MainWindow.SetTextDisplayed(string.Empty); // Clear displayed text
      }

      return true;
    }
  }
}
