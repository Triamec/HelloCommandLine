// Copyright © 2025 Triamec Motion AG

namespace Triamec.Tam.Samples {
    internal class Program {

        static StateMachine app;
        static void Main(string[] args) {

            app = new StateMachine();

            Console.CancelKeyPress += OnExit;
            AppDomain.CurrentDomain.ProcessExit += OnExit;

            try {
                app.StateHandler();
            }
            catch(TamException ex) {
                Console.WriteLine($"\nAn error occurred: {ex.Message}");
                Console.WriteLine($"\nPlease check the drive's configurations, close the application and try again or start a simulation.");
            } finally {
                app.Dispose();
            }
        }

        static void OnExit(object sender, EventArgs e) {
            Console.WriteLine("Disposing resources...");
            app?.Dispose();
        }
    }
}
