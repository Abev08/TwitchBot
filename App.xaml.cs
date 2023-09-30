using System.Windows;

namespace AbevBot
{
  /// <summary> The best Twitch bot :) </summary>
  public partial class App : Application
  {
    private void ApplicationStartup(object sender, StartupEventArgs e)
    {
      MainWindow window = new(e?.Args);
      window.Show();
    }
  }
}
