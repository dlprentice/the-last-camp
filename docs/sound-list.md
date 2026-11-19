# Sound list

Every moment in the micro cut that needs a sound, what it should tell the player, and
how it is made. All audio comes from ElevenLabs ([direction](direction.md)); the
generated files are licensed, stay out of Git, and ship only inside builds. The
tracked manifest names each file, its prompt and its take.

Kinds: **bed** is a seamless loop (generated with `loop`), **one-shot** plays once,
**set** is several variants of one sound so repeats never sound identical.

## Ambience beds

| Id | Kind | Length | What it says |
| --- | --- | --- | --- |
| bed_day_woods | bed | 30 s | a mixed northern forest in late summer: light wind in leaves, distant birds; calm, alive |
| bed_evening | bed | 30 s | the same woods near sunset: fewer birds, the first crickets; the day closing |
| bed_night | bed | 30 s | crickets and the odd frog by a pond, very still; night is safe, not eerie |
| bed_wind_canopy | bed | 30 s | wind moving through tall trees, faded with the wind strength |
| bed_pond_lap | bed | 20 s | small water lapping at wooden pier posts, heard near the pier |
| bed_fire | bed | 30 s | a small campfire crackling, steady; warmth, company |
| bed_rain | bed | 30 s | steady rain on leaves and ground |
| bed_rain_canvas | bed | 30 s | rain drumming on a canvas tent from inside; shelter |
| bed_storm_wind | bed | 30 s | strong gusting wind through a forest at night; the storm is real |
| bed_wading | bed | 10 s | legs moving slowly through shallow water |

## Wildlife and weather one-shots

| Id | Kind | What it says |
| --- | --- | --- |
| birds_day | set of 8 | single songbird phrases at a distance (thrush, chickadee, warbler, vireo) |
| owl | set of 3 | a barred owl calling far off at night |
| loon | set of 2 | a common loon's wail across water at dusk; the pond is big and old |
| thunder | set of 4 | thunder, from distant roll to close crack |
| fire_hiss | set of 3 | rain hitting hot coals; the fire is in danger |

## The player

| Id | Kind | What it says |
| --- | --- | --- |
| step_grass | set of 6 | a footstep on dry grass |
| step_dirt | set of 6 | a footstep on a packed dirt path |
| step_wood | set of 6 | a footstep on weathered pier planks |
| step_rock | set of 6 | a footstep on granite and flat stone steps |
| step_water | set of 6 | a footstep in ankle-deep water |
| step_hollow | set of 3 | a foot on a loose flat stone that rocks and knocks hollow: something is under it |
| water_in, water_out | one-shot | stepping into and out of the pond |

## Actions

| Id | Kind | What it says |
| --- | --- | --- |
| log_pickup | set of 3 | lifting a split log off a woodpile |
| log_into_fire | set of 3 | a log dropped on a fire, a burst of sparks |
| stone_pickup | set of 3 | picking a flat stone off wood |
| stone_throw | set of 3 | a stone leaving the hand, a short whoosh |
| stone_skip | set of 4 | a stone skipping off still water |
| stone_sink | set of 2 | a stone plopping into water and sinking |
| lantern_on, lantern_off | one-shot | an oil lantern's wick turned up and lit; turned down |
| fire_light | one-shot | a match struck and kindling catching |
| tent_enter | one-shot | brushing through a canvas tent flap |
| tin_open | one-shot | an old metal tackle tin's hinged lid opening, stones rattling inside |
| page_turn | set of 3 | turning an old paper notebook page |
| letter_unfold | one-shot | unfolding an old paper letter |
| step_lift | one-shot | a heavy flat stone lifted and set aside on rock |
| tin_lift | one-shot | a small rusted tin box pulled from soil and opened |
| ash_rake | set of 3 | raking wet or dry ash in a fire ring |
| ring_stone | set of 3 | a fire-ring stone lifted and set down on earth |
| tent_peg | set of 3 | a tent peg pulled from ground |
| tent_fall | one-shot | a canvas tent collapsing as its poles come out |
| sit_down | one-shot | sitting down on a log or stone |

## Interface and story

| Id | Kind | What it says |
| --- | --- | --- |
| ui_move | one-shot | a soft paper tick as the menu focus moves |
| ui_select | one-shot | a pencil tap on paper |
| journal_open, journal_close | one-shot | a cloth-bound book opened and closed |
| card_in | one-shot | a low, soft breath of wind under a chapter card |
| title_theme | music, 60–90 s | sparse solo piano or acoustic guitar, late summer, unresolved; title and credits only (if it earns its place) |

## Voice

June's lines are in [the script](script.md): seven entries and nine margin notes,
rendered with one fixed ElevenLabs voice.

## Budget

At about 40 credits a second for effects: 10 beds averaging 27 s, three takes each,
cost about 32,000. About 120 one-shots and variants averaging 2 s, three takes each,
cost about 29,000. With the voice (about 7,000 for two takes) and a theme (about
1,500), the full first pass is about 70,000 credits, a little over one month's share.
