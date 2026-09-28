# listing/

What players read on [Borea](https://ksamodding.github.io/Borea/mod/KSArmory/) and
[SpaceDock](https://spacedock.info/mod/4460/KSArmory). The repository's own `README.md` is for
developers and ships in the archive; this folder is the store page.

| File | Where it goes |
| --- | --- |
| `abstract.txt` | Borea's one-line summary in lists and search |
| `description.md` | the long description on both, as CommonMark |
| `icon.png` | Borea's icon: square, 256–2048 px, at most 256 KiB |
| `images/` | screenshots the description shows: PNG, or JPEG for anything photographic, at most 2048 px and 1 MiB, 16 in all |

Write an image as `![alt](images/<id>.png)`, so it previews on GitHub as written. The file name
is its id: letters, digits, `-` and `_`. `./tools/listing.py --check` fails on a missing, oversized
or unreferenced image.

## Publishing

Neither site reads this folder itself. Commit and push, then:

```bash
./tools/listing.py editor      # Borea's in-app listing editor: the abstract, the description,
                               #   and an id and URL per image for its Images step
./tools/listing.py borea       # or by hand: paste over abstract, description and [images] in
                               #   KSAModding/content-index listings/KSArmory.toml, and open a PR
./tools/listing.py spacedock   # paste into the description on SpaceDock's Edit page
```

Image URLs are pinned to the commit, because Borea's checks fetch each image and fail the listing
when its bytes no longer match the record's hash. So replacing an image means running `borea`
again and sending the new records.
