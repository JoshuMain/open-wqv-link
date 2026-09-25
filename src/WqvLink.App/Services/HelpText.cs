namespace WqvLink.App.Services;

//Text for the Help menu
public static class HelpText
{
    public const string Wiring = """
        Raspberry Pi Pico  ->  MikroE IrDA 3 Click

          Pico (physical pin)     Click label
          ---------------------   -----------
          GP0,  pin 1             RX
          GP1,  pin 2             TX
          GP3,  pin 5             RST  (or tie RST to 3V3)
          3V3(OUT), pin 36        3V3
          GND,  pin 38            GND

        Set jumper JP1 on the Click to 3V3.

        TX and RX cross over: the Pico's transmit pin (GP0) goes to the Click's RX.

        If RST is low, the Click is held in reset and nothing works. With a multimeter
        you should see about 3.3 V on RST.

                              Pico (USB at top)
                           +------[USB]------+
          Click RX <- GP0  | 1            40 | VBUS
          Click TX -> GP1  | 2            39 | VSYS
                       GND | 3            38 | GND       -> Click GND
                       GP2 | 4            37 | 3V3_EN
         Click RST <- GP3  | 5            36 | 3V3(OUT)  -> Click 3V3
                           |       ...       |
                           +-----------------+
        """;

    public const string Watch = """
        Getting the watch ready

          1. On the WQV-1, press MODE until you reach IR mode.
          2. Choose COM, then PC.
          3. Hold the watch 5-10 cm from the Click, with the IR windows facing each other.
          4. Press "Get photos from watch" in Open WQV Link.

        Tips
          - The watch gives up after about 2 minutes of idling. Start it again if that happens.
          - A low battery makes infrared unreliable.
          - Bright sunlight or fluorescent light can interfere. Shade the watch and Click.
          - Downloads run at about 0.7 KB/s: 29 photos take about 5 minutes.
            Keep the watch still until it finishes.
        """;

    public const string Troubleshooting = """
        Troubleshooting

          Nothing received at all
            - RST held low: check the GP3 wire or tie RST to 3V3.
            - TX and RX swapped: GP0 must go to the Click's RX, GP1 to its TX.
            - JP1 must be in the 3V3 position.
            - Run Tests > Sniff and press a TV remote at the Click: any bytes mean the receiver works.

          The watch never answers
            - Check the watch is in IR > COM > PC and 5-10 cm away.
            - Run Tests > Blast IR and look at the Click through a phone's front camera.

          Checksum errors or stalls
            - Improve alignment, move closer, shade from bright light.

          Linux: permission denied
            - sudo usermod -aG dialout $USER, then log out and back in.
        """;

    public const string About = """
        Open WQV Link

        Downloads photos from the Casio WQV-1 wrist camera over infrared, using a
        Raspberry Pi Pico and a MikroElektronika IrDA 3 Click.

        Protocol documented by Marcus Gröber (mgroeber.de/wqvprot.html).
        Thanks to Kees Jongenburger.

        Licence: MIT (code), CERN-OHL-P (hardware).
        """;
}
