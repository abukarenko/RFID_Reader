using System.Globalization;
using System.IO.Ports;
using System.Text.RegularExpressions;
using System.Windows;

namespace RFIDReader.Desktop;

public partial class MainWindow : Window
{
    private const int BaudRate = 115200;
    private SerialPort? _serialPort;

    public MainWindow()
    {
        InitializeComponent();
        RefreshPorts();
    }

    protected override void OnClosed(EventArgs e)
    {
        Disconnect();
        base.OnClosed(e);
    }

    private void RefreshPortsButton_Click(object sender, RoutedEventArgs e) => RefreshPorts();

    private void RefreshPorts()
    {
        var selectedPort = PortComboBox.SelectedItem as string;
        var ports = SerialPort.GetPortNames().OrderBy(port => port).ToArray();

        PortComboBox.ItemsSource = ports;
        PortComboBox.SelectedItem = ports.Contains(selectedPort) ? selectedPort : ports.FirstOrDefault();
        StatusTextBlock.Text = ports.Length == 0 ? "No serial ports found." : "Select the Arduino port and connect.";
    }

    private void ConnectButton_Click(object sender, RoutedEventArgs e)
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
            _serialPort = new SerialPort(portName, BaudRate)
            {
                NewLine = "\n",
                ReadTimeout = 3000,
                WriteTimeout = 3000
            };
            _serialPort.Open();

            ConnectButton.Content = "Disconnect";
            ReadButton.IsEnabled = true;
            WriteButton.IsEnabled = true;
            StatusTextBlock.Text = $"Connected to {portName} at {BaudRate} baud.";
        }
        catch (Exception ex)
        {
            Disconnect();
            ShowError($"Could not connect: {ex.Message}");
        }
    }

    private void ReadButton_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetBlockAndKey(out var block, out var key))
        {
            SendCommand($"READ {block} {key}");
        }
    }

    private void WriteButton_Click(object sender, RoutedEventArgs e)
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

        SendCommand($"WRITE {block} {key} {data}");
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

        return true;
    }

    private void SendCommand(string command)
    {
        if (_serialPort?.IsOpen != true)
        {
            ShowError("Connect to the Arduino first.");
            return;
        }

        try
        {
            _serialPort.DiscardInBuffer();
            _serialPort.WriteLine(command);
            var response = _serialPort.ReadLine().Trim();

            ResponseTextBox.Text = response;
            StatusTextBlock.Text = response.StartsWith("OK", StringComparison.Ordinal) ? "Command completed." : "The reader returned an error.";

            if (response.StartsWith("OK READ ", StringComparison.Ordinal))
            {
                var parts = response.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 5)
                {
                    UidTextBox.Text = parts[2];
                    DataTextBox.Text = parts[4];
                }
            }
        }
        catch (TimeoutException)
        {
            ShowError("The reader did not answer. Place a card on the RC522 and try again.");
        }
        catch (Exception ex)
        {
            ShowError($"Serial communication failed: {ex.Message}");
        }
    }

    private void Disconnect()
    {
        if (_serialPort is not null)
        {
            if (_serialPort.IsOpen)
            {
                _serialPort.Close();
            }

            _serialPort.Dispose();
            _serialPort = null;
        }

        ConnectButton.Content = "Connect";
        ReadButton.IsEnabled = false;
        WriteButton.IsEnabled = false;
        StatusTextBlock.Text = "Disconnected.";
    }

    private void ShowError(string message)
    {
        StatusTextBlock.Text = message;
        MessageBox.Show(message, "RFID RC522 Reader", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private static string NormalizeHex(string value) => Regex.Replace(value, "[^0-9A-Fa-f]", string.Empty).ToUpperInvariant();
}
