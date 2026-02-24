using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using PlotterControl.Utils;

namespace PlotterControl.Services
{
    public class TemplateManager
    {
        private readonly ConfigManager _configManager;
        private Dictionary<string, object> _templates;

        public TemplateManager(ConfigManager configManager)
        {
            _configManager = configManager;
            _templates = new Dictionary<string, object>();
        }

        public List<string> GetAvailableTemplates()
        {
            return new List<string>(_templates.Keys);
        }
    }
}
