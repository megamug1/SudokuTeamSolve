# Sudoku Team Solve Command Language

This document defines the Twitch chat syntax for entering values in the current puzzle.

## Command shape

Every command begins with `$` and a target:

```text
$TARGET [DETAILS]
```

The prefix must touch the target. When details are present, at least one space must separate them from the target.

```text
$r2c6 5       valid
$r2c6 ^159    valid
$r2c6^159     invalid
```

The entire language is case-insensitive.

## Cell targets

A cell target contains a row and column. They may appear in either order:

```text
r2c6
c6r2
```

Both examples identify row 2, column 6.

Row and column numbers are one-based positive integers.

A target may include a position within the cell by appending `p1` through `p9`:

```text
r2c6p4
c6r2p4
```

Positions follow reading order:

```text
1 2 3
4 5 6
7 8 9
```

Xs, Os, and line endpoints use these positions. If `p` is omitted for one of those commands, `p5`, the center of the cell, is assumed.

## Operations

Commands add or set their specified item by default. A `-` at the beginning of the details removes or clears it.

## Digits and letters

A single value with no type symbol sets the cell's digit or letter. Valid values are `0` through `9` and `a` through `i`.

```text
$r2c6 5       set 5
$r2c6 0       set 0
$r2c6 a       set A
$r2c6 -       erase the digit or letter
```

Only one digit or letter may be set by a command.

## Corner marks

`^` identifies corner marks. It is followed by an uninterrupted list containing one or more values from `0` through `9` and `a` through `i`.

```text
$r2c6 ^159ai       add 1, 5, 9, A, and I
$r2c6 -^15a        remove 1, 5, and A
$r2c6 -^           clear all corner marks
```

## Center marks

`*` identifies center marks. It follows the same list rules as corner marks.

```text
$r2c6 *237bdf      add 2, 3, 7, B, D, and F
$r2c6 -*37b        remove 3, 7, and B
$r2c6 -*           clear all center marks
```

## Cell colors

A cell color has the form `cCOLOR` or `cCOLORsSET`.

- `COLOR` is `0` through `9`.
- `SET` is `1` through `3`.
- If the set is omitted, set 1 is assumed.

```text
$r2c6 c2         add color 2 from set 1
$r2c6 c2s1       add color 2 from set 1
$r2c6 c2s3       add color 2 from set 3
$r2c6 -c2s3      remove color 2 from set 3
$r2c6 -c         clear all cell colors
```

## X and O marks

`x` and `o` add positioned marks. An optional color is written as a separate detail after the mark.

```text
$r2c6 x          add a color-1 X at p5
$r2c6p4 x c2     add a color-2 X at p4
$r2c6 o c7       add a color-7 O at p5
$r2c6p9 o        add a color-1 O at p9
```

X and O colors range from `c1` through `c9`. They have only one color set, so an `sSET` suffix is invalid. If the color is omitted, `c1` is assumed.

Removal never includes a color and removes the mark regardless of its color:

```text
$r2c6 -x         remove the X at p5
$r2c6p4 -x       remove the X at p4
$r2c6p9 -o       remove the O at p9
```

## Lines

A line target contains exactly two cell targets separated by a colon:

```text
START:END
```

The two targets identify the command as a line command.

```text
$r2c1:r2c7                   add a color-1 line between the cell centers
$r2c1:r2c7 c3                add a color-3 line between the cell centers
$r2c1p6:r2c7p4 c3            add a color-3 line between explicit positions
$r2c1:r2c7 -                 remove the line regardless of its color
```

Each endpoint may independently omit its position, in which case `p5` is assumed. Line colors range from `c1` through `c9`, have no set suffix, and default to `c1`.

Endpoint order does not matter. These identify the same line:

```text
$r2c1:r2c7
$r2c7:r2c1
```

## Syntax summary

```text
command             = "$" (cell-command | mark-command | line-command)

cell-command        = cell-target SPACE cell-details
mark-command        = positioned-target SPACE mark-details
line-command        = positioned-target ":" positioned-target
                      [SPACE (drawing-color | "-")]

cell-target         = row column | column row
positioned-target   = cell-target [position]
row                 = "r" positive-integer
column              = "c" positive-integer
position            = "p" ("1" ... "9")

cell-details        = value
                    | "-"
                    | "^" value-list
                    | "-^" [value-list]
                    | "*" value-list
                    | "-*" [value-list]
                    | cell-color
                    | "-" cell-color
                    | "-c"

mark-details        = ("x" | "o") [SPACE drawing-color]
                    | "-" ("x" | "o")

cell-color          = "c" ("0" ... "9") ["s" ("1" ... "3")]
drawing-color       = "c" ("1" ... "9")
value               = "0" ... "9" | "a" ... "i"
value-list          = one or more values without separators
SPACE               = one or more spaces
```
