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

    btnPlay.Click += (sender, e) =>
    {
      Notifications.MoveToTop(Notif);
    };
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
      tbTime.Text = Notif.CreationTime.ToString("T");
      tbUserName.Text = Notif.Sender;
      if (Notif.TextToDisplay?.Length > 0) { tbDisplayedText.Text = Notif.TextToDisplay.Replace("\r\n", " "); }
      tbTTS.Text = Notif.TextToRead;

      if (Notif.Started)
      {
        btnPlay.Visibility = System.Windows.Visibility.Hidden;
        btnSkip.Visibility = System.Windows.Visibility.Hidden;
      }
    }));
  }
}
