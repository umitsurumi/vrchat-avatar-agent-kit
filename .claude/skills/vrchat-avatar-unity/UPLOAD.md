# Upload

**Ask first, every time.** Settle three things with the user:
- which avatar this is: a **new blueprint**, or an **update** of an existing one, and if so which;
- the name;
- that it stays **private**.

An upload is outward-facing, and an update overwrites what people already see.

Before asking, bake and run `CheckAvatar` ([BAKE.md](BAKE.md)): no errors, and every warning settled with the user. It also says whether the blueprint ID is shared with another branch.

## Which blueprint gets overwritten

`BuildAndUpload` uses the `blueprintId` on the root's `PipelineManager`:
- **Empty:** a new avatar.
- **Set:** an update of that avatar.
- **Clones and copied scenes keep the ID.** An avatar variant made from another (a new outfit, a new branch) overwrites the original until its ID is cleared. Clear it on the variant before its first upload.
- **After a first upload,** the new ID is written to the `PipelineManager`. Check it, save the scene so later uploads update that avatar, and note the ID in the project's handoff.

## Upload through the SDK builder

The VRChat SDK control panel must be open and logged in. It logs out after domain reloads, so ask the user to log in again if `TryGetBuilder` fails. The call returns at once; the events go to a log file, which you poll from the shell.

```csharp
string AVATAR = "MyAvatar", NAME = "My Avatar", EXPECT_ID = "avtr_…";   // EXPECT_ID "" for a new avatar
string THUMB = null;   // new avatar: absolute path of a 4:3 PNG (RENDER.md); update: null
GameObject root = null;
foreach (var g in Resources.FindObjectsOfTypeAll<GameObject>()) if (g.name == AVATAR && g.scene.IsValid() && g.transform.parent == null) root = g;
VRC.SDK3A.Editor.IVRCSdkAvatarBuilderApi builder;
if (!VRCSdkControlPanel.TryGetBuilder<VRC.SDK3A.Editor.IVRCSdkAvatarBuilderApi>(out builder)) return "no builder: open the SDK panel and log in";
System.IO.Directory.CreateDirectory("Temp/upload");
string log = System.IO.Path.GetFullPath("Temp/upload/status.log");
System.IO.File.WriteAllText(log, System.DateTime.Now.ToString("HH:mm:ss") + " start\n");
System.Action<string> w = s => { try { System.IO.File.AppendAllText(log, System.DateTime.Now.ToString("HH:mm:ss") + " " + s + "\n"); } catch { } };
builder.OnSdkBuildStateChange += (s, e) => w("build state " + e);
builder.OnSdkBuildError += (s, e) => w("BUILD ERROR " + e);
builder.OnSdkUploadStateChange += (s, e) => w("upload state " + e);
builder.OnSdkUploadError += (s, e) => w("UPLOAD ERROR " + e);
builder.OnSdkUploadSuccess += (s, e) => w("UPLOAD SUCCESS " + e);
var av = new VRC.SDKBase.Editor.Api.VRCAvatar();
av.ID = root.GetComponent<VRC.Core.PipelineManager>().blueprintId;
if ((av.ID ?? "") != EXPECT_ID) return "unexpected blueprint '" + av.ID + "'";
av.Name = NAME; av.Description = ""; av.ReleaseStatus = "private"; av.Tags = new System.Collections.Generic.List<string>();
var task = builder.BuildAndUpload(root, av, THUMB, System.Threading.CancellationToken.None);
task.ContinueWith(t => { if (t.IsFaulted) w("TASK FAULTED " + t.Exception.GetBaseException().Message); else w("task done"); });
return "upload started for '" + av.ID + "'";
```

Poll the log from the shell, in a POSIX shell (Linux, macOS, Git Bash on Windows):

```sh
for i in $(seq 1 40); do sleep 10; grep -qE "UPLOAD SUCCESS|ERROR|FAULTED|task done" Temp/upload/status.log && break; done; cat Temp/upload/status.log
```

or in PowerShell:

```powershell
foreach ($i in 1..40) { Start-Sleep 10; if (Select-String -Quiet -Path Temp/upload/status.log -Pattern 'UPLOAD SUCCESS|ERROR|FAULTED|task done') { break } }; Get-Content Temp/upload/status.log
```

- **Thumbnail:** an update passes `null`; sending the same image again fails with "This file was already uploaded".
- **Copyright notice:** the first upload of an avatar may need the user to confirm it in the SDK panel. Say so before starting, and wait for them to click.
- **Pre-upload checks:** the SDK blocks on validation errors. Its "Auto Fix" buttons are the user's to press. Report which checks fail rather than changing settings to silence them.

## After the upload

- Tell the user what to test in game, and what the editor could not show.
- When the user accepts the version, tag the commit: an **annotated** tag naming the avatar, its blueprint ID and the key numbers (rank, triangles, PhysBones).
