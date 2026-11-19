# Neon Disk Management: Microsoft Store submission kit

This kit has everything to paste into Partner Center, plus the submission steps in order.

---

## 1. One-time setup

1. **Developer account.** If you already registered one for Neon Undelete, use that account. Otherwise sign up at https://partner.microsoft.com/dashboard/registration. An *Individual* account is free.
2. In Partner Center choose **Apps and games → New product → MSIX or PWA app**, then **reserve the name "Neon Disk Management"**. If it's taken, reserve a variant and change `DisplayName` in `AppxManifest.template.xml` to match.
3. Open **Product management → Product identity** and copy three values:
   - `Package/Identity/Name` → `-IdentityName`
   - `Package/Identity/Publisher` (starts with `CN=`) → `-Publisher`
   - `Package/Properties/PublisherDisplayName` → `-PublisherDisplayName`
4. Build the Store package with your real identity:

```powershell
.\packaging\package.ps1 -IdentityName "<Identity Name>" -Publisher "CN=<...>" -PublisherDisplayName "<Publisher display name>" -Version 1.0.0.0
```

Upload `packaging\out\NeonDiskManagement_1.0.0.0_x64.msix`. Microsoft signs Store packages, so it doesn't need your own signature.

---

## 2. Packages page

- Upload the `.msix` above.
- **Device families:** Windows 10/11 Desktop only.
- **Restricted capabilities:** the package declares two, and the form asks you to justify each one. Paste these:

**runFullTrust**
> Neon Disk Management is a classic Win32 desktop application (.NET Framework 4.8 WinForms). It manages disk partitions through the Windows Storage Management API (root\Microsoft\Windows\Storage), which isn't available from a sandboxed AppContainer.

**allowElevation**
> Changing partitions (resize, create, format, delete, change drive letter, initialize a disk) requires administrator rights on Windows. This is the core function of a partition manager. The app starts unelevated and can show the disk layout without elevation. It asks for elevation with a standard UAC prompt only when the user clicks "restart as administrator" or chooses an action that changes a disk. Every change uses the documented Windows storage API (the same one Disk Management and the Resize-Partition cmdlet use). Destructive actions need explicit confirmation, and the app refuses to delete the running Windows partition or the boot/EFI partition.

---

## 3. Properties

| Field | Value |
|---|---|
| Category | Utilities & tools |
| Subcategory | Backup & manage |
| Privacy policy URL | required, see `PRIVACY.md` (host it at any public URL) |
| Website | optional |
| Support contact | an email or URL you control |
| System requirements | Minimum: x64, Windows 10 1809+. Recommended: Windows 11 |
| Product declarations | ✔ "This product has been tested to meet accessibility guidelines" only if you have tested it, otherwise leave unticked |

---

## 4. Age ratings (IARC questionnaire)

Category **Utility / productivity**. Answer "no" to every content question: no violence, user interaction, data sharing, purchases or location. The expected result is 3+ / Everyone.

---

## 5. Store listing (English)

**Product name:** Neon Disk Management

**Short description** (shown in search results):
> A simple partition manager. Shrink, extend, create and delete partitions on a clear, to-scale map of your drives, without the clutter of Disk Management.

**Description:**
```
Need more room on C:, a new partition for games, or to clean up an old drive? Neon Disk Management shows every disk as a clear, to-scale bar of its partitions and free space. Click a partition and the actions that apply to it appear.

FEATURES
• Extend to fill free space: one click grows a partition into the unallocated space after it
• Shrink or extend with a slider, or type an exact size (e.g. "250 GB")
• Windows works out the smallest and largest allowed size before you choose
• Create new partitions and format them as NTFS, exFAT or FAT32, with a drive letter and label
• Change or remove drive letters
• Delete partitions, with a type-to-confirm safety check
• Initialize brand-new blank disks (GPT)
• Explains why an action isn't possible, e.g. "the free space isn't next to this partition"
• Protects the Windows and boot partitions from deletion
• Shows used and free space, file system, partition type and disk model at a glance
• Keyboard friendly: arrow keys to move, Enter for the main action, Del to delete, F5 to refresh
• Neon retro terminal look

SAFE BY DESIGN
Neon Disk Management uses the same Windows storage API as Disk Management, so Windows still enforces its own rules: only NTFS and ReFS volumes can be resized, and a partition can only grow into space directly after it. Shrinking and extending never erase data, but always back up important files before changing partitions.

Viewing your disks needs no special rights. Making changes needs administrator rights, so the app asks with a standard UAC prompt.
```

**What's new in this version:**
> First release.

**Product features** (one per line, max 20):
```
To-scale map of every disk, partition and unallocated area
One-click "extend to fill free space"
Shrink or extend with a slider or an exact size
Create and format NTFS, exFAT and FAT32 partitions
Change or remove drive letters
Delete partitions with type-to-confirm protection
Initialize new blank disks as GPT
Clear explanations when an action isn't possible
Windows and boot partitions protected from deletion
Keyboard shortcuts for every action
```

**Search terms** (max 7):
```
partition manager
disk management
resize partition
extend partition
shrink volume
partition
disk
```

**Copyright:** © 2026 \<your name\>

---

## 6. Screenshots

You need at least one PNG, 1366×768 or larger. The ready-made set is in `screenshots\store\` (1484×892, drawn from sample disks, so no real drive data):
1. `store-1-main.png`: the main window with a partition selected (upload this one first)
2. `store-2-unallocated.png`: unallocated space selected, "new partition" available
3. `store-3-shrink.png`: the shrink / extend dialog
4. `store-4-create.png`: the new partition dialog
5. `store-5-delete.png`: the delete confirmation

`screenshots\01-main.png` is an older capture that shows real disk model names; don't use it for the Store.

The app tile icon (`packaging\Assets\Square150x150Logo.scale-200.png`, 300×300) is taken from the package automatically.

---

## 7. Pricing and availability

Choose free or a price, markets (all is fine), and visibility (public, or "private, hidden" for a test run first).

---

## 8. Notes for certification (submission options)

```
Neon Disk Management is a partition manager. To test without risking data, attach a small virtual disk first:
  diskmgmt.msc → Action → Create VHD (1 GB) → Attach
1. Launch the app. It starts as a standard user and shows all disks read-only.
2. Click "restart as administrator" and accept the UAC prompt.
3. Select the new virtual disk's header → "initialize disk (GPT)".
4. Select its unallocated area → "new partition…" → create & format.
5. Select the new partition → "shrink / extend…" (move the slider) → apply. Then "extend to fill free space".
6. "delete partition…" → type the confirmation word → delete forever.
The app makes no network connections and collects no data.
```

---

## Before you submit: local test install

Your own machine needs the test certificate trusted once:

```powershell
.\packaging\package.ps1 -Sign                       # builds and signs with a self-signed test cert
# in an administrator PowerShell, once:
Import-Certificate .\packaging\out\NeonDiskManagement-test.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople
# then double-click packaging\out\NeonDiskManagement_1.0.0.0_x64.msix → Install
```

Check that:
- the Start menu entry and taskbar icon look right
- "restart as administrator" works from the installed app (this exercises `allowElevation`)
- your disks are listed

## If certification rejects `allowElevation`

There's a fallback: Partner Center also accepts classic **MSI/EXE installers** (Apps and games → New product → *EXE or MSI app*). The installer is hosted on your own HTTPS URL and must be signed with a code-signing certificate you buy, for example Azure Trusted Signing (about $10 a month) or a commercial certificate. That route has no restrictions on elevation.
