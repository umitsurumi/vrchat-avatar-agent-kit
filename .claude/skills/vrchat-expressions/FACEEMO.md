# FaceEmo

FaceEmo (`jp.suzuryg.face-emo`, notes from 1.7.0) builds a gesture-expression FX and merges it with Modular Avatar. The menu, the settings and the generated output can all be driven from `execute_code` (codedom compiles against it).

## What it generates

- **An MA prefab** (`FaceEmoPrefab`) with Merge Animator, MA Parameters and an expression menu.
- **The FX layers:**
  - `[ USER EDIT ] DEFAULT FACE` writes every face-mesh shape key except visemes, blink (unless FaceEmo replaces blink) and the **Excluded Blend Shapes**. It writes them at the scene's current weights, every frame.
  - `FACE EMOTE PLAYER` plays the expressions.
  - `BLINK`.
  - `MOUTH MORPH CANCELLER`: during speech, resets the listed mouth keys so they don't fight the visemes.
- **The settings submenu** in game: emote lock, blink off, dance gimmick, contact lock, gesture swap and disable, controller type.
- **The output folder:** each apply writes a new `Assets/Suzuryg/FaceEmo/Generated/<timestamp>/` (thumbnails included, several MB) and deletes the previous one.

## Settings worth setting on purpose

`AV3Setting` fields, shown in the inspector under the same names:

| Field | What it does |
|---|---|
| `ExcludedBlendShapes` | Keys FaceEmo never writes. Put here any key another gimmick owns (blush toggles, tears, MMD keys if MMD worlds should own them, see MMD.md) |
| `MouthMorphs` | Keys the mouth-morph canceller resets during speech. Include every mouth key the expressions use; MMD mouth keys too, if expressions use them |
| `ReplaceBlink` | FaceEmo takes over blinking (and can turn it off per expression) |
| `MatchAvatarWriteDefaults` | Follow the avatar's Write Defaults convention. Needed for the "disable entire FX" dance option |
| `DisableFxDuringDancing` | Dance gimmick on: false = bypass only the expression layers, true = disable the whole FX (toggles reset to default while dancing) |
| `AddConfig_DanceGimmick` | Adds the dance gimmick toggle to the in-game menu. Its parameter defaults to off and isn't saved |
| `ChangeAfkFace`, `AfkFace` | The AFK face |
| `TransitionDurationSeconds` | Blend time between expressions |
| `AddParameterPrefix` | Prefixes FaceEmo's parameters, so they don't collide with others |

## Scripting

- **Every FaceEmo API call starts from its installer:** `new Suzuryg.FaceEmo.AppMain.FaceEmoInstaller(GameObject.Find("FaceEmo"))`, then `installer.Container.Resolve<T>()`.
- **Side effect:** constructing the installer re-creates FaceEmo's editor-state objects in the scene with new IDs, even when you only read. Expect a large no-op diff on the next save. `git diff` the scene after, and revert pure churn (`vrchat-avatar-unity` GOTCHAS).
- **Reading the settings** (adapted from code that ran on 1.7.0):

  ```csharp
  var installer = new Suzuryg.FaceEmo.AppMain.FaceEmoInstaller(GameObject.Find("FaceEmo"));
  var setting = installer.Container.Resolve<Suzuryg.FaceEmo.Components.Settings.AV3Setting>();
  var sb = new System.Text.StringBuilder();
  foreach (var f in setting.GetType().GetFields())
  { if (f.DeclaringType != setting.GetType()) continue; var v = f.GetValue(setting); sb.Append(f.Name).Append(" = ").Append(v).Append("\n"); }
  return sb.ToString();
  ```

- **Building the menu.** The signatures were checked by reflection on 1.7.0; this outline has not been run as written. The menu used on the real avatar was built through these calls.

  ```csharp
  // using Suzuryg.FaceEmo.Domain; clipPath = an AnimationClip asset that writes only the keys it changes
  var installer = new Suzuryg.FaceEmo.AppMain.FaceEmoInstaller(GameObject.Find("FaceEmo"));
  var repo = installer.Container.Resolve<Suzuryg.FaceEmo.Domain.IMenuRepository>();
  var menu = repo.Load(null);
  var modeId = menu.AddMode(Suzuryg.FaceEmo.Domain.Menu.RegisteredId);
  menu.ModifyModeProperties(modeId, displayName: "Sleepy", changeDefaultFace: true);
  menu.SetAnimation(new Suzuryg.FaceEmo.Domain.Animation(AssetDatabase.AssetPathToGUID(clipPath)), modeId);
  // a gesture branch inside a mode:
  menu.AddBranch(modeId, new[] { new Suzuryg.FaceEmo.Domain.Condition(Suzuryg.FaceEmo.Domain.Hand.Either, Suzuryg.FaceEmo.Domain.HandGesture.Fist, Suzuryg.FaceEmo.Domain.ComparisonOperator.Equals) });
  menu.SetAnimation(new Suzuryg.FaceEmo.Domain.Animation(AssetDatabase.AssetPathToGUID(fistClipPath)), modeId, 0, Suzuryg.FaceEmo.Domain.BranchAnimationType.Base);
  repo.Save(null, menu, "describe the change");
  ```

  - `Hand`: Left, Right, OneSide, Either, Both.
  - `HandGesture`: Neutral, Fist, HandOpen, Fingerpoint, Victory, RockNRoll, HandGun, ThumbsUp.
  - `ModifyBranchProperties(modeId, index, eyeTrackingControl:, mouthTrackingControl:, blinkEnabled:, mouthMorphCancelerEnabled:, isLeftTriggerUsed:, isRightTriggerUsed:)` sets the per-branch switches.
  - **Branch order is priority:** with both hands gesturing, the earlier branch wins.
  - **A mode without branches** needs `changeDefaultFace: true`, or it is left out of the menu.
- **Apply to the avatar:** `installer.Container.Resolve<Suzuryg.FaceEmo.UseCase.IGenerateFxUseCase>().Handle("", new List<string>())`. Keep the editable-prefab list empty, so FaceEmo never edits a vendor prefab.

## Testing

- Gesture Manager didn't pick up an avatar driven from MCP. Run the baked FX by hand instead (`vrchat-avatar-unity` TEST.md): set `GestureLeft`/`GestureRight`, `AFK`, `Voice`, and FaceEmo's own parameters.
- FaceEmo uses parameter drivers, which do nothing in a bare Animator. Patch `VRC.SDKBase.VRC_AvatarParameterDriver.OnApplySettings` temporarily, as TEST.md describes.
- **Previewing an expression:** sample its clip onto a preview clone, then render in the next call, or `BakeMesh` the face into a temporary MeshRenderer. A render in the same frame shows stale skinning.
