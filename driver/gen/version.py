"""The driver's version (driver\\VERSION), checked against its sources.

The driver takes the version of the release in which it last changed (labeltool\\version\\VERSION, x.y.0, shown as
"x.y"); driver\\VERSION records it with that change's DriverVer and a hash of the driver sources (gen, gpd, inf, src,
build.cmd). Identical sources give an identical driver: no per-build stamp. A release with a driver change raises the
programs' minor (0.19.0), then `version.py set` records the driver as 0.19.

  version.py build <obj dir>               version of this build -> <obj>\\driver_version.txt
                                           (sources as recorded: driver\\VERSION; changed: a development build,
                                           the programs' x.y with the build time as stamp, and a note)
  version.py stamp <obj dir> <driver dir>  DriverVer into <driver>\\qlunidrv.inf, *GPDFileVersion into the GPDs
  version.py check                         exit 1 when the sources differ from driver\\VERSION (make-release.ps1)
  version.py set                           records the sources as the driver of the coming release
  version.py show                          "0.18 0.18.304.1114" (release, DriverVer)
"""
import datetime
import hashlib
import os
import re
import sys

DRIVER = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
REPO = os.path.dirname(DRIVER)
VERSION = os.path.join(DRIVER, "VERSION")
APP_VERSION = os.path.join(REPO, "labeltool", "version", "VERSION")
SOURCES = ["build.cmd", "gen", "gpd", "inf", "src"]


def sources_hash():
    files = []
    for s in SOURCES:
        p = os.path.join(DRIVER, s)
        if os.path.isfile(p):
            files.append(s)
            continue
        for root, dirs, names in os.walk(p):
            dirs[:] = [d for d in dirs if d != "__pycache__"]
            for n in names:
                if not n.endswith(".pyc"):
                    files.append(os.path.relpath(os.path.join(root, n), DRIVER).replace("\\", "/"))
    h = hashlib.sha256()
    for f in sorted(files, key=str.lower):
        with open(os.path.join(DRIVER, f), "rb") as fh:
            data = fh.read()
        h.update(f.lower().encode() + b"\0" + str(len(data)).encode() + b"\0" + data)
    return h.hexdigest()


def read_recorded():
    values = {}
    with open(VERSION, encoding="ascii") as f:
        for line in f:
            line = line.strip()
            if line and not line.startswith("#") and "=" in line:
                k, v = line.split("=", 1)
                values[k.strip()] = v.strip()
    return values


def app_release():
    with open(APP_VERSION, encoding="ascii") as f:
        v = f.readline().strip()
    if not re.fullmatch(r"\d+\.\d+\.\d+", v):
        sys.exit("labeltool\\version\\VERSION must be major.minor.patch, not '%s'" % v)
    return v


def stamp_now(release):
    now = datetime.datetime.now()
    days = (now.date() - datetime.date(2026, 1, 1)).days
    return now.strftime("%m/%d/%Y"), "%s.%d.%d" % (release, days, now.hour * 60 + now.minute)


def current():
    """(release, date, DriverVer, development?) of the sources as they are"""
    rec = read_recorded()
    if rec.get("sources") == sources_hash():
        date, ver = rec["driverver"].split(",")
        return rec["version"], date, ver, False
    release = ".".join(app_release().split(".")[:2])
    date, ver = stamp_now(release)
    return release, date, ver, True


def write_text(path, text):
    with open(path, "w", encoding="ascii", newline="") as f:
        f.write(text)


def main(argv):
    cmd = argv[1] if len(argv) > 1 else ""
    if cmd == "build" and len(argv) == 3:
        release, date, ver, dev = current()
        if dev:
            print("Driver sources differ from driver\\VERSION: development build %s. For a release: raise "
                  "labeltool\\version\\VERSION to the next minor (x.y.0), run python driver\\gen\\version.py set "
                  "and commit." % ver)
        else:
            print("Driver %s (DriverVer %s)" % (release, ver))
        os.makedirs(argv[2], exist_ok=True)
        write_text(os.path.join(argv[2], "driver_version.txt"), "%s %s %s\n" % (release, date, ver))
        return 0
    if cmd == "stamp" and len(argv) == 4:
        with open(os.path.join(argv[2], "driver_version.txt"), encoding="ascii") as f:
            release, date, ver = f.read().split()
        inf = os.path.join(argv[3], "qlunidrv.inf")
        with open(inf, "rb") as f:
            text = f.read().decode("ascii")
        text, n = re.subn(r"(?m)^DriverVer=[^\r\n]*", "DriverVer=%s,%s" % (date, ver), text)
        if n != 1:
            sys.exit("DriverVer not found in " + inf)
        write_text(inf, text)
        for name in os.listdir(argv[3]):
            if name.upper().endswith(".GPD"):
                p = os.path.join(argv[3], name)
                with open(p, "rb") as f:
                    text = f.read().decode("latin-1")
                text, n = re.subn(r'(?m)^\*GPDFileVersion: "[^"\r\n]*"', '*GPDFileVersion: "%s"' % release, text)
                if n != 1:
                    sys.exit("*GPDFileVersion not found in " + p)
                with open(p, "wb") as f:
                    f.write(text.encode("latin-1"))
        return 0
    if cmd == "check":
        rec = read_recorded()
        if rec.get("sources") != sources_hash():
            print("The driver sources changed since driver\\VERSION (driver %s): a release with a driver change raises "
                  "labeltool\\version\\VERSION to the next minor (x.y.0); then run python driver\\gen\\version.py set "
                  "and commit." % rec.get("version"))
            return 1
        print("Driver sources as recorded: driver %s (DriverVer %s)" % (rec["version"], rec["driverver"].split(",")[1]))
        return 0
    if cmd == "set":
        app = app_release()
        if not app.endswith(".0"):
            sys.exit("A driver change goes with a new minor release: set labeltool\\version\\VERSION to x.y.0 first "
                     "(now %s)." % app)
        release = app[:-2]
        date, ver = stamp_now(release)
        write_text(VERSION,
                   "# The driver's version: the release in which it last changed, its DriverVer and a hash of its\r\n"
                   "# sources (driver\\gen\\version.py; written by \"version.py set\", not by hand).\r\n"
                   "version=%s\r\ndriverver=%s,%s\r\nsources=%s\r\n" % (release, date, ver, sources_hash()))
        print("driver\\VERSION: driver %s, DriverVer %s. Commit it with the driver change." % (release, ver))
        return 0
    if cmd == "show":
        rec = read_recorded()
        print(rec["version"], rec["driverver"].split(",")[1])
        return 0
    print(__doc__)
    return 2


if __name__ == "__main__":
    sys.exit(main(sys.argv))
