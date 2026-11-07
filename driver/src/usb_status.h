// Printer status read by the plug-in itself, over the printer's usbprint device interface (the same
// way the label tool's QlDevice.cs does it).
//
// Used once per job, before anything of the job is written, and only for black-and-red jobs: a
// two-colour job on a black-only roll stops the printer with an error that needs the power button, so such a job is printed in black instead.
#pragma once

#include <array>
#include <cstdint>
#include <string>
#include <vector>

namespace ql {

using StatusBytes = std::array<uint8_t, 32>;

// A status reply (message type 0) for a loaded roll that prints black only: byte 25 (text colour)
// is 0x81 for the black/red roll DK-22251, 0x01 for black rolls. False for anything else (no
// reply, no roll, red/black roll), so that an unclear answer never changes the job.
inline bool BlackOnlyRoll(const StatusBytes& s) {
    return s[0] == 0x80 && s[1] == 0x20 && s[18] == 0x00 && s[10] != 0 && s[25] != 0x81;
}

// Port "USB001" -> the usbprint device interface path, or "" (not USB, not connected).
std::wstring UsbInterfaceOfPort(const wchar_t* port);

// Opens the interface, drains the messages the printer sent by itself, sends ESC i S and waits up
// to timeoutMs for the reply. All messages read (the reply last) go to `messages`. True if a reply
// arrived. Never throws; fails quietly (e.g. a printer in an error state answers nothing).
bool QueryUsbStatus(const std::wstring& path, std::vector<StatusBytes>& messages, StatusBytes& reply, int timeoutMs);

}  // namespace ql
