// PlotterControl/PlotterControl/ViewModels/SettingsVM.cs

using MaterialDesignThemes.Wpf;
using PlotterControl.Models;
using PlotterControl.Services;
using System.Threading.Tasks;
using System.Windows.Input;

namespace PlotterControl.ViewModels
{
    public class SettingsVM : BaseViewModel
    {
        private readonly ConfigManager _configManager;
        private readonly IPlotterService _plotterService;
        private PlotterConfig _editableConfig;

        public SettingsVM(ConfigManager configManager, IPlotterService plotterService)
        {
            _configManager = configManager;
            _plotterService = plotterService;

            PlotterConfig liveConfig = configManager.CurrentConfig;
            _editableConfig = new PlotterConfig
            {
                XMaxMM = liveConfig.XMaxMM,
                YMaxMM = liveConfig.YMaxMM,
                ZMaxMM = liveConfig.ZMaxMM,
                PenUpZ = liveConfig.PenUpZ,
                PenDownZ = liveConfig.PenDownZ,
                RapidFeedrate = liveConfig.RapidFeedrate,
                DrawFeedrate = liveConfig.DrawFeedrate,
                ZFeedrate = liveConfig.ZFeedrate,
                CalibrationOrigin = liveConfig.CalibrationOrigin,
                CalibrationWidth = liveConfig.CalibrationWidth,
                CalibrationHeight = liveConfig.CalibrationHeight,
                LastComPort = liveConfig.LastComPort,
                BaudRate = liveConfig.BaudRate,
                CommandTimeout = liveConfig.CommandTimeout,
                DefaultFont = liveConfig.DefaultFont,
                DefaultTemplate = liveConfig.DefaultTemplate,
                ShowGrid = liveConfig.ShowGrid,
                AutoConnect = liveConfig.AutoConnect,
                DarkTheme = liveConfig.DarkTheme
            };

            _isDarkTheme = liveConfig.DarkTheme;

            SaveSettingsCommand = new AsyncRelayCommand(SaveSettings, CanSaveSettings);
        }

        public PlotterConfig EditableConfig
        {
            get => _editableConfig;
            set => SetProperty(ref _editableConfig, value);
        }

        private string _statusMessage;
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        private bool _isDarkTheme;
        public bool IsDarkTheme
        {
            get => _isDarkTheme;
            set
            {
                if (SetProperty(ref _isDarkTheme, value))
                {
                    ApplyTheme(value);
                    EditableConfig.DarkTheme = value;
                }
            }
        }

        public static void ApplyTheme(bool isDark)
        {
            var paletteHelper = new PaletteHelper();
            var theme = paletteHelper.GetTheme();
            theme.SetBaseTheme(isDark ? BaseTheme.Dark : BaseTheme.Light);
            paletteHelper.SetTheme(theme);
        }

        public ICommand SaveSettingsCommand { get; }

        private bool CanSaveSettings() => true;

        private async Task SaveSettings()
        {
            _configManager.CurrentConfig.XMaxMM = EditableConfig.XMaxMM;
            _configManager.CurrentConfig.YMaxMM = EditableConfig.YMaxMM;
            _configManager.CurrentConfig.ZMaxMM = EditableConfig.ZMaxMM;
            _configManager.CurrentConfig.PenUpZ = EditableConfig.PenUpZ;
            _configManager.CurrentConfig.PenDownZ = EditableConfig.PenDownZ;
            _configManager.CurrentConfig.RapidFeedrate = EditableConfig.RapidFeedrate;
            _configManager.CurrentConfig.DrawFeedrate = EditableConfig.DrawFeedrate;
            _configManager.CurrentConfig.ZFeedrate = EditableConfig.ZFeedrate;
            _configManager.CurrentConfig.LastComPort = EditableConfig.LastComPort;
            _configManager.CurrentConfig.BaudRate = EditableConfig.BaudRate;
            _configManager.CurrentConfig.CommandTimeout = EditableConfig.CommandTimeout;
            _configManager.CurrentConfig.DefaultFont = EditableConfig.DefaultFont;
            _configManager.CurrentConfig.DefaultTemplate = EditableConfig.DefaultTemplate;
            _configManager.CurrentConfig.ShowGrid = EditableConfig.ShowGrid;
            _configManager.CurrentConfig.AutoConnect = EditableConfig.AutoConnect;
            _configManager.CurrentConfig.DarkTheme = EditableConfig.DarkTheme;

            _configManager.Save();
            StatusMessage = "Settings saved!";

            if (_plotterService.MachineState.IsConnected && _plotterService.MachineState.BaudRate != EditableConfig.BaudRate)
            {
                StatusMessage = "Baud rate changed. Please reconnect for changes to take effect.";
            }
        }
    }
}
