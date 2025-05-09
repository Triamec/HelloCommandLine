# TAM HelloCommandLine
[![TAM - API](https://img.shields.io/static/v1?label=TAM&message=API&color=b51839)](https://www.triamec.com/en/tam-api.html)

This application example helps you getting started to command a Triamec drive. 

**Offline** (default): Run the application without a connected drive using a simulation.

**Connected**: Connect and move a real axis with a *Triamec* drive.

## Hardware Prerequisites

No hardware is needed when running the application as **Offline**.

To command a real axis, you need a *Triamec* drive with a motor and encoder connected and configured with a stable position controller. Connect the drive by *Tria-Link*, *USB* or *Ethernet*.

## Konzept

### 1) Abfragen, ob ein Motor angeschlossen (Connected) oder nicht (offline)
falls Connected: Version -> Nach AxisName abfragen  |  Distanz-Konstante abfragen

### 2) StartUp Funktion => Wird automatisch ausgeführt 
- offline: eine vorgefertigte Konfiguration (HelloWorld.TAMcfg) kopieren verwenden
- Connected: mit FirstOrDefault (aber richtigem AxisName) verbinden
- genau gleich wie StartUp Funktion des HelloWorld (ohne Timer?)


### 3) Thread starten, der z.B. alle 5 Sekunden, die aktuelle Position auf die Konsole ausschreibt??
=> Wollen wir das überhaupt? oder soll einfach nach jeder Bewegung & Enable die Position ausgegeben werden

### 4) Konsole wartet mit ReadLine() auf Befehlseingabe in immerwiederholender Schleife 
Folgende Befehle gibt es: 
- Enable, Disable, Left, Right, Speed [0-100], exit

While(True)
{
if (disabled): -> Erklärungstext mit Enable, Speed und Exit
if (enabled): -> Erklärungstext mit allen Befehlen
switch Case 
}


=> eingegebnerer Input wird mit Switch-Case überprüft und dann die entsprechenden Befehle ausgeführt: 
- Enable => gemäss EnableAxis von HelloWorld
- Disable => gemäss DisableAxis von HelloWorld, überprüfen ob enabled
- Left => MoveAxis(-1) gemäss HelloWorld, überprüfen ob enabled
- Right => MoveAxis(+1) gemäss HelloWorld, überprüfen ob enabled
- Speed => _velocityMaximum (gemäss HelloWorld) wird geändert
- Exit  => gemäss OnFormClosed von HelloWorld => DisableAxis() und dann aus Schleife austreten (ist es wichtig das DisableAxis funktioniert?)

### => Exceptions werden einfach nur auf Konsole ausgegeben und nicht geloggt!
