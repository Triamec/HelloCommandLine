# TAM HelloCommandLine
[![TAM - API](https://img.shields.io/static/v1?label=TAM&message=API&color=b51839)](https://www.triamec.com/en/tam-api.html)

This application example helps you getting started to command a Triamec drive. 

**Offline** (default): Run the application without a connected drive using a simulation.

**Connected**: Connect and move a real axis with a *Triamec* drive.

## Hardware Prerequisites

No hardware is needed when running the application as **Offline**.

To command a real axis, you need a *Triamec* drive with a motor and encoder connected and configured with a stable position controller. Connect the drive by *Tria-Link*, *USB* or *Ethernet*.

## Software Prerequisites

This project is made and built with [Microsoft Visual Studio](https://visualstudio.microsoft.com/en/).

In addition you need [TAM Software](https://www.triamec.com/en/tam-software-support.html) installation.


## Run the *Hello World!* Application

For the **Offline** mode, simply clone the repository, open the solution and hit run.

**Connected** mode:

1. Make sure the *TAM System Explorer* is not connected to the drive, or simply close it.
2. The Hardware needs to be configured correctly and needs to be connected.
3. Open the solution and hit run.

## Operate the *HelloCommandLine* Application

This application is a command-line tool that guides the user through the setup and control of a TAM (Triamec Automation Module) system using a state machine. The user can choose between running the application in simulation mode or with connected hardware. The application walks the user through the following steps:
1.	AddTopology: Initializes the system topology, either in simulation or with real hardware.
2.	ChoseStation: Detects all available stations and prompts the user to select one.
3.	ChoseAxis: Lists all axes of the selected station and lets the user choose an axis to control.
4.	SetDistance: User defines the distance applied to each move on the selected axis
5.	ReceiveCommands: The User can execute different commands depending, if axis is enabled or disabled:
    - Enable Axis, Disable Axis, Change Speed, Change Axis, Restart System, Move Left and Move Right

All user interactions and state transitions are managed by a state machine, ensuring a clear and guided workflow for configuring and controlling the TAM system.
