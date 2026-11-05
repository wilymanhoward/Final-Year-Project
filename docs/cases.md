# Case content (Phase D)

Source of truth for the two study cases. `CaseSpecs` (code) and the case definition assets are built from this file;
if you change the content here, update `CaseSpecs` to match.

## Shared structure (both cases)

- **4 suspects:** 1 culprit, 3 innocents. Every suspect card shows only the suspect's own claim, in the same
  style, never whether it is confirmed (supervisor, 2026-11: "unconfirmed" on the board gave the answer away). The
  key evidence contradicts the culprit's claim; the red herrings give each innocent a motive.
- **No key clue names the culprit outright (user, 2026-11-04: "harder and less obvious", both cases):** each case has
  one time (K1), the culprit's initials (K2), an object tied to the culprit's role (K3: "BEST MANAGER" mug / tour
  guide's scarf) and a motive sent to the culprit's role (K4: emailed to the studio manager / to the tour guide). Only putting
  them together with the suspect cards (role, claim) gives the answer. Each red herring makes its innocent look as
  suspicious as a single key clue.
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

## Case X: "Last Coffee at Kite" (murder by poisoning; culprit: Grace Wong)

Rewritten 2026-11-05 (user: both cases murders, Case X looked empty; Option A, a quiet poisoning, so it is clearly
different from Case Y's stabbing). The award theft stays as the motive.

**Briefing:** Last night, Encik Rizal Hamid, founder of Kite Design Studio, was found dead beside the front desk. The
gold Design of the Year award is gone from its smashed display case. It looks like a break-in, but the doctor says he
was poisoned. Four people had a reason to be at the studio last night. Search the room, tag the evidence, and name the
person who killed him.

**What happened:** Encik Rizal had found out that Grace planned to sell the award (the pawn-shop email). At 21:35 she
signed back in, mixed strong sleeping pills into a coffee in her own "BEST MANAGER" mug and served it to him in a cup.
He was sitting on the front-desk chair; he slid off it and collapsed on the floor, the chair rolled back and the
coffee spilled. At 21:42 she opened the display case with her staff code, took
the award, smashed the front pane so it would look like an outside break-in, rinsed her mug in a hurry and pushed it
under the display stand. No fight, no blood (unlike Case Y).

**Scene:** the front-desk chair rolled back and turned, the taped outline of the victim on his side on the floor in
front of it (legs by the chair), the spilled coffee just past the chair (zone 2); smashed, open, empty display case with the broken glass in front (zone 1); muddy footprints from the door
(zone 3: it was raining when she came back).

### Suspects

| Suspect | Role | Alibi |
|---|---|---|
| **Grace Wong** (culprit) | Studio manager | "I locked up at 7:00 pm and was home by 7:30." |
| Kumar Raj | Night cleaner | "I finished cleaning and left the building at 9:05 pm." |
| Lena Ortiz | Rival designer next door | "I was at dinner nearby from 7 until 10 pm." |
| Aiman Hakim | Junior designer | "I was at the gym from 9:15 pm until late." |

### Clues

| Id | Object | Inspect text | Height |
|---|---|---|---|
| K1 | Display case lock panel | "Case opened: 21:42." | eye |
| K2 | Sign-in sheet on front desk | Last entry: "G.W., in 21:35." Someone tried to scribble it out. | table |
| K3 | Wet mug under the display stand | A mug printed "BEST MANAGER", pushed under the display stand. It is still wet: someone rinsed it in a hurry. | floor |
| K4 | Printed email on manager's desk | Email from a pawn shop to studio.manager@kite.my: "We can offer RM 15,000 if the award is real gold. Bring it after closing." | table |
| R1 (Kumar) | Cleaning glove beside display | A yellow cleaning glove beside the display case. It smells of glass cleaner. Encik Rizal fired Kumar this week. | floor |
| R2 (Lena) | Torn sketch near door | A torn drawing of the award, signed "L. Ortiz". Scrawled across it: "Rizal stole my design." | floor |
| R3 (Aiman) | Sticky note on a monitor | "Rizal promised me that award. Liar. - A." | eye |

### Exclusion zones

1. Broken glass in front of the display case, from its smashed front pane (user, 2026-11-05: the case shows the
   damage). Grace opened the case with her code (K1, 21:42) and smashed the pane to make it look like a break-in.
2. Spilled (poisoned) coffee beside the front desk, by the victim's hand.
3. Muddy footprints from the door toward the shelf.

---

## Case Y: "Death in the Melaka Room" (murder; culprit: Farid Rahman)

Its own murder-scene layout (user decision, session 6: Case Y doesn't have to mirror Case X; same 5 x 5 m room, 7 clues,
3 zones, similar mean clue distance). Stylised low-poly blood and a taped body outline, no body model.

**Briefing:** Last night, Datuk Hashim Karim, founder of the Heritage Gallery, was found stabbed to death in the Melaka
Room with the gallery's own keris. The keris is gone from its plinth. He had found out that real pieces were being swapped for replicas. Bloody
footprints lead from the body towards the entrance. Four people had access to the room. Search the scene, tag the
evidence, and name the person who killed him.

**What happened (the scene makes sense):** Farid took the gallery key at 20:05 and met the Datuk in the room at 8
(his email). They fought: the vase and a chair went over and the wall clock was knocked off and smashed at 8:17. Farid
took the keris from the plinth and stabbed him. In the struggle the Datuk tore a scrap off Farid's green tour-guide
scarf. Farid took the keris away with him (user, 2026-11-04: a killer who leaves the weapon is "too dumb") but didn't
notice the scrap, and he left through the entrance, leaving bloody footprints.

### Suspects

| Suspect | Role | Alibi |
|---|---|---|
| **Farid Rahman** (culprit) | Tour guide | "I was in the staff room preparing tomorrow's tour, 7:30 to 9 pm." |
| Mr Tan | Security guard | "I was on gate duty from 8 to 8:40 pm." |
| Dr Sofia Idris | Curator | "I was on a video call with a lender from 7:50 pm." |
| Mr Lim | Private collector | "I was having dinner at my hotel from 8 to 9 pm." |

### Layout (CaseRoot-local, metres; start mark at 0, 0 facing the front wall +z)

- Display plinth (empty keris cradle) at the front wall (0.94, 1.94); reception table on the right wall (1.81, 0.56);
  tour desk on the right wall (1.81, -0.94); noticeboard on the left wall (-2.25, 0.31); entrance in the back wall,
  back-left (-1.69, -2.31); evidence board at the back (0, -2.19).
- The body (taped outline) lies in front of the plinth, centre (0, 1.25), head towards -x; the blood pool is under the
  head; the torn scarf scrap lies beside the body; the fallen chair is by the plinth; the keris cradle is empty.
- Mean clue distance 2.12 m (Case X 2.10 m).

### Clues

| Id | Object | Inspect text | Height |
|---|---|---|---|
| K1 | Smashed wall clock on the front wall (-1.50, 2.35) | Knocked off the wall in the struggle. The hands stopped at 8:17. | eye |
| K2 | Visitor logbook on the reception table | "Gallery key out 20:05, F.R." No return time written. | table |
| K3 | Torn scarf scrap on the floor beside the body (0.35, 0.88) | A scrap of green tour-guide scarf, torn off in the struggle. A spot of blood on it. | floor |
| K4 | Printed email on the tour desk | Email from the Datuk to the tour guide: "I know about the replicas. Meet me in the Melaka Room at 8. - H." | table |
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
