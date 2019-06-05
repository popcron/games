namespace Popcron.Updater
{
    public class Settings
    {
        public const string VersionFile = "version.txt";
        public const int Delay = 5;
        public const int ErrorDelay = 2000;

        public string company = "popcron";
        public string repositoryOwner = "popcron";
        public string repositoryName = "games";
        public string gameName = "Rocket Jump";
        public string execName = "RocketJump/RocketJump.exe";
        public string tagPrefix = "rocketjump_";
    }
}