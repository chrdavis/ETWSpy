# ETWSpy - Event Tracing Monitor

ETWSpy is a Windows desktop application for monitoring and capturing Event Tracing for Windows (ETW) events in real-time. It provides a user-friendly interface for configuring ETW providers, filtering events, and analyzing captured data.

![.NET 8](https://img.shields.io/badge/.NET-8.0-blue)
![Windows](https://img.shields.io/badge/Platform-Windows-lightgrey)
![License](https://img.shields.io/badge/License-MIT-green)

<img width="1173" height="703" alt="image" src="https://github.com/user-attachments/assets/1e46f62a-fe7a-49aa-b42a-35ac2e46c7c2" />


## Features

- **Real-time ETW Event Capture**: Monitor ETW events from any registered provider on your system
- **Provider Configuration**: Add and configure multiple ETW providers with custom keywords, trace levels, and trace flags
- **Event Filtering**: Filter events by Event ID or text matching with include/exclude logic
- **Dark/Light Theme**: Supports both dark and light themes, with an option to follow system preferences
- **Export Capabilities**: Export captured events to CSV format for further analysis
- **Save/Load Configuration**: Save your provider and filter configurations to `.etwspy` files and reload them later
- **Session Restore**: Optionally restore your previous session configuration on launch
- **High-Performance**: Optimized for high-volume event capture with batching and virtualized UI

## Requirements

- Windows 10 or later
- .NET 8.0 Runtime
- **Administrator privileges** (required for ETW tracing)

## Installation

### From Release

1. Download the latest release from the [Releases](https://github.com/chrdavis/ETWSpy/releases) page
2. Run the installer or extract the portable version
3. Launch ETWSpy as Administrator

### Building from Source

1. Clone the repository:
   ```bash
   git clone https://github.com/chrdavis/ETWSpy.git
   ```
2. Open `ETWSpy.slnx` in Visual Studio 2022 or later
3. Build the solution
4. Run the `ETWSpyUI` project

## Getting Started

### Basic Usage

1. **Launch as Administrator**: ETWSpy requires elevated privileges to capture ETW events

2. **Add Providers**: 
   - Go to **Events ? Providers...** or click the Providers toolbar button
   - Search for and select an ETW provider from the list
   - Configure optional settings:
     - **Keywords**: Filter specific event categories (hexadecimal, e.g., `0xFFFFFFFF` for all)
     - **Trace Level**: Set verbosity (Verbose, Informational, Warning, Error, Critical)
     - **Trace Flags**: Additional provider-specific flags
   - Click **Add Provider**

3. **Configure Filters** (Optional):
   - Go to **Events ? Filters...** or click the Filters toolbar button
   - Select a provider and filter category:
     - **Event Id**: Filter by specific event IDs (e.g., `1,2,3` or `1-10` for ranges)
     - **Match Text**: Filter by case-insensitive text matching in event/task names
   - Choose **Include** or **Exclude** logic
   - Click **Add Filter**

4. **Start Capturing**:
   - Click the **Play** button in the toolbar or go to **File ? Start event capture**
   - Events will appear in the main grid as they are received

5. **Analyze Events**:
   - Double-click an event to view detailed properties
   - Select events and press **Ctrl+C** to copy
   - Use **File ? Export events...** to save to CSV

### Keyboard Shortcuts

| Shortcut | Action |
|----------|--------|
| `Ctrl+O` | Open configuration file |
| `Ctrl+S` | Save configuration file |
| `Ctrl+C` | Copy selected events |
| `Alt+F4` | Exit application |

## Configuration Files

ETWSpy saves configurations as `.etwconfig` files (JSON format) containing:
- Provider configurations (provider name/GUID, keywords, trace level, flags)
- Filter rules (provider, filter type, value, include/exclude logic)

You can associate `.etwconfig` files with ETWSpy via **View ? Associate .etwconfig Files**.

## Settings

### View Menu Options

- **Theme**: Choose Light, Dark, or Use System Theme
- **Time Format**: Display timestamps in Local or UTC time
- **Max Events to Show**: Limit displayed events (1,000 / 10,000 / 100,000)
- **Autoscroll**: Automatically scroll to new events
- **Restore Session on Launch**: Remember your last configuration

## Troubleshooting

### "No ETW trace sessions are available"
Windows has a limited number of concurrent ETW trace sessions. Close other tracing tools or stop some existing trace sessions in Windows Performance Monitor and try again.

<img width="983" height="703" alt="image" src="https://github.com/user-attachments/assets/19964887-5770-48c3-bec7-39815cf37a12" />


### Events not appearing
- Ensure you're running as Administrator
- Verify the provider is generating events
- Check that your filters aren't excluding all events
- Ensure event capture is started (Play button should show Pause icon)

### High memory usage
- Reduce the **Max Events to Show** setting
- Clear events periodically during long capture sessions
- Use filters to capture only relevant events

## Project Structure

- **ETWSpyUI**: WPF desktop application
- **ETWSpyLib**: Core ETW tracing library
- **ETWSpyLib.Tests**: Unit tests
- **ETWSpyInstaller**: Installer projects (WiX, Inno Setup)
- **ETWSpyPackage**: MSIX packaging project

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## Acknowledgments

- Built with [krabsetw library](https://github.com/microsoft/krabsetw) for ETW functionality

