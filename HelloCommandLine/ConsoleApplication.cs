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
    internal class ConsoleApplication: IDisposable {

        TamTopology? _topology;
        TamSystem? _system;
        TamStation[]? _stations;
        TamAxis[]? _axes;
        TamAxis? _axis;

        int numberInput;
        string? stringInput;
        float _velocityMaximum;
        string? _unit;
        float speed = 50f; // Default speed value for the axis movement

        /// <summary>
        /// The distance to move when send a moving command.
        /// </summary>
        // CAUTION!
        // The unit of this constant depends on the PositionUnit parameter provided with the TAM configuration.
        // Additionally, the encoder must be correctly configured.
        // Consider any limit stops.
        double Distance = 0.5 * Math.PI;

        /// <summary>
        /// Whether to use a (rather simplified) simulation of the axis.
        /// </summary>
        bool _offline = true;

        /// <summary>
        /// The configuration file to seed the simulation.
        /// </summary>
        const string OfflineConfigurationPath = "HelloWorld.TAMcfg";


        public void StartUp() {

            // Create the root object representing the topology of the TAM hardware.
            _topology = new TamTopology();


            // User needs to decide whether to use the simulation or the real hardware.
            Console.WriteLine("You can run the application with a connected motor or a simulation. Please select an option by entering the corresponding number:");
            Console.WriteLine("(0): Simulation (default)");
            Console.WriteLine("(1): Connected Motor");

            numberInput = GetAndCheckNumberInput(1, "Invalid input. Please enter 0 or 1.");

            // The Tam System is added.
            if (numberInput == 0) {
                _offline = true;
                Console.WriteLine("\nSimulation is being created. Please wait... ");


                string executablePath = AppDomain.CurrentDomain.BaseDirectory;
                using (var deserializer = new Deserializer()) {

                    // Load and add a simulated TAM system as defined in the .TAMcfg file.
                    deserializer.Load(Path.Combine(executablePath, OfflineConfigurationPath));
                    var adapters = CreateSimulatedTriaLinkAdapters(deserializer.Configuration).First();
                    _system = _topology.ConnectTo(adapters.Key, adapters.ToArray());

                    // Boot the Tria-Link so that it learns about connected stations.
                    _system.Identify();
                }
                // Load a TAM configuration.
                // This API doesn't feature GUI. Refer to the Gear Up! example which uses an API exposing a GUI.
                _topology.Load(OfflineConfigurationPath);

            } else {
                _offline = false;
                Console.WriteLine("\nConnecting to the connected drives. Please wait...");

                // Add the local TAM system on this PC to the topology
                _system = _topology.AddLocalSystem();

                // Boot the Tria-Link so that it learns about connected stations
                _system.Identify();

                // Don't load TAM configuration, assuming that the drive is already configured,
                // for example since parametrization is persisted in the drive.

            }

            // Find all connected stations (drives)
            // The AsDepthFirst extension method performs a tree search an returns all instances of type TamStation.
            _stations = _system.AsDepthFirst<TamStation>().ToArray();

            // More than one station (drive) found. User needs to decide which one to use.
            if (_stations.Length > 1) {

                // Print all found stations (drives)
                Console.WriteLine("\nMore than one station was found. Please select one by entering the corresponding number:");
                for (int i = 0; i < _stations.Length; i++) {
                    Console.WriteLine($"({i}): {_stations[i].Name}");
                }

                // Check if input is valid
                numberInput = GetAndCheckNumberInput(_stations.Length, "Invalid input. Please enter a number of a station.");

            } else numberInput = 0;

            // Find all axes of the selected station (drive)
            // The AsDepthFirstLeaves extension method performs a tree search an returns all instances of type TamAxis.
            _axes = _stations[numberInput].AsDepthFirst<TamAxis>().ToArray();

            // User needs to decide to which axis the application should connect
            Console.WriteLine("\nPlease select an axis by entering the corresponding number:");
            for (int i = 0; i < _axes.Length; i++) {
                Console.WriteLine($"({i}): {_axes[i].Name}");
            }

            // Check if input is valid
            numberInput = GetAndCheckNumberInput(_axes.Length, "Invalid input. Please enter a number of an axis.");


            // Connect to the selected axis
            _axis = _axes[numberInput];
            

            // Most drives get integrated into a real time control system. Accessing them via TAM API like we do here is considered
            // a secondary use case. Tell the axis that we're going to take control. Otherwise, the axis might reject our commands.
            // You should not do this, though, when this application is about to access the drive via the PCI interface.
            _axis.ControlSystemTreatment.Override(enabled: true);

            // Simulation always starts up with LinkNotReady error, which we acknowledge.
            if (_offline) _axis.Drive.ResetFault();

            // Get the register layout of the axis
            // and cast it to the RLID-specific register layout.
            var register = (Axis)_axis.Register;



            // Read and cache the original velocity maximum value,
            // which was applied from the configuration file.
            _velocityMaximum = register.Parameters.PathPlanner.VelocityMaximum.Read();

            // Cache the position unit.
            _unit = register.Parameters.PositionController.PositionUnit.Read().ToString();

            if (!_offline) {
                // User needs to define a distance to move when sending a moving command.
                Console.WriteLine("\nPlease enter a distance to move the axis, when sending a corresponding command.");
                Console.WriteLine("The value must be in the unit of the axis, which is: " + _unit);
                while (true) {
                    stringInput = Console.ReadLine()?.Trim();
                    if (double.TryParse(stringInput, out Distance)) {
                        break;
                    } else {
                        Console.WriteLine("Invalid input. Please enter a number (double).");
                    }
                }
            }
        }

        public void CommandLoop() {
            bool enabled = false;
            while (true) {
                Console.WriteLine("\nPlease enter a command: ");
                if (!enabled) {
                    Console.WriteLine("(0): Enable Axis (recommended)");
                    Console.WriteLine("(1): Change Speed");

                    numberInput = GetAndCheckNumberInput(1, "Invalid input. Please enter the number of the corresponding command");
                } else {
                    Console.WriteLine("(0): Disable Axis");
                    Console.WriteLine("(1): Change Speed");
                    Console.WriteLine($"(2): Move left ({Distance} {_unit})");
                    Console.WriteLine($"(3): Move right({Distance} {_unit})");
                    numberInput = GetAndCheckNumberInput(4, "Invalid input. Please enter the number of the corresponding command");
                }

                switch (numberInput) {
                    case 0:
                        
                        if (enabled == false) {
                            enabled = true;
                            EnableAxis();
                        } else { 
                            enabled = false;
                            DisableAxis();

                            
                        }
                        break;
                    case 1:
                        Console.WriteLine("\nAt what percentage of the maximum speed should the motor operate? Please enter the desired percentage:");
                        float tmpPercentage;
                        do {
                            stringInput = Console.ReadLine()?.Trim();
                            if(float.TryParse(stringInput, out speed) && speed >= 0 && speed <= 100) {
                                break;
                            } else {
                                Console.WriteLine("Invalid Input. Please enter a number between 0 and 100.");
                            }
                        } while (true);
                        break;
                    case 2:
                        MoveAxis(-1);
                        break;
                    case 3:
                        MoveAxis(1);
                        break;
                    default:
                        Console.WriteLine("Invalid command. Please try again");
                        break;
                }
            }

        }

        private void EnableAxis() {
            if (_axis.Drive.Station.Link.Adapter.IsSimulated) {

                // [LEGACY] Set the drive operational, i.e. switch the power section on
                _axis.Drive.SwitchOn();
            }

            // Reset any axis error and enable the axis controller.
            _axis.Control(AxisControlCommands.ResetErrorAndEnable);

            ShowPosition();
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
            _axis.MoveRelative(Math.Sign(sign) * Distance, _velocityMaximum * speed * 0.01f);
            
            ShowPosition();
        }

        /// <summary>
        /// Measures the axis position and shows it in the CLI.
        /// </summary>
        private void ShowPosition() {
            var register = (Axis)_axis.Register;

            // Wait until the axis is in standstill.
            while (true) {

                if(register.Signals.General.AxisState.Read() == Registers.AxisState.Standstill) {
                    break;
                }
            }
            Thread.Sleep(2);
            var position = register.Signals.PositionController.MasterPosition.Read();
            Console.WriteLine($"\nNew position: {position} {_unit}");
        }

        /// <summary>
        /// Validates user input to ensure it is a number and within the specified range.
        /// Repeats the prompt until a valid input is received.
        /// </summary>
        /// <param name="maxNumber"> The exclusive upper limit for valid input (0 to maxNumber) </param>
        /// <param name="errMessage"> The error message displayed when the input is invalid.</param>
        /// <returns>The valid number entered by the user</returns>
        private int GetAndCheckNumberInput(int maxNumber, string errMessage) {

            do {
                stringInput = Console.ReadLine()?.Trim();
                if (int.TryParse(stringInput, out numberInput) && numberInput >= 0 && numberInput <= maxNumber) {
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
            // Dispose of the topology and system objects to release resources.
            _topology?.Dispose();
            _system?.Dispose();
            // Dispose of the axis object to release resources.
            _axis?.Dispose();
            // Dispose of the stations and axes arrays to release resources.
            foreach (var station in _stations) {
                station.Dispose();
            }
            foreach (var axis in _axes) {
                axis.Dispose();
            }
        }

    }
}
