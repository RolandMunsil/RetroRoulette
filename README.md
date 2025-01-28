# RetroRoulette

RetroRoulette is a tool for managing and playing retro games from various systems. It provides a user-friendly interface to browse, configure, and launch games using ImGui.NET and Veldrid.

## Features
- Browse and filter games by name, region, and other properties.
- Configure game settings and launch commands.
- Support for multiple game systems including MAME.
- Slot machine style random game selection.

## Requirements
- .NET 7.0 SDK
- Visual Studio 2022 or later
- ImGui.NET
- Veldrid

## Setup and Installation

1. **Clone the repository:**
    ```sh
    git clone https://github.com/yourusername/RetroRoulette.git
    cd RetroRoulette
    ```

2. **Open the solution:**
    Open `RetroRoulette.sln` in Visual Studio.

3. **Restore NuGet packages:**
    Visual Studio should automatically restore the required NuGet packages. If not, you can restore them manually:
    ```sh
    dotnet restore
    ```

4. **Build the project:**
    Build the solution in Visual Studio to ensure all dependencies are correctly installed.

## Running the Application

1. **Configure the application:**
    - Ensure you have the necessary ROM files and directories set up.
    - Edit the configuration file `rr_config.txt` if needed.

2. **Run the application:**
    - Set `RetroRoulette` as the startup project in Visual Studio.
    - Press `F5` to start debugging or `Ctrl+F5` to run without debugging.

## Usage

- **Browser Tab:** Browse and filter games by name.
- **Roulette Tab:** Use the slot machine to randomly select a game.
- **Config Tab:** Configure game settings and launch commands.

## Contributing

Contributions are welcome! Please fork the repository and submit a pull request with your changes.

## License

This project is licensed under the MIT License. See the `LICENSE` file for details.
