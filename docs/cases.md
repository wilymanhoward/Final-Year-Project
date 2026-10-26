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

## Case Y: "Death in the Melaka Room" (murder; culprit: Farid Rahman)

Its own murder-scene layout (user decision, session 6: Case Y doesn't have to mirror Case X; same 5 x 5 m room, 7 clues,
3 zones, similar mean clue distance). Stylised low-poly blood and a taped body outline, no body model.

**Briefing:** Last night, Datuk Hashim Karim, founder of the Heritage Gallery, was found stabbed to death in the Melaka
Room with the gallery's own keris. He had found out that real pieces were being swapped for replicas. Bloody
footprints lead from the body towards the entrance. Four people had access to the room. Search the scene, tag the
evidence, and name the person who killed him.

**What happened (the scene makes sense):** Farid took the gallery key at 20:05 and met the Datuk in the room at 8
(his email). They fought: the vase and a chair went over and the wall clock was knocked off and smashed at 8:17. Farid
took the keris from the plinth and stabbed him. He wiped the hilt but a strand of his green tour-guide scarf stayed
caught in it, and he left through the entrance, leaving bloody footprints.

### Suspects

| Suspect | Role | Alibi |
|---|---|---|
| **Farid Rahman** (culprit) | Tour guide | "I was alone, preparing tomorrow's tour." (No one can confirm.) |
| Mr Tan | Security guard | On gate duty 8:00-8:40 pm; the gate camera shows him there. |
| Dr Sofia Idris | Curator | On a video call with a lender 7:50-8:45 pm; the recording confirms it. |
| Mr Lim | Private collector | At the hotel restaurant 8:00-9:00 pm; the receipt and staff confirm it. |

### Layout (CaseRoot-local, metres; start mark at 0, 0 facing the front wall +z)

- Display plinth (empty keris cradle) at the front wall (0.94, 1.94); reception table on the right wall (1.81, 0.56);
  tour desk on the right wall (1.81, -0.94); noticeboard on the left wall (-2.25, 0.31); entrance in the back wall,
  back-left (-1.69, -2.31); evidence board at the back (0, -2.19).
- The body (taped outline) lies in front of the plinth, centre (0, 1.25), head towards -x; the blood pool is under the
  head; the keris lies beside the body; the fallen chair is by the plinth.
- Mean clue distance 2.12 m (Case X 2.10 m).

### Clues

| Id | Object | Inspect text | Height |
|---|---|---|---|
| K1 | Smashed wall clock on the front wall (-1.50, 2.35) | Knocked off the wall in the struggle. The hands stopped at 8:17. | eye |
| K2 | Visitor logbook on the reception table | "Gallery key out 20:05, F.R." No return time written. | table |
| K3 | The keris (murder weapon) on the floor beside the body (0.35, 0.88) | Blade bloody. The hilt was wiped, but a strand of green tour-guide scarf is caught in it. | floor |
| K4 | Printed email on the tour desk | From the Datuk to Farid: "I know about the replicas. Meet me in the Melaka Room at 8. - H." | table |
| R1 (Mr Tan) | Torch beside the plinth | A security-issue torch, battery flat. Mr Tan argued with the Datuk this week. | floor |
| R2 (Mr Lim) | Business card near the entrance | Mr Lim's card; on the back: "That keris will be mine." | floor |
| R3 (Dr Sofia) | Note on the noticeboard | "Datuk, I will resign if the keris is sold. - S." | eye |

### Exclusion zones

1. Pool of blood under the body's head (0.6 x 0.6 m).
2. Bloody footprints leading from the pool towards the entrance (0.45 x 2.2 m, yaw 13.4).
3. Broken vase beside the plinth, right of the body (0.45 x 0.45 m).

---

## Layout rules

- The play area is **5 x 5 m** (one constant in code, `PlayArea.HalfSize`); furniture stands near the edges, the middle
  stays open, the start mark is the centre.
- **Case X** is the reference layout. **Case Y** has its own layout (it is not a mirror of X since session 6).
- The validator checks:
  - matching counts;
  - height bands;
  - similar mean distance from the start;
  - no clue inside a zone;
  - the same height band and grab style per slot in both cases.
