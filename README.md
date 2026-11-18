# BlindShell App Updater — beta

A Windows 10/11, 64-bit app for finding, installing and updating Android apps on a connected BlindShell phone. Press F1 in the app to read these instructions; press Escape to return. This independent beta is not affiliated with BlindShell, Aurora, Google or Meta. It is intended for supervised testing; the recovery setup has only been tested on one Android 10 BlindShell configuration. NVDA and other phone models have not yet been validated.

## Open the app

Extract the entire beta ZIP to a folder, then open **BlindShell App Updater.exe**. If Windows announces that the publisher is unknown, note that this beta is unsigned. Close older updater windows first. Connect one phone with a USB data cable and leave USB debugging enabled. The app installs its Android helper automatically; **Install helper** is also available in the window. A separate Android USB driver may be required on some PCs.

## One window

- Enter a name in **Search apps** and press Enter. Search results appear in the app list in the same window.
- Choose **Refresh categories**, select a category using the arrow keys, and press Enter. Apps appear in the same list. Categories are hidden after a search; choose **Show or hide categories** to show them again.
- **Installed apps** lists the phone's apps. **Check updates** checks them and shows the result in that list. Select an app and choose **Install selected app** for an available update, or to install a search result.
- **BlindShell-related apps** searches for a few apps mentioned in BlindShell materials and community discussion. It is not a popularity ranking or a compatibility endorsement.
- **App details** reads the selected app's information. Third-party apps may not work with the BlindShell interface. Google Play may reject apps for device, region, account, or availability reasons. Anonymous downloads are limited to free apps.

Use Tab and Shift+Tab to move through controls and arrow keys to select a list item. The app moves focus to the results list when search, category, related-app, or installed-app results are ready. Search hides the category controls; choose Show or hide categories to bring them back. Alt+A focuses the current status, including during work. Search and category controls accept Enter. The interface uses native Windows controls and accessible names; hands-on JAWS validation remains needed.

## Progress

During installs and updates, the app announces progress to JAWS or through Windows accessibility notifications for other screen readers, including NVDA. Downloads announce percentage at roughly 10 percent intervals when a total is available, otherwise megabytes. It announces copying, signature verification, Android installation, completion, and errors. During long operations, it announces that work continues and gives the elapsed time every 20 seconds. The app also announces the actual percentage Android reports for the installation session. Android may leave that value unchanged for a long time while it optimizes files; it is not a precise estimate of time remaining. Press Alt+A at any time to reread the current status.

## USB setup and privacy

For a first connection, select **First-time setup** in the Windows app for these instructions: (1) connect a USB data cable; (2) on the phone, open **Settings > About phone > Service menu** and wait for it to start; (3) in the Windows app, select **Prepare USB**, then Yes. The app backs up USB settings, enables USB debugging, and restarts the phone. (4) Wait for the normal phone menu, then search or check installed apps. Do not choose Factory reset. Keep USB debugging enabled for future use. **Restore USB settings** is available if you wish to undo preparation.

The helper uses Aurora GPlayApi to request app data and downloads from Google Play, sending device-profile information and package names to Google. Updates are checked against the installed signing certificate. For a new install there is no existing signature to compare; downloaded file hashes and signatures across the APK parts are verified. Existing app data is not cleared during updates.

For newly installed apps, the updater attempts to add a BlindShell third-party menu entry. To repair an entry for an app already installed, select it under Installed apps and choose Add selected app to phone menu. Reopen the phone menu afterwards. Whether the phone accepts the entry depends on its software; this is not yet verified across devices.

Backups and extracted tools are stored under `%LOCALAPPDATA%\BlindShellUpdater`. The EXE is unsigned, so Windows may display a reputation warning. A USB driver may be needed on some PCs. USB preparation supports only the tested BlindShell Android 10 recovery layout. The helper and its Aurora dependency are GPL-3.0-or-later; see LICENSE.txt and the source in `aurora-helper`.
