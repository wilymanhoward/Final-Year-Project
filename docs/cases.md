# Case content (Phase D)

Source of truth for the two study cases. `CaseSpecs` (code) and the case definition assets are built from this file;
if you change the content here, update `CaseSpecs` to match.

## Shared structure (both cases)

- **4 suspects:** 1 culprit with an unconfirmed alibi, 3 innocent suspects with confirmed alibis.
- **7 clues:** 4 key evidence + 3 red herrings.
  - K1 = time, K2 = access, K3 = trace, K4 = motive.
  - R1-R3 = red herrings, one per innocent suspect.
- **3 exclusion zones.**
- **Clue heights:** K1 eye, K2 table, K3 floor, K4 table, R1 floor, R2 floor, R3 eye.
- **Suspect cards** are shown on the evidence board from the start. They are not clues and cannot be tagged.

### Grading

- **answer_correct** = the correct culprit (whatever evidence was submitted).
- **perfect_submission** = correct culprit + K1-K4 + no red herrings.
- **Reasoning errors** = 1 for a wrong suspect + 1 per red herring submitted.
- **Evidence score** = number of K1-K4 submitted (0-4).

---

## Case X: "The Missing Design Award" (culprit: Grace Wong)

**Briefing:** Last night, the gold Design of the Year award vanished from the display case at Kite Design Studio,
just hours before the company's big exhibition. The case was found open this morning. Four people had a reason to
be near the studio last night. Search the room, tag the evidence, and name the person who took the award.

### Suspects

| Suspect | Role | Alibi |
|---|---|---|
| **Grace Wong** (culprit) | Studio manager | "I locked up at 7:00 pm and went straight home." (No one can confirm.) |
| Kumar Raj | Night cleaner | Clocked out at 9:05 pm; security confirms he left the building. |
| Lena Ortiz | Rival designer next door | At a restaurant 7:00-10:00 pm; the restaurant confirms her booking. |
| Aiman Hakim | Junior designer | At the gym 9:15-10:30 pm; the gym's check-in record confirms it. |

### Clues

| Id | Object | Inspect text | Height |
|---|---|---|---|
| K1 | Display case lock panel | "Case opened: 21:42." | eye |
| K2 | Sign-in sheet on front desk | Last entry: "G. Wong, in 21:35." Someone tried to scribble it out. | table |
| K3 | Staff lanyard clip under display shelf | Broken clip with a tag: "Staff #004, G. Wong." | floor |
| K4 | Printed email on manager's desk | From a pawn shop to Grace: "We can offer RM 15,000 if the award is real gold." | table |
| R1 (Kumar) | Cleaning glove beside display | A yellow rubber glove from the cleaning trolley. | floor |
| R2 (Lena) | Torn sketch near door | A drawing of the award, signed "L. Ortiz." | floor |
| R3 (Aiman) | Sticky note on a monitor | "This award should have been mine. - A." | eye |

### Exclusion zones

1. Broken glass in front of the display case.
2. Spilled coffee beside the front desk.
3. Muddy footprints from the door toward the shelf.

---

## Case Y: "The Missing Keris" (culprit: Farid Rahman)

**Briefing:** Yesterday evening, an antique keris disappeared from its display case in the Heritage Gallery's
Melaka Room, one week before it was due to go on loan overseas. The case was found open this morning. Four people
had a reason to be near the room last night. Search the room, tag the evidence, and name the person who took the
keris.

### Suspects

| Suspect | Role | Alibi |
|---|---|---|
| **Farid Rahman** (culprit) | Tour guide | "My last tour ended at 7:30 pm and I went home." (No one can confirm.) |
| Mr Tan | Security guard | On gate duty 8:00-9:00 pm; the front-desk camera shows him there. |
| Dr Sofia Idris | Curator | Hosting a donor dinner across town 7:00-9:30 pm; the guests confirm it. |
| Mr Lim | Private collector | On a flight that landed at KLIA at 9:00 pm; the airline confirms it. |

### Clues

| Id | Object | Inspect text | Height |
|---|---|---|---|
| K1 | Display case alarm panel | "Case 3 opened: 20:17." | eye |
| K2 | Key logbook on reception table | "Gallery key out 20:05, F.R." No return time written. | table |
| K3 | Laser pointer under display plinth | Engraved: "Farid, Guide Team." | floor |
| K4 | Printed chat message on tour desk | To Farid: "Still selling the keris? I can pay cash this week." | table |
| R1 (Mr Tan) | Torch beside plinth | A security-issue torch, battery flat. | floor |
| R2 (Mr Lim) | Business card near entrance | Mr Lim's card; on the back: "That keris will be mine." | floor |
| R3 (Dr Sofia) | Note on noticeboard | "Move keris to storage before loan. - S." | eye |

### Exclusion zones

1. Shattered display glass around the plinth.
2. Water from an overturned vase beside the reception table.
3. Wet floor varnish in the restoration corner.

---

## Layout rules

- The play area is **4 x 4 m** (one constant in code, `PlayArea.HalfSize`); the layouts keep everything inside the
  central **3 x 3 m**.
- **Case Y = Case X mirrored/rotated**, so the display, desk and door are in different places.
- The validator checks:
  - matching counts;
  - height bands;
  - similar mean distance from the start;
  - no clue inside a zone;
  - the mirror rule.
