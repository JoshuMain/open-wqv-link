# Troubleshooting

Start with **Pico setup -> 3. Test it works**. It tells you which half is failing: sending or receiving.

## No Pico found

- Use a USB **data** cable. Charge-only cables are common.
- Try another USB port, and avoid unpowered hubs.
- A brand-new or blank Pico has no serial port until it's flashed. Use the BOOTSEL method in
  [setup](setup.md#2-firmware).
- Close anything else that might be using the port (Arduino IDE serial monitor, PuTTY, another copy of
  Open WQV Link).

## Receive test: nothing arrives from the TV remote

In order of likelihood:

1. **RST is low.** The Click is held in reset. Check the GP3 -> RST wire, or tie RST to 3V3. You should
   measure ~3.3 V on RST (This is what got me)
2. **TX and RX swapped.** Click **TX** must go to **GP1**, and Click **RX** to **GP0**.
3. **JP1** isn't on 3V3.
4. **Power**: check the 3V3 and GND wires.
5. Some remotes use radio or Bluetooth rather than infrared. Try another one.

## Send test: no flicker on the phone camera

- Use the phone's **front** camera. Many rear cameras have an infrared filter.
- If the **receive** test passes, the Click is powered and wired correctly, so a failed send check is
  most likely just the camera.
- Otherwise, check GP0 -> Click RX.

## The watch never answers

- The watch must be in **IR -> COM -> PC**, not just IR mode.
- Distance **5-10 cm**, infrared windows facing each other. Closer isn't always better.
- The watch gives up after about **2 minutes**. Select PC again and press Start straight away.
- A **low battery** makes infrared unreliable - Test the coin battery using a multimeter. 
- Bright **sunlight** or some **fluorescent/LED lights** interfere. Shade the watch and the board.

## The download stops part-way

- Keep the watch still. Resting both on a table helps.
- Retries are automatic. If it still fails, click **Try again**.
- **Show trace log** opens `session.log`. Include it if you report a problem.

## Checksum errors

The data is arriving but corrupted. Improve alignment, move a little closer or further, and shade from
bright light.

## Linux: permission denied opening the port

```bash
sudo usermod -aG dialout $USER
```

Then log out and back in.

## Reporting a problem

Please include:
- what you were doing and what happened;
- `session.log` from the download folder, if there is one;
- your OS, and which Pico board you're using.
