/*
  wqv_bridge.ino - USB CDC <-> UART bridge for Raspberry Pi Pico + MikroE IrDA 3 Click
  --------------------------------------------------------------------------------
  Board core : "Raspberry Pi Pico/RP2040" by Earle F. Philhower (Arduino Boards Manager)
  Board      : Raspberry Pi Pico

  Wiring (Pico physical pin -> Click silkscreen label):
    GP0  / pin 1  (UART0 TX)  -> RX   (right column, 4th from top)
    GP1  / pin 2  (UART0 RX)  <- TX   (right column, 3rd from top)
    GP3  / pin 5              -> RST  (left column, 2nd from top)
    3V3(OUT) / pin 36         -> 3V3  (left column, 7th)   JP1 in the 3V3 (left) position
    GND  / pin 38             -> GND  (either GND)

  The Click's MCP2122 is clocked by a 1.8432 MHz oscillator, so the IR side is
  always 115200 8N1. The USB side ignores the baud rate the PC asks for.
  All protocol logic lives on the PC (the Open WQV Link app) - this is a dumb pipe.
*/
#include <Arduino.h>

static const uint8_t  PIN_TX  = 0;
static const uint8_t  PIN_RX  = 1;
static const uint8_t  PIN_RST = 3;
static const uint32_t IR_BAUD = 115200;   // fixed by the Click's oscillator

static uint32_t lastActivity = 0;

void setup() {
  pinMode(LED_BUILTIN, OUTPUT);

  // Hold the MCP2122 in reset until its clock is stable (datasheet requirement)
  pinMode(PIN_RST, OUTPUT);
  digitalWrite(PIN_RST, LOW);

  Serial1.setTX(PIN_TX);
  Serial1.setRX(PIN_RX);
  Serial1.setFIFOSize(8192);   // plenty of room for a whole image burst
  Serial1.begin(IR_BAUD);

  Serial.begin(115200);        // USB CDC - rate is ignored

  delay(20);
  digitalWrite(PIN_RST, HIGH); // MCP2122 running
}

void loop() {
  uint8_t buf[256];

  int n = Serial.available();                  // PC -> IR
  if (n > 0) {
    n = Serial.readBytes(buf, min(n, (int)sizeof(buf)));
    Serial1.write(buf, n);
    lastActivity = millis();
  }

  n = Serial1.available();                     // IR -> PC
  if (n > 0) {
    n = Serial1.readBytes(buf, min(n, (int)sizeof(buf)));
    Serial.write(buf, n);
    lastActivity = millis();
  }

  // Onboard LED flickers whenever bytes pass in either direction
  digitalWrite(LED_BUILTIN, (millis() - lastActivity) < 40 ? HIGH : LOW);
}
