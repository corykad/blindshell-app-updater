"""Package the current updater as a clearly labeled, locally shareable beta."""
from pathlib import Path
from datetime import datetime
from urllib.request import Request, urlopen
import hashlib
import os
import shutil
import zipfile

root = Path(__file__).resolve().parent
stage = root / "build" / ("beta-package-" + datetime.now().strftime("%Y%m%d-%H%M%S-%f"))
output = root / "deliverables"
output.mkdir(exist_ok=True)
stage.mkdir(parents=True)
exe = stage / "BlindShell App Updater.exe"
shutil.copy2(root / "release" / "BlindShellUpdater-Accessible.exe", exe)
shutil.copy2(root / "README.md", stage / "README.md")
shutil.copy2(root / "LICENSE.txt", stage / "LICENSE.txt")

(stage / "Start Here.txt").write_text("""BlindShell App Updater - beta

This is a supervised beta for Windows 10 or 11, 64-bit. It has been tested
with one Android 10 BlindShell configuration. Other models and NVDA still need
testing. This independent project is not affiliated with BlindShell, Meta,
Google, or Aurora. The executable is not digitally signed.

GET STARTED

1. Extract the entire ZIP to a folder. Open BlindShell App Updater.exe.
2. Connect the phone with a USB data cable. The phone needs internet access.
3. For a first connection, choose First-time setup in the Windows app. On the
   phone, open Settings, About phone, then Service menu. Wait for the Service
   menu to start. Back in the Windows app, choose Prepare USB and confirm.
4. Wait for the phone to restart and return to its normal menu. Leave USB
   debugging enabled for later use. Do not choose Factory reset.
5. Search or choose Installed apps or Check updates. Use the arrow keys in the
   results list. Select an app and choose Install selected app.

Press F1 inside the app for the full readme. It opens in a text control; press
Escape to return. Use Tab and Shift+Tab to move through controls. After search
or browsing, focus moves to the results list. Select a category and press Enter
to load its apps. Categories are hidden after search; Show or hide categories
brings them back.

Install and update progress is announced through JAWS or Windows accessibility
notifications. Android's reported installation percentage can pause during
optimization. Unsupported or unavailable apps cannot always be installed.
If an installed app does not appear in the phone's third-party apps menu, select
it under Installed apps and choose Add selected app to phone menu.

Read README.md for support limits, privacy, and further details. Licenses are in
the Licenses folder; source and rebuild information are in Source.
""", encoding="utf-8")

licenses = stage / "Licenses"
licenses.mkdir()
sdk = Path(os.environ["LOCALAPPDATA"]) / "Android" / "Sdk"
shutil.copy2(sdk / "platform-tools" / "NOTICE.txt", licenses / "Android-platform-tools-NOTICE.txt")
shutil.copy2(sdk / "build-tools" / "36.1.0" / "NOTICE.txt", licenses / "Android-build-tools-NOTICE.txt")
aurora = Path(os.environ["TEMP"]) / "bs-gplayapi"
shutil.copy2(aurora / "LICENSE", licenses / "GPL-3.0.txt")
with zipfile.ZipFile(licenses / "Java-runtime-legal.zip", "w", zipfile.ZIP_DEFLATED) as z:
    jbr = root / "build" / "payload" / "java"
    z.write(jbr / "release", "release")
    for item in (jbr / "legal").rglob("*"):
        if item.is_file():
            z.write(item, item.relative_to(jbr).as_posix())
(licenses / "Third-party notice.txt").write_text("""The bundled Android helper uses Aurora OSS GPlayApi 4.0.0, licensed
GPL-3.0-or-later. Its published source archive is in Source. Upstream project:
https://gitlab.com/AuroraOSS/gplayapi. The upstream project describes the
library as reverse-engineered and for educational and research purposes only.

Android SDK platform tools and build tools notices and the Java runtime legal
files are included here. The beta's third-party distribution terms have not
been independently reviewed; do that before public or commercial distribution.
""", encoding="utf-8")

source = stage / "Source"
source.mkdir()
url = "https://repo.maven.apache.org/maven2/com/auroraoss/gplayapi/4.0.0/gplayapi-4.0.0-sources.jar"
with urlopen(Request(url, headers={"User-Agent": "BlindShellUpdater beta packager"}), timeout=60) as response:
    (source / "gplayapi-4.0.0-sources.jar").write_bytes(response.read())
source_note = """BlindShell App Updater beta source

The source archive contains the Windows source, Android helper source, build
scripts, Gradle wrapper, and documentation. The beta menu-entry key is embedded
in the distributed EXE and is public; it is not a security credential or
production publisher identity. The build script creates a new local beta key
if none is available; that different key cannot update previous menu entries.
The developer's Android debug keystore is not included. A rebuild with a new
debug key cannot update an already-installed helper signed by a different key.
Establish a protected release signing key and package identity before shipping
updates to a wider audience. Do not publish personal debug keystores.

Build the Android helper with its Gradle wrapper and :app:assembleDebug, then
run build.ps1 on Windows. Android SDK 36, platform-tools, build-tools 36.1.0,
Android Studio's JetBrains Runtime, and the .NET Framework compiler are needed.
Set AndroidSdk and JavaHome arguments if the default paths differ. Do not use
the build script or the public menu key as a production signing process.

GPlayApi 4.0.0 source is supplied separately in this folder. Java runtime source:
https://github.com/JetBrains/JetBrainsRuntime. Android tools source:
https://android.googlesource.com/. Runtime version is in the Java legal ZIP.
"""
(source / "Build and source information.txt").write_text(source_note, encoding="utf-8")
with zipfile.ZipFile(source / "Updater-source.zip", "w", zipfile.ZIP_DEFLATED) as z:
    for item in [root / "Updater.cs", root / "build.ps1", root / "package_beta.py", root / "README.md", root / "LICENSE.txt", root / ".gitignore"]:
        z.write(item, item.relative_to(root).as_posix())
    helper = root / "aurora-helper"
    for relative in ["settings.gradle.kts", "build.gradle.kts", "gradle.properties", "gradlew", "gradlew.bat", "gradle/wrapper/gradle-wrapper.jar", "gradle/wrapper/gradle-wrapper.properties", "app/build.gradle.kts", "app/src/main/AndroidManifest.xml", "app/src/main/java/org/example/bshelper/Receiver.kt"]:
        item = helper / relative
        z.write(item, "aurora-helper/" + relative)

def digest(path):
    h = hashlib.sha256()
    with path.open("rb") as f:
        for block in iter(lambda: f.read(1024 * 1024), b""):
            h.update(block)
    return h.hexdigest()

files = sorted(p for p in stage.rglob("*") if p.is_file())
(stage / "SHA256SUMS.txt").write_text("".join(f"{digest(p)}  {p.relative_to(stage).as_posix()}\n" for p in files), encoding="utf-8")
archive = output / ("BlindShell-App-Updater-Beta-" + datetime.now().strftime("%Y%m%d-%H%M%S") + ".zip")
with zipfile.ZipFile(archive, "x", zipfile.ZIP_DEFLATED, compresslevel=6, allowZip64=True) as z:
    for p in sorted(stage.rglob("*")):
        if p.is_file():
            z.write(p, p.relative_to(stage).as_posix())
with zipfile.ZipFile(archive) as z:
    error = z.testzip()
    if error:
        raise RuntimeError("ZIP verification failed: " + error)
    assert z.namelist().count("BlindShell App Updater.exe") == 1
print("Package:", archive)
print("SHA-256:", digest(archive))
print("Executable SHA-256:", digest(exe))
print("Staging:", stage)
