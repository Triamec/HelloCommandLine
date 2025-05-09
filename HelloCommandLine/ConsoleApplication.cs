using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

// Rlid19 represents the register layout of drives of the current generation. A previous generation drive has layout 4.
using Axis = Triamec.Tam.Rlid19.Axis;

namespace Triamec.Tam.Samples {
    internal class ConsoleApplication {

        TamTopology _topology;
        TamSystem _system;
        TamStation[] _stations;
        TamAxis[] _axes;
        TamAxis _axis;
        bool _offline;
        int numberInput;
        string stringInput;
        float _velocityMaximum;
        string _unit;

        public void StartUp() {

            // Create the root object representing the topology of the TAM hardware.
            _topology = new TamTopology();


            // User needs to decide whether to use the simulation or the real hardware.
            Console.WriteLine("You can run the application with a connected motor or a simulation. Please select an option by entering the corresponding number:");
            Console.WriteLine("(0): Simulation (default)");
            Console.WriteLine("(1): Connected Motor");

            numberInput = CheckNumberInput(1, "Invalid input. Please enter 0 or 1.");

            // The Tam System is added.
            if (numberInput == 0 ) {
                _offline = true;
                Console.WriteLine("Simulation is being created. Please wait... \n ");

                // TODO: Add Simulation

            } else {
                _offline = false;
                Console.WriteLine("\nConnecting to the connected drives. Please wait...\n");

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
            if (_stations.Length>1) {

                // Print all found stations (drives)
                Console.WriteLine("More than one station was found. Please select one by entering the corresponding number:");
                for (int i = 0; i < _stations.Length; i++) {
                    Console.WriteLine($"({i}): {_stations[i].Name}");
                }

                // Check if input is valid
                numberInput = CheckNumberInput(_stations.Length, "Invalid input. Please enter a number of a station.");

            } else numberInput = 0;

            // Find all axes of the selected station (drive)
            // The AsDepthFirstLeaves extension method performs a tree search an returns all instances of type TamAxis.
            _axes = _stations[numberInput].AsDepthFirst<TamAxis>().ToArray();

            // User needs to decide to which axis the application should connect
            Console.WriteLine("Every Drive has two axis. Please select one by entering the corresponding number:");
            for (int i = 0; i < _axes.Length; i++) {
                Console.WriteLine($"({i}): {_axes[i].Name}");
            }

            // Check if input is valid
            numberInput = CheckNumberInput(_axes.Length, "Invalid input. Please enter a number of an axis.");


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

        }

        /// <summary>
        /// Validates user input to ensure it is a number and within the specified range.
        /// Repeats the prompt until a valid input is received.
        /// </summary>
        /// <param name="maxNumber"> The exclusive upper limit for valid input (0 to maxNumber) </param>
        /// <param name="errMessage"> The error message displayed when the input is invalid.</param>
        /// <returns>The valid number entered by the user</returns>
        private int CheckNumberInput(int maxNumber, string errMessage) {

            do {
                stringInput = Console.ReadLine()?.Trim();
                if (int.TryParse(stringInput, out numberInput) && numberInput >= 0 && numberInput <= maxNumber) {
                    return numberInput;
                } else {
                    Console.WriteLine(errMessage);
                }
            } while (true);

        }
         
    }
}
