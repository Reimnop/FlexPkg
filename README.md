# FlexPkg

**FlexPkg** is a tool designed to automatically manage updates for Unity IL2CPP applications on Steam, generate BepInEx IL2CPP interop assemblies for modding, and package them into NuGet packages. It handles the entire process from detecting updates to pushing the generated assemblies to NuGet.

## Features

- **Watch for Updates**: Watches Steam for updates to applications.
- **Download and Build**: Automatically downloads apps and generates BepInEx IL2CPP interop assemblies.
- **NuGet Packaging**: Packages generated assemblies into a NuGet package and pushes them to a configured NuGet repository.
- **Discord Interface**: Interact with FlexPkg via a Discord bot.
- **Database**: Stores update information in a choice of databases (SQLite, MySQL, PostgreSQL).
- **Dockerized**: Available as a Docker container.
- **Configurable**: Configure via a JSON file and CLI arguments.

## Getting Started

### Prerequisites

- Docker (if using the Docker container)
- Steam account with access to the desired application
- NuGet API key or your own NuGet repository
- Discord server and bot token
- A database (SQLite, MySQL, PostgreSQL)

### Installation

1. **Create the Configuration File**:
   Create a directory where you would like to store persistent data for your container, then create a `config.json` file in the directory according to the JSON schema.

2. **Start the Docker Container**:
   Run the Docker container using the [reimnop/flexpkg](https://hub.docker.com/r/reimnop/flexpkg) image. Mount a volume to `/app/userdata` to store your persistent data directory. Run the following command:

   ```bash
   docker run -d -v /path/to/your/host/userdata:/app/userdata reimnop/flexpkg
   ```

   Replace `/path/to/your/host/userdata` with the path where you would like to store the persistent data on your host machine.

### Configuration

Configuration schema and examples can be found in the [config.schema.json](FlexPkg/config.schema.json) and [config.example.json](FlexPkg/config.example.json) files, respectively.

### Usage

Once configured, FlexPkg will:

1. Monitor Steam for updates to the specified Unity IL2CPP application.
2. Notify via Discord when an application update is detected.
3. Use `/addmanifest` to begin downloading and processing the new update.
4. Generate BepInEx IL2CPP interop assemblies.
5. Package the generated assemblies into a NuGet package.
6. Push the package to the configured NuGet repository.
7. Notify via Discord using the configured webhook settings.

### License

FlexPkg is licensed under the GNU General Public License (GPL) v3.0. See the [LICENSE](LICENSE) file for details.

## Contributing

Contributions are welcome! Please feel free to submit issues, fork the repository, and send pull requests.

## Similar Projects

- [NuGet-GameLib-Dehumidifier](https://github.com/Lordfirespeed/NuGet-GameLib-Dehumidifier)

## Contact

For any inquiries or issues, please reach out via GitHub Issues or the contact information provided in the package configuration.