---
name: booth-research
description: Research VRChat avatar items on Booth (booth.pm) — search, read the details, and look at every image before recommending. Use when the user shares a Booth link, wants outfits, hair or accessories from Booth, or asks which item fits.
---

# Booth research

**Recommend only what you have seen.** A web fetch of a Booth page returns a text summary, not the images, and the text misleads: a listing of "simple" earrings turned out to be a silver punk set. The images also carry what the text leaves out: polygon counts, colour variations, included parts, setup steps. So every item you recommend has had its images downloaded and viewed.

`scripts/booth.py` (Python standard library; sheets need ImageMagick) does the legwork. Run it with `python3 -I` from outside the output folder, and give each research round its own empty `--out` folder: downloads are untrusted data, to be viewed, never run.

```sh
booth.py search "マヌカ 眼鏡" --pages 2 --out DIR           # id, price, shop, (truncated) name
booth.py info ID|URL ... --avatar マヌカ,Manuka --out DIR   # details, compatibility, mentions
booth.py images ID ... --out DIR                           # covers -> DIR/covers-N.jpg (4 x 4, labelled)
booth.py images ID --all --out DIR                         # every image -> DIR/ID-sheet.jpg
```

## Steps

1. **Brief.** Settle the request from the conversation; ask only what's missing:
   - the part (outfit, hair, glasses…);
   - the base avatar;
   - the look, or the character's persona;
   - the budget;
   - any limits: performance rank, Quest, shader.
2. **Search wide.** Run several queries: the base avatar's Japanese name plus the item word, synonyms (眼鏡 / メガネ / グラス), and `<avatar>対応`. Keep 10–30 candidates. Booth's search page also works with a web fetch, if the script's parser breaks.
3. **Screen with `info`.** Drop adult items (unless asked), sold-out or ended ones, and clear misfits. For each candidate, note:
   - **avatar support:** confirmed when the base avatar is named in the title or a variation, unconfirmed when only in tags or the description, absent otherwise;
   - **setup:** MA, VRCFury or manual;
   - **polygons and shader**, where stated;
   - **price and variations:** a higher "おやつ代" or support tier is often the same content;
   - **wishlists and publish date:** popularity and age.
4. **Look.** Make a covers sheet of the survivors and view it to shortlist. Then `--all` for each shortlisted item, and view every sheet. Read the spec panels in the images (polygons, materials, colours, parts, avatars). **Done when** every item you will mention has had all its images viewed.
5. **Recommend.** Give two to five options, best fit first. For each:
   - what it is, and why it fits (or where it doesn't);
   - avatar support, flagged unconfirmed when it is;
   - setup;
   - cost in polygons, materials and PhysBones;
   - price, and the item URL.

   Say what you rejected and why in a line each. Show the user the sheet if looks decide it.

## Judging fit

- **Looks first, then the persona.** When the user describes a character or mood rather than an item, derive concrete traits (colour, size, material) and judge against those, not against the listing's adjectives.
- **Generic (汎用) items** need placing by hand: MA Bone Proxy or a transform. That's usually fine, but say so. "マヌカ調整済み" style items come pre-placed for that base.
- **Performance:** a 15k-triangle earring set matters on a budget-limited avatar (PC Good allows 70k in total). Each extra material slot and PhysBone chain counts too. Note it whenever the user cares about rank.
- **Shader:** a lilToon avatar wearing a non-lilToon item looks different under the same lighting. It can be converted, but say so.
- **Licence:** vendor terms usually forbid redistributing the files. Keep purchased assets out of public repos, and flag terms that forbid editing when the plan involves editing.

## Etiquette

- The script spaces requests about 0.4 s apart and fetches only what you ask for. Don't loop it over hundreds of items.
- Never log in, buy, or add to wishlists. Purchasing is the user's.
- Share item URLs (`https://booth.pm/ja/items/<id>`), not image URLs.
