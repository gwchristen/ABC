# AEP Barcode Companion (ABC)

A WPF desktop application for downloading barcode scan files from **Opticon OPN-200x** and **OPN-6000** family of barcode scanners over USB and Bluetooth.

## Supported Scanners

- OPN-2001, OPN-2002, OPN-2003, OPN-2004, OPN-2005, OPN-2006
- OPN-2500
- OPN-6000
- PX-20
- RS-3000

## Prerequisites

- **Windows 11** (Windows 10.0.22621.0 or later)
- **.NET 8 Runtime** — [Download](https://dotnet.microsoft.com/download/dotnet/8.0)
- **Opticon USB Drivers** — Install the drivers that come with your scanner or download from Opticon's website

## Building and Running

### Development (Mock Mode)

By default, the application runs in **Mock Mode** — no scanner hardware required. This is ideal for UI development and testing.

```bash
git clone https://github.com/gwchristen/ABC.git
cd ABC
dotnet build src/ABC/ABC.csproj
dotnet run --project src/ABC/ABC.csproj
```

Or open `ABC.slnx` in Visual Studio 2022 and press **F5**.

### With Real Scanner Hardware

To use the real Opticon scanner hardware, you must obtain and install the proprietary Opticon CSP2 SDK DLLs.

## Installing Opticon SDK DLLs

1. Download the Opticon OPN Companion SDK from:  
   **[https://github.com/OpticonOSEDevelopment/opn_companion_sdk](https://github.com/OpticonOSEDevelopment/opn_companion_sdk)**

2. From the SDK, copy the following DLL files into the application's output directory  
   (e.g., `src/ABC/bin/Debug/net8.0-windows10.0.22621.0/`):
   - `Csp2.dll`
   - `Csp2Ex.dll`
   - `Opticon.csp2.net.dll`
   - `Opticon.csp2Ex.net.dll`

3. The application automatically detects whether the DLLs are present at startup and switches between the real `OpticonScannerService` and the `MockScannerService`.

> **Note:** The Opticon DLLs are proprietary and must **not** be committed to this repository (they are listed in `.gitignore`).

## Usage

### Tab 1: USB Batch Download

1. Connect the scanner to your PC via USB.
2. Click **Detect** to auto-detect the COM port, or select it manually from the dropdown.
3. Click **Preview / Download** to read all barcodes stored in the scanner's memory.
4. Review the barcodes in the preview grid.
5. Choose a save directory, filename, and format (`.txt` or `.csv`).
6. Click **Save** to export. Optionally check **"Clear scanner data after save"** to wipe the scanner's memory after saving.

### Tab 2: Bluetooth Live Scan

1. Pair your scanner with your PC via Bluetooth (the scanner appears as a virtual COM port).
2. Select the Bluetooth COM port from the dropdown (click **↻** to refresh).
3. Click **Connect** to open the serial connection.
4. Scan barcodes — they appear in real-time in the live feed.
5. Click **Save** to export all captured barcodes to a file.
6. Click **Disconnect** when done.

## Project Structure

```
ABC.slnx
src/
  ABC/
    ABC.csproj                    (.NET 8 WPF project)
    App.xaml / App.xaml.cs
    MainWindow.xaml / MainWindow.xaml.cs
    Models/
      BarcodeEntry.cs             (Barcode data, timestamp, code type, scanner ID)
      ScannerInfo.cs              (Model, firmware, serial, battery, barcode count)
    ViewModels/
      ViewModelBase.cs            (INotifyPropertyChanged base)
      RelayCommand.cs             (ICommand implementation)
      MainViewModel.cs            (Main window VM, coordinates both tabs)
      UsbDownloadViewModel.cs     (USB batch download tab logic)
      BluetoothLiveViewModel.cs   (Bluetooth live scan tab logic)
    Services/
      IScannerService.cs          (Interface for USB batch scanner operations)
      OpticonScannerService.cs    (Real implementation using Opticon CSP2 .NET wrapper)
      MockScannerService.cs       (Mock for development/testing without hardware)
      IBluetoothScanService.cs    (Interface for Bluetooth SPP serial scanning)
      BluetoothScanService.cs     (Real implementation using System.IO.Ports.SerialPort)
      IFileExportService.cs       (Interface for saving barcode data to files)
      FileExportService.cs        (Saves as .txt or .csv)
    Views/
      UsbDownloadView.xaml        (UserControl for Tab 1)
      BluetoothLiveView.xaml      (UserControl for Tab 2)
    Converters/
      BoolToVisibilityConverter.cs
      InverseBoolConverter.cs
    Resources/
      Styles.xaml                 (Shared styles, colors, theming)
```

## File Export Formats

### `.txt` format
One barcode per line, no header:
```
0123456789012
9876543210987
5555555555555
```

### `.csv` format
```csv
Barcode,CodeType,Timestamp,ScannerID
0123456789012,EAN-13,2026-03-04 14:30:22,ABC12345
9876543210987,EAN-13,2026-03-04 14:30:25,ABC12345
5555555555555,Code128,2026-03-04 14:30:28,ABC12345
```

## License

See [LICENSE](LICENSE) for details.
