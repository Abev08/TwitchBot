using System;
using System.Windows.Controls;

namespace AbevBot;

public partial class NotificationControl : UserControl
{
  private readonly Notification Notif;

  public NotificationControl(Notification notif)
  {
    InitializeComponent();

    Notif = notif;

    Update();

    btnPlay.Click += Play;
    btnSkip.Click += (sender, e) =>
    {
      Notifications.Skip(Notif);
    };
  }

  public void Update()
  {
    MainWindow.I.Dispatcher.Invoke(new Action(() =>
    {
      tbType.Text = Notif.Type switch
      {
        NotificationType.FOLLOW => "FOLLOW",
        NotificationType.SUBSCRIPTION => "SUBSCRIPTION",
        NotificationType.SUBSCRIPTIONGIFT => "SUBSCRIPTION GIFT",
        NotificationType.CHEER => "CHEER",
        NotificationType.CHEERRANGE => "CHEER",
        NotificationType.RAID => "RAID",
        NotificationType.REDEMPTION => "CHANNEL POINTS REDEMPTION",
        NotificationType.TIMEOUT => "TIMEOUT",
        NotificationType.BAN => "BAN",
        NotificationType.ONSCREENCELEBRATION => "ON SCREEN CELEBRATION",
        NotificationType.OTHER => "OTHER",
        _ => "UNDEFINED"
      };
      if (Notif.SubType?.Length > 0)
      {
        tbType.Text += string.Concat(" (", Notif.SubType, ')');
      }

      if (Notif.Finished)
      {
        tbTime.Text = string.Concat(
          "C: ", Notif.CreationTime.ToString("T"),
          ", S: ", Notif.StartTime.ToString("T"),
          ", F: ", Notif.FinishTime.ToString("T"));
      }
      else if (Notif.Started)
      {
        tbTime.Text = string.Concat(
          "C: ", Notif.CreationTime.ToString("T"),
          ", S: ", Notif.StartTime.ToString("T"));
      }
      else { tbTime.Text = string.Concat("C: ", Notif.CreationTime.ToString("T")); }

      tbUserName.Text = Notif.Sender;
      if (Notif.TextToDisplay?.Length > 0) { tbDisplayedText.Text = Notif.TextToDisplay.Replace("\r\n", " "); }
      tbTTS.Text = Notif.TextToRead;

      if (Notif.NotPausable || (Notif.Finished && Notif.NotReplayable)) { btnPlay.Visibility = System.Windows.Visibility.Hidden; }
      else if (Notif.Finished && !Notif.NotReplayable)
      {
        if (btnPlay.Content.ToString() != "REPLAY")
        {
          btnPlay.Content = "REPLAY";
          btnPlay.Click -= Play;
          btnPlay.Click += Replay;
        }
        btnPlay.Visibility = System.Windows.Visibility.Visible;
      }
      else if (!Notif.Started)
      {
        btnPlay.Content = "PLAY";
        btnPlay.Visibility = System.Windows.Visibility.Visible;
      }
      if (Notif.Started || Notif.Finished) { btnSkip.Visibility = System.Windows.Visibility.Hidden; }
    }));
  }

  private void Play(object sender, System.Windows.RoutedEventArgs e)
  {
    Notifications.MoveToTop(Notif);
    ((Button)sender).Content = "REPLAY";
    ((Button)sender).Visibility = System.Windows.Visibility.Hidden;
    ((Button)sender).Click -= Play;
    ((Button)sender).Click += Replay;
  }

  private void Replay(object sender, System.Windows.RoutedEventArgs e)
  {
    Notifications.ReplayNotification(Notif);
    ((Button)sender).Content = "PLAY";
    ((Button)sender).Click += Play;
    ((Button)sender).Click -= Replay;
  }
}
