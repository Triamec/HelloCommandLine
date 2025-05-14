// Copyright © 2025 Triamec Motion AG

namespace Triamec.Tam.Samples {
    internal class Program {

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        /// <param name="args"></param>
        static void Main(string[] args) {

            using StateMachine app = new StateMachine();

            try {
                app.StateHandler();
            }
            catch(TamException ex) {
                Console.WriteLine($"\nAn error occurred: {ex.Message}");
                Console.WriteLine($"\nPlease check the drive's configurations, close the application and try again or start a simulation.");
            }
        }
    }
}
