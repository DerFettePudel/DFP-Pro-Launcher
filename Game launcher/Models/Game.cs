using System.Windows.Media;

namespace Game_launcher.Models
{
    public class Game
    {
        public string Title { get; set; }
        public string ExecutablePath { get; set; }
        public string Publisher { get; set; }
        public ImageSource Icon { get; set; }
    }
}