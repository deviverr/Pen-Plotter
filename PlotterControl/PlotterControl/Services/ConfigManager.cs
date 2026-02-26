// PlotterControl/PlotterControl/Services/ConfigManager.cs

using System;
using System.IO;
using System.Text.Json;
using PlotterControl.Models; // For PlotterConfig
using PlotterControl.Utils; // For Logger

namespace PlotterControl.Services
{
    public class ConfigManager
    {
        private const string ConfigFileName = "appsettings.json";
        private readonly string _configFilePath;

        public PlotterConfig CurrentConfig { get; private set; }

        public ConfigManager()
        {
            _configFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ConfigFileName);
            Load();
        }

        public void Load()
        {
            if (File.Exists(_configFilePath))
            {
                try
                {
                    string jsonString = File.ReadAllText(_configFilePath);
                    CurrentConfig = JsonSerializer.Deserialize<PlotterConfig>(jsonString);
                    Logger.Info("Configuration loaded successfully.");
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error loading configuration: {ex.Message}. Using default settings.", ex);
                    CurrentConfig = new PlotterConfig(); // Fallback to default
                }
            }
            else
            {
                Logger.Warning("Configuration file not found. Using default settings.");
                CurrentConfig = new PlotterConfig(); // Use default settings
            }
        }

        public void Save()
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                string jsonString = JsonSerializer.Serialize(CurrentConfig, options);
                File.WriteAllText(_configFilePath, jsonString);
                Logger.Info("Configuration saved successfully.");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error saving configuration: {ex.Message}", ex);
            }
        }

        public bool ExportConfig(string filePath)
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                string jsonString = JsonSerializer.Serialize(CurrentConfig, options);
                File.WriteAllText(filePath, jsonString);
                Logger.Info($"Configuration exported to {filePath}");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error($"Error exporting configuration: {ex.Message}", ex);
                return false;
            }
        }

        public bool ImportConfig(string filePath)
        {
            try
            {
                string jsonString = File.ReadAllText(filePath);
                var imported = JsonSerializer.Deserialize<PlotterConfig>(jsonString);
                if (imported != null)
                {
                    CurrentConfig = imported;
                    Save();
                    Logger.Info($"Configuration imported from {filePath}");
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                Logger.Error($"Error importing configuration: {ex.Message}", ex);
                return false;
            }
        }
    }
}
