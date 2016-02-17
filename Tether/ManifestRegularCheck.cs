using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Principal;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using NLog;
using Quartz;
using SharpCompress.Archive;
using SharpCompress.Archive.Zip;
using SharpCompress.Common;

namespace Tether
{
    [PersistJobDataAfterExecution]
    [DisallowConcurrentExecution]
    public class ManifestRegularCheck : IJob
    {
        private static Logger logger = LogManager.GetCurrentClassLogger();

        private string basePath = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
        public void Execute(IJobExecutionContext context)
        {
            try
            {
                var pluginPath = Path.Combine(basePath, "plugins");
                var tempPluginPath = Path.Combine(pluginPath, "_temp");

                string contents;
                WebClient client = new WebClient();

                if (String.IsNullOrWhiteSpace(ConfigurationSingleton.Instance.Config.PluginManifestLocation))
                {
                    return;
                }

                if (ConfigurationSingleton.Instance.Config.PluginManifestLocation.StartsWith("http"))
                {
                    contents = client.DownloadString(ConfigurationSingleton.Instance.Config.PluginManifestLocation);
                }
                else
                {
                    string localPath = ConfigurationSingleton.Instance.Config.PluginManifestLocation;
                    if (localPath.StartsWith("~"))
                    {
                        localPath = Path.Combine(basePath, localPath.Substring(2));
                    }
                    logger.Debug("Reading Plugin Manifest from " + localPath);
                    contents = File.ReadAllText(localPath);
                }

                var manifest = JsonConvert.DeserializeObject<PluginManifest>(contents);

                if (!manifest.Items.Any())
                {
                    logger.Debug("No items found");
                    return;
                }

                bool requiresServiceRestart = false;
                
                foreach (var manifestItem in manifest.Items.Where(f => new Regex(f.MachineFilter, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).IsMatch(Environment.MachineName)))
                {
                    var assembly = ConfigurationSingleton.Instance.PluginAssemblies.FirstOrDefault(e => string.Compare(e.GetName().Name, manifestItem.PluginName, StringComparison.InvariantCultureIgnoreCase) > 0);

                    if (assembly != null)
                    {
                        if (assembly.GetName().Version.ToString() != manifestItem.PluginVersion)
                        {
                            var zipPath = Path.Combine(tempPluginPath, assembly.GetName().Name + ".zip");

                            if (!Directory.Exists(tempPluginPath))
                            {
                                Directory.CreateDirectory(tempPluginPath);
                            }

                                client.DownloadFile(manifestItem.PluginDownloadLocation, zipPath);

                            Unzip(zipPath, tempPluginPath);

                            File.Delete(zipPath);

                            requiresServiceRestart = true;
                        }
                    }
                    else
                    {
                        var zipPath = Path.Combine(tempPluginPath, manifestItem.PluginName + ".zip");
                        if (!Directory.Exists(tempPluginPath))
                        {
                            Directory.CreateDirectory(tempPluginPath);
                        }
                        client.DownloadFile(manifestItem.PluginDownloadLocation, zipPath);

                        Unzip(zipPath, tempPluginPath);

                        File.Delete(zipPath);

                        requiresServiceRestart = true;
                    }
                }

                if (requiresServiceRestart)
                {
                    string strCmdText = "/C net stop ThreeOneThree.Tether && move /Y _temp\\* . && net start ThreeOneThree.Tether";

                    ProcessStartInfo info = new ProcessStartInfo("CMD.exe", strCmdText);

                    info.WorkingDirectory = pluginPath;
                    logger.Fatal("!!! GOING DOWN FOR AN UPDATE TO PLUGINS !!!");
                    Process.Start(info);
                }

            }
            catch (Exception e)
            {
                logger.Warn("Error while checking Manifests", e);
            }
        }


        private void Unzip(string zipFileName, string destinationPath)
        {
            if (!Directory.Exists(destinationPath))
            {
                Directory.CreateDirectory(destinationPath);
            }
            ExtractZipFile(zipFileName, destinationPath);

        }

        private void ExtractZipFile(string filePath, string destination)
        {
            logger.Info("Unzipping '{0}' to {1}", filePath, destination);
            using (var archive = ZipArchive.Open(new FileInfo(filePath)))
            {
                
                archive.WriteToDirectory(destination, ExtractOptions.ExtractFullPath | ExtractOptions.Overwrite);
            }
        }
    }
}