using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Triamec.Tam.Configuration;
using Triamec.TriaLink;
using Triamec.TriaLink.Adapter;

// Rlid19 represents the register layout of drives of the current generation. A previous generation drive has layout 4.
using Axis = Triamec.Tam.Rlid19.Axis;

namespace Triamec.Tam.Samples {

    internal class StateMachine : IDisposable {

        private State _state = State.AddTopology;
        private readonly Dictionary<State, Action> _stateActions;

        TamTopology? _topology;
        TamSystem? _system;
        TamStation? _station;
        TamAxis? _axis;

        bool _isSimulation;
        float _velocityMaximum;
        string? _unit;
        float _speed = 50f; // Default speed value for the axis movement
        double _distance = 0.5 * Math.PI; /// The distance to move when send a moving command.

        /// <summary>
        /// The configuration file to seed the simulation.
        /// </summary>
        const string _offlineConfigurationPath = "HelloWorld.TAMcfg";

        public StateMachine() {
            // Initialize the state actions dictionary with the corresponding methods for each state.
            _stateActions = new Dictionary<State, Action> {
                { State.AddTopology, ExecuteState_AddTopology },
                { State.ChoseStation, ExecuteState_ChoseStation },
                { State.ChoseAxis, ExecuteState_ChoseAxis },
                { State.SetDistance, ExecuteState_SetDistance },
                { State.AxisDisabled, ExecuteState_AxisDisabled },
                { State.AxisEnabled, ExecuteState_AxisEnabled }
            };
        }

        /// <summary>
        /// Main loop of the state machine. Continuously executes the action associated with the current state
        /// by invoking the corresponding method from the <c>_stateActions</c> dictionary.
        /// If an unknown state is encountered, logs a message and resets the state machine to the initial state.
        /// </summary>
        public void StateHandler() {
            while (true) {
                if(_stateActions.TryGetValue(_state, out var action)) {
                    action();
                }
                // Restart the state machine if an unknown state is encountered.
                else {
                    Console.WriteLine($"State {_state} not found.");
                    _state = State.AddTopology; 
                }
            }
        }

        /// <summary>
        /// Initializes the TAM system topology based on user input, either in simulation mode or with connected hardware.
        /// Prompts the user to select the mode, sets up the corresponding system.
        /// Advances the state machine to the next state if successful; otherwise, displays an error message.
        /// </summary>
        private void ExecuteState_AddTopology() {

            Dispose();
            _topology = new TamTopology();

            // User needs to decide whether to use the simulation or the real hardware.
            Console.WriteLine("\nYou can run the application with a connected motor or a simulation. Please select an option by entering the corresponding number:");
            Console.WriteLine("(0): Simulation (default)");
            Console.WriteLine("(1): Connected Motor");

            if (GetAndCheckNumberInput(1, "Please enter a valid number (0 or 1)") == 0) {
                _isSimulation = true;
            } else { _isSimulation = false; }

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
        /// Updates the state machine to <c>ChoseAxis</c> if at least one station is found; otherwise, restarts the topology setup.
        /// </summary>
        private void ExecuteState_ChoseStation() {
            TamStation[]? _stations;

            // Find all connected stations
            // The AsDepthFirst extension method performs a tree search an returns all instances of type TamStation.
            _stations = _system.AsDepthFirst<TamStation>().ToArray();

            // More than one station found. User needs to decide which one to use.
            if (_stations.Length > 1) {

                int stationIndex = 0;

                // Print all found stations
                Console.WriteLine("\nMore than one station was found. Please select one by entering the corresponding number:");
                for (int i = 0; i < _stations.Length; i++) {
                    Console.WriteLine($"({i}): {_stations[i].Name}");
                }

                // Check if input is valid and overwrite _stations[0] with the selected station
                stationIndex = GetAndCheckNumberInput(_stations.Length, "Invalid input. Please enter a number of a station.");
                _station = _stations[stationIndex];
            } 
            else if (_stations.Length == 1) _station = _stations[0];

            if (_station != null) { _state = State.ChoseAxis; } else {
                _state = State.AddTopology;
                Console.WriteLine("\nNo station found. System is restarting...\n");
            }
        }

        /// <summary>
        /// Finds all axes of the selected station and prompts the user to select one by number.
        /// Connects to the selected axis, takes control of it, and resets any simulation faults if necessary.
        /// Advances the state machine to <c>SetParameters</c> if an axis is selected; otherwise, restarts the topology setup.
        /// </summary>
        private void ExecuteState_ChoseAxis() {
            TamAxis[]? _axes;

            // Find all axes of the selected station (drive)
            // The AsDepthFirstLeaves extension method performs a tree search an returns all instances of type TamAxis.
            _axes = _station.AsDepthFirst<TamAxis>().ToArray();

            // User needs to decide to which axis the application should connect
            Console.WriteLine("\nPlease select an axis by entering the corresponding number:");
            for (int i = 0; i < _axes.Length; i++) {
                Console.WriteLine($"({i}): {_axes[i].Name}");
            }

            // Check if input is valid
            int numberInput = GetAndCheckNumberInput(_axes.Length, "Invalid input. Please enter a number of an axis.");


            // Connect to the selected axis
            _axis = _axes[numberInput];


            // Most drives get integrated into a real time control system. Accessing them via TAM API like we do here is considered
            // a secondary use case. Tell the axis that we're going to take control. Otherwise, the axis might reject our commands.
            // You should not do this, though, when this application is about to access the drive via the PCI interface.
            _axis.ControlSystemTreatment.Override(enabled: true);

            // Simulation always starts up with LinkNotReady error, which we acknowledge.
            if (_isSimulation) _axis.Drive.ResetFault();

            if (_axis != null) { _state = State.SetDistance; } else {
                _state = State.AddTopology;
                Console.WriteLine("\nNo axis found. System is restarting...\n");
            }
        }

        /// <summary>
        /// Reads and caches axis parameters such as maximum velocity and position unit from the hardware configuration.
        /// If not in simulation mode, prompts the user to enter a movement distance, validates the input, and updates the internal distance value.
        /// </summary>
        private void ExecuteState_SetDistance() {

            // Get the register layout of the axis
            // and cast it to the RLID-specific register layout.
            var register = (Axis)_axis.Register;

            // Cache the position unit.
            _unit = register.Parameters.PositionController.PositionUnit.Read().ToString();
            if(_unit == "unspecified") {
                Console.WriteLine("\nThe unit of the axis is unspecified. Please check the configurations or start with a simulation.");
                _state = State.AddTopology;
                return;
            }

            // Read and cache the original velocity maximum value,
            // which was applied from the configuration file.
            _velocityMaximum = register.Parameters.PathPlanner.VelocityMaximum.Read();

            // User needs to define a distance to move when sending a moving command, if application is not in simulation mode.
            if (!_isSimulation) {
                Console.WriteLine("\nPlease enter a distance to move the axis, when sending a corresponding command.");
                Console.WriteLine("The value must be in the unit of the axis, which is: " + _unit);

                // Get and validate input and distance
                do {
                    string? stringInput = Console.ReadLine()?.Trim();
                    if (double.TryParse(stringInput, out _distance)) {
                        break;
                    } else {
                        Console.WriteLine("Invalid input. Please enter a number (double).");
                    }
                } while (true);
            }

            _state = State.AxisDisabled;
        }

        /// <summary>
        /// Handles user interaction when the axis is currently disabled.
        /// Displays available commands to the user (restart system, enable axis, or change speed),
        /// reads and validates the user's input, and executes the selected command.
        /// </summary>
        private void ExecuteState_AxisDisabled() {
            Console.WriteLine("\nPlease enter a command: ");
            Console.WriteLine("(0): RestartSystem");
            Console.WriteLine("(1): Enable Axis (recommended)");
            Console.WriteLine("(2): Change Speed");

            Commands command = (Commands)GetAndCheckNumberInput(2, "Invalid input. Please enter the number of the corresponding command");
            ExecuteCommand(command, true);
        }

        /// <summary>
        /// Handles user interaction when the axis is currently enabled.
        /// Displays available commands to the user (restart system, disable axis, change speed, move left, move right),
        /// reads and validates the user's input, and executes the selected command.
        /// </summary>
        private void ExecuteState_AxisEnabled() {
            Console.WriteLine("\nPlease enter a command: ");
            Console.WriteLine("(0): RestartSystem");
            Console.WriteLine("(1): Disable Axis");
            Console.WriteLine("(2): Change Speed");
            Console.WriteLine($"(3): Move left ({_distance} {_unit})");
            Console.WriteLine($"(4): Move right({_distance} {_unit})");

            Commands command = (Commands)GetAndCheckNumberInput(4, "Invalid input. Please enter the number of the corresponding command");
            ExecuteCommand(command, false);
        }

        /// <summary>
        /// Executes the specified command for the axis, taking into account whether the axis is currently disabled or enabled.
        /// The <paramref name="command"/> parameter determines the action to perform (restart system, enable/disable axis, change speed, move left, move right).
        /// The <paramref name="axisIsDisabled"/> parameter indicates if the axis is currently disabled (true) or enabled (false),
        /// and controls whether the axis should be enabled or disabled when the corresponding command is selected.
        /// Updates the state machine accordingly after executing the command.
        /// </summary>
        /// <param name="command">The command to execute.</param>
        /// <param name="axisIsDisabled">Indicates whether the axis is currently disabled (true) or enabled (false).</param>
        private void ExecuteCommand(Commands command, bool axisIsDisabled) {

            switch (command) {

                case Commands.RestartSystem:
                    _state = State.AddTopology;
                    break;

                case Commands.EnableDisableAxis:
                    if (axisIsDisabled) {
                        EnableAxis();
                        ShowPosition();
                        _state = State.AxisEnabled;
                    } else {
                        DisableAxis();
                        _state = State.AxisDisabled;
                    }
                    break;

                case Commands.ChangeSpeed:
                    ChangeSpeed();
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
        }

        #region commandsToAxis

        private void EnableAxis() {
            if (_axis.Drive.Station.Link.Adapter.IsSimulated) {
                // [LEGACY] Set the drive operational, i.e. switch the power section on
                _axis.Drive.SwitchOn();
            }
            // Reset any axis error and enable the axis controller.
            _axis.Control(AxisControlCommands.ResetErrorAndEnable);
        }

        private void DisableAxis() {
            // Disable the axis controller.
            _axis.Control(AxisControlCommands.Disable);

            if (_axis.Drive.Station.Link.Adapter.IsSimulated) {

                // [LEGACY] Switch the power section off.
                _axis.Drive.SwitchOff();
            }
        }

        private void MoveAxis(int sign) {
            // Move a distance with dedicated velocity.
            // If the axis is just moving, it is reprogrammed with this command.
            // Please note that in offline mode, the velocity parameter is ignored.
            _axis.MoveRelative(Math.Sign(sign) * _distance, _velocityMaximum * _speed * 0.01f);

        }

        private void ChangeSpeed() {

            // User can change the percentage of the maximum speed
            Console.WriteLine("\nAt what percentage of the maximum speed should the motor operate? Please enter the desired percentage:");
            do {
                string? stringInput = Console.ReadLine()?.Trim();
                if (float.TryParse(stringInput, out _speed) && _speed >= 0 && _speed <= 100) {
                    break;
                } else {
                    Console.WriteLine("Invalid Input. Please enter a number between 0 and 100.");
                }
            } while (true);
        }

        private void ShowPosition() {
            var register = (Axis)_axis.Register;

            // Wait until the axis is in standstill.
            while (true) {

                if (register.Signals.General.AxisState.Read() == Registers.AxisState.Standstill) {
                    break;
                }
            }

            // Wait a bit to ensure that the axis is in standstill.
            Thread.Sleep(2);

            // Read the current position of the axis.
            var position = register.Signals.PositionController.MasterPosition.Read();
            Console.WriteLine($"\nNew position: {position} {_unit}");

        }

        #endregion commandsToAxis

        /// <summary>
        /// Validates user input to ensure it is a number and within the specified range.
        /// Repeats the prompt until a valid input is received.
        /// </summary>
        /// <param name="maxNumber"> The exclusive upper limit for valid input (0 to maxNumber) </param>
        /// <param name="errMessage"> The error message displayed when the input is invalid.</param>
        /// <returns>The valid number entered by the user</returns>
        private int GetAndCheckNumberInput(int maxNumber, string errMessage) {
            do {
                string? stringInput = Console.ReadLine()?.Trim();
                if (int.TryParse(stringInput, out int numberInput) && numberInput >= 0 && numberInput <= maxNumber) {
                    return numberInput;
                } else {
                    Console.WriteLine(errMessage);
                }
            } while (true);
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
            _speed = 50f;

            // Dispose of the topology, system objects, station objects and axis objects to release resources.
            _topology?.Dispose();
            _system?.Dispose();
            _station?.Dispose();
            _axis?.Dispose();
        }
    }

    public enum State {
        AddTopology,
        ChoseStation,
        ChoseAxis,
        SetDistance,
        AxisDisabled,
        AxisEnabled
    }

    public enum Commands {
        RestartSystem,
        EnableDisableAxis,
        ChangeSpeed,
        MoveLeft,
        MoveRight

    }
}
