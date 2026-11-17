# The Amiga version's pictures (D-642)

A player's option, off by default: the pictures of the Amiga version of Covert Action, read from the player's own Amiga
files, shown in place of the PC's where the Amiga version has the same picture on the same layout. Built as the Amiga music
and effects (AMIGA-MUSIC.md, AMIGA-EFFECTS.md, D-640, D-641): the same Amiga files folder (`sound.amigaFiles`) and search;
the setting is `graphics.amiga` (SETTINGS.md), a row of the launcher's Advanced tab. No Amiga file, picture or colour is in
the repository or shipped: the player's folder is read when the game starts.

The code: `Content.AmigaPictureFile` (the reader), `Drivers.PictureColours` (the display colours the pictures' own colours
take), `Launcher.AmigaPictureList` (which pictures, and why), `Launcher.AmigaPictureSource` (reads a picture when the game
loads its PC twin, `Platform.IPictureReplacements`), `Launcher.AmigaFolderSearch` (where the files are),
`Presentation.Library.LibraryPictures.LoadPicture` (the one place the game's pixels are put on a page).

## The files

The Amiga version keeps each picture in a `.pix` file (68 on the three disks: disk 1 faces, icons and the title pictures; disk 2
the maps, the city pictures, the equipment sheets and the wiring board; disk 3 the car chase's pictures). The format, read
from the files by comparing them with the PC's `.PIC` files:

| Offset | Content |
|---|---|
| 0 | one byte: the number of palettes, 1 or 2 |
| 1 | one byte: a format number (6 or 7); nothing here needs it |
| 2, 4 | the width and the height, big-endian words (always 320 and 200) |
| 6 | 16 colours for each palette, big-endian words `0x0RGB` of three 4-bit components |
| after | the PC's picture stream with every 16-bit word byte-swapped: the byte with the largest code width, then the same LZW and run-length coding as a PC picture (`PictureFile`) |

With one palette the pixels are packed two to a byte as on the PC (colour numbers 0 to 15); with two they are one to a byte
(colour numbers 0 to 31). Most pictures have two palettes but use only 8 to 24 of the 32 colours. **Every picture brings
its own palette.** The PC's pictures have 16 colours that the game shows through the palette it sets for the screen; the
Amiga's are not in the EGA order, and a number does not mean the same colour in two pictures.

## The palette

The game draws its own things (text, lines, the cursor, the menus, sprites, the PC pictures) with display colours 0 to 15,
through the 16-colour palette it sets for each screen (`set_palette`), and recolours them by number (the menu bar's highlight,
the kit screen's cursor). Colour 0 means "nothing" in several places: a sprite's pixel 0 leaves the page as it is, and the
campaign measures a city's thumbnail up to the first 0. Nothing the game does touches the display's colours 128 to 255 (the
mode set leaves them alone, D-10).

So the Amiga picture's colours are put in colours 128 to 255 and its pixels are written with those numbers
(`Drivers.PictureColours`, `Launcher.AmigaPictureSource`):

* One display colour for each **distinct colour** (12 bits), shared between pictures: the black and the greys that most
  pictures have are one entry. The 32 pictures of the list together have 26 distinct colours besides the background; the 128
  entries are never short in a game. Should they be, a further colour is shown as the nearest one already given.
* **Colour 0 of the picture stays display colour 0**, which the game's palette shows as it always did (black). Colour 0 is the
  background and see-through colour of the Amiga's pictures too: the canvas around a city's thumbnail, the cells between the
  face parts of a sheet. The game's tests for it (`pixel != 0`) read the same pixels on both pictures. (On the maps and the
  windscreen the Amiga uses colour 0 for black areas where the PC uses another colour; those areas are black, as they are.)
* The game's colours 0 to 15 are not touched, so the text, lines, menus, highlights and the cursor drawn over a picture are
  exactly the game's, and a picture's pixels (display colours 128 and up) are not recoloured by the game's
  `recolor_rect`, which only changes the colours it is told to.
* The numbers are given in the order the colours first appear in a picture and kept until the display mode is set anew (the
  start of each part: `McgaGraphicsDriver.SubCode0208` without the keep-screen bit); then the pages hold no picture of the
  one before.
* **Palette fades:** the game has none: it sets its 16 colours at once (a screen's palette, the raid's flashes and alarm, the
  car chase's two palettes) and never reads the palette back. A picture in colours 128 and up is not touched by such a change:
  the Amiga pictures keep their colours while the game's own pixels flash. In the building raid that is the faces' icons of the
  panel (cut from the Amiga's sheet), which do not flash with the room. The 16 colours that the PC's pictures show through are
  the game's, so a colour effect on a PC picture is as ever.
* The game does not read back the pixels of a picture on the list by colour number (that is why the board, whose wires the
  workbench reads from the screen, and the cars, whose bodies the offer recolours, are not on it).

No picture is changed for display: this is the whole of it. The pixels are written by the picture loader
(`LibraryPictures.LoadPicture`) in place of the PC's after it has read and decoded the PC's picture as ever, so the game's
file calls and drivers' calls are the same and, with the option off (or without the files) nothing is different, byte for
byte (the session proof and the oracle run with the defaults).

## The list

A picture is replaced only when it is on `Launcher.AmigaPictureList`, one by one with the reason. The test for a picture:

1. **Layout.** The edges of the two pictures coincide (the edge overlap of every picture of the Amiga disks with its PC twin was
   computed; 1.00 is the same layout): 0.95 or more. The game cuts sprites out of the sheets at fixed places, draws its menus at
   fixed places and measures the thumbnails; a picture with another layout would break them.
2. **The game's use of the pixels.** No colour of the picture is read back or recoloured by number by the game's code.
3. **The game's measure of a city.** For a city picture, the width and height the campaign measures (along the first row and
   column up to the first colour 0) are the PC picture's.
4. **Looked at.** The recorded sessions of the session proof were played with the option on and the screens that show the
   pictures looked at (`AmigaPictureScreensTests`, below: the faces and icons in the menus, the clue and intelligence screens, the
   debriefing and the raid's panel; the flight maps; the hotel; the cities; the car chase), and each picture of the list was
   loaded by the game's loader and looked at by itself (`AmigaPicturesFilesTests`). A picture whose colours or screen looked wrong
   would come off; none of the 32 did. The cities and maps the sessions never reach were looked at by themselves only.

| Pictures | Count | Why |
|---|---|---|
| `faces`, `facesf` | 2 | the sheets of face parts, clothes and envelopes; the same sheet cell for cell (1.00); colour 0 is see-through in both |
| `icons` | 1 | the debriefing's icons (0.96); colour 0 see-through in both |
| `europe`, `africa`, `central` | 3 | the flight maps (1.00, 1.00, 0.99); the game draws its dots, menu and lines over them |
| `hotel` | 1 | the hotel lobby (0.95), drawn on the screen with the menu over it |
| `equip2` | 1 | the loose item pictures the raid's status panel cuts its icons from (0.99); the cells cut have colour 0 as their see-through colour in both pictures (the Amiga's light background lies outside them) |
| `street` | 1 | the car chase's windscreen views (0.96); copied to the windscreen as they are |
| 15 cities: `vienna` `athens` `telaviv` `madrid` `belgrade` `budapest` `prague` `cairo` `amman` `beirut` `tripoli` `damascus` `tehran` `bogota` `nassau` | 15 | the picture at the top left of the canvas on colour 0, the same layout (0.95 to 1.00) and the same measure |
| 8 thumbnails: `caracas` `losangel` `managua` `medellin` `mexicoci` `panama` `rio` `sanjuan` | 8 | the PC's picture is a thumbnail (72 to 112 wide); the Amiga's is that thumbnail at the top left of a full canvas on colour 0, so it is cut to the PC's size (0.98 to 1.00) |

32 pictures. **Not replaced**, and why:

| Pictures | Why |
|---|---|
| `ad`, `camera`, `gender`, `training`, `sneakin`, `wiretap`, `guysnew2`, `guysnew3` | redrawn by the Amiga's artists: another art or layout (scores 0.00 to 0.46), the game's rectangles would cut the wrong parts |
| `sprites`, `spritesf` | the raid's guards, redrawn with 16 colours (0.48), and the raid recolours their wall colour |
| `bugs` | the workbench's pieces (0.64) |
| `geneva`, `helsinki`, `istanbul`, `khartoum`, `london`, `miami`, `rome`, `washingt` | redrawn on another layout (0.51 to 0.78) |
| `paris`, `moscow`, `berlin`, `amsterda`, `baghdad`, `riyadh` | the same layout, but the game's measure of the thumbnail gives other numbers (the Amiga fills the canvas with a colour, or its first row is a pixel longer), so the picture would be cut wrongly |
| `havana`, `kingston` | the thumbnail is another size on the Amiga |
| `board` | the wiring puzzle reads the wires' colour back from the picture (`ReadWiresFromPicture`: a pixel compared with colour 10) |
| `cars` | the offer recolours the bodies by colour number (`CarChase.Offer`) |
| `equip1`, `equip1m` | the equipment screen's cursor recolours a colour of the picture |
| the Amiga-only pictures (`bchback`, `casback`, `lauback`, `offback`, `disk`, `setupscr`, `titlesnew`, `chase2`, `samerica`) | no place in the PC game |
| the animations (`.pan`) other than the 11 of the second round | their program or the sizes of their images differ from the PC's (see "The Amiga's animations" below) |

## The second round (version 1.3, D-681 onward)

The first list kept to pictures whose layout is the PC's as they stand. A second look at the pictures listed above as not
replaced (`build/amiga-study2/`, each compared with the PC's by eye, in the game's own palette) shows that several are the
same picture on another canvas, or a picture the game puts on the screen whole, so that they can be shown where the game's
layout allows, with every spot the game reads kept the PC's. Each group is one commit and one register entry; each picture was
written side by side with the PC's (`AmigaPicturesFilesTests.EveryPictureOfTheListIsWrittenAsAPngWhenAskedTo` now writes
`picture-<name>.png` and `picture-<name>-pc.png`; the pairs were put together in `build/amiga-study2/pictures/`, not committed)
and approved by eye.

### Cities in a window (D-681)

`paris`, `moscow`, `berlin`, `amsterda`, `baghdad`, `riyadh`, `london`, `miami`, `rome`. The Amiga's picture is the PC's view
(the same composition in the Amiga's dithering and colours); what differed was only what the campaign measures
(`load_picture_measure`: the first row and column up to the first colour 0). The picture is shown in a window of the size the
game measures the PC's picture to be, on a canvas of colour 0; a pixel of colour 0 on the first row or column inside the window
(a hole in the sky, a black edge: up to three pixels) takes the colour of the nearest picture pixel on that line, so the game
measures exactly the PC's size and frames, cuts and places it as the PC's. The Amiga's strip beyond the window is not shown (the
game would not copy it). Left out: `geneva`, `helsinki`, `istanbul`, `khartoum`, `havana`, `kingston` (the Amiga's picture is the
same view without the PC's 12 leftmost columns, so the PC's frame would hold a black strip or another view) and `washingt` (a
repainted picture: other trees and sky).

### Pictures the game shows whole (D-682)

`sneakin` (the break-in), `ad` (shown when the player leaves) and `training` (the training screen). The game puts each on the
screen as it is: no sheet to cut from, no pixel read, nothing recoloured but the menu's own colours, so the Amiga's picture shows
in its own composition (`Backdrop` on the list; no layout score applies). On the training screen the four skills' cells are at the
PC's places and the black strips under them are where the game draws its rating boxes; the menu opens over the empty top.
Left out: `gender` (the woman on the left and the man on the right, the reverse of the game's menu), `wiretap` (the Amiga's board is
at the left, where the game's popup opens at 20, 20) and `camera` (the frames the game cuts out are elsewhere).

### Pictures the game shows whole (D-682)

`sneakin` (the break-in), `ad` (shown when the player leaves) and `training` (the training screen). The game puts each on the
screen as it is: no sheet to cut from, no pixel read, nothing recoloured but the menu's own colours, so the Amiga's picture shows
in its own composition (`Backdrop` on the list; no layout score applies). On the training screen the four skills' cells are at the
PC's places and the black strips under them are where the game draws its rating boxes; the menu opens over the empty top.
Left out: `gender` (the woman on the left and the man on the right, the reverse of the game's menu), `wiretap` (the Amiga's board is
at the left, where the game's popup opens at 20, 20) and `camera` (the frames the game cuts out are elsewhere).

### The Amiga's animations (D-683)

The Amiga's `.pan` files (23 on the three disks) are another container for the PC's small animation machine. Read from the
files and compared with the PC's `.PAN` and their disassembled programs (`Content.AmigaPanFile`, `Content.AmigaPanProgram`):

| Offset | Content |
|---|---|
| 0 | the mark `ANIMAM` |
| 6 | 32 colours, words `0x0RGB` (four bits each); most files use 16, the Amiga's EGA colours, with entry 0 the see-through marker |
| 0x46 | the place (two words), the width, the height, the pause between frames (a word; the PC's field) |
| then | `BGND`: a word for the kind; kind 1 (a picture): a word 0, the width, the height, the picture's stream; kind 2 (one colour): the colour number and the number of images |
| then | `SPRH` for each image: its number, a word the program does not need, 5 words 0, the width and the height, 4 words 0, a word that looks like a colour count, the image's stream |
| end | `AMPG`: a word with the program's size in bytes, the program |

A stream is the pictures' compressed one (the largest code width, then the codes; not byte-swapped here) and the pixels are one to a byte, the colour
number whole. The program is the PC's: every operation is a word (the PC's a byte); a Push is the word 5, a flag word (1: the
value is a variable's) and the value; every other operand is a big-endian word, except the **targets of the main program's jumps and
calls, which are little-endian** (the targets of the sprites' loops are big-endian). The sprites' programs follow the main program, which ends
where the lowest address a sprite is started at begins. The Amiga's animation code lets the game set the
variables from outside; its scripts differ from the PC's only where a PC script sets a variable itself (the dossier, the three rooms).

Of the 21 animations the Amiga shares with the PC, **11 are the PC's own program** (each instruction and address the same, a rest or an
image 0 after the last instruction aside) **with images of the same numbers and sizes**: `blding01` to `blding04`, `bugcar`,
`bustout`, `escape`, `followed`, `injail`, `intrgate`, `surndr01`. For these the pace, the program, the sound requests and so the timing are the
PC's exactly, so the game's own file plays and only the pixels of the images and the background are the Amiga's (the same display
colours 128 and up as the pictures; 11 to 69 % of an image's pixels differ in colour from the PC's: the Amiga's dithering and
recolouring; the backgrounds are the same colours). The rest differ in program or geometry and stay the PC's: `briefing` (other
images), `officef` and `officem` (other image sizes and a 160-pixel port), `comm`, `crypto`, `hq` and `research` (a 160-pixel port and a wider
side strip, image 7 of 41 pixels for the PC's 3; the three rooms also leave one variable to the game), `binos3` (fewer images, a bigger
picture), `woundii` (another program); `tit.pan` is the Amiga's own title sequence and has no PC twin. Side by side frames of the 11 were
written to `build/amiga-study2/animations/` (not committed; the test of the same name as for the pictures writes them).

### The Amiga's own dossier (D-684)

`CAPT1FPO.PAN` (disk 2; unused by every Amiga program, like the PC's `CAPTRD01.PAN`) plays in place of the PC's page when the
cut-content dossier option (`cutContent.capturedDossier`, D-660) and `graphics.amiga` are both on. It is the one animation
that plays **the Amiga's program** (translated to the PC's encoding by `AmigaPanProgram`) instead of the game's: the Amiga's
differs from the PC's. Its images are the PC's (2, 4, 6, 8, 9, 12, 14, 16, 18, 19, the same sizes) and two more, 5 and 7 (70 x 66),
the stand-in faces drawn behind the torsos 4 and 6; its script **has the woman's-photograph switch and a working one**: it
tests variable 0 (1: images 7 and 6, the woman; -1: images 5 and 4, the man; the game sets the variable from bit 0 of the face as for
the jail scene) and does not begin by setting the variable to -1 as the PC's script does (CC-19). Side by side frames, for a man and
for a woman, are in `build/amiga-study2/animations/captrd01-man.png` and `captrd01-woman.png` (not committed). The page shows the
Amiga's placeholder faces; neither version draws the captured person's own.

## The third round (D-685 onward)

The rule decided with the user: a picture is added only if **every pixel shown is a pixel the Amiga drew, in the colour it drew it**.
Moving whole regions and renumbering colour indices are allowed; drawing, cropping into missing areas and mixing the PC's and the
Amiga's artwork are not. Each picture added has a side by side PNG (the PC's, then the Amiga's as the game shows it) in
`build/amiga-study2/pictures/`, named after the picture (not committed). Added so far, and what stays out:

### The choice of agent, halves exchanged (D-685)

`gender`. The Amiga's picture has the woman on the left and the man on the right, the reverse of the game's Max and Maxine. The two
halves are exchanged (`AmigaPictureFile.SwapHalves`, `AmigaPictureEntry.Arrange`): a permutation of whole columns, so no pixel is
new. The Amiga's gradient between the two portraits is cut at its middle by the exchange and lies at the two edges of the screen.
The game shows the picture whole and reads none of it (`Backdrop`).

### The workbench's icons, the strip moved one pixel (D-686)

`bugs`. The game cuts 32 wire pieces and 12 chips from the sheet's top 32 rows and shows nothing else of it. The Amiga's cells are
one pixel to the right of the PC's rectangles; the strip is moved one pixel to the left (`AmigaPictureFile.MoveRegion`), after which
every rectangle holds the Amiga's cell and no cell loses a drawn pixel. The Amiga's board begins one column earlier than the PC's, so
the last wire piece and the last chip carry the board's edge in their last column (the Amiga drew those pixels there). The pieces are
the Amiga's dotted redrawing, not the PC's art shifted.

## Without the files

The folder is the one named in the launcher, else `amiga/files` (or `amiga`) next to the program, else next to the game folder
and the two folders above it, each counting when it holds `faces.pix` itself, in its `disk1` folder or in a folder directly below
it (for a folder named like a disk, the same below its parent). The other pictures are looked for in that folder, the folders
below it, its parent and the folders below that, so disk 2's city pictures are found beside disk 1. Without any the PC's
pictures show and the launcher says so (as for the MT-32 without ROMs, D-20); with some missing those pictures stay the PC's.
Where a picture exists on two disks (`faces` on disk 1 and disk 3, 227 pixels of one face part differ) the first is taken, disk 1's.

## Looking at it

`AmigaPictureScreensTests.TheScreensShowingAmigaPicturesAreWrittenAsPngFilesWhenAskedTo` plays the session proof's recorded
sessions (the campaign and whole sessions again at other times of day, so that other cases and cities are dealt) with the
option on and writes each screen with new picture colours as a PNG, with an index of the pictures loaded before it. It
needs `COVERT_SESSION_DIR`, `COVERT_ORACLE_DIR`, `COVERT_GAME_DIR`, `COVERT_AMIGA_DIR` and `COVERT_AMIGA_SCREENS_DIR` (where to
write; outside the repository). `AmigaPicturesFilesTests` reads every `.pix` file and re-checks each picture of the list
against the game's twin (layout, measure) when `COVERT_AMIGA_DIR` is set.
