namespace Tether
{
    public class Configuration
    {
        public string ServerDensityUrl { get; set; }

        public string ServerDensityKey { get; set; }

        public Configuration()
        {
            CheckInterval = 60;
        }

        public int CheckInterval { get; set; }

        public string PluginManifestLocation { get; set; }        
    }
}