# Counterbalancing sheet

Within-subjects: every participant plays **both conditions** (A = solo, B = team of 2) and **both cases** (X = "The
Missing Design Award", Y = "Death in the Melaka Room"), one case per condition. Participants come as pairs, and both
partners of a pair follow the same order (in B they play together, so they must share the case).

Four orders, so condition order and case-condition pairing are both balanced:

| Order | Run 1 | Run 2 |
|---|---|---|
| 1 | A, Case X | B, Case Y |
| 2 | A, Case Y | B, Case X |
| 3 | B, Case X | A, Case Y |
| 4 | B, Case Y | A, Case X |

Assign pairs to orders in rotation (G01 = 1, G02 = 2, G03 = 3, G04 = 4, G05 = 1, ...). Use a multiple of 4 pairs (8, 12,
16, 20, 24) so each order is used equally often. If a pair drops out, give the next pair the same order, so the counts
stay balanced.

Set on the setup panel before each run: **Pair** = the pair code, **Participant** = P01 / P02 (P01 = host headset in
B), **Case** and **Condition** from the table. The codes go into every file name, so the analysis can match the runs
(`python -I tools/analyze_runs.py StudyData` adds `run_order` = 1 or 2 per participant, from the start time).

| Pair | Order | Run 1 | Run 2 | Date | Run 1 done | Run 2 done | Notes |
|---|---|---|---|---|---|---|---|
| G01 | 1 | A-X | B-Y | | | | |
| G02 | 2 | A-Y | B-X | | | | |
| G03 | 3 | B-X | A-Y | | | | |
| G04 | 4 | B-Y | A-X | | | | |
| G05 | 1 | A-X | B-Y | | | | |
| G06 | 2 | A-Y | B-X | | | | |
| G07 | 3 | B-X | A-Y | | | | |
| G08 | 4 | B-Y | A-X | | | | |
| G09 | 1 | A-X | B-Y | | | | |
| G10 | 2 | A-Y | B-X | | | | |
| G11 | 3 | B-X | A-Y | | | | |
| G12 | 4 | B-Y | A-X | | | | |
| G13 | 1 | A-X | B-Y | | | | |
| G14 | 2 | A-Y | B-X | | | | |
| G15 | 3 | B-X | A-Y | | | | |
| G16 | 4 | B-Y | A-X | | | | |
| G17 | 1 | A-X | B-Y | | | | |
| G18 | 2 | A-Y | B-X | | | | |
| G19 | 3 | B-X | A-Y | | | | |
| G20 | 4 | B-Y | A-X | | | | |
| G21 | 1 | A-X | B-Y | | | | |
| G22 | 2 | A-Y | B-X | | | | |
| G23 | 3 | B-X | A-Y | | | | |
| G24 | 4 | B-Y | A-X | | | | |

Pilot pairs use codes outside this list (e.g. PIL1, PIL2) and are moved out of StudyData before the analysis.

**Condition A (decided 2026-10-29):** the two partners play their solo runs one after the other; the one not playing
waits outside the room, so nobody else is in view during a solo run. Both use the same case (the table's A case);
the order of the two partners within A (P01 first or P02 first) can alternate per pair.
