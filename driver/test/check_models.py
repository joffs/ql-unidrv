"""Checks that the hand-written model lists match MODELS in driver/gen/media.py: the INF (driver name -> USB hardware
ID) and install.ps1's $models (driver name and hardware ID, in MODELS order). A new model is easy to forget in one of
them. Reads only, changes nothing; exit 1 with the differences.

Not in driver/gen: everything there counts as driver source for gen/version.py's hash (a change there would mean a new
driver version). Run by tools/make-release.ps1; by hand:
    python -I driver/test/check_models.py
"""
import os
import re
import sys

DRIVER = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(DRIVER, "gen"))
from media import MODELS  # noqa: E402


def main():
    with open(os.path.join(DRIVER, "inf", "qlunidrv.inf"), encoding="ascii", errors="replace") as f:
        inf = f.read()
    names = dict(re.findall(r'^(\w+)="(Brother QL-[^"]+ \(Unidrv\))"', inf, re.M))
    inf_ids = {}
    for key, hwid in re.findall(r'^%(\w+)%=\w+,USBPRINT\\(\S+?),', inf, re.M):
        if key in names:
            inf_ids.setdefault(names[key], set()).add(hwid)
    with open(os.path.join(DRIVER, "install.ps1"), encoding="utf-8-sig") as f:
        ps = re.findall(r'@\{ Name = "([^"]+)"; Id = "USBPRINT\\([^"]+)" \}', f.read())
    drivers = [md["driver"] for md in MODELS]
    problems = []
    if [n for n, _ in ps] != drivers:
        problems.append("install.ps1 $models: " + ", ".join(n for n, _ in ps) + "\n  MODELS: " + ", ".join(drivers))
    for name, hwid in ps:
        if inf_ids.get(name) != {hwid}:
            problems.append(f"{name}: install.ps1 has USBPRINT\\{hwid}, the INF {sorted(inf_ids.get(name, []))}")
    missing = [d for d in drivers if d not in inf_ids]
    if missing:
        problems.append("not in the INF: " + ", ".join(missing))
    if problems:
        print("Model lists differ from MODELS (driver/gen/media.py):\n" + "\n".join(problems))
        return 1
    print(f"model lists: INF and install.ps1 match MODELS ({len(drivers)} models)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
