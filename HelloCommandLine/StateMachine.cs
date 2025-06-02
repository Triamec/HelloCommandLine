// Copyright © 2025 Triamec Motion AG

using LinqToDB.Common;
using Triamec.Tam.Configuration;
using Triamec.Tam.Registers;
using Triamec.TriaLink;
using Triamec.TriaLink.Adapter;

// Rlid19 represents the register layout of drives of the current generation. A previous generation drive has layout 4.
using Axis = Triamec.Tam.Rlid19.Axis;

namespace Triamec.Tam.Samples {

    internal class StateMachine : IDisposable {

        State _state = State.AddTopology;

        TamTopology? _topology;
        TamSystem? _system;
        TamStation? _station;
        TamDevice? _device;
        TamAxis? _axis;

        bool _isSimulation;
        float _velocityMaximum;
        string? _unit;
        float _speedPercentage = 100f; // Default percentage of speed for the axis movement
        double _distance = 0.5 * Math.PI; // The distance to move when send a moving command.

        const string _offlineConfigurationPath = "HelloWorld.TAMcfg"; // The configuration file to seed the simulation.

        /// <summary>
        /// Main loop of the state machine. 
        /// Continuously executes the action associated with the current state by invoking the corresponding method.
        /// If an unknown state is encountered, logs a message and resets the state machine to the initial state.
        /// his method blocks and runs indefinitely until the process is terminated.
        /// </summary>
        public void StateHandler() {
            while (true) {
                switch (_state) {
                    case State.AddTopology:
                        AddTopology();
                        break;
                    case State.ChoseStation:
                        ChoseStation();
                        break;
                    case State.ChoseAxis:
                        ChoseAxis();
                        break;
                    case State.SetDistance:
                        SetDistance();
                        _state = State.ReceiveCommands;
                        break;
                    case State.ReceiveCommands:
                        ReceiveCommands();
                        break;
                    default:
                        throw new NotImplementedException($"State {_state} is not implemented.");
                }
            }
        }

        /// <summary>
        /// Initializes the TAM system topology based on user input, either in simulation mode or with connected hardware.
        /// Prompts the user to select the mode, sets up the corresponding system.
        /// Advances the state machine to the next state if successful; otherwise, displays an error message.
        /// </summary>
        void AddTopology() {

            _topology = new TamTopology();

            // User needs to decide whether to use the simulation or the real hardware.
            Console.WriteLine("\nYou can run the application with a connected motor or a simulation. Please select an option by entering the corresponding number:");
            Console.WriteLine("(0): Simulation (default)");
            Console.WriteLine("(1): Connected Motor");

            if (GetAndCheckNumberInput(1, "Please enter a valid number (0 or 1)") == 0) {
                _isSimulation = true;
            } else {
                _isSimulation = false;
            }

            // The Tam System is added.
            if (_isSimulation == true) {

                Console.WriteLine("\nSimulated Topology is being created. Please wait... ");

                string executablePath = AppDomain.CurrentDomain.BaseDirectory;
                using (var deserializer = new Deserializer()) {

                    // Load and add a simulated TAM system as defined in the .TAMcfg file.
                    deserializer.Load(Path.Combine(executablePath, _offlineConfigurationPath));
                    var adapters = CreateSimulatedTriaLinkAdapters(deserializer.Configuration).First();
                    _system = _topology.ConnectTo(adapters.Key, adapters.ToArray());

                    // Boot the Tria-Link so that it learns about connected stations.
                    _system.Identify();
                }
                // Load a TAM configuration.
                // This API doesn't feature GUI. Refer to the Gear Up! example which uses an API exposing a GUI.
                _topology.Load(_offlineConfigurationPath);
            } else {
                Console.WriteLine("\nAdd Topology. Please wait...");

                // Add the local TAM system on this PC to the topology
                _system = _topology.AddLocalSystem();

                // Boot the Tria-Link so that it learns about connected stations
                _system.Identify();

                // Don't load TAM configuration, assuming that the drive is already configured,
                // for example since parametrization is persisted in the drive.
            }

            if (_system != null) { _state = State.ChoseStation; } else {
                Console.WriteLine("No system found. Try again...");
            }
        }

        /// <summary>
        /// Finds all connected stations in the current TAM system and prompts the user to select one if multiple stations are available.
        /// Updates the state machine to <c>ChoseAxis</c> if at least one station is connected; otherwise, restarts the topology setup.
        /// </summary>
        void ChoseStation() {
            TamStation[]? stations;

            // Find all connected stations
            // The AsDepthFirst extension method performs a tree search an returns all instances of type TamStation.
            stations = _system.AsDepthFirst<TamStation>().ToArray();

            if (stations.IsNullOrEmpty()) {
                Console.WriteLine("\nNo station found. System is restarting...\n");
                _state = State.AddTopology;
                return;
            }

            // More than one station found. User needs to decide which one to use.
            if (stations.Length > 1) {
                // Print all found stations
                Console.WriteLine("\nMore than one station was found. Please select one by entering the corresponding number:");
                for (int i = 0; i < stations.Length; i++) {
                    Console.WriteLine($"({i}): {stations[i].Name}");
                }

                // Check if input is valid and overwrite _stations[0] with the selected station
                int stationIndex = GetAndCheckNumberInput(stations.Length, "Invalid input. Please enter a number of a station.");
                _station = stations[stationIndex];
            } else {
                _station = stations[0];
            }


            _device = _station?.AsDepthFirst<TamDevice>().FirstOrDefault(); // From this point on, we work with devices (TamDevice) instead of stations (TamStation)

            if (_device != null) {
                _device.AddStateObserver(this); // Add a state observer to the device, if it exists. Is for example needed for axis operations such as .WaitForSuccess()
                if (_isSimulation == true) _device.ResetFault(); // Simulation always starts up with LinkNotReady error, which we acknowledge.
                _state = State.ChoseAxis;
            } else {
                _state = State.AddTopology;
                Console.WriteLine("\nCouldn't connect to a device. System is restarting...\n");
                return;
            }
        }

        /// <summary>
        /// Finds all axes of the selected station and prompts the user to select one by number.
        /// Connects to the selected axis, takes control of it, and resets any simulation faults if necessary.        
        /// Reads and caches axis parameters such as maximum velocity and position unit from the hardware configuration.
        /// Advances the state machine to <c>AxisDisabled</c> if an axis is connected; otherwise, restarts the topology setup.
        /// </summary>
        void ChoseAxis() {
            TamAxis[]? axes;

            // Find all axes of the selected station (drive)
            // The AsDepthFirstLeaves extension method performs a tree search an returns all instances of type TamAxis.
            axes = _device.AsDepthFirst<TamAxis>().ToArray();

            if (axes.IsNullOrEmpty()) {
                Console.WriteLine("\nNo axis found. Check the drive configurations. System is restarting...\n");
                _state = State.AddTopology;
                return;
            }

            // User needs to decide to which axis the application should connect
            Console.WriteLine("\nPlease select an axis by entering the corresponding number:");
            for (int i = 0; i < axes.Length; i++) {
                Console.WriteLine($"({i}): {axes[i].Name}");
            }

            // Check if input is valid
            int numberInput = GetAndCheckNumberInput(axes.Length, "Invalid input. Please enter a number of an axis.");

            // Connect to the selected axis
            _axis = axes[numberInput];

            if (_axis == null) {
                _state = State.AddTopology;
                Console.WriteLine("\nAxis couldn't connect. Check the drive configurations. System is restarting...\n");
                return;
            }

            // Most drives get integrated into a real time control system. Accessing them via TAM API like we do here is considered
            // a secondary use case. Tell the axis that we're going to take control. Otherwise, the axis might reject our commands.
            // You should not do this, though, when this application is about to access the drive via the PCI interface.
            _axis.ControlSystemTreatment.Override(enabled: true);

            //// Get the register layout of the axis
            //// and cast it to the RLID-specific register layout.
            var register = (Axis)_axis!.Register;

            // Cache the position unit.
            _unit = register.Parameters.PositionController.PositionUnit.Read().ToString();

            // Read and cache the original velocity maximum value,
            // which was applied from the configuration file.
            _velocityMaximum = register.Parameters.PathPlanner.VelocityMaximum.Read();



            _state = State.ReceiveCommands;
        }

        /// <summary>
        /// Displays available commands to the user based on the axis state (disabled or enabled),
        /// reads and validates the user's input, and executes the selected command.
        /// </summary>
        void ReceiveCommands() {
            var register = (Axis)_axis!.Register;
            if (register.Signals.General.AxisState.Read() <= AxisState.Disabled) {
                Console.WriteLine("\nPlease enter a command: ");
                Console.WriteLine("(0): Enable Axis (recommended)");
                Console.WriteLine("(1): Change Speed");
                Console.WriteLine("(2): Change Axis");
                Console.WriteLine("(3): Restart System");

                int index = GetAndCheckNumberInput(3, "Invalid input. Please enter the number of the corresponding command");
                switch (index) {
                    case 0:
                        ExecuteCommand(Commands.EnableAxis);
                        break;
                    case 1:
                        ExecuteCommand(Commands.ChangeSpeed);
                        break;
                    case 2:
                        ExecuteCommand(Commands.ChangeAxis);
                        break;
                    case 3:
                        ExecuteCommand(Commands.RestartSystem);
                        break;
                }
            } else {
                Console.WriteLine("\nPlease enter a command: ");
                Console.WriteLine($"(0): Move left ({_distance:F4} {_unit})");
                Console.WriteLine($"(1): Move right({_distance:F4} {_unit})");
                Console.WriteLine("(2): Disable Axis");
                Console.WriteLine("(3): Change Speed");
                Console.WriteLine("(4): Change Axis");
                Console.WriteLine("(5): Restart System");

                int index = GetAndCheckNumberInput(5, "Invalid input. Please enter the number of the corresponding command");

                switch (index) {
                    case 0:
                        ExecuteCommand(Commands.MoveLeft);
                        break;
                    case 1:
                        ExecuteCommand(Commands.MoveRight);
                        break;
                    case 2:
                        ExecuteCommand(Commands.DisableAxis);
                        break;
                    case 3:
                        ExecuteCommand(Commands.ChangeSpeed);
                        break;
                    case 4:
                        ExecuteCommand(Commands.ChangeAxis);
                        break;
                    case 5:
                        ExecuteCommand(Commands.RestartSystem);
                        break;
                }
            }
        }

        /// <summary>
        /// If not in simulation mode, prompts the user to enter a movement distance, validates the input, and updates the internal distance value.
        /// </summary>
        void SetDistance() {
            // User needs to define a distance to move when sending a moving command, if application is not in simulation mode.

            Console.WriteLine("\nPlease enter a distance to move the axis, when sending a corresponding command.");
            Console.WriteLine("The value must be in the unit of the axis, which is: " + _unit);

            // Get and validate input and distance
            do {
                ClearInputBuffer(); // Clear the input buffer to avoid reading leftover characters from previous inputs
                string? stringInput = Console.ReadLine()?.Trim();
                if (double.TryParse(stringInput, out _distance)) {
                    break;
                } else {
                    Console.WriteLine("Invalid input. Please enter a number (double).");
                }
            } while (true);
        }

        /// <summary>
        /// Executes the specified command for the axis.
        /// The <paramref name="command"/> parameter determines the action to perform (restart system, enable/disable axis, change speed, move left, move right).
        /// Updates the state machine accordingly after executing the command.
        /// </summary>
        /// <param name="command">The command to execute.</param>
        void ExecuteCommand(Commands command) {
            try {
                switch (command) {
                    case Commands.RestartSystem:
                        Dispose();
                        _state = State.AddTopology;
                        break;
                    case Commands.ChangeAxis:
                        DisableAxis();
                        _axis?.Dispose();
                        _state = State.ChoseAxis;
                        break;
                    case Commands.EnableAxis:
                        EnableAxis();
                        ShowPosition();
                        _state = State.SetDistance;
                        break;
                    case Commands.DisableAxis:
                        DisableAxis();
                        _state = State.ReceiveCommands;
                        break;
                    case Commands.ChangeSpeed:
                        ChangeSpeedPercentage();
                        break;
                    case Commands.MoveLeft:
                        MoveAxis(-1);
                        ShowPosition();
                        break;
                    case Commands.MoveRight:
                        MoveAxis(1);
                        ShowPosition();
                        break;
                }
            } catch (TamException e) {
                Console.WriteLine($"\nTamException Error: {e.Message}");
                Console.WriteLine($"\nCommand {command} couldn't be executed.");
                if (_state >= State.ReceiveCommands) {
                    _state = State.ReceiveCommands;
                    if (_axis != null) { DisableAxis(); }
                    Console.WriteLine("Axis is automatically disabled.");
                }
                Console.WriteLine("Check your configurations and try again or restart the system.\n");
            }
        }

        #region commandsToAxis

        void EnableAxis() {
            if (_axis!.Drive.Station.Link.Adapter.IsSimulated) {
                // [LEGACY] Set the drive operational, i.e. switch the power section on
                _axis.Drive.SwitchOn();
            }
            // Reset any axis error and enable the axis controller.
            _axis.Control(AxisControlCommands.ResetErrorAndEnable);
        }

        void DisableAxis() {
            Console.WriteLine("\nDisabled axis...");
            // Disable the axis controller.
            _axis!.Control(AxisControlCommands.Disable);

            if (_axis!.Drive.Station.Link.Adapter.IsSimulated) {
                // [LEGACY] Switch the power section off.
                _axis!.Drive.SwitchOff();
            }
        }

        void MoveAxis(int sign) {

            TimeSpan timeout = TimeSpan.FromSeconds(10);
            // Move a distance with dedicated velocity.
            // If the axis is just moving, it is reprogrammed with this command.
            // Please note that in offline mode, the velocity parameter is ignored.
            // Does not continue, until MoveRelative is terminated => WaitForSuccess
            _axis!.MoveRelative(Math.Sign(sign) * _distance, _velocityMaximum * _speedPercentage * 0.01f).WaitForSuccess(timeout);
        }

        void ChangeSpeedPercentage() {

            // User can change the percentage of the maximum speed
            Console.WriteLine("\nAt what percentage of the maximum speed should the motor operate? Please enter the desired percentage:");
            do {
                ClearInputBuffer(); // Clear the input buffer to avoid reading leftover characters from previous inputs
                string? stringInput = Console.ReadLine()?.Trim();
                if (float.TryParse(stringInput, out _speedPercentage) && _speedPercentage >= 0 && _speedPercentage <= 100) {
                    break;
                } else {
                    Console.WriteLine("Invalid Input. Please enter a number between 0 and 100.");
                }
            } while (true);
        }

        void ShowPosition() {
            var register = (Axis)_axis!.Register;

            // Read the current position of the axis.
            var position = register.Signals.PositionController.MasterPosition.Read();
            Console.WriteLine($"\nNew position: {position:F4} {_unit}");
        }

        #endregion commandsToAxis

        /// <summary>
        /// Validates user input to ensure it is a number and within the specified range.
        /// Repeats the prompt until a valid input is received.
        /// </summary>
        /// <param name="maxNumber"> The exclusive upper limit for valid input (0 to maxNumber) </param>
        /// <param name="errMessage"> The error message displayed when the input is invalid.</param>
        /// <returns>The valid number entered by the user</returns>
        int GetAndCheckNumberInput(int maxNumber, string errMessage = "Invalid Input.") {
            do {
                ClearInputBuffer(); // Clear the input buffer to avoid reading leftover characters from previous inputs
                string? stringInput = Console.ReadLine()?.Trim();
                if (int.TryParse(stringInput, out int numberInput) && numberInput >= 0 && numberInput <= maxNumber) {
                    return numberInput;
                } else {
                    Console.WriteLine(errMessage);
                }
            } while (true);
        }

        void ClearInputBuffer() {
            while (Console.KeyAvailable) { Console.ReadKey(true); } // Clear the input buffer by reading all available keys
        }

        /// <summary>
        /// Creates simulated Tria-Link adapters from a specified configuration.
        /// </summary>
        /// <param name="configuration">The configuration.</param>
        /// <returns>The newly created simulated Tria-Link adapters.</returns>
        static IEnumerable<IGrouping<Uri, ITriaLinkAdapter>> CreateSimulatedTriaLinkAdapters(
            TamTopologyConfiguration configuration) =>

            // This call must be in this extra method such that the Tam.Simulation library is only loaded
            // when simulating. This happens when this method is jitted because the SimulationFactory is the first
            // symbol during runtime originating from the Tam.Simulation library.
            SimulationFactory.FromConfiguration(configuration, null);

        public void Dispose() {
            // Reset parameters
            _distance = 0.5 * Math.PI;
            _speedPercentage = 50f;

            if (_axis != null) {
                try {
                    DisableAxis();
                } catch (TamException ex) {
                    Console.WriteLine($"\nAn error occurred while disabling the axis: \n{ex.Message}");
                }
            }

            // Dispose of the topology and all it's components if not null
            _topology?.Dispose();
        }
    }

    public enum State {
        AddTopology,
        ChoseStation,
        ChoseAxis,
        SetDistance,
        ReceiveCommands
    }

    /// <summary>
    /// Represents the available commands that can be executed by the user in the state machine (in State Axis Disabled and Axis Enabled).
    /// Each command corresponds to a specific action for controlling the TAM system:
    /// <list type="bullet">
    /// <item><term>RestartSystem</term><description>Restart the system and return to the initial state.</description></item>
    /// <item><term>EnableDisableAxis</term><description>Enable or disable the selected axis, depending on its current state.</description></item>
    /// <item><term>ChangeSpeed</term><description>Change the speed (as a percentage of the maximum) for axis movement.</description></item>
    /// <item><term>MoveLeft</term><description>Move the axis to the left by the configured distance.</description></item>
    /// <item><term>MoveRight</term><description>Move the axis to the right by the configured distance.</description></item>
    /// </list>
    /// </summary>
    public enum Commands {
        MoveLeft,
        MoveRight,
        EnableAxis,
        DisableAxis,
        ChangeSpeed,
        ChangeAxis,
        RestartSystem
    }
}
