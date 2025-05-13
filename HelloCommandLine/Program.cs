
using Triamec.Tam;

namespace Triamec.Tam.Samples {
    internal class Program {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        /// <param name="args"></param>

        static void Main(string[] args) {
            //using ConsoleApplication app = new ConsoleApplication();
            //app.StartUp();
            //app.CommandLoop();

            using StateMachine app = new StateMachine();
            app.StateHandler();
        }


    }
}
