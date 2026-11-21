# Reactions

Small gimmicks on an accessory: it shows something when the avatar speaks, mutes, goes AFK or gets a head pat. They are built from MA parts (Merge Animator, Menu Item, Object Toggle or Shape Changer) and a contact receiver, and tested on a bake by running the FX by hand ([TEST.md](../vrchat-avatar-unity/TEST.md)). The reference implementation is `scripts/AccessoryReactions.cs`.

## What drives them

| Source | Parameter | Costs | Notes |
|---|---|---|---|
| VRChat built-ins | `AFK` (bool), `MuteSelf` (bool), `Voice` (float 0–1) | no synced bits | VRChat sets them itself. Add them to the merged controller's parameters with exactly these names and types, never to the expression parameters. The full list and how each one syncs: [Animator Parameters](https://creators.vrchat.com/avatars/animator-parameters/) |
| A contact receiver | a float you name (`<Project>/HeadPat`) | one contact; no synced bits | `Constant` is 1 while touched ("for as long as the hand stays"); `OnEnter` for one shot; `Proximity` for how close. Tags `Hand` and `Finger` for hands |
| Its OFF toggle | a bool | 1 bit | an Object Toggle on the receiver |

- **Others' hands only:** a head-pat receiver gets *allow self* off. Your own hand on your head is often an expression tool's lock (FaceEmo), and should not trigger it.
- **A base's own gimmick** (many bases ship a head pat): keep one. Two receivers driving two faces fight. Retiring the base's: its receiver and menu item EditorOnly, its parameter out of the project's copy of the expression parameters.

## Rules that kept them working

- **States are shape keys, not objects.** Each state's shapes sit collapsed inside the accessory at 0 and come out at 100. The accessory stays one active renderer whatever it shows, so it still merges into the main mesh (`vrchat-pc-good`). Swapping objects costs a renderer per state.
- **One layer for the accessory's states,** priorities by Any State transitions: AFK over a head pat, over speaking, over muted, over idle. **Every clip writes every state key,** so a crossfade only ever blends two states and nothing collapses twice.
- **Hold after speech and pats** (0.5 s and 0.8 s on MANUKA), so gaps between syllables or patting strokes don't flicker.
- **Voice** above 0.02 counts as speaking. A 1D blend tree on `Voice` (full at 0.3) grows the speaking state with the volume.
- **Light effects without the animator:** a lilToon emission map scrolled by the shader (dots that light in turn: each dot at a different U of the scrolling map). Animating material properties would make the renderer's animations differ from the rest, and AAO stops merging it.
- **Face reactions go after the expression tool:** an MA Merge Animator with a higher layer priority, or the tool's default-face layer rewrites the face every frame. Use only keys the gestures don't touch (blush, gaze, brows), so gestures and lip sync keep working under it. Its idle state has no motion and relies on Write Defaults ON to hand the face back. Face shaping and gestures: `vrchat-expressions`.
- **The receiver and the toggles live beside the accessory,** not inside it (MENUS.md); the receiver gets its own Bone Proxy to the Head, so head pats keep working while the accessory is hidden.

## Testing

- **Logic:** bake, then run the baked FX by hand ([TEST.md](../vrchat-avatar-unity/TEST.md), Animator logic; its example is this pin). Set `AFK`, `MuteSelf`, `Voice` and the contact's parameter yourself, and read the state names after each step, including the holds running out.
- **Looks:** set each state's keys on the baked mesh and render close-ups (`RenderAvatar` with `focus`). On a merged mesh the keys are renamed by AAO; TEST.md says how to match them.
- **In game** (a checklist in the handoff, for the user and a friend):
  - Talk: the speaking state shows and follows the loudness; after you stop, idle comes back after the hold.
  - Mute the mic: the muted state; unmute: idle.
  - Go AFK (in VR, take the headset off): the AFK state, over everything else.
  - The friend pats your head: the pat state (and the face) for as long as the hand stays; your own hand does nothing; with "HeadPat OFF" on, nothing.
  - The accessory's own OFF: it disappears, and head pats still work.
  - Ask the friend whether they see each state too.

## Worked example: the MANUKA speech-bubble pin (October 2026)

- **The pin:** generated (headless Blender) and placed on the upper side hair, opposite the braid, Bone Proxy to Head. Idle shows "…" with three dots lighting in turn from a scrolling emission map.
- **States:** AFK "z z Z"; a big yellow "?" while patted; green dots that grow with the voice; still, unlit dots when muted. Each glyph set is a shape key on the pin.
- **The face:** while patted, a shy face (blush, eyes away and down, troubled brows), merged after FaceEmo. MANUKA's own head pat, a layer before FaceEmo's, had only ever shown its blush, because FaceEmo's default face rewrote the rest every frame. It was retired.
- **Toggles:** "Hairpin OFF" and "HeadPat OFF" side by side in the character menu, beside the pin in the hierarchy.
- **Cost:** one contact and one synced bit. The build stayed PC Good (69,165 triangles, 8 material slots, 2 skinned meshes), because every state is a shape key in the merged mesh.
