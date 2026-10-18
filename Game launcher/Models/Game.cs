using System.Windows.Media;

namespace Game_launcher.Models
{
    public class Game
    {
        public string Title { get; set; } = string.Empty;
        public string ExecutablePath { get; set; } = string.Empty;
        public string Publisher { get; set; } = string.Empty;
        public ImageSource? Icon { get; set; }
    }
}