using System.Globalization;
using System.IO;
using System.IO.Ports;
using System.Text.RegularExpressions;
using System.Windows;

namespace RFIDReader.Desktop;

public partial class MainWindow : Window
{
    private const int BaudRate = 115200;
    private static readonly string SettingsPath = Path.Combine(AppContext.BaseDirectory, "settings.ini");

    private readonly ReaderSettings _settings = ReaderSettings.Load(SettingsPath);
    private readonly SemaphoreSlim _commandLock = new(1, 1);
    private SerialPort? _serialPort;
    private bool _isClosing;

    public MainWindow()
    {
        InitializeComponent();

        BlockTextBox.Text = _settings.Block;
        KeyTextBox.Text = _settings.Key;
        DataTextBox.Text = _settings.Data;
        RefreshPorts(_settings.Port);
        SaveSettings();
    }

    protected override void OnClosed(EventArgs e)
    {
        _isClosing = true;
        SaveSettings();
        Disconnect();
        base.OnClosed(e);
    }

    private void RefreshPortsButton_Click(object sender, RoutedEventArgs e) => RefreshPorts();

    private void PortComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => SaveSettings();

    private void SettingsInput_LostFocus(object sender, RoutedEventArgs e) => SaveSettings();

    private void RefreshPorts(string? preferredPort = null)
    {
        var selectedPort = preferredPort ?? PortComboBox.SelectedItem as string ?? _settings.Port;
        var ports = SerialPort.GetPortNames().OrderBy(port => port).ToArray();

        PortComboBox.ItemsSource = ports;
        PortComboBox.SelectedItem = ports.Contains(selectedPort) ? selectedPort : ports.FirstOrDefault();
        StatusTextBlock.Text = ports.Length == 0 ? "No serial ports found." : "Select the Arduino port and connect.";
    }

    private async void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        if (_serialPort?.IsOpen == true)
        {
            Disconnect();
            return;
        }

        if (PortComboBox.SelectedItem is not string portName)
        {
            ShowError("Select a serial port first.");
            return;
        }

        try
        {
            ConnectButton.IsEnabled = false;
            _serialPort = new SerialPort(portName, BaudRate)
            {
                NewLine = "\n",
                ReadTimeout = Timeout.Infinite,
                WriteTimeout = 3000
            };
            _serialPort.Open();

            StatusTextBlock.Text = "Waiting for Arduino to start...";
            await Task.Delay(1200);

            if (_serialPort?.IsOpen != true)
            {
                return;
            }

            _serialPort.DiscardInBuffer();
            ConnectButton.Content = "Disconnect";
            ReadButton.IsEnabled = true;
            WriteButton.IsEnabled = true;
            SaveSettings();
            StatusTextBlock.Text = $"Connected to {portName} at {BaudRate} baud.";
        }
        catch (Exception ex)
        {
            Disconnect();
            ShowError($"Could not connect: {ex.Message}");
        }
        finally
        {
            if (_serialPort?.IsOpen == true)
            {
                ConnectButton.IsEnabled = true;
            }
        }
    }

    private async void ReadButton_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetBlockAndKey(out var block, out var key))
        {
            await SendCommandAsync($"READ {block} {key}");
        }
    }

    private async void WriteButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetBlockAndKey(out var block, out var key))
        {
            return;
        }

        var data = NormalizeHex(DataTextBox.Text);
        if (data.Length != 32)
        {
            ShowError("Data must contain exactly 16 bytes (32 hexadecimal characters).");
            return;
        }

        DataTextBox.Text = data;
        SaveSettings();
        await SendCommandAsync($"WRITE {block} {key} {data}");
    }

    private bool TryGetBlockAndKey(out int block, out string key)
    {
        block = 0;
        key = NormalizeHex(KeyTextBox.Text);

        if (!int.TryParse(BlockTextBox.Text, NumberStyles.None, CultureInfo.InvariantCulture, out block) || block < 0 || block > 63)
        {
            ShowError("Block must be a number from 0 to 63.");
            return false;
        }

        if (block == 0 || block % 4 == 3)
        {
            ShowError("Manufacturer and sector trailer blocks are protected.");
            return false;
        }

        if (key.Length != 12)
        {
            ShowError("Key A must contain 6 bytes (12 hexadecimal characters).");
            return false;
        }

        BlockTextBox.Text = block.ToString(CultureInfo.InvariantCulture);
        KeyTextBox.Text = key;
        SaveSettings();
        return true;
    }

    private async Task SendCommandAsync(string command)
    {
        var port = _serialPort;
        if (port?.IsOpen != true)
        {
            ShowError("Connect to the Arduino first.");
            return;
        }

        if (!await _commandLock.WaitAsync(0))
        {
            return;
        }

        try
        {
            ReadButton.IsEnabled = false;
            WriteButton.IsEnabled = false;
            ResponseTextBox.Text = "Waiting for a card...";
            StatusTextBlock.Text = "Waiting for a card. You can disconnect to cancel.";

            var response = await Task.Run(() =>
            {
                port.DiscardInBuffer();
                port.WriteLine(command);
                return port.ReadLine().Trim();
            });

            if (!ReferenceEquals(port, _serialPort))
            {
                return;
            }

            ResponseTextBox.Text = response;
            StatusTextBlock.Text = response.StartsWith("OK", StringComparison.Ordinal) ? "Command completed." : "The reader returned an error.";

            if (response.StartsWith("OK READ ", StringComparison.Ordinal))
            {
                var parts = response.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 5)
                {
                    UidTextBox.Text = parts[2];
                    DataTextBox.Text = parts[4];
                    SaveSettings();
                }
            }
        }
        catch (Exception ex) when (ex is TimeoutException or InvalidOperationException or IOException)
        {
            if (ReferenceEquals(port, _serialPort) && !_isClosing)
            {
                ShowError($"Serial communication failed: {ex.Message}");
            }
        }
        finally
        {
            if (ReferenceEquals(port, _serialPort) && port.IsOpen)
            {
                ReadButton.IsEnabled = true;
                WriteButton.IsEnabled = true;
            }

            _commandLock.Release();
        }
    }

    private void Disconnect()
    {
        var port = _serialPort;
        _serialPort = null;

        if (port is not null)
        {
            if (port.IsOpen)
            {
                port.Close();
            }

            port.Dispose();
        }

        ConnectButton.IsEnabled = true;
        ConnectButton.Content = "Connect";
        ReadButton.IsEnabled = false;
        WriteButton.IsEnabled = false;
        StatusTextBlock.Text = "Disconnected.";
    }

    private void SaveSettings()
    {
        _settings.Port = PortComboBox?.SelectedItem as string ?? _settings.Port;
        _settings.Block = BlockTextBox?.Text ?? _settings.Block;
        _settings.Key = KeyTextBox is null ? _settings.Key : NormalizeHex(KeyTextBox.Text);
        _settings.Data = DataTextBox is null ? _settings.Data : NormalizeHex(DataTextBox.Text);
        _settings.Save(SettingsPath);
    }

    private void ShowError(string message)
    {
        StatusTextBlock.Text = message;
        MessageBox.Show(message, "RFID RC522 Reader", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private static string NormalizeHex(string value) => Regex.Replace(value, "[^0-9A-Fa-f]", string.Empty).ToUpperInvariant();
}
