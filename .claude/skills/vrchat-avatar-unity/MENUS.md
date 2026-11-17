# Menus, toggles and parameters

How an avatar's menu is put together with Modular Avatar, and the rules that kept toggles working on real avatars. Check every result on a bake: the menu map in [BAKE.md](BAKE.md) shows what each control drives after MA has merged everything, and [TEST.md](TEST.md) runs the FX by hand.

## Building blocks

- **Menu:** an `MA Menu Installer` puts a menu or a submenu into the avatar's root menu; `MA Menu Item`s are its controls; an `MA Menu Install Target` places an existing item inside another submenu. Keep each toggle's menu item on the toggle's own object, so menu and effect move together.
- **What a control switches:**
  - `MA Object Toggle`: turns objects on or off. Simple, but a renderer animated on/off can't merge with the others (AAO), so each toggled part costs a mesh, and often a material slot.
  - `MA Shape Changer`: sets or deletes shape keys. Hiding a part with a `Hide` shape key instead of turning it off keeps the renderer mergeable; changing layering keys (a shirt that shrinks under a coat) belongs here too.
  - `MA Material Setter`: swaps a material while the control is on. It also stops that renderer from merging.
  - Several on one control: each state of a variant menu can carry an Object Toggle and a Shape Changer together.
- **Parameters:** a menu item creates its parameter; `MA Parameters` sets names, defaults, saved and synced. Prefix your own parameters (`<Project>/Hairpin`) so they don't collide with a vendor's.
- **Budget:** synced parameters share 256 bits: a bool costs 1, an int or a float 8. `expressionParameters.CalcTotalCost()` on a bake gives the total (BAKE.md). A variant menu with three or more states is cheaper as one int than as several bools, and the states can't overlap.

## Rules

- **A toggle sits beside what it switches, never inside it.** An Object Toggle inside its own target switches itself off along with the target, and the target never comes back (or never hides).
- **A Bone Proxy goes on the object the toggle switches.** On a child, the child moves under the bone at build time and the toggle switches an empty shell.
- **Anything that should keep working while a part is hidden** (a contact receiver, a light, another part's PhysBone) lives outside that part, with its own Bone Proxy if it needs one.
- **Save each target the way it should look while its control is off.** A toggle applies its setting only while the control is on; off, the target is as saved in the scene. That holds even when the control defaults to on. (From agentic-unity's notes.)
- **Mutually exclusive parts** (an ahoge and a hat, two coat states) are one control, not two that can both be on.
- **Defaults on purpose:** for each control, decide whether it is saved and what it starts as, and write it in the project's notes.
- **Tools that rebuild menus or FX** (light-setting gimmicks, expression tools, generators) can drop or duplicate Menu Install Targets when they recreate their objects. Check the menu map after running one.
- **Remove what you replace:** a deleted part leaves its menu item, install target and parameter behind unless they go too.

## Write Defaults

- Keep one Write Defaults convention across the FX. Mixing WD on and off is a classic cause of toggles that stay stuck or faces that don't reset. The BAKE.md and MMD.md (`vrchat-expressions`) snippets report WD per layer.
- MA's Merge Animator can match the avatar's convention for merged controllers.
- `Tools/Avatar Agent Kit/Set All Write Defaults ON` sets every state of a selected controller to WD on. Use it on your own copies only, never on vendor controllers.

## Layer order

In a merged FX, later layers win. Anything that sets the face goes after the expression tool's layers (MA Merge Animator layer priority), or the expression tool's default face overwrites it every frame.
