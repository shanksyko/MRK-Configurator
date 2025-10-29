# Manual Verification Checklist: Start Menu Shortcuts

Follow this checklist on a Windows machine to ensure that shortcut discovery scans
both per-user and common Start Menu program folders without triggering ACL errors.

1. **Prepare test shortcuts**
   - Create a shortcut to a known launcher (for example, `notepad.exe`).
   - Place one copy in `%AppData%\Microsoft\Windows\Start Menu\Programs`.
   - Place another copy in `%ProgramData%\Microsoft\Windows\Start Menu\Programs`.

2. **Run the installed application scan**
   - Launch Mieruka and trigger the installed application discovery flow.
   - Wait for the scan to complete; this may take a few seconds while shortcuts are inspected.

3. **Confirm results**
   - Verify that the application represented by both shortcuts appears exactly once in the installed applications list.
   - Confirm that no access-denied or ACL-related errors are surfaced in the logs or user interface during the scan.

4. **Cleanup**
   - Remove the temporary shortcuts that were created for this verification.

